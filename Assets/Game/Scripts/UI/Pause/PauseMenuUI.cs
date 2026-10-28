using System;
using System.Collections;
using PiGame.Lobby;
using PiGame.Input;
using PiGame.Sound;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class PauseMenuUI : MonoBehaviour
    {
        [Header("Configuração")]
        [SerializeField] private InputActionAsset _inputActions;

        [Header("Hierarquia")]
        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _confirmationText;
        [SerializeField] private GameObject _actionsRoot;
        [SerializeField] private GameObject _confirmationRoot;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _controlsButton;
        [Tooltip("Opcional: botão SAIR do pause da partida.")]
        [SerializeField] private Button _exitButton;
        [SerializeField] private Button _confirmExitButton;
        [SerializeField] private Button _cancelExitButton;
        [SerializeField] private ControlsSettingsUI _controlsView;
        [Tooltip("Opções já presentes no prefab do pause da partida.")]
        [SerializeField] private PauseMenuUI _optionsView;
        [SerializeField] private GameObject _panelRoot;
        [Tooltip("Opcional: usado nas opções do menu e do lobby, não no pause da partida.")]
        [SerializeField] private Button _playerNameButton;

        [Header("Opções na mesma tela (opcional)")]
        [SerializeField] private Button _audioCategoryButton;
        [SerializeField] private GameObject _audioContent;
        [SerializeField] private GameObject _controlsContent;
        [SerializeField] private GameObject _playerContent;
        [SerializeField] private Slider _masterVolume;
        [SerializeField] private AudioMixer _audioMixer;
        [SerializeField] private TMP_Text _masterVolumeValue;
        [SerializeField] private Slider _musicVolume;
        [SerializeField] private TMP_Text _musicVolumeValue;
        [SerializeField] private Slider _sfxVolume;
        [SerializeField] private TMP_Text _sfxVolumeValue;
        [SerializeField] private Slider _gamepadDeadzone;
        [SerializeField] private TMP_Text _gamepadDeadzoneValue;
        [SerializeField] private InputField _inlinePlayerName;
        [SerializeField] private Button _saveNameButton;

        private bool HasInlineOptions => _audioCategoryButton != null && _audioContent != null
            && _controlsContent != null && _playerContent != null;

        private PauseMenuDefinition _definition;
        private Coroutine _selectionRoutine;
        private bool _buttonsBound;

        public event Action ResumeRequested;
        public event Action ExitRequested;
        public event Action ExitConfirmed;
        public event Action ExitCanceled;
        public event Action ControlsClosed;
        public event Action PlayerNameRequested;

        public bool IsVisible => enabled && _root.activeSelf;
        public bool IsConfirmationVisible => enabled && _confirmationRoot.activeSelf;
        public bool IsControlsVisible => _controlsView != null && _controlsView.IsVisible;
        public bool IsOptionsVisible => _optionsView != null && _optionsView.IsVisible;
        public bool BlocksControlsBackShortcut => IsControlsVisible && _controlsView.BlocksBackShortcut;
        public bool IsInlineControlsBusy => HasInlineOptions && _controlsView != null
            && _controlsView.IsVisible && _controlsView.BlocksBackShortcut;

        private void Awake()
        {
            if (!ValidateReferences())
            {
                enabled = false;
                return;
            }

            ConfigureConfirmationNavigation();
            BindButtons();
            if (HasInlineOptions)
            {
                if (_controlsView == null || _masterVolume == null || _masterVolumeValue == null
                    || _musicVolume == null || _musicVolumeValue == null
                    || _sfxVolume == null || _sfxVolumeValue == null
                    || _gamepadDeadzone == null || _gamepadDeadzoneValue == null
                    || _inlinePlayerName == null || _saveNameButton == null)
                {
                    Debug.LogError("Configure as referências das opções no prefab do lobby.", this);
                    enabled = false;
                    return;
                }

                _audioCategoryButton.onClick.AddListener(ShowInlineAudio);
                GameAudioSettings.UseMixer(_audioMixer);
                _saveNameButton.onClick.AddListener(SaveInlineName);
                _inlinePlayerName.onSubmit.AddListener(HandleInlineNameSubmitted);
                _inlinePlayerName.characterLimit = LocalPlayerName.MaximumLength;
                _masterVolume.SetValueWithoutNotify(GameAudioSettings.Master);
                _musicVolume.SetValueWithoutNotify(GameAudioSettings.Music);
                _sfxVolume.SetValueWithoutNotify(GameAudioSettings.Sfx);
                _gamepadDeadzone.SetValueWithoutNotify(GamepadSettings.Deadzone);
                RefreshVolumeLabels();
                _masterVolume.onValueChanged.AddListener(HandleMasterVolumeChanged);
                _musicVolume.onValueChanged.AddListener(HandleMusicVolumeChanged);
                _sfxVolume.onValueChanged.AddListener(HandleSfxVolumeChanged);
                _gamepadDeadzone.onValueChanged.AddListener(HandleDeadzoneChanged);
            }
        }

        private void OnDestroy()
        {
            if (_optionsView != null) _optionsView.ResumeRequested -= HideOptions;
            UnbindButtons();
            if (HasInlineOptions && _masterVolume != null && _saveNameButton != null)
            {
                _audioCategoryButton.onClick.RemoveListener(ShowInlineAudio);
                _saveNameButton.onClick.RemoveListener(SaveInlineName);
                _inlinePlayerName.onSubmit.RemoveListener(HandleInlineNameSubmitted);
                _masterVolume.onValueChanged.RemoveListener(HandleMasterVolumeChanged);
                if (_musicVolume != null)
                    _musicVolume.onValueChanged.RemoveListener(HandleMusicVolumeChanged);
                if (_sfxVolume != null)
                    _sfxVolume.onValueChanged.RemoveListener(HandleSfxVolumeChanged);
                _gamepadDeadzone.onValueChanged.RemoveListener(HandleDeadzoneChanged);
            }
        }

        private void OnDisable()
        {
            StopSelectionRoutine();
        }

        private void Update()
        {
            if (!HasInlineOptions || !IsVisible || _controlsView.BlocksBackShortcut
                || Gamepad.current == null || !Gamepad.current.leftShoulder.wasPressedThisFrame) return;

            // Atalho já indicado no rodapé: sai do conteúdo e volta às categorias.
            ShowInlineAudio();
            QueueSelection(_audioCategoryButton);
        }

        public bool Initialize(PauseMenuDefinition definition)
        {
            _definition = definition;
            if (_definition == null || !ValidateReferences())
            {
                Debug.LogError("PauseMenuUI precisa de uma definição e das referências da cena.", this);
                enabled = false;
                return false;
            }

            RefreshStaticTexts();
            ConfigureControlsView(_inputActions);
            return true;
        }

        public bool Initialize(
            InputActionAsset actions,
            string title,
            string resumeLabel,
            string exitLabel,
            string confirmationMessage)
        {
            _inputActions = actions;
            if (!ValidateReferences())
            {
                enabled = false;
                return false;
            }

            _titleText.text = title;
            _confirmationText.text = confirmationMessage;
            SetButtonLabel(_resumeButton, resumeLabel);
            SetButtonLabel(_controlsButton, "CONTROLES");
            if (_exitButton != null)
                SetButtonLabel(_exitButton, exitLabel);
            ConfigureActions(true);
            ConfigureControlsView(actions);
            return true;
        }

        public void SetVisible(bool isVisible)
        {
            if (!isVisible && _optionsView != null) _optionsView.SetVisible(false);
            _root.SetActive(isVisible);
            if (!isVisible)
            {
                if (_controlsView != null && _controlsView.IsVisible)
                {
                    _controlsView.Hide(false);
                }
                StopSelectionRoutine();
                return;
            }

            if (HasInlineOptions) GameAudioSettings.UseMixer(_audioMixer);
            if (_panelRoot != null) _panelRoot.SetActive(true);
            HideConfirmation();
            if (HasInlineOptions) ShowInlineAudio();
            QueueSelection(HasInlineOptions ? _audioCategoryButton : _resumeButton);
        }

        public void ShowControls()
        {
            if (_optionsView != null)
            {
                _panelRoot.SetActive(false);
                _optionsView.SetVisible(true);
                return;
            }
            if (HasInlineOptions)
            {
                ShowInlineContent(_controlsContent);
                _controlsView.Show();
                return;
            }

            _actionsRoot.SetActive(false);
            _controlsView.Show();
            _root.SetActive(false);
        }

        public bool HandleControlsBack()
        {
            return _controlsView != null
                && _controlsView.IsVisible
                && _controlsView.HandleBack();
        }

        public void HandleOptionsBack()
        {
            if (_optionsView.IsInlineControlsBusy) return;
            if (_optionsView._controlsView.IsVisible) _optionsView.HandleControlsBack();
            else HideOptions();
        }

        private void HideOptions()
        {
            _optionsView.SetVisible(false);
            _panelRoot.SetActive(true);
            QueueSelection(_controlsButton);
        }

        public void ShowConfirmation()
        {
            _actionsRoot.SetActive(false);
            _confirmationRoot.SetActive(true);
            QueueSelection(_cancelExitButton);
        }

        public void HideConfirmation()
        {
            _confirmationRoot.SetActive(false);
            _actionsRoot.SetActive(true);
            if (IsVisible)
            {
                if (HasInlineOptions) ShowInlineAudio();
                QueueSelection(HasInlineOptions ? _audioCategoryButton : _resumeButton);
            }
        }

        public void SetBusy(bool isBusy)
        {
            SetButtonInteractable(_resumeButton, !isBusy);
            SetButtonInteractable(_controlsButton, !isBusy);
            if (_exitButton != null)
                SetButtonInteractable(_exitButton, !isBusy);
            if (_playerNameButton != null)
                _playerNameButton.interactable = !isBusy;
            SetButtonInteractable(_confirmExitButton, !isBusy);
            SetButtonInteractable(_cancelExitButton, !isBusy);
        }

        private void BindButtons()
        {
            if (_buttonsBound)
            {
                return;
            }

            _resumeButton.onClick.AddListener(HandleResumeClicked);
            _controlsButton.onClick.AddListener(ShowControls);
            if (HasInlineOptions)
                _playerNameButton?.onClick.AddListener(ShowInlinePlayer);
            else
                _playerNameButton?.onClick.AddListener(HandlePlayerNameClicked);
            _exitButton?.onClick.AddListener(HandleExitClicked);
            _confirmExitButton.onClick.AddListener(HandleExitConfirmedClicked);
            _cancelExitButton.onClick.AddListener(HandleExitCanceledClicked);
            _buttonsBound = true;
        }

        private void UnbindButtons()
        {
            if (!_buttonsBound)
            {
                return;
            }

            _resumeButton.onClick.RemoveListener(HandleResumeClicked);
            _controlsButton.onClick.RemoveListener(ShowControls);
            if (HasInlineOptions)
                _playerNameButton?.onClick.RemoveListener(ShowInlinePlayer);
            else
                _playerNameButton?.onClick.RemoveListener(HandlePlayerNameClicked);
            _exitButton?.onClick.RemoveListener(HandleExitClicked);
            _confirmExitButton.onClick.RemoveListener(HandleExitConfirmedClicked);
            _cancelExitButton.onClick.RemoveListener(HandleExitCanceledClicked);
            if (_controlsView != null)
            {
                _controlsView.Closed -= HandleControlsClosed;
            }

            _buttonsBound = false;
        }

        private void HandleResumeClicked() => ResumeRequested?.Invoke();
        private void HandlePlayerNameClicked() => PlayerNameRequested?.Invoke();
        private void HandleInlineNameSubmitted(string _) => SaveInlineName();
        private void ShowInlineAudio() => ShowInlineContent(_audioContent);
        private void ShowInlinePlayer()
        {
            _inlinePlayerName.SetTextWithoutNotify(LocalPlayerName.Load());
            ShowInlineContent(_playerContent);
            QueueSelection(_inlinePlayerName);
        }

        private void ShowInlineContent(GameObject content)
        {
            if (_controlsView != null && _controlsView.IsVisible && content != _controlsContent)
                _controlsView.Hide(false);
            _audioContent.SetActive(content == _audioContent);
            _controlsContent.SetActive(content == _controlsContent);
            _playerContent.SetActive(content == _playerContent);
        }

        private void SaveInlineName()
        {
            _inlinePlayerName.SetTextWithoutNotify(LocalPlayerName.Save(_inlinePlayerName.text));
            QueueSelection(_saveNameButton);
        }

        private void HandleMasterVolumeChanged(float value)
        {
            GameAudioSettings.SetMaster(value);
            RefreshVolumeLabels();
        }

        private void HandleMusicVolumeChanged(float value)
        {
            GameAudioSettings.SetMusic(value);
            RefreshVolumeLabels();
        }

        private void HandleSfxVolumeChanged(float value)
        {
            GameAudioSettings.SetSfx(value);
            RefreshVolumeLabels();
        }

        private void RefreshVolumeLabels()
        {
            _masterVolumeValue.text = $"{Mathf.RoundToInt(_masterVolume.value * 100)}%";
            _musicVolumeValue.text = $"{Mathf.RoundToInt(_musicVolume.value * 100)}%";
            _sfxVolumeValue.text = $"{Mathf.RoundToInt(_sfxVolume.value * 100)}%";
            _gamepadDeadzoneValue.text = $"{Mathf.RoundToInt(_gamepadDeadzone.value * 100)}%";
        }

        private void HandleDeadzoneChanged(float value)
        {
            GamepadSettings.SetDeadzone(value);
            RefreshVolumeLabels();
        }
        private void HandleExitClicked() => ExitRequested?.Invoke();
        private void HandleExitConfirmedClicked() => ExitConfirmed?.Invoke();
        private void HandleExitCanceledClicked() => ExitCanceled?.Invoke();

        private void RefreshStaticTexts()
        {
            _titleText.text = _definition.Title;
            _confirmationText.text = _definition.ExitConfirmationMessage;
            SetButtonLabel(_resumeButton, _definition.ResumeLabel);
            SetButtonLabel(_controlsButton, _definition.ControlsLabel);
            if (_exitButton != null)
                SetButtonLabel(_exitButton, _definition.ExitLabel);
            ConfigureActions(_definition.ShowControlsButton);
        }

        private void ConfigureActions(bool showControls)
        {
            _controlsButton.gameObject.SetActive(showControls);
        }

        private void ConfigureConfirmationNavigation()
        {
            _confirmExitButton.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnRight = _cancelExitButton
            };
            _cancelExitButton.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = _confirmExitButton
            };
        }

        private void ConfigureControlsView(InputActionAsset actions)
        {
            if (_optionsView != null)
            {
                _optionsView.Initialize(actions, "OPÇÕES", "VOLTAR", string.Empty, string.Empty);
                _optionsView.SetVisible(false);
                _optionsView.ResumeRequested -= HideOptions;
                _optionsView.ResumeRequested += HideOptions;
                return;
            }
            if (!_controlsButton.gameObject.activeSelf)
            {
                return;
            }

            if (_controlsView == null || actions == null)
            {
                Debug.LogError("O botão CONTROLES precisa de ControlsSettingsUI e InputActionAsset.", this);
                _controlsButton.interactable = false;
                return;
            }

            if (!_controlsView.Initialize(actions))
            {
                _controlsButton.interactable = false;
                return;
            }

            _controlsView.Closed -= HandleControlsClosed;
            _controlsView.Closed += HandleControlsClosed;
        }

        private void HandleControlsClosed()
        {
            if (HasInlineOptions)
            {
                ShowInlineAudio();
                QueueSelection(_controlsButton);
                ControlsClosed?.Invoke();
                return;
            }

            _root.SetActive(true);
            _actionsRoot.SetActive(true);
            QueueSelection(_controlsButton);
            ControlsClosed?.Invoke();
        }

        private bool ValidateReferences()
        {
            bool valid = _root != null
                && _titleText != null
                && _confirmationText != null
                && _actionsRoot != null
                && _confirmationRoot != null
                && _resumeButton != null
                && _controlsButton != null
                && (_optionsView == null || _panelRoot != null)
                && _confirmExitButton != null
                && _cancelExitButton != null;
            if (!valid)
            {
                Debug.LogError(
                    "PauseMenuUI possui referências obrigatórias ausentes. Configure a hierarquia pela cena ou prefab.",
                    this);
            }

            return valid;
        }

        private void QueueSelection(Selectable button)
        {
            StopSelectionRoutine();
            if (gameObject.activeInHierarchy)
            {
                _selectionRoutine = StartCoroutine(SelectNextFrame(button));
            }
        }

        private IEnumerator SelectNextFrame(Selectable button)
        {
            yield return null;
            _selectionRoutine = null;
            if (UnityEngine.EventSystems.EventSystem.current != null
                && button.interactable)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(button.gameObject);
            }
        }

        private void StopSelectionRoutine()
        {
            if (_selectionRoutine == null)
            {
                return;
            }

            StopCoroutine(_selectionRoutine);
            _selectionRoutine = null;
        }

        private static void SetButtonLabel(Button button, string label)
        {
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            if (text == null)
            {
                Debug.LogError($"O botão {button.name} precisa de um TMP_Text filho.", button);
                return;
            }

            text.text = label;
        }

        private static void SetButtonInteractable(Button button, bool interactable)
        {
            button.interactable = interactable;
        }
    }
}
