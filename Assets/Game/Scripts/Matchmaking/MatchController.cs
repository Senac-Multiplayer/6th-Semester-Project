using System;
using System.Collections;
using System.Collections.Generic;
using PiGame.Lobby;
using PiGame.Sound;
using PiGame.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.SceneManagement;

namespace PiGame.Gameplay
{
    public class MatchController : NetworkBehaviour
    {
        private readonly NetworkVariable<int> _killLimit = new NetworkVariable<int>(3);
        [SerializeField] private float _respawnDelaySeconds = 2f;
        [SerializeField, Min(0f)] private float _respawnPlayerClearance = 2.5f;
        [SerializeField, Min(0f)] private float _respawnClearanceWaitSeconds = 1f;
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
        [SerializeField, Min(10f)] private float _spawnPreparationTimeoutSeconds = 45f;
        private float _preparationStartedAt;
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

        private readonly NetworkVariable<LobbyMatchMode> _mode = new(LobbyMatchMode.Solo);
        private readonly NetworkVariable<LobbyTeam> _winningTeam = new(LobbyTeam.None);
        private readonly NetworkVariable<int> _blueKills = new();
        private readonly NetworkVariable<int> _orangeKills = new();
        public bool IsTeamMode => _mode.Value == LobbyMatchMode.Team;
        public LobbyTeam WinningTeam => _winningTeam.Value;
        public int GetTeamScore(LobbyTeam team) => team == LobbyTeam.Blue ? _blueKills.Value
            : team == LobbyTeam.Orange ? _orangeKills.Value : 0;
        public bool IsWinner(MatchPlayerScoreData player) => IsTeamMode
            ? LobbyTeams.IsValid(WinningTeam) && player.Team == WinningTeam : player.PlayerSlot == WinningPlayerSlot;
        public bool IsDraw => IsTeamMode ? !LobbyTeams.IsValid(WinningTeam) : WinningPlayerSlot < 0;
        public bool AreTeammates(ulong first, ulong second)
        {
            if (!IsTeamMode || first == second) return false;
            int a = FindScoreIndex(first), b = FindScoreIndex(second);
            return a >= 0 && b >= 0 && LobbyTeams.IsValid(_scores[a].Team) && _scores[a].Team == _scores[b].Team;
        }

        private NetworkList<MatchPlayerScoreData> _scores;
        private readonly Dictionary<ulong, NetworkPlayerState> _players =
            new Dictionary<ulong, NetworkPlayerState>();
        private readonly Dictionary<ulong, Vector3> _spawnPositions =
            new Dictionary<ulong, Vector3>();
        private Transform[] _respawnPoints = Array.Empty<Transform>();
        private readonly List<Vector3> _safeRespawnPositions = new(4);
        private readonly List<Rect> _occupiedRespawnAreas = new(4);
        private readonly List<Rect> _occupiedPlayerBodies = new(4);
        private float _nextArenaCheckAt;
        public Rect? SpawnArea { get; private set; }

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
        public float ResultElapsed => IsSpawned && Phase == MatchPhase.ShowingResult
            ? Mathf.Max(0f, (float)(NetworkManager.ServerTime.Time - _resultStartedAt.Value)) : 0f;
        public float ResultSecondsRemaining => IsSpawned
            ? Mathf.Max(0f, (float)(_resultStartedAt.Value
                + Mathf.Max(_resultDisplaySeconds, _resultInputDelaySeconds) - NetworkManager.ServerTime.Time))
            : 0f;


        private void Awake()
        {
            _scores = new NetworkList<MatchPlayerScoreData>();
        }

        public override void OnNetworkSpawn()
        {
            _scores.OnListChanged += HandleScoresChanged;
            _blueKills.OnValueChanged += HandleKillLimitChanged;
            _orangeKills.OnValueChanged += HandleKillLimitChanged;
            _mode.OnValueChanged += HandleModeChanged;
            _winningTeam.OnValueChanged += HandleWinningTeamChanged;
            _phase.OnValueChanged += HandlePhaseChanged;
            _winningPlayerSlot.OnValueChanged += HandleWinningPlayerChanged;
            _killLimit.OnValueChanged += HandleKillLimitChanged;

            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            }

            ScoresChanged?.Invoke();
            HandlePhaseChanged(_phase.Value, _phase.Value);
        }

