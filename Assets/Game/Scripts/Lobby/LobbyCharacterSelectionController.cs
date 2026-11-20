using System.Collections.Generic;
using PiGame.UI;
using PiGame.Sound;
using UnityEngine;

namespace PiGame.Lobby
{
    public class LobbyCharacterSelectionController : MonoBehaviour
    {
        [SerializeField] private CharacterSelectionPanelUI _view;
        [SerializeField] private NetworkLobbyState _lobbyState;

        private ILobbyState _lobbyStateContract;

        private void Awake()
        {
            _lobbyStateContract = _lobbyState;
        }

        private void OnEnable()
        {
            _view.BrowseRequested += HandleBrowseRequested;
            _view.SubmitRequested += HandleSubmitRequested;
            _view.BackRequested += HandleBackRequested;

            if (_lobbyStateContract != null)
            {
                _lobbyStateContract.PlayersChanged += Refresh;
                _lobbyStateContract.CharacterReadyRejected += HandleCharacterReadyRejected;
                _lobbyStateContract.StageChanged += Refresh;
            }

            Refresh();
        }

        private void OnDisable()
        {
            _view.BrowseRequested -= HandleBrowseRequested;
            _view.SubmitRequested -= HandleSubmitRequested;
            _view.BackRequested -= HandleBackRequested;

            if (_lobbyStateContract != null)
            {
                _lobbyStateContract.PlayersChanged -= Refresh;
                _lobbyStateContract.CharacterReadyRejected -= HandleCharacterReadyRejected;
                _lobbyStateContract.StageChanged -= Refresh;
            }
        }

        public void SetInteractionEnabled(bool isEnabled)
        {
            _view.SetInteractionEnabled(isEnabled);
        }

        public void Focus() => _view.Focus();

        private void HandleBrowseRequested(int direction, LobbyInputDeviceKind inputDevice)
        {
            if (_lobbyStateContract == null
                || _lobbyStateContract.Stage != LobbyStage.CharacterSelection
                || !_lobbyStateContract.TryGetPlayer(_lobbyStateContract.LocalClientId, out LobbyPlayerData player)
                || player.IsReady)
            {
                return;
            }

            _lobbyStateContract.RequestInputDevice(inputDevice);
            _view.ClearCharacterBlockedFeedback();
            var next = FindNextCharacter(player, direction);
            _lobbyStateContract.RequestCharacterSelection(next);
            if (next != player.CharacterId) UiAudioManager.PlayCharacterBrowse();
        }

        private void HandleSubmitRequested(LobbyInputDeviceKind inputDevice)
        {
            if (_lobbyStateContract == null
                || _lobbyStateContract.Stage != LobbyStage.CharacterSelection
                || !_lobbyStateContract.TryGetPlayer(_lobbyStateContract.LocalClientId, out LobbyPlayerData player))
            {
                return;
            }

            _lobbyStateContract.RequestInputDevice(inputDevice);
            if (player.IsReady)
            {
                _lobbyStateContract.RequestReadyState(false);
                UiAudioManager.PlayButton();
                return;
            }

            if (player.CharacterId == LobbyCharacterId.None)
            {
                return;
            }

            if (_lobbyStateContract.TryGetCharacterLock(
                    player.CharacterId,
                    player.ClientId,
                    out int lockingPlayerSlot))
            {
                _view.ShowCharacterBlocked(lockingPlayerSlot);
                return;
            }

            _lobbyStateContract.RequestReadyState(true);
            UiAudioManager.PlayReadyBanner();
        }

        private void HandleBackRequested()
        {
            if (_lobbyStateContract == null
                || _lobbyStateContract.Stage != LobbyStage.CharacterSelection
                || !_lobbyStateContract.TryGetPlayer(
                    _lobbyStateContract.LocalClientId,
                    out LobbyPlayerData player)
                || !player.IsReady)
            {
                return;
            }

            _lobbyStateContract.RequestReadyState(false);
            UiAudioManager.PlayButton();
        }

        private LobbyCharacterId FindNextCharacter(LobbyPlayerData player, int direction)
        {
            int characterCount = _lobbyStateContract.SelectableCharacterCount;
            if (characterCount <= 0)
            {
                return LobbyCharacterId.None;
            }

            int step = direction >= 0 ? 1 : -1;
            int currentIndex = FindSelectableCharacterIndex(player.CharacterId);
            if (currentIndex < 0)
            {
                currentIndex = step > 0 ? -1 : 0;
            }

            int candidateIndex = (currentIndex + step + characterCount) % characterCount;
            return _lobbyStateContract.GetSelectableCharacter(candidateIndex);
        }

        private int FindSelectableCharacterIndex(LobbyCharacterId characterId)
        {
            for (int i = 0; i < _lobbyStateContract.SelectableCharacterCount; i++)
            {
                if (_lobbyStateContract.GetSelectableCharacter(i) == characterId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void HandleCharacterReadyRejected(int lockingPlayerSlot)
        {
            _view.ShowCharacterBlocked(lockingPlayerSlot);
        }

        private void Refresh()
        {
            if (_lobbyStateContract == null)
            {
                return;
            }

            List<LobbyPlayerData> snapshot = new(_lobbyStateContract.PlayerCount);
            for (int i = 0; i < _lobbyStateContract.PlayerCount; i++)
            {
                snapshot.Add(_lobbyStateContract.GetPlayer(i));
            }

            _view.Render(
                snapshot,
                _lobbyStateContract.LocalClientId,
                _lobbyStateContract.RequireUniqueCharacters);
        }
    }
}
