using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;


namespace PiGame.Lobby
{
    public class MatchLauncher : NetworkBehaviour
    {
        [SerializeField] private NetworkLobbyState _lobbyState;
        [SerializeField] private MatchSession _matchSession;



        public void OnEnable()
        {
            _lobbyState.MatchStartRequested += HandleMatchStartRequested;
        }
        public void OnDisable()
        {
            _lobbyState.MatchStartRequested -= HandleMatchStartRequested;
        }
        private void HandleMatchStartRequested(LobbyMapId winningMap)
        {
            if (!IsServer) return;

            MatchSnapshot snapshot = CreateMatchSnapshot(winningMap);

            MatchSession matchSession = MatchSession.Instance != null
                ? MatchSession.Instance
                : _matchSession;
            if (matchSession == null)
            {
                Debug.LogError("MatchSession não encontrada.");
                return;
            }

            matchSession.SetSnapshot(snapshot);


            LoadGameplayScene(winningMap);
        }

        private MatchSnapshot CreateMatchSnapshot(LobbyMapId winningMap)
        {
            LobbyMatchSettingsData settings = _lobbyState.MatchSettings;
            LobbyPlayerData[] players = new LobbyPlayerData[_lobbyState.PlayerCount];

            for(int i = 0; i < _lobbyState.PlayerCount; i++)
            {
                players[i] = _lobbyState.GetPlayer(i);
            }

            return new MatchSnapshot(winningMap, settings, players);
        }



        private void LoadGameplayScene(LobbyMapId lobbyMapId)
        {

            LobbyMapDefinition mapDefinition = _lobbyState.FindWinningMap(lobbyMapId);

            if(mapDefinition == null)
            {
                Debug.LogError($"Não foi encontrada uma definição para o mapa {lobbyMapId}");
                return;
            }

            LobbySceneFlow.Instance.LoadMapServer(mapDefinition.SceneName);
        }

    }
}