        public override void OnNetworkDespawn()
        {
            _scores.OnListChanged -= HandleScoresChanged;
            _blueKills.OnValueChanged -= HandleKillLimitChanged;
            _orangeKills.OnValueChanged -= HandleKillLimitChanged;
            _mode.OnValueChanged -= HandleModeChanged;
            _winningTeam.OnValueChanged -= HandleWinningTeamChanged;
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

            _mode.Value = settings.Mode;
            _winningTeam.Value = LobbyTeam.None;
            _blueKills.Value = 0;
            _orangeKills.Value = 0;
            _killLimit.Value = Mathf.Clamp(settings.KillLimit, 2, 100);
            // Valor negativo identifica partida sem limite de tempo também nos clients.
            _remainingTime.Value = settings.DurationMinutes == 0
                ? -1f : Mathf.Clamp(settings.DurationMinutes, 2, 10) * 60f;
            _winningPlayerSlot.Value = -1;
            _introReady.Clear();
            _introStartsAt.Value = 0d;
            _phase.Value = MatchPhase.WaitingForPlayers;
            _preparationStartedAt = Time.unscaledTime;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReadyForIntroRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (_returningToLobby || _phase.Value != MatchPhase.WaitingForPlayers || !_players.ContainsKey(sender)) return;
            _introReady.Add(sender);
            TryBeginPresentation();
        }

        private void TryBeginPresentation()
        {
            if (_returningToLobby || _phase.Value != MatchPhase.WaitingForPlayers || _players.Count == 0) return;
            foreach (ulong clientId in _players.Keys)
                if (!_introReady.Contains(clientId) || !_players[clientId].IsInitialSpawnPrepared) return;

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

        internal void SetRespawnPoints(Transform[] points)
        {
            _respawnPoints = points ?? Array.Empty<Transform>();
            SpawnArea = ReadArenaBounds();
        }

        private Rect? ReadArenaBounds()
        {
            int terrain = LayerMask.GetMask("Ground", "Wall", "Roof");
            int outerTilesLayer = LayerMask.NameToLayer("Out of Bounds");
            Bounds? bounds = null;
            Physics2D.SyncTransforms();
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (Collider2D surface in root.GetComponentsInChildren<Collider2D>())
                {
                    if (!surface.enabled || surface.isTrigger || (terrain & (1 << surface.gameObject.layer)) == 0
                        || (surface.usedByEffector && surface.GetComponent<PlatformEffector2D>())
                        || (surface.attachedRigidbody && surface.attachedRigidbody.bodyType != RigidbodyType2D.Static))
                        continue;

                    Bounds combined = bounds ?? surface.bounds;
                    combined.Encapsulate(surface.bounds);
                    bounds = combined;
                }

                // The external tilemap describes the edge after decorative tiles are split from the terrain.
                foreach (TilemapRenderer tiles in root.GetComponentsInChildren<TilemapRenderer>())
                {
                    if (tiles.gameObject.layer != outerTilesLayer) continue;

                    Bounds combined = bounds ?? tiles.bounds;
                    combined.Encapsulate(tiles.bounds);
                    bounds = combined;
                }
            }

            if (!bounds.HasValue || bounds.Value.size.x <= 0f || bounds.Value.size.y <= 0f) return null;
            Bounds arena = bounds.Value;
            return Rect.MinMaxRect(arena.min.x, arena.min.y, arena.max.x, arena.max.y);
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
            if (_phase.Value == MatchPhase.WaitingForPlayers && !_returningToLobby)
            {
                // A confirmação por RPC e a variável de preparo podem chegar em ticks diferentes.
                TryBeginPresentation();
                if (_phase.Value == MatchPhase.WaitingForPlayers && _players.Count > 0
                    && Time.unscaledTime - _preparationStartedAt >= _spawnPreparationTimeoutSeconds)
                    AbortIntroServer();
            }
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
            if (_phase.Value != MatchPhase.Playing) return;
            RecoverPlayersOutsideArena();
            if (_remainingTime.Value < 0f) return;

            _remainingTime.Value = Mathf.Max(0f, _remainingTime.Value - Time.deltaTime);
            if (_remainingTime.Value <= 0f)
            {
                EndByHighestScore();
            }
        }

