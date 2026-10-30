using System.Threading.Tasks;
using PiGame.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PiGame.Gameplay
{
    public class NetcodeMatchDisconnectService : MonoBehaviour, IMatchDisconnectService
    {
        [SerializeField] private string _lobbySceneName = "scene_lobby";
        [SerializeField, Min(0.1f)] private float _shutdownWaitSeconds = 2f;

        private NetcodeLobbyConnectionService _connectionService;
        private bool _isReturning;

        private void OnEnable()
        {
            BindConnectionService();
        }

        private void Start()
        {
            BindConnectionService();
        }

        private void OnDisable()
        {
            UnbindConnectionService();
        }

        public async Task DisconnectAsync()
        {
            if (_isReturning)
            {
                return;
            }

            _isReturning = true;
            BindConnectionService();

            NetworkManager networkManager = NetworkManager.Singleton;
            if (_connectionService != null)
            {
                await _connectionService.ShutdownAsync();
            }
            else if (networkManager != null && networkManager.IsListening)
            {
                networkManager.Shutdown();
            }

            float timeoutAt = Time.realtimeSinceStartup + _shutdownWaitSeconds;
            while (networkManager != null
                && networkManager.IsListening
                && Time.realtimeSinceStartup < timeoutAt)
            {
                await Task.Yield();
            }

            ReturnToLobby();
        }

        private void BindConnectionService()
        {
            NetcodeLobbyConnectionService service = NetcodeLobbyConnectionService.Instance;
            if (_connectionService == service)
            {
                return;
            }

            UnbindConnectionService();
            _connectionService = service;
            if (_connectionService != null)
            {
                _connectionService.Disconnected += HandleDisconnected;
            }
        }

        private void UnbindConnectionService()
        {
            if (_connectionService == null)
            {
                return;
            }

            _connectionService.Disconnected -= HandleDisconnected;
            _connectionService = null;
        }

        private void HandleDisconnected()
        {
            if (_isReturning)
            {
                return;
            }

            _isReturning = true;
            ReturnToLobby();
        }

        private void ReturnToLobby()
        {
            if (string.IsNullOrWhiteSpace(_lobbySceneName))
            {
                Debug.LogError("Cena de retorno do pause não configurada.", this);
                _isReturning = false;
                return;
            }

            if (SceneManager.GetActiveScene().name != _lobbySceneName || SceneManager.sceneCount > 1)
            {
                SceneManager.LoadScene(_lobbySceneName, LoadSceneMode.Single);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _shutdownWaitSeconds = Mathf.Max(0.1f, _shutdownWaitSeconds);
        }
#endif
    }
}
