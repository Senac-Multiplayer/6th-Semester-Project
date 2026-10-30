using System.Collections;
using System.Collections.Generic;
using PiGame.Lobby;
using PiGame.Networking;
using Unity.Netcode;
using UnityEngine;

namespace PiGame.Gameplay
{
    public class MatchManager : NetworkBehaviour
    {
        [SerializeField] private Transform[] _spawnPoints;

        [Header("Characters")]
        [SerializeField] private List<LobbyCharacterDefinition> _characterDefinitions;
        [SerializeField] private MatchController _matchController;

        private MatchSnapshot _snapshot;

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                return;
            }

            // A reconexão do SDK pode reiniciar o host ainda na cena do mapa.
            // A sala recuperada volta ao lobby, sem recriar esta partida.
            if (NetcodeLobbyConnectionService.Instance != null
                && NetcodeLobbyConnectionService.Instance.IsRecoveringConnection) return;

            if (MatchSession.Instance == null || MatchSession.Instance.Snapshot == null)
            {
                Debug.LogError("MatchSnapshot não encontrado.");
                return;
            }

            _snapshot = MatchSession.Instance.Snapshot;
            StartCoroutine(StartMatchWhenReady());
        }

        private IEnumerator StartMatchWhenReady()
        {
            yield return new WaitUntil(() =>
                _matchController != null && _matchController.IsSpawned);

            _matchController.Initialize(_snapshot.MatchSettings);
            SpawnPlayers();
        }

        private void SpawnPlayers()
        {
            if (_snapshot.Players == null || _spawnPoints == null)
            {
                return;
            }

            List<int> availableSpawnPoints = new List<int>();
            for (int i = 0; i < _spawnPoints.Length; i++)
            {
                if (_spawnPoints[i] != null)
                {
                    availableSpawnPoints.Add(i);
                }
            }

            foreach (LobbyPlayerData playerData in _snapshot.Players)
            {
                if (availableSpawnPoints.Count == 0)
                {
                    Debug.LogWarning("Não há spawn points suficientes para todos os jogadores.");
                    return;
                }

                LobbyCharacterDefinition character = FindCharacter(playerData.CharacterId);
                if (character == null || character.PlayerPrefab == null)
                {
                    Debug.LogError($"Personagem {playerData.CharacterId} não encontrado.");
                    continue;
                }

                int randomIndex = Random.Range(0, availableSpawnPoints.Count);
                int spawnPointIndex = availableSpawnPoints[randomIndex];
                availableSpawnPoints.RemoveAt(randomIndex);

                Transform spawnPoint = _spawnPoints[spawnPointIndex];
                NetworkObject playerObject = Instantiate(
                    character.PlayerPrefab,
                    spawnPoint.position,
                    spawnPoint.rotation);

                playerObject.SpawnAsPlayerObject(playerData.ClientId, true);

                NetworkPlayerState playerState =
                    playerObject.GetComponent<NetworkPlayerState>();
                if (playerState == null)
                {
                    Debug.LogError(
                        $"O prefab de {playerData.CharacterId} precisa de NetworkPlayerState.");
                    playerObject.Despawn(true);
                    continue;
                }

                playerState.InitializeServer(
                    playerData,
                    PlayerSlotColors.Get(playerData.PlayerSlot));
                _matchController.RegisterPlayer(playerState, spawnPoint.position, playerData);
            }
        }

        private LobbyCharacterDefinition FindCharacter(LobbyCharacterId characterId)
        {
            foreach (LobbyCharacterDefinition character in _characterDefinitions)
            {
                if (character != null && character.Id == characterId)
                {
                    return character;
                }
            }

            return null;
        }
    }
}