        private void RecoverPlayersOutsideArena()
        {
            if (!SpawnArea.HasValue || Time.unscaledTime < _nextArenaCheckAt) return;
            _nextArenaCheckAt = Time.unscaledTime + 0.2f;

            const float sideAndFloorMargin = 1f;
            const float overheadMargin = 6f;
            Rect arena = SpawnArea.Value;
            foreach (NetworkPlayerState player in _players.Values)
            {
                if (!player || !player.IsSpawned || !player.CanAct) continue;

                Vector2 position = player.transform.position;
                if (position.x >= arena.xMin - sideAndFloorMargin && position.x <= arena.xMax + sideAndFloorMargin
                    && position.y >= arena.yMin - sideAndFloorMargin && position.y <= arena.yMax + overheadMargin)
                    continue;

                player.HandleArenaExitServer();
            }
        }

        private void HandlePlayerDied(NetworkPlayerState victim, ulong attackerClientId)
        {
            if (!IsServer || _phase.Value != MatchPhase.Playing)
            {
                return;
            }

            int attackerIndex = FindScoreIndex(attackerClientId);
            if (attackerIndex >= 0 && attackerClientId != victim.OwnerClientId
                && !AreTeammates(attackerClientId, victim.OwnerClientId))
            {
                MatchPlayerScoreData score = _scores[attackerIndex];
                score.Kills++;
                _scores[attackerIndex] = score;

                if (IsTeamMode)
                {
                    if (score.Team == LobbyTeam.Blue) _blueKills.Value++;
                    else if (score.Team == LobbyTeam.Orange) _orangeKills.Value++;
                    if (LobbyTeams.IsValid(score.Team) && GetTeamScore(score.Team) >= _killLimit.Value)
                    {
                        FinishTeamMatch(score.Team);
                        return;
                    }
                }
                else if (score.Kills >= _killLimit.Value)
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

            float fallbackAt = Time.time + _respawnClearanceWaitSeconds;
            var retryDelay = new WaitForSeconds(0.2f);
            while (_phase.Value == MatchPhase.Playing && player && player.IsSpawned && !player.IsAlive)
            {
                bool foundClearPosition = TryPickRespawnPosition(player, out Vector3 position);
                if (foundClearPosition || (Time.time >= fallbackAt && TryReusePreviousRespawn(player, out position)))
                {
                    _spawnPositions[player.OwnerClientId] = position;
                    player.RespawnServer(position);
                    yield break;
                }
                yield return retryDelay;
            }
        }

        private bool TryPickRespawnPosition(NetworkPlayerState player, out Vector3 position)
        {
            position = default;
            var body = player.StandingSpawnBody;
            if (!body.Collider) return false;

            Physics2D.SyncTransforms();
            _occupiedRespawnAreas.Clear();
            _occupiedPlayerBodies.Clear();
            Vector2 margin = Vector2.one * _respawnPlayerClearance;
            foreach (NetworkPlayerState other in _players.Values)
            {
                if (!other || other == player || !other.IsSpawned || !other.IsAlive) continue;

                Rect occupied = other.StandingSpawnBody.BoundsAt(other.PositionForSpawnClearance);
                _occupiedPlayerBodies.Add(occupied);
                _occupiedRespawnAreas.Add(new Rect(occupied.position - margin, occupied.size + margin * 2f));
            }

            _safeRespawnPositions.Clear();
            foreach (Transform point in _respawnPoints)
            {
                if (!point) continue;
                if (HasRespawnClearance(body.BoundsAt(point.position), _occupiedRespawnAreas))
                    _safeRespawnPositions.Add(point.position);
            }

            bool hasPrevious = _spawnPositions.TryGetValue(player.OwnerClientId, out Vector3 previous);
            if (_safeRespawnPositions.Count == 0)
            {
                if (!hasPrevious || !HasRespawnClearance(body.BoundsAt(previous), _occupiedRespawnAreas))
                    return false;
                position = previous;
                return true;
            }

            if (hasPrevious && _safeRespawnPositions.Exists(candidate => (candidate - previous).sqrMagnitude >= 0.04f))
                _safeRespawnPositions.RemoveAll(candidate => (candidate - previous).sqrMagnitude < 0.04f);
            position = _safeRespawnPositions[UnityEngine.Random.Range(0, _safeRespawnPositions.Count)];
            return true;
        }

        private bool TryReusePreviousRespawn(NetworkPlayerState player, out Vector3 position)
        {
            position = default;
            if (!_spawnPositions.TryGetValue(player.OwnerClientId, out Vector3 previous)) return false;

            var body = player.StandingSpawnBody;
            if (!body.Collider) return false;

            if (!HasRespawnClearance(body.BoundsAt(previous), _occupiedPlayerBodies)) return false;
            position = previous;
            return true;
        }

        private static bool HasRespawnClearance(Rect position, List<Rect> occupied)
        {
            foreach (Rect area in occupied)
                if (position.Overlaps(area)) return false;
            return true;
        }

        private void EndByHighestScore()
        {
            if (IsTeamMode)
            {
                FinishTeamMatch(_blueKills.Value == _orangeKills.Value ? LobbyTeam.None
                    : _blueKills.Value > _orangeKills.Value ? LobbyTeam.Blue : LobbyTeam.Orange);
                return;
            }
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

        private void FinishTeamMatch(LobbyTeam team)
        {
            if (_phase.Value != MatchPhase.Playing) return;
            _winningTeam.Value = team;
            FinishMatch(-1);
        }

        private void FinishMatch(int winningPlayerSlot)
        {
            if (_phase.Value != MatchPhase.Playing)
            {
                return;
            }

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

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AbortIntroRpc(RpcParams rpcParams = default)
        {
            if (_players.ContainsKey(rpcParams.Receive.SenderClientId)) AbortIntroServer();
        }

        internal void AbortIntroServer()
        {
            if (_returningToLobby || _phase.Value != MatchPhase.WaitingForPlayers) return;
            _returningToLobby = true;
            Debug.LogWarning("A preparação do spawn falhou. Retornando a sala ao lobby.", this);
            SetPlayersActive(false, false);
            if (LobbySceneFlow.Instance == null)
            {
                NetworkManager.Shutdown();
                return;
            }
            LobbySceneFlow.Instance.PrepareNextMatchServer();
            ShowLobbyRpc();
            foreach (NetworkPlayerState player in _players.Values)
                if (player != null && player.IsSpawned) player.NetworkObject.Despawn(true);
            LobbySceneFlow.Instance.UnloadMapServer();
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
            if (scoreIndex >= 0 && _phase.Value != MatchPhase.ShowingResult && !IsTeamMode)
            {
                _scores.RemoveAt(scoreIndex);
            }

            TryBeginPresentation();
            TryFinishWhenOnePlayerRemains();
            TryReturnEveryoneToLobby();
        }

        private void TryFinishWhenOnePlayerRemains()
        {
            if (_phase.Value == MatchPhase.Playing && IsTeamMode)
            {
                bool blue = false, orange = false;
                for (int i = 0; i < _scores.Count; i++)
                {
                    MatchPlayerScoreData score = _scores[i];
                    if (!_players.ContainsKey(score.ClientId)) continue;
                    blue |= score.Team == LobbyTeam.Blue;
                    orange |= score.Team == LobbyTeam.Orange;
                }
                if (!blue || !orange) FinishTeamMatch(blue ? LobbyTeam.Blue : orange ? LobbyTeam.Orange : LobbyTeam.None);
                return;
            }
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
            // Executa no host e nos clientes; quem já voltou ao lobby mantém a música do menu.
            if (currentValue == MatchPhase.ShowingResult && gameObject.scene == SceneManager.GetActiveScene()
                && (!LoadingScreenUI.Instance || !LoadingScreenUI.Instance.IsVisible))
                NetworkSoundManager.Instance?.LocalOST(OSTList.Battle_Vitoria);
            PhaseChanged?.Invoke();
        }

        private void HandleModeChanged(LobbyMatchMode previousValue, LobbyMatchMode currentValue)
        {
            ScoresChanged?.Invoke();
        }

        private void HandleWinningTeamChanged(LobbyTeam previousValue, LobbyTeam currentValue)
        {
            PhaseChanged?.Invoke();
        }

        private void HandleWinningPlayerChanged(int previousValue, int currentValue)
        {
            PhaseChanged?.Invoke();
        }
    }
}
