using PiGame.UI;
using UnityEngine;

namespace PiGame.Lobby
{
    public class LobbyMatchSettingsController : MonoBehaviour
    {
        [SerializeField] private MatchSettingsPanelUI _view;
        [SerializeField] private NetworkLobbyState _lobbyState;

        private ILobbyState _lobbyStateContract;

        private void Awake()
        {
            _lobbyStateContract = _lobbyState;
        }

        private void OnEnable()
        {
            _view.SettingsApplied += HandleSettingsApplied;

            if (_lobbyStateContract != null)
            {
                _lobbyStateContract.MatchSettingsChanged += Refresh;
            }

            Refresh();
        }

        private void OnDisable()
        {
            _view.SettingsApplied -= HandleSettingsApplied;

            if (_lobbyStateContract != null)
            {
                _lobbyStateContract.MatchSettingsChanged -= Refresh;
            }
        }

        private void HandleSettingsApplied(int durationMinutes, LobbyMatchMode mode, int killLimit)
        {
            _lobbyStateContract?.RequestMatchSettings(durationMinutes, mode, killLimit);
        }

        private void Refresh()
        {
            if (_lobbyStateContract == null)
            {
                return;
            }

            _view.Render(_lobbyStateContract.MatchSettings);
        }
    }
}
