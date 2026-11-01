using System.Collections;
using PiGame.Gameplay;
using PiGame.Lobby;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PiGame.Testing
{
    [DisallowMultipleComponent]
    public sealed class MapPlaytest : MonoBehaviour
    {
        [Header("Teste direto no Editor")]
        [Tooltip("Ative antes de dar Play diretamente neste mapa. Não altera partidas iniciadas pelo lobby.")]
        [SerializeField] private bool _enableDirectPlay;
        [SerializeField] private bool _invulnerable = true;
        [SerializeField, Min(1)] private int _initialSpawn = 1;
        [Tooltip("Escolha um data_character já cadastrado no MatchManager deste mapa.")]
        [SerializeField] private LobbyCharacterDefinition _character;

        [Header("Referências da cena e do prefab")]
        [SerializeField] private LobbyMapDefinition _map;
        [SerializeField] private Transform _spawnPoints;
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private MatchSession _session;

#if UNITY_EDITOR
        private NetworkPlayerState _player;
        private int _spawnIndex;
        private bool _testing;

        private IEnumerator Start()
        {
            if (!_enableDirectPlay || NetworkManager.Singleton != null
                || (MatchSession.Instance != null && MatchSession.Instance.Snapshot != null))
            {
                yield break;
            }

            if (!CompareTag("EditorOnly") || !_map || !_character || !_spawnPoints || !_networkManager || !_session
                || _spawnPoints.childCount == 0 || _networkManager.gameObject.activeSelf)
            {
                Debug.LogError("MapPlaytest precisa da tag EditorOnly, Character, Map, Spawn Points e contexto de rede inativo configurados no Inspector.", this);
                enabled = false;
                yield break;
            }

            // Contexto já serializado na raiz da cena, inativo até iniciar este teste.
            _networkManager.gameObject.SetActive(true);
            var device = Gamepad.current != null ? LobbyInputDeviceKind.Gamepad : LobbyInputDeviceKind.Keyboard;
            _session.SetSnapshot(new MatchSnapshot(_map.Id,
                new LobbyMatchSettingsData(60, LobbyMatchMode.Solo, 1, false),
                new[] { new LobbyPlayerData(NetworkManager.ServerClientId, 0, _character.Id, device, false, "Teste") }));
            _networkManager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", 17863, "127.0.0.1");
            if (!_networkManager.StartHost())
            {
                Debug.LogError("Não foi possível iniciar o host local de teste na porta 17863.", this);
                enabled = false;
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 10f;
            while ((_networkManager.LocalClient == null || _networkManager.LocalClient.PlayerObject == null)
                && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            var playerObject = _networkManager.LocalClient?.PlayerObject;
            if (playerObject == null || !playerObject.TryGetComponent(out _player))
            {
                Debug.LogError($"O MatchManager não criou {_character.DisplayName}. Confira as definições de personagens e os spawns deste mapa.", this);
                _networkManager.Shutdown();
                enabled = false;
                yield break;
            }

            yield return new WaitUntil(() => _player.CanAct);
            _spawnIndex = Mathf.Clamp(_initialSpawn - 1, 0, _spawnPoints.childCount - 1);
            _testing = true;
            ResetSpawn();
            Debug.Log("[Teste de mapa] Host local sem Relay. F6: próximo spawn; F7: reiniciar no spawn; F8: invencibilidade. Pare o Play para encerrar.", this);
        }

        private void Update()
        {
            if (!_testing || !_player || !_player.IsSpawned) return;
            if (_player.IsInvulnerable != _invulnerable) _player.SetInvulnerableServer(_invulnerable);
            var keyboard = Keyboard.current;
            if (keyboard == null || Time.timeScale == 0f) return;
            if (keyboard.f6Key.wasPressedThisFrame) NextSpawn();
            if (keyboard.f7Key.wasPressedThisFrame) ResetSpawn();
            if (keyboard.f8Key.wasPressedThisFrame) ToggleInvulnerability();
        }

        [ContextMenu("Teste/Próximo spawn F6")]
        private void NextSpawn()
        {
            if (!_testing) return;
            _spawnIndex = (_spawnIndex + 1) % _spawnPoints.childCount;
            ResetSpawn();
        }

        [ContextMenu("Teste/Reiniciar no spawn F7")]
        private void ResetSpawn()
        {
            if (!_testing) return;
            Transform spawn = _spawnPoints.GetChild(_spawnIndex);
            _player.RespawnServer(spawn.position);
            _player.GetComponent<Rigidbody2D>().angularVelocity = 0f;
            _player.SetInvulnerableServer(_invulnerable);
            Debug.Log($"[Teste de mapa] Spawn {_spawnIndex + 1}/{_spawnPoints.childCount}: {spawn.name}. Invencível: {_invulnerable}.", this);
        }

        [ContextMenu("Teste/Alternar invencibilidade F8")]
        private void ToggleInvulnerability()
        {
            if (!_testing) return;
            _invulnerable = !_invulnerable;
            _player.SetInvulnerableServer(_invulnerable);
            Debug.Log($"[Teste de mapa] Invencível: {_invulnerable}.", this);
        }
#endif
    }
}
