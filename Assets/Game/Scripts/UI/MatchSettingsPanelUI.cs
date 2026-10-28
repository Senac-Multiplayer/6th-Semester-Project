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
        [SerializeField] private Button[] _durationButtons;

        [Header("Kills to win")]
        [SerializeField] private Button[] _killLimitButtons;

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

        private int _selectedDurationMinutes = DefaultDurationMinutes;
        private int _selectedKillLimit = 3;
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
            RefreshSelectionVisuals();
            RefreshSummary();
        }

        private void OnEnable()
        {
            _menuButton.onClick.AddListener(Show);
            _closeButton.onClick.AddListener(Hide);

            _durationButtons[0].onClick.AddListener(SelectTwoMinutes);
            _durationButtons[1].onClick.AddListener(SelectThreeMinutes);
            _durationButtons[2].onClick.AddListener(SelectFourMinutes);
            _durationButtons[3].onClick.AddListener(SelectFiveMinutes);

            _killLimitButtons[0].onClick.AddListener(SelectTwoKills);
            _killLimitButtons[1].onClick.AddListener(SelectThreeKills);
            _killLimitButtons[2].onClick.AddListener(SelectFourKills);
            _killLimitButtons[3].onClick.AddListener(SelectFiveKills);

            _modeButtons[1].onClick.AddListener(SelectSoloMode);
        }

        private void OnDisable()
        {
            _menuButton.onClick.RemoveListener(Show);
            _closeButton.onClick.RemoveListener(Hide);

            _durationButtons[0].onClick.RemoveListener(SelectTwoMinutes);
            _durationButtons[1].onClick.RemoveListener(SelectThreeMinutes);
            _durationButtons[2].onClick.RemoveListener(SelectFourMinutes);
            _durationButtons[3].onClick.RemoveListener(SelectFiveMinutes);

            _killLimitButtons[0].onClick.RemoveListener(SelectTwoKills);
            _killLimitButtons[1].onClick.RemoveListener(SelectThreeKills);
            _killLimitButtons[2].onClick.RemoveListener(SelectFourKills);
            _killLimitButtons[3].onClick.RemoveListener(SelectFiveKills);

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

            SettingsApplied?.Invoke(_selectedDurationMinutes, _selectedMode, _selectedKillLimit);
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

        private void SelectTwoMinutes()
        {
            SelectDuration(2);
        }

        private void SelectThreeMinutes()
        {
            SelectDuration(3);
        }

        private void SelectFourMinutes()
        {
            SelectDuration(4);
        }

        private void SelectFiveMinutes()
        {
            SelectDuration(5);
        }

        private void SelectSoloMode()
        {
            SelectMode(LobbyMatchMode.Solo);
        }

        private void SelectDuration(int durationMinutes)
        {
            _selectedDurationMinutes = durationMinutes;
            RefreshSelectionVisuals();
        }

        private void SelectMode(LobbyMatchMode mode)
        {
            _selectedMode = mode;
            RefreshSelectionVisuals();
        }

        private void SelectTwoKills() => SelectKillLimit(2);
        private void SelectThreeKills() => SelectKillLimit(3);
        private void SelectFourKills() => SelectKillLimit(4);
        private void SelectFiveKills() => SelectKillLimit(5);

        private void SelectKillLimit(int killLimit)
        {
            _selectedKillLimit = killLimit;
            RefreshSelectionVisuals();
        }

        private void RefreshSelectionVisuals()
        {
            for (int index = 0; index < _durationButtons.Length; index++)
            {
                bool isSelected = _selectedDurationMinutes == index + 2;
                ApplySelectionVisual(_durationButtons[index], isSelected);
            }

            for (int index = 0; index < _killLimitButtons.Length; index++)
            {
                ApplySelectionVisual(_killLimitButtons[index], _selectedKillLimit == index + 2);
            }

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
            int durationIndex = Mathf.Clamp(_selectedDurationMinutes - 2, 0, _durationButtons.Length - 1);
            EventSystem.current?.SetSelectedGameObject(_durationButtons[durationIndex].gameObject);
        }

        private void RefreshSummary()
        {
            if (_summaryText == null)
            {
                return;
            }

            string modeName = _confirmedSettings.Mode == LobbyMatchMode.Team ? "EQUIPE" : "SOLO";
            string repeatedCharacters = _requireUniqueCharacters ? "NÃO" : "SIM";
            _summaryText.text =
                $"PARTIDA: {_confirmedSettings.DurationMinutes} MIN  |  {modeName}\n"
                + $"VITÓRIA: {_confirmedSettings.KillLimit} KILLS\n"
                + $"MÍNIMO: {_minimumPlayers}  |  REPETIDOS: {repeatedCharacters}";
        }

        private void LoadDraftFromConfirmedSettings()
        {
            _selectedDurationMinutes = _confirmedSettings.DurationMinutes;
            _selectedKillLimit = _confirmedSettings.KillLimit;
            _selectedMode = _confirmedSettings.Mode;
        }
    }
}
