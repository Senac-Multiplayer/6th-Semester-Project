using System;
using System.Collections;
using PiGame.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class MainMenuUI : MonoBehaviour
    {
        [SerializeField] private GameObject _visuals;
        [SerializeField] private CanvasGroup _home;
        [Header("Botões — On Click disponível no Inspector para áudio local")]
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _optionsButton;
        [SerializeField] private Button _creditsButton;
        [SerializeField] private Button _charactersButton;
        [SerializeField] private Button _quitButton;
        [SerializeField] private GameObject _credits;
        [SerializeField] private Button _creditsBackButton;
        [Header("Ícones de voltar — catálogo já usado no lobby")]
        [SerializeField] private InputPromptCatalog _promptCatalog;
        [SerializeField] private Image[] _backIcons;

        private Button _returnButton;
        private Coroutine _focusRoutine;
        private bool _usesGamepad;

        public bool IsHomeVisible => _home.gameObject.activeInHierarchy;
        public bool IsSubpanelVisible => _credits.activeInHierarchy;
        public event Action PlayRequested;
        public event Action OptionsRequested;
        public event Action QuitRequested;
        public event Action CharactersRequested;

        private void Awake()
        {
            if (!_visuals || !_home || !_playButton || !_optionsButton
                || !_creditsButton || !_charactersButton || !_quitButton || !_credits || !_creditsBackButton
                || !_promptCatalog || _backIcons == null || _backIcons.Length == 0
                || Array.Exists(_backIcons, icon => !icon))
            {
                Debug.LogError("Configure as referências do MainMenuUI no prefab.", this);
                enabled = false;
                return;
            }

            // Áudio do menu: os Buttons já oferecem On Click no Inspector.
            // Seleção/hover: SelectablePointerFocus.OnPointerEnter é o ponto existente.
            // Tocar esses sons só localmente, nunca por RPC.
            _playButton.onClick.AddListener(Play);
            _optionsButton.onClick.AddListener(OpenOptions);
            _quitButton.onClick.AddListener(Quit);
            _creditsButton.onClick.AddListener(OpenCredits);
            _charactersButton.onClick.AddListener(OpenCharacters);
            _creditsBackButton.onClick.AddListener(ShowHome);
            _returnButton = _playButton;
            RefreshBackIcons();
        }

        private void Update()
        {
            if (!_visuals.activeInHierarchy) return;
            bool usesGamepad = _usesGamepad;
            if (Gamepad.current != null && (Gamepad.current.dpad.IsPressed()
                || Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.36f
                || Gamepad.current.buttonSouth.wasPressedThisFrame
                || Gamepad.current.buttonNorth.wasPressedThisFrame
                || Gamepad.current.buttonEast.wasPressedThisFrame)) usesGamepad = true;
            else if ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                || (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame
                    || Mouse.current.delta.ReadValue().sqrMagnitude > 4f))) usesGamepad = false;
            if (usesGamepad == _usesGamepad) return;
            _usesGamepad = usesGamepad;
            RefreshBackIcons();
        }

        private void RefreshBackIcons()
        {
            Sprite sprite = _promptCatalog.GetSprite(_usesGamepad ? "<Gamepad>/buttonEast" : "<Keyboard>/escape");
            foreach (Image icon in _backIcons) icon.sprite = sprite;
        }

        public void ShowHome()
        {
            _visuals.SetActive(true);
            _credits.SetActive(false);
            _home.gameObject.SetActive(true);
            SetInteractionEnabled(true);
            FocusDefaultButton();
        }

        public void ShowConnection()
        {
            _visuals.SetActive(true);
            _home.gameObject.SetActive(false);
            _credits.SetActive(false);
        }

        public void Hide()
        {
            ShowConnection();
            _visuals.SetActive(false);
        }

        public void SetInteractionEnabled(bool value)
        {
            _home.alpha = value ? 1f : 0f;
            _home.interactable = value;
            _home.blocksRaycasts = value;
        }

        public void FocusDefaultButton()
        {
            if (_focusRoutine != null) StopCoroutine(_focusRoutine);
            _focusRoutine = StartCoroutine(FocusHome());
        }

        private IEnumerator FocusHome()
        {
            yield return null;
            if (IsHomeVisible && _home.interactable)
                EventSystem.current.SetSelectedGameObject(_returnButton.gameObject);
            _focusRoutine = null;
        }

        public void HandleBack()
        {
            if (IsSubpanelVisible) { PiGame.Sound.UiAudioManager.PlayButton(); ShowHome(); }
        }

        private void Play()
        {
            _returnButton = _playButton;
            PlayRequested?.Invoke();
        }

        private void OpenOptions()
        {
            _returnButton = _optionsButton;
            OptionsRequested?.Invoke();
        }

        private void Quit()
        {
            _returnButton = _quitButton;
            QuitRequested?.Invoke();
        }

        private void OpenCredits()
        {
            _returnButton = _creditsButton;
            _home.gameObject.SetActive(false);
            _credits.SetActive(true);
            _credits.GetComponent<CreditsPanelUI>().FocusDefaultTeam();
        }

        private void OpenCharacters()
        {
            _returnButton = _charactersButton;
            CharactersRequested?.Invoke();
        }
    }
}
