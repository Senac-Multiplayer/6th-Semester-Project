using System;
using System.Collections.Generic;
using PiGame.UI;
using UnityEngine;

namespace PiGame.Lobby
{
    public class LobbyMapVotingController : MonoBehaviour
    {
        [SerializeField] private MapVotingPanelUI _view;
        [SerializeField] private NetworkLobbyState _lobbyState;

        public event Action DisconnectRequested;
        public event Action<bool> VisibilityChanged;

        private ILobbyState _lobbyStateContract;
        private bool _isVisible;

        public bool IsVisible => _isVisible;

        private void Awake()
        {
            _lobbyStateContract = _lobbyState;
        }

        private void OnEnable()
        {
            _view.VoteRequested += HandleVoteRequested;
            _view.ConfirmRequested += HandleConfirmRequested;
            _view.BackRequested += HandleBackRequested;

            if (_lobbyStateContract != null)
            {
                _lobbyStateContract.PlayersChanged += Refresh;
                _lobbyStateContract.MapVotesChanged += Refresh;
                _lobbyStateContract.StageChanged += Refresh;
            }

            Refresh();
        }

        private void OnDisable()
        {
            _view.VoteRequested -= HandleVoteRequested;
            _view.ConfirmRequested -= HandleConfirmRequested;
            _view.BackRequested -= HandleBackRequested;

            if (_lobbyStateContract != null)
            {
                _lobbyStateContract.PlayersChanged -= Refresh;
                _lobbyStateContract.MapVotesChanged -= Refresh;
                _lobbyStateContract.StageChanged -= Refresh;
            }
        }

        private void Update()
        {
            if (_isVisible && _lobbyState.IsSpawned && _lobbyStateContract.Stage == LobbyStage.MatchStarting)
                _view.RenderDraw(_lobbyStateContract.ServerTime);
        }

        public void SetInteractionEnabled(bool isEnabled)
        {
            _view.SetInteractionEnabled(isEnabled && _isVisible);
        }

        private void HandleVoteRequested(LobbyMapId mapId, LobbyInputDeviceKind inputDevice)
        {
            if (_lobbyStateContract == null
                || _lobbyStateContract.Stage != LobbyStage.MapVoting
                || !_lobbyStateContract.TryGetMapVote(
                    _lobbyStateContract.LocalClientId,
                    out LobbyMapVoteData vote)
                || vote.IsConfirmed)
            {
                return;
            }

            _lobbyStateContract.RequestInputDevice(inputDevice);
            _lobbyStateContract.RequestMapVote(mapId);
        }

        private void HandleConfirmRequested(LobbyInputDeviceKind inputDevice)
        {
            if (_lobbyStateContract == null
                || _lobbyStateContract.Stage != LobbyStage.MapVoting
                || !_lobbyStateContract.TryGetMapVote(
                    _lobbyStateContract.LocalClientId,
                    out LobbyMapVoteData vote)
                || vote.IsConfirmed)
            {
                return;
            }

            _lobbyStateContract.RequestInputDevice(inputDevice);
            _lobbyStateContract.RequestMapVoteConfirmation(true);
        }

        private void HandleBackRequested()
        {
            if (_lobbyStateContract == null || _lobbyStateContract.Stage != LobbyStage.MapVoting)
            {
                return;
            }

            if (_lobbyStateContract.TryGetMapVote(
                    _lobbyStateContract.LocalClientId,
                    out LobbyMapVoteData vote)
                && vote.IsConfirmed)
            {
                _lobbyStateContract.RequestMapVoteConfirmation(false);
                return;
            }

            if (_lobbyStateContract.LocalClientIsHost)
            {
                _lobbyStateContract.RequestReturnToCharacterSelection();
                return;
            }

            DisconnectRequested?.Invoke();
        }

        private void Refresh()
        {
            if (_lobbyStateContract == null)
            {
                SetVisible(false);
                return;
            }

            bool shouldShow = _lobbyStateContract.Stage != LobbyStage.CharacterSelection;
            SetVisible(shouldShow);
            if (!shouldShow)
            {
                return;
            }

            List<LobbyPlayerData> players = new(_lobbyStateContract.PlayerCount);
            for (int i = 0; i < _lobbyStateContract.PlayerCount; i++)
            {
                players.Add(_lobbyStateContract.GetPlayer(i));
            }

            List<LobbyMapVoteData> votes = new(_lobbyStateContract.MapVoteCount);
            for (int i = 0; i < _lobbyStateContract.MapVoteCount; i++)
            {
                votes.Add(_lobbyStateContract.GetMapVote(i));
            }

            _view.Render(
                players,
                votes,
                _lobbyStateContract.LocalClientId,
                _lobbyStateContract.Stage,
                _lobbyStateContract.MapDraw,
                _lobbyStateContract.ServerTime);
        }

        private void SetVisible(bool isVisible)
        {
            _view.gameObject.SetActive(isVisible);
            if (_isVisible == isVisible)
            {
                return;
            }

            _isVisible = isVisible;
            VisibilityChanged?.Invoke(isVisible);
        }
    }
}
