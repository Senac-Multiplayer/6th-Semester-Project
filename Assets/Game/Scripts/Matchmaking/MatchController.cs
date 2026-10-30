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
        private readonly NetworkVariable<int> _killLimit = new NetworkVariable<int>(3);
        [SerializeField] private float _respawnDelaySeconds = 2f;
        [Header("Apresentação da partida")]
        [Tooltip("Desative para mostrar a contagem somente depois de apresentar os jogadores.")]
        [SerializeField] private bool _countdownDuringPresentation = true;
        [Tooltip("Duração total da apresentação com contagem. Os dois segundos finais ficam reservados ao mapa inteiro.")]
        [SerializeField, Min(3f)] private float _presentationSeconds = 6f;
        [Tooltip("Tempo por jogador no modo de contagem após a apresentação.")]
        [SerializeField, Min(1f)] private float _secondsPerPlayer = 1f;
        [SerializeField, Min(0.1f)] private float _cameraReturnSeconds = 1f;
        private readonly NetworkVariable<double> _introStartsAt = new NetworkVariable<double>();
        private readonly NetworkVariable<int> _introPlayerCount = new NetworkVariable<int>();
        private readonly HashSet<ulong> _introReady = new HashSet<ulong>();
        [Header("Tela de resultado")]
        [Tooltip("Espera para assistir à subida dos pilares antes de liberar o retorno.")]
        [SerializeField, Min(0f)] private float _resultInputDelaySeconds = 3f;
        [SerializeField, Min(3f)] private float _resultDisplaySeconds = 15f;
        private bool _returningToLobby;
        private readonly HashSet<ulong> _returnedPlayers = new();
        private readonly NetworkVariable<double> _resultStartedAt = new();

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

        public bool CountdownDuringPresentation => _countdownDuringPresentation;
        public float SecondsPerPlayer => _countdownDuringPresentation
            ? (PresentationDuration - 2f) / Mathf.Max(1, _introPlayerCount.Value) : _secondsPerPlayer;
        public float CameraReturnSeconds => _cameraReturnSeconds;
        public float PresentationDuration => _countdownDuringPresentation
            ? Mathf.Max(3f, _presentationSeconds)
            : _introPlayerCount.Value * _secondsPerPlayer + _cameraReturnSeconds;
        public float IntroElapsed => _introStartsAt.Value <= 0d || !IsSpawned ? -1f
            : (float)(NetworkManager.ServerTime.Time - _introStartsAt.Value);
        public float RemainingTime => _remainingTime.Value;
        public MatchPhase Phase => _phase.Value;
        public int WinningPlayerSlot => _winningPlayerSlot.Value;
        public int KillLimit => _killLimit.Value;
        public int ScoreCount => _scores != null ? _scores.Count : 0;
        public bool CanReturnToLobby => IsSpawned && Phase == MatchPhase.ShowingResult
            && NetworkManager.ServerTime.Time >= _resultStartedAt.Value + _resultInputDelaySeconds;
        public float ResultSecondsRemaining => IsSpawned
            ? Mathf.Max(0f, (float)(_resultStartedAt.Value
                + Mathf.Max(_resultDisplaySeconds, _resultInputDelaySeconds) - NetworkManager.ServerTime.Time))
            : 0f;

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
            _killLimit.OnValueChanged += HandleKillLimitChanged;

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
            _killLimit.OnValueChanged -= HandleKillLimitChanged;

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

            _killLimit.Value = Mathf.Clamp(settings.KillLimit, 2, 100);
            // Valor negativo identifica partida sem limite de tempo também nos clients.
            _remainingTime.Value = settings.DurationMinutes == 0
                ? -1f : Mathf.Clamp(settings.DurationMinutes, 2, 10) * 60f;
            _winningPlayerSlot.Value = -1;
            _introReady.Clear();
            _introStartsAt.Value = 0d;
            _phase.Value = MatchPhase.WaitingForPlayers;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReadyForIntroRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (_phase.Value != MatchPhase.WaitingForPlayers || !_players.ContainsKey(sender)) return;
            _introReady.Add(sender);
            TryBeginPresentation();
        }

        private void TryBeginPresentation()
        {
            if (_phase.Value != MatchPhase.WaitingForPlayers || _players.Count == 0) return;
            foreach (ulong clientId in _players.Keys)
                if (!_introReady.Contains(clientId)) return;

            _introPlayerCount.Value = _players.Count;
            _introStartsAt.Value = NetworkManager.ServerTime.Time + 0.35d;
            _phase.Value = MatchPhase.PresentingPlayers;
        }

        private void SetPlayersActive(bool active, bool indicatorsVisible)
        {
            foreach (NetworkPlayerState player in _players.Values)
                if (player != null && player.IsSpawned)
                    player.SetMatchActiveServer(active, indicatorsVisible);
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
            player.SetMatchActiveServer(false, false);

            if (FindScoreIndex(player.OwnerClientId) < 0)
            {
                _scores.Add(new MatchPlayerScoreData(playerData));
            }
        }

        private void Update()
        {
            if (!IsServer) return;
            if (_phase.Value == MatchPhase.ShowingResult)
            {
                if (!_returningToLobby && ResultSecondsRemaining <= 0f) ReturnToLobbyServer();
                return;
            }
            if (_phase.Value == MatchPhase.PresentingPlayers && IntroElapsed >= PresentationDuration)
            {
                SetPlayersActive(_countdownDuringPresentation, true);
                _phase.Value = _countdownDuringPresentation ? MatchPhase.Playing : MatchPhase.Countdown;
            }
            if (_phase.Value == MatchPhase.Countdown && IntroElapsed >= PresentationDuration + 3f)
            {
                SetPlayersActive(true, true);
                _phase.Value = MatchPhase.Playing;
            }
            if (_phase.Value != MatchPhase.Playing || _remainingTime.Value < 0f) return;

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

                if (score.Kills >= _killLimit.Value)
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
            _resultStartedAt.Value = NetworkManager.ServerTime.Time;
            _phase.Value = MatchPhase.ShowingResult;
            _returnedPlayers.Clear();
            LobbySceneFlow.Instance?.PrepareNextMatchServer();

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
            if (!IsServer || !CanReturnToLobby || _returningToLobby) return false;
            _returningToLobby = true;
            if (LobbySceneFlow.Instance != null)
            {
                ShowLobbyRpc();
                LobbySceneFlow.Instance.UnloadMapServer();
                return true;
            }
            if (NetworkManager.SceneManager.LoadScene("scene_lobby", LoadSceneMode.Single)
                == SceneEventProgressStatus.Started) return true;

            _returningToLobby = false;
            Debug.LogError("Não foi possível retornar a sala ao lobby.", this);
            return false;
        }

        public bool RequestReturnToLobby()
        {
            if (!CanReturnToLobby) return false;
            if (LobbySceneFlow.Instance == null) return IsServer && ReturnToLobbyServer();
            ReturnPlayerToLobbyRpc();
            LobbySceneFlow.Instance.ShowLobbyLocally();
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ReturnPlayerToLobbyRpc(RpcParams rpcParams = default)
        {
            if (!CanReturnToLobby) return;
            _returnedPlayers.Add(rpcParams.Receive.SenderClientId);
            TryReturnEveryoneToLobby();
        }

        private void TryReturnEveryoneToLobby()
        {
            if (_phase.Value != MatchPhase.ShowingResult || _returningToLobby) return;
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                if (!_returnedPlayers.Contains(clientId)) return;
            ReturnToLobbyServer();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShowLobbyRpc()
        {
            LobbySceneFlow.Instance.ShowLobbyLocally();
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
            _introReady.Remove(clientId);
            _returnedPlayers.Remove(clientId);

            int scoreIndex = FindScoreIndex(clientId);
            // Depois do encerramento, a tela mantém o placar final mesmo se alguém sair.
            if (scoreIndex >= 0 && _phase.Value != MatchPhase.ShowingResult)
            {
                _scores.RemoveAt(scoreIndex);
            }

            TryBeginPresentation();
            TryFinishWhenOnePlayerRemains();
            TryReturnEveryoneToLobby();
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

        private void HandleKillLimitChanged(int previousValue, int currentValue)
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
