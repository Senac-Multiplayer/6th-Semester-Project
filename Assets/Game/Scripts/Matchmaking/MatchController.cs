using System;
using System.Collections;
using System.Collections.Generic;
using PiGame.Lobby;
using PiGame.Sound;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PiGame.Gameplay
{
    public class MatchController : NetworkBehaviour
    {
        [SerializeField] private int _killLimit = 3;
        [SerializeField] private float _respawnDelaySeconds = 2f;
        private bool _returningToLobby;

        private readonly NetworkVariable<float> _remainingTime =
            new NetworkVariable<float>();
        private readonly NetworkVariable<MatchPhase> _phase =
            new NetworkVariable<MatchPhase>(MatchPhase.WaitingForPlayers);
        private readonly NetworkVariable<int> _winningPlayerSlot =
            new NetworkVariable<int>(-1);

        private NetworkList<MatchPlayerScoreData> _scores;
        private readonly Dictionary<ulong, NetworkPlayerState> _players =
            new Dictionary<ulong, NetworkPlayerState>();
        private readonly Dictionary<ulong, Vector3> _spawnPositions =
            new Dictionary<ulong, Vector3>();

        public event Action ScoresChanged;
        public event Action PhaseChanged;

        public float RemainingTime => _remainingTime.Value;
        public MatchPhase Phase => _phase.Value;
        public int WinningPlayerSlot => _winningPlayerSlot.Value;
        public int KillLimit => _killLimit;
        public int ScoreCount => _scores != null ? _scores.Count : 0;

        private SceneOSTManager _sceneOSTManager;

        private void Awake()
        {
            _scores = new NetworkList<MatchPlayerScoreData>();
            _sceneOSTManager = FindFirstObjectByType<SceneOSTManager>();
        }

        public override void OnNetworkSpawn()
        {
            _scores.OnListChanged += HandleScoresChanged;
            _phase.OnValueChanged += HandlePhaseChanged;
            _winningPlayerSlot.OnValueChanged += HandleWinningPlayerChanged;

            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            }

            ScoresChanged?.Invoke();
            PhaseChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            _scores.OnListChanged -= HandleScoresChanged;
            _phase.OnValueChanged -= HandlePhaseChanged;
            _winningPlayerSlot.OnValueChanged -= HandleWinningPlayerChanged;

            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        public MatchPlayerScoreData GetScore(int index)
        {
            return _scores[index];
        }

        public void Initialize(LobbyMatchSettingsData settings)
        {
            if (!IsServer)
            {
                return;
            }

            int durationMinutes = Mathf.Max(1, settings.DurationMinutes);
            _remainingTime.Value = durationMinutes * 60f;
            _winningPlayerSlot.Value = -1;
            _phase.Value = MatchPhase.Playing;
        }

        public void RegisterPlayer(NetworkPlayerState player, Vector3 spawnPosition, LobbyPlayerData playerData)
        {
            if (!IsServer || player == null)
            {
                return;
            }

            _players[player.OwnerClientId] = player;
            _spawnPositions[player.OwnerClientId] = spawnPosition;
            player.Died += HandlePlayerDied;

            if (FindScoreIndex(player.OwnerClientId) < 0)
            {
                _scores.Add(new MatchPlayerScoreData(playerData));
            }
        }

        private void Update()
        {
            if (!IsServer || _phase.Value != MatchPhase.Playing)
            {
                return;
            }

            _remainingTime.Value = Mathf.Max(0f, _remainingTime.Value - Time.deltaTime);
            if (_remainingTime.Value <= 0f)
            {
                EndByHighestScore();
            }
        }

        private void HandlePlayerDied(NetworkPlayerState victim, ulong attackerClientId)
        {
            if (!IsServer || _phase.Value != MatchPhase.Playing)
            {
                return;
            }

            int attackerIndex = FindScoreIndex(attackerClientId);
            if (attackerIndex >= 0 && attackerClientId != victim.OwnerClientId)
            {
                MatchPlayerScoreData score = _scores[attackerIndex];
                score.Kills++;
                _scores[attackerIndex] = score;

                if (score.Kills >= _killLimit)
                {
                    FinishMatch(score.PlayerSlot);
                    return;
                }
            }

            StartCoroutine(RespawnAfterDelay(victim));
        }

        private IEnumerator RespawnAfterDelay(NetworkPlayerState player)
        {
            yield return new WaitForSeconds(_respawnDelaySeconds);

            if (_phase.Value != MatchPhase.Playing
                || player == null
                || !player.IsSpawned
                || !_spawnPositions.TryGetValue(player.OwnerClientId, out Vector3 spawnPosition))
            {
                yield break;
            }

            player.RespawnServer(spawnPosition);
        }

        private void EndByHighestScore()
        {
            int highestKills = -1;
            int winningSlot = -1;
            bool tied = false;

            for (int i = 0; i < _scores.Count; i++)
            {
                MatchPlayerScoreData score = _scores[i];
                if (score.Kills > highestKills)
                {
                    highestKills = score.Kills;
                    winningSlot = score.PlayerSlot;
                    tied = false;
                }
                else if (score.Kills == highestKills)
                {
                    tied = true;
                }
            }

            FinishMatch(tied ? -1 : winningSlot);
        }

        private void FinishMatch(int winningPlayerSlot)
        {
            if (_phase.Value != MatchPhase.Playing)
            {
                return;
            }

            _sceneOSTManager?.PlayVictoryOST();

            _winningPlayerSlot.Value = winningPlayerSlot;
            _phase.Value = MatchPhase.ShowingResult;

            foreach (NetworkPlayerState player in _players.Values)
            {
                if (player != null && player.IsSpawned)
                {
                    player.SetMatchActiveServer(false);
                }
            }
        }

        public bool ReturnToLobbyServer()
        {
            if (!IsServer || _phase.Value != MatchPhase.ShowingResult || _returningToLobby) return false;
            _returningToLobby = true;
            if (NetworkManager.SceneManager.LoadScene("scene_lobby", LoadSceneMode.Single)
                == SceneEventProgressStatus.Started) return true;

            _returningToLobby = false;
            Debug.LogError("Não foi possível retornar a sala ao lobby.", this);
            return false;
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            if (_players.TryGetValue(clientId, out NetworkPlayerState player)
                && player != null)
            {
                player.Died -= HandlePlayerDied;
            }

            _players.Remove(clientId);
            _spawnPositions.Remove(clientId);

            int scoreIndex = FindScoreIndex(clientId);
            // Depois do encerramento, a tela mantém o placar final mesmo se alguém sair.
            if (scoreIndex >= 0 && _phase.Value != MatchPhase.ShowingResult)
            {
                _scores.RemoveAt(scoreIndex);
            }

            TryFinishWhenOnePlayerRemains();
        }

        private void TryFinishWhenOnePlayerRemains()
        {
            if (_phase.Value != MatchPhase.Playing || _scores.Count != 1)
            {
                return;
            }

            FinishMatch(_scores[0].PlayerSlot);
        }

        private int FindScoreIndex(ulong clientId)
        {
            for (int i = 0; i < _scores.Count; i++)
            {
                if (_scores[i].ClientId == clientId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void HandleScoresChanged(NetworkListEvent<MatchPlayerScoreData> changeEvent)
        {
            ScoresChanged?.Invoke();
        }

        private void HandlePhaseChanged(MatchPhase previousValue, MatchPhase currentValue)
        {
            PhaseChanged?.Invoke();
        }

        private void HandleWinningPlayerChanged(int previousValue, int currentValue)
        {
            PhaseChanged?.Invoke();
        }
    }
}
