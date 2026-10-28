using System;
using PiGame.Lobby;
using PiGame.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class MatchSettingsPanelUI : MonoBehaviour
    {
        private const int DefaultDurationMinutes = 3;
        private const LobbyMatchMode DefaultMode = LobbyMatchMode.Solo;
        private const int DefaultMinimumPlayers = 2;

        [Header("Panels")]
        [SerializeField] private Button _menuButton;
        [SerializeField] private GameObject _overlay;
        [SerializeField] private Button _closeButton;

        [Header("Duration")]
        [SerializeField] private Slider _durationSlider;
        [SerializeField] private Text _durationValue;
        [SerializeField] private Button _decreaseDuration;
        [SerializeField] private Button _increaseDuration;

        [Header("Kills to win")]
        [SerializeField] private Slider _killLimitSlider;
        [SerializeField] private Text _killLimitValue;
        [SerializeField] private Button _decreaseKills;
        [SerializeField] private Button _increaseKills;

        [Header("Mode")]
        [SerializeField] private Button[] _modeButtons;

        [Header("Summary")]
        [SerializeField] private Text _summaryText;

        [Header("Settings shortcut")]
        [SerializeField] private Image _menuShortcutIcon;
        [SerializeField] private InputPromptCatalog _promptCatalog;

        [Header("Selection visual")]
        [SerializeField] private Color _normalColor = new Color(0.82f, 0.76f, 0.88f, 1f);
        [SerializeField] private Color _selectedColor = new Color(0.45f, 0.22f, 0.63f, 1f);
        [SerializeField] private Color _normalTextColor = new Color(0.14f, 0.08f, 0.18f, 1f);
        [SerializeField] private Color _selectedTextColor = Color.white;
        [SerializeField] private Color _unavailableColor = new Color(0.32f, 0.3f, 0.36f, 1f);
        [SerializeField] private Color _unavailableTextColor = new Color(0.68f, 0.66f, 0.72f, 1f);
        [SerializeField] private float _selectedScale = 1.15f;

        public event Action Opened;
        public event Action Closed;
        public event Action<int, LobbyMatchMode, int> SettingsApplied;

        public bool IsOpen => _overlay != null && _overlay.activeSelf;

        // A última posição do seletor (11) representa duração infinita, enviada como 0.
        private int SelectedDurationMinutes => _durationSlider.value > 10 ? 0 : (int)_durationSlider.value;
        private LobbyMatchMode _selectedMode = DefaultMode;
        private int _minimumPlayers = DefaultMinimumPlayers;
        private bool _requireUniqueCharacters;
        private LobbyMatchSettingsData _confirmedSettings;

        private void Awake()
        {
            _modeButtons[0].interactable = false;
            _confirmedSettings = new LobbyMatchSettingsData(
                DefaultDurationMinutes,
                DefaultMode,
                DefaultMinimumPlayers,
                false);
            _menuShortcutIcon.sprite = _promptCatalog.GetSprite("<Keyboard>/tab");
            LoadDraftFromConfirmedSettings();
            RefreshSelectionVisuals();
            RefreshSummary();
        }

        private void OnEnable()
        {
            _menuButton.onClick.AddListener(Show);
            _closeButton.onClick.AddListener(Hide);

            _decreaseDuration.onClick.AddListener(DecreaseDuration);
            _increaseDuration.onClick.AddListener(IncreaseDuration);
            _decreaseKills.onClick.AddListener(DecreaseKills);
            _increaseKills.onClick.AddListener(IncreaseKills);
            _durationSlider.onValueChanged.AddListener(RefreshValues);
            _killLimitSlider.onValueChanged.AddListener(RefreshValues);

            _modeButtons[1].onClick.AddListener(SelectSoloMode);
        }

        private void OnDisable()
        {
            _menuButton.onClick.RemoveListener(Show);
            _closeButton.onClick.RemoveListener(Hide);

            _decreaseDuration.onClick.RemoveListener(DecreaseDuration);
            _increaseDuration.onClick.RemoveListener(IncreaseDuration);
            _decreaseKills.onClick.RemoveListener(DecreaseKills);
            _increaseKills.onClick.RemoveListener(IncreaseKills);
            _durationSlider.onValueChanged.RemoveListener(RefreshValues);
            _killLimitSlider.onValueChanged.RemoveListener(RefreshValues);

            _modeButtons[1].onClick.RemoveListener(SelectSoloMode);
        }

        private void Update()
        {
            if (_menuButton.gameObject.activeInHierarchy)
            {
                if (Gamepad.current != null
                    && (Gamepad.current.buttonNorth.wasPressedThisFrame
                        || Gamepad.current.buttonSouth.wasPressedThisFrame
                        || Gamepad.current.buttonEast.wasPressedThisFrame
                        || Gamepad.current.dpad.IsPressed()
                        || Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.36f))
                {
                    _menuShortcutIcon.sprite = _promptCatalog.GetSprite("<Gamepad>/buttonNorth");
                }
                else if ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                    || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame))
                {
                    _menuShortcutIcon.sprite = _promptCatalog.GetSprite("<Keyboard>/tab");
                }
            }
        }

        public void Show()
        {
            if (IsOpen || !_menuButton.gameObject.activeInHierarchy)
            {
                return;
            }

            _overlay.SetActive(true);
            _menuButton.interactable = false;
            LoadDraftFromConfirmedSettings();
            RefreshSelectionVisuals();
            SelectCurrentOption();
            Opened?.Invoke();
        }

        public void Hide()
        {
            if (!IsOpen)
            {
                return;
            }

            SettingsApplied?.Invoke(SelectedDurationMinutes, _selectedMode, (int)_killLimitSlider.value);
            _overlay.SetActive(false);
            _menuButton.interactable = true;
            EventSystem.current?.SetSelectedGameObject(_menuButton.gameObject);
            Closed?.Invoke();
        }

        public void ResetView()
        {
            _overlay.SetActive(false);
            _menuButton.interactable = true;
            LoadDraftFromConfirmedSettings();
            RefreshSelectionVisuals();
            RefreshSummary();
        }

        public void Render(LobbyMatchSettingsData settings)
        {
            _confirmedSettings = settings;
            _minimumPlayers = settings.MinimumPlayers;
            _requireUniqueCharacters = settings.RequireUniqueCharacters;

            if (!IsOpen)
            {
                LoadDraftFromConfirmedSettings();
            }

            RefreshSelectionVisuals();
            RefreshSummary();
        }

        public void SetMenuVisible(bool isVisible)
        {
            if (!isVisible)
            {
                _overlay.SetActive(false);
            }

            _menuButton.gameObject.SetActive(isVisible);
        }

        private void SelectSoloMode()
        {
            SelectMode(LobbyMatchMode.Solo);
        }

        private void SelectMode(LobbyMatchMode mode)
        {
            _selectedMode = mode;
            RefreshSelectionVisuals();
        }

        private void DecreaseDuration() => StepValue(_durationSlider, -1);
        private void IncreaseDuration() => StepValue(_durationSlider, 1);
        private void DecreaseKills() => StepValue(_killLimitSlider, -1);
        private void IncreaseKills() => StepValue(_killLimitSlider, 1);

        private static void StepValue(Slider slider, int step)
        {
            slider.value += step;
            EventSystem.current?.SetSelectedGameObject(slider.gameObject);
        }

        private void RefreshValues(float value = 0)
        {
            _durationValue.text = SelectedDurationMinutes == 0 ? "INFINITO" : $"{SelectedDurationMinutes} MIN";
            _killLimitValue.text = ((int)_killLimitSlider.value).ToString();
        }

        private void RefreshSelectionVisuals()
        {
            RefreshValues();

            for (int index = 0; index < _modeButtons.Length; index++)
            {
                if (index == 0)
                {
                    Button unavailableButton = _modeButtons[index];
                    unavailableButton.targetGraphic.color = _unavailableColor;
                    unavailableButton.transform.localScale = Vector3.one;
                    Text unavailableLabel = unavailableButton.GetComponentInChildren<Text>(true);
                    unavailableLabel.color = _unavailableTextColor;
                    continue;
                }

                bool isSelected = (int)_selectedMode == index;
                ApplySelectionVisual(_modeButtons[index], isSelected);
            }
        }

        private void ApplySelectionVisual(Button button, bool isSelected)
        {
            button.targetGraphic.color = isSelected ? _selectedColor : _normalColor;
            button.transform.localScale = isSelected ? Vector3.one * _selectedScale : Vector3.one;

            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.color = isSelected ? _selectedTextColor : _normalTextColor;
            }
        }

        private void SelectCurrentOption()
        {
            EventSystem.current?.SetSelectedGameObject(_durationSlider.gameObject);
        }

        private void RefreshSummary()
        {
            if (_summaryText == null)
            {
                return;
            }

            string modeName = _confirmedSettings.Mode == LobbyMatchMode.Team ? "EQUIPE" : "SOLO";
            string repeatedCharacters = _requireUniqueCharacters ? "NÃO" : "SIM";
            string duration = _confirmedSettings.DurationMinutes == 0 ? "INFINITO" : $"{_confirmedSettings.DurationMinutes} MIN";
            _summaryText.text =
                $"PARTIDA: {duration}  |  {modeName}\n"
                + $"VITÓRIA: {_confirmedSettings.KillLimit} KILLS\n"
                + $"MÍNIMO: {_minimumPlayers}  |  REPETIDOS: {repeatedCharacters}";
        }

        private void LoadDraftFromConfirmedSettings()
        {
            _durationSlider.SetValueWithoutNotify(_confirmedSettings.DurationMinutes == 0 ? 11 : _confirmedSettings.DurationMinutes);
            _killLimitSlider.SetValueWithoutNotify(_confirmedSettings.KillLimit);
            _selectedMode = _confirmedSettings.Mode;
        }
    }
}
