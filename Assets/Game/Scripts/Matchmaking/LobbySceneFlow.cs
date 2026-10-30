using System.Collections;
using PiGame.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace PiGame.Lobby
{
    // O lobby permanece carregado. Cada jogador troca de tela sem sair da sala.
    public sealed class LobbySceneFlow : NetworkBehaviour
    {
        [SerializeField] private NetworkLobbyState _lobbyState;
        [SerializeField] private GameObject[] _lobbyPresentation;

        public static LobbySceneFlow Instance { get; private set; }
        private Scene _mapScene;
        private bool _unloading;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            NetworkManager.SceneManager.PostSynchronizationSceneUnloading = true;
            if (IsServer) NetworkManager.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
            NetworkManager.SceneManager.OnSceneEvent += HandleSceneEvent;
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.OnSceneEvent -= HandleSceneEvent;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public void LoadMapServer(string sceneName)
        {
            if (!IsServer) return;
            HideLobbyInputRpc();
            StartCoroutine(LoadMapWhenReady(sceneName));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HideLobbyInputRpc()
        {
            // O EventSystem do mapa será ativado ao carregar a cena aditiva.
            foreach (GameObject item in _lobbyPresentation)
                if (item.TryGetComponent<EventSystem>(out _)) item.SetActive(false);
        }

        private IEnumerator LoadMapWhenReady(string sceneName)
        {
            while (_unloading) yield return null;
            SceneEventProgressStatus status;
            do
            {
                status = NetworkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
                if (status == SceneEventProgressStatus.SceneEventInProgress) yield return null;
            } while (status == SceneEventProgressStatus.SceneEventInProgress);

            if (status != SceneEventProgressStatus.Started)
                Debug.LogError($"Não foi possível carregar o mapa {sceneName}: {status}.", this);
        }

        public void PrepareNextMatchServer()
        {
            _lobbyState.PrepareNextMatchServer();
        }

        public void ShowLobbyLocally()
        {
            SceneManager.SetActiveScene(gameObject.scene);
            if (_mapScene.IsValid() && _mapScene.isLoaded)
            {
                // Somente apresentação: managers e objetos de rede continuam ativos.
                foreach (GameObject root in _mapScene.GetRootGameObjects())
                {
                    foreach (Camera camera in root.GetComponentsInChildren<Camera>())
                        camera.gameObject.SetActive(false);
                    foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>())
                        canvas.gameObject.SetActive(false);
                    foreach (EventSystem events in root.GetComponentsInChildren<EventSystem>())
                        events.gameObject.SetActive(false);
                }
            }
            foreach (GameObject item in _lobbyPresentation) item.SetActive(true);
        }

        public void UnloadMapServer()
        {
            if (!IsServer || _unloading || !_mapScene.IsValid() || !_mapScene.isLoaded) return;
            _unloading = true;
            StartCoroutine(UnloadMapWhenReady());
        }

        private IEnumerator UnloadMapWhenReady()
        {
            SceneEventProgressStatus status;
            do
            {
                status = NetworkManager.SceneManager.UnloadScene(_mapScene);
                if (status == SceneEventProgressStatus.SceneEventInProgress) yield return null;
            } while (status == SceneEventProgressStatus.SceneEventInProgress);
            if (status != SceneEventProgressStatus.Started)
            {
                _unloading = false;
                Debug.LogError($"Não foi possível descarregar o mapa: {status}.", this);
            }
        }

        private void HandleSceneEvent(SceneEvent sceneEvent)
        {
            if (sceneEvent.SceneEventType == SceneEventType.UnloadEventCompleted)
            {
                _unloading = false;
                return;
            }
            if (sceneEvent.SceneEventType == SceneEventType.UnloadComplete
                && sceneEvent.ClientId == NetworkManager.LocalClientId)
            {
                ShowLobbyLocally();
                return;
            }
            if (sceneEvent.SceneEventType != SceneEventType.LoadComplete
                || sceneEvent.ClientId != NetworkManager.LocalClientId
                || sceneEvent.Scene == gameObject.scene) return;

            _mapScene = sceneEvent.Scene;
            if (!_mapScene.IsValid() || !_mapScene.isLoaded) return;
            if (NetcodeLobbyConnectionService.Instance != null
                && NetcodeLobbyConnectionService.Instance.IsRecoveringConnection) return;
            SceneManager.SetActiveScene(_mapScene);
            foreach (GameObject item in _lobbyPresentation) item.SetActive(false);
        }
    }
}
