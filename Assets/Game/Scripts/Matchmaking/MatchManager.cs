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
            _matchController.SetRespawnPoints(_spawnPoints);
            SpawnPlayers();
        }

        private void SpawnPlayers()
        {
            if (_snapshot.Players == null || _spawnPoints == null)
            {
                return;
            }

            var availableSpawnPoints = new List<Transform>(_spawnPoints.Length);
            foreach (Transform point in _spawnPoints)
                if (point) availableSpawnPoints.Add(point);

            foreach (LobbyPlayerData playerData in _snapshot.Players)
            {
                if (availableSpawnPoints.Count == 0)
                {
                    Debug.LogWarning("Não há spawn points suficientes para todos os jogadores.");
                    _matchController.AbortIntroServer();
                    return;
                }

                LobbyCharacterDefinition character = FindCharacter(playerData.CharacterId);
                if (character == null || character.PlayerPrefab == null)
                {
                    Debug.LogError($"Personagem {playerData.CharacterId} não encontrado.");
                    continue;
                }

                int index = Random.Range(0, availableSpawnPoints.Count);
                Transform spawnPoint = availableSpawnPoints[index];
                availableSpawnPoints.RemoveAt(index);
                Vector3 spawnPosition = spawnPoint.position;
                NetworkObject playerObject = Instantiate(
                    character.PlayerPrefab,
                    spawnPosition,
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
                _matchController.RegisterPlayer(playerState, spawnPosition, playerData);
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
