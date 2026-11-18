using System;
using System.Collections;
using PiGame.Input;
using PiGame.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PiGame.UI
{
    public sealed class LobbyPlayersPanelUI : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private Button _entryButton;
        [SerializeField] private Image _entryIcon;
        [SerializeField] private InputPromptCatalog _prompts;
        [SerializeField] private GameObject[] _rows;
        [SerializeField] private TMP_Text[] _labels;
        [SerializeField] private Button[] _kickButtons;
        [SerializeField] private TMP_Text _emptyText;
        [SerializeField] private Button _closeButton;
        [SerializeField] private ConfirmationPanelUI _confirmation;
        private readonly ulong[] _targets = new ulong[3];
        private NetworkLobbyState _state;
        private ulong? _pendingTarget;
        private bool _entryEnabled;
        private bool _gamepad;
        private int _backFrame = -1;
        private Coroutine _focusRoutine;
        public event Action Opened;
        public event Action Closed;
        public bool IsOpen => _root.activeSelf;
        public bool CanOpen => _entryEnabled && _entryButton.gameObject.activeInHierarchy
            && _state && _state.IsSpawned && _state.LocalClientIsHost
            && _state.Stage == LobbyStage.CharacterSelection;
        private void Awake() => _state = FindFirstObjectByType<NetworkLobbyState>(FindObjectsInactive.Include);
        private void OnEnable()
        {
            _entryButton.onClick.AddListener(Show); _closeButton.onClick.AddListener(Hide);
            _kickButtons[0].onClick.AddListener(KickFirst); _kickButtons[1].onClick.AddListener(KickSecond);
            _kickButtons[2].onClick.AddListener(KickThird);
            _confirmation.Confirmed += ConfirmKick; _confirmation.Canceled += CancelKick;
            if (_state) { _state.PlayersChanged += Refresh; _state.StageChanged += Refresh; }
            Refresh(); RefreshPrompt();
        }
        private void OnDisable()
        {
            _entryButton.onClick.RemoveListener(Show); _closeButton.onClick.RemoveListener(Hide);
            _kickButtons[0].onClick.RemoveListener(KickFirst); _kickButtons[1].onClick.RemoveListener(KickSecond);
            _kickButtons[2].onClick.RemoveListener(KickThird);
            _confirmation.Confirmed -= ConfirmKick; _confirmation.Canceled -= CancelKick;
            if (_state) { _state.PlayersChanged -= Refresh; _state.StageChanged -= Refresh; }
            StopFocus(); Hide();
        }
        private void Update()
        {
            bool previous = _gamepad;
            if (Gamepad.current != null && (Gamepad.current.leftShoulder.wasPressedThisFrame
                || Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.dpad.IsPressed()
                || Gamepad.current.leftStick.ReadValue().sqrMagnitude > .36f)) _gamepad = true;
            else if ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)) _gamepad = false;
            if (previous != _gamepad) RefreshPrompt();
        }
        private void RefreshPrompt() => _entryIcon.sprite = _prompts.GetSprite(_gamepad ? "<Gamepad>/leftShoulder" : "<Keyboard>/p");
        public void SetEntryEnabled(bool value) { _entryEnabled = value; _entryButton.interactable = value; RefreshEntry(); }
        private void RefreshEntry() => _entryButton.gameObject.SetActive(_state && _state.IsSpawned
            && _state.LocalClientIsHost && _state.Stage == LobbyStage.CharacterSelection && _entryEnabled && !IsOpen);
        public void Show()
        {
            if (!CanOpen || IsOpen) return;
            _root.SetActive(true); _pendingTarget = null; _confirmation.Hide();
            Opened?.Invoke(); Refresh(); FocusFirst();
        }
        public void Hide()
        {
            if (!IsOpen) return;
            StopFocus(); _confirmation.Hide(); _pendingTarget = null; _root.SetActive(false);
            Closed?.Invoke(); RefreshEntry();
        }
        public void HandleBack()
        {
            if (_backFrame == Time.frameCount) return;
            if (_pendingTarget.HasValue) CancelKick(); else Hide();
        }
        private void KickFirst() => AskKick(0);
        private void KickSecond() => AskKick(1);
        private void KickThird() => AskKick(2);
        private void AskKick(int index)
        {
            if (!IsOpen || _pendingTarget.HasValue || !_state.CanKickPlayer(_targets[index])
                || !_state.TryGetPlayer(_targets[index], out LobbyPlayerData player)) return;
            StopFocus(); _pendingTarget = player.ClientId; RefreshInteractable();
            _confirmation.Show($"EXPULSAR ESTE JOGADOR?\n{player.PlayerName}\n<size=24>O jogador será desconectado da sala.</size>", "EXPULSAR");
        }
        private void ConfirmKick()
        {
            if (!_pendingTarget.HasValue) return;
            ulong target = _pendingTarget.Value; _pendingTarget = null; _confirmation.Hide();
            _state.KickPlayer(target); Refresh(); FocusFirst();
        }
        private void CancelKick()
        {
            _backFrame = Time.frameCount; _pendingTarget = null; _confirmation.Hide(); RefreshInteractable(); FocusFirst();
        }
        public void Refresh()
        {
            RefreshEntry(); if (!IsOpen) return;
            if (!_state || !_state.IsSpawned || !_state.LocalClientIsHost
                || _state.Stage != LobbyStage.CharacterSelection) { Hide(); return; }
            if (_pendingTarget.HasValue && !_state.TryGetPlayer(_pendingTarget.Value, out _)) CancelKick();
            GameObject selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            int row = 0;
            for (int i = 0; i < _state.PlayerCount && row < _rows.Length; i++)
            {
                LobbyPlayerData player = _state.GetPlayer(i); if (player.ClientId == _state.LocalClientId) continue;
                _targets[row] = player.ClientId; _rows[row].SetActive(true);
                string team = LobbyTeams.IsValid(player.Team)
                    ? $"<color=#{LobbyTeams.Html(player.Team)}>EQUIPE {LobbyTeams.Name(player.Team)}</color>"
                    : player.IsReady ? "PRONTO" : "ESCOLHENDO";
                _labels[row].text = $"P{player.PlayerSlot + 1}  {player.PlayerName}\n<size=16>{team}</size>"; row++;
            }
            for (int i = row; i < _rows.Length; i++) _rows[i].SetActive(false);
            _emptyText.gameObject.SetActive(row == 0); RefreshInteractable(); ConfigureNavigation();
            if (!_pendingTarget.HasValue && (!selected || !selected.activeInHierarchy)) FocusFirst();
        }
        private void RefreshInteractable()
        {
            bool unlocked = !_pendingTarget.HasValue; _closeButton.interactable = unlocked;
            for (int i = 0; i < _kickButtons.Length; i++) _kickButtons[i].interactable = unlocked && _state && _state.CanKickPlayer(_targets[i]);
        }
        private void ConfigureNavigation()
        {
            Selectable previous = _closeButton;
            for (int i = 0; i < _kickButtons.Length; i++)
            {
                if (!_rows[i].activeSelf || !_kickButtons[i].interactable) continue;
                var nav = previous.navigation; nav.mode = Navigation.Mode.Explicit; nav.selectOnDown = _kickButtons[i]; previous.navigation = nav;
                nav = _kickButtons[i].navigation; nav.mode = Navigation.Mode.Explicit; nav.selectOnUp = previous;
                nav.selectOnLeft = nav.selectOnRight = null; _kickButtons[i].navigation = nav; previous = _kickButtons[i];
            }
            var last = previous.navigation; last.mode = Navigation.Mode.Explicit; last.selectOnDown = _closeButton; previous.navigation = last;
            var back = _closeButton.navigation; back.mode = Navigation.Mode.Explicit; back.selectOnUp = previous;
            back.selectOnLeft = back.selectOnRight = null; _closeButton.navigation = back;
        }
        private void FocusFirst() { StopFocus(); if (isActiveAndEnabled && IsOpen) _focusRoutine = StartCoroutine(FocusNextFrame()); }
        private IEnumerator FocusNextFrame()
        {
            yield return null;
            Button target = _closeButton;
            foreach (Button button in _kickButtons) if (button.gameObject.activeInHierarchy && button.interactable) { target = button; break; }
            if (EventSystem.current && !_pendingTarget.HasValue && IsOpen) EventSystem.current.SetSelectedGameObject(target.gameObject);
            _focusRoutine = null;
        }
        private void StopFocus() { if (_focusRoutine != null) StopCoroutine(_focusRoutine); _focusRoutine = null; }
    }
}
