using PiGame.Lobby;
using PiGame.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PiGame.UI
{
    public sealed class LobbyTeamsPanelUI : MonoBehaviour
    {
        [SerializeField] private CharacterSelectionPanelUI _selection;
        [SerializeField] private GameObject _controls;
        [SerializeField] private Button _blueButton;
        [SerializeField] private Button _orangeButton;
        [SerializeField] private Text _hint;
        [SerializeField] private Text[] _teamLabels;
        [SerializeField] private Text _modeLabel;
        [SerializeField] private Text _teamStatus;
        [SerializeField] private Image _leftIcon;
        [SerializeField] private Image _rightIcon;
        [SerializeField] private InputPromptCatalog _prompts;
        private NetworkLobbyState _state;
        private int _heldDirection;

        private void Awake()
        {
            _state = FindFirstObjectByType<NetworkLobbyState>(FindObjectsInactive.Include);
            RefreshIcons(false);
        }
        private void RefreshIcons(bool gamepad)
        {
            _leftIcon.sprite = _prompts.GetSprite(gamepad ? "<Gamepad>/dpad/left" : "<Keyboard>/leftArrow");
            _rightIcon.sprite = _prompts.GetSprite(gamepad ? "<Gamepad>/dpad/right" : "<Keyboard>/rightArrow");
        }
        private void OnEnable()
        {
            _blueButton.onClick.AddListener(SelectBlue);
            _orangeButton.onClick.AddListener(SelectOrange);
            if (_state)
            {
                _state.PlayersChanged += Refresh;
                _state.MatchSettingsChanged += Refresh;
                _state.StageChanged += Refresh;
            }
            Refresh();
        }
        private void OnDisable()
        {
            _blueButton.onClick.RemoveListener(SelectBlue);
            _orangeButton.onClick.RemoveListener(SelectOrange);
            if (_state)
            {
                _state.PlayersChanged -= Refresh;
                _state.MatchSettingsChanged -= Refresh;
                _state.StageChanged -= Refresh;
            }
            _heldDirection = 0;
        }
        private void Update()
        {
            if (!_state || !_controls.activeSelf || !_selection.InteractionEnabled) { _heldDirection = 0; return; }
            if (Gamepad.current != null && (Gamepad.current.dpad.IsPressed()
                || Gamepad.current.leftStick.ReadValue().sqrMagnitude > .36f
                || Gamepad.current.buttonSouth.wasPressedThisFrame)) RefreshIcons(true);
            else if ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)) RefreshIcons(false);
            int direction = 0;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.aKey.wasPressedThisFrame) direction = -1;
                else if (Keyboard.current.rightArrowKey.wasPressedThisFrame || Keyboard.current.dKey.wasPressedThisFrame) direction = 1;
            }
            if (direction != 0)
            {
                _state.RequestInputDevice(LobbyInputDeviceKind.Keyboard);
                Select(direction < 0 ? LobbyTeam.Blue : LobbyTeam.Orange);
                return;
            }
            if (Gamepad.current == null) { _heldDirection = 0; return; }
            float horizontal = Mathf.Abs(Gamepad.current.dpad.x.ReadValue()) > .5f
                ? Gamepad.current.dpad.x.ReadValue() : Gamepad.current.leftStick.x.ReadValue();
            direction = horizontal < -.55f ? -1 : horizontal > .55f ? 1 : 0;
            if (direction != 0 && direction != _heldDirection)
            {
                _state.RequestInputDevice(LobbyInputDeviceKind.Gamepad);
                Select(direction < 0 ? LobbyTeam.Blue : LobbyTeam.Orange);
            }
            _heldDirection = direction;
        }
        private void SelectBlue() => Select(LobbyTeam.Blue);
        private void SelectOrange() => Select(LobbyTeam.Orange);
        private void Select(LobbyTeam team)
        {
            if (!_state || !_selection.InteractionEnabled) return;
            if (_state.TryGetPlayer(_state.LocalClientId, out LobbyPlayerData me) && me.Team == team)
                team = LobbyTeam.None;
            if (LobbyTeams.IsValid(team) && Count(team) >= LobbyTeams.TeamSize) return;
            PiGame.Sound.UiAudioManager.PlayButton();
            _state.RequestTeam(team);
            _selection.Focus();
        }
        private int Count(LobbyTeam team)
        {
            int count = 0;
            for (int i = 0; i < _state.PlayerCount; i++) if (_state.GetPlayer(i).Team == team) count++;
            return count;
        }
        public void Refresh()
        {
            bool selecting = _state && _state.IsSpawned && _state.Stage == LobbyStage.CharacterSelection;
            _modeLabel.gameObject.SetActive(selecting);
            bool teamMode = selecting && _state.MatchSettings.Mode == LobbyMatchMode.Team;
            if (selecting) _modeLabel.text = teamMode ? "MODO: DUPLAS (2×2)" : "MODO: SOLO";
            _controls.SetActive(teamMode);
            foreach (Text label in _teamLabels) label.gameObject.SetActive(false);
            if (!teamMode) return;
            for (int i = 0; i < _state.PlayerCount; i++)
            {
                LobbyPlayerData player = _state.GetPlayer(i);
                if (player.PlayerSlot < 0 || player.PlayerSlot >= _teamLabels.Length) continue;
                Text label = _teamLabels[player.PlayerSlot];
                label.gameObject.SetActive(true);
                label.text = LobbyTeams.IsValid(player.Team) ? "EQUIPE " + LobbyTeams.Name(player.Team) : "ESCOLHA SUA EQUIPE";
                label.color = LobbyTeams.Color(player.Team);
            }
            LobbyTeam local = _state.TryGetPlayer(_state.LocalClientId, out LobbyPlayerData me) ? me.Team : LobbyTeam.None;
            UpdateButton(_blueButton, LobbyTeam.Blue, local);
            UpdateButton(_orangeButton, LobbyTeam.Orange, local);
            int missing = Mathf.Max(0, LobbyTeams.PlayerCount - _state.PlayerCount);
            if (missing > 0)
                _teamStatus.text = $"{_state.PlayerCount}/4 JOGADORES — AGUARDANDO MAIS {missing}";
            else if (Count(LobbyTeam.Blue) != LobbyTeams.TeamSize || Count(LobbyTeam.Orange) != LobbyTeams.TeamSize)
                _teamStatus.text = "FORME DUAS EQUIPES DE 2 JOGADORES";
            else _teamStatus.text = "4/4 JOGADORES";
            _hint.text = "TROCAR DE TIME | SAIR DO TIME";
        }
        private void UpdateButton(Button button, LobbyTeam team, LobbyTeam local)
        {
            int count = Count(team);
            button.interactable = count < LobbyTeams.TeamSize || local == team;
            button.image.overrideSprite = local == team ? button.spriteState.selectedSprite : null;
            Text label = button.GetComponentInChildren<Text>();
            label.text = $"{LobbyTeams.Name(team)} {count}/2";
            label.color = LobbyTeams.Color(team);
        }
    }
}
