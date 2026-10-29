using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PiGame.Lobby
{
    public class NetworkLobbyState : NetworkBehaviour, ILobbyState
    {
        public const float RandomMapDrawSeconds = 1.5f;

        private static readonly LobbyMatchSettingsData InitialMatchSettings = new(
            3,
            LobbyMatchMode.Solo,
            2,
            false);

        [SerializeField] private LobbyRulesDefinition _rules;
        [SerializeField] private LobbyMapDefinition[] _maps;
        
        [SerializeField, Min(0.1f)] private float _mapResultDisplaySeconds = 2.5f;
        [SerializeField, Min(0f)] private float _characterReadyDisplaySeconds = 0.7f;

        private readonly NetworkList<LobbyPlayerData> _players = new();
        private readonly NetworkList<LobbyMapVoteData> _mapVotes = new();
        private readonly NetworkVariable<LobbyStage> _stage = new(LobbyStage.CharacterSelection);
        private readonly NetworkVariable<LobbyMapId> _winningMap = new(LobbyMapId.None);
        private readonly NetworkVariable<bool> _isRandomMapResult = new(false);
        private readonly NetworkVariable<LobbyMatchSettingsData> _matchSettings =
            new(InitialMatchSettings);

        private Coroutine _matchStartRoutine;
        private Coroutine _mapVotingRoutine;
        private bool _localNameSubmitted;

        public event Action PlayersChanged;
        public event Action<int> CharacterReadyRejected;
        public event Action StageChanged;
        public event Action MapVotesChanged;
        public event Action MatchSettingsChanged;
        public event Action<LobbyMapId> MatchStartRequested;

        public int PlayerCount => _players.Count;
        public int MapVoteCount => _mapVotes.Count;
        public ulong LocalClientId => NetworkManager != null
            ? NetworkManager.LocalClientId
            : ulong.MaxValue;
        public LobbyStage Stage => _stage.Value;
        public LobbyMapId WinningMap => _winningMap.Value;
        public bool IsRandomMapResult => _isRandomMapResult.Value;
        public LobbyMatchSettingsData MatchSettings => _matchSettings.Value;
        private int MinimumPlayers => Mathf.Max(1, MatchSettings.MinimumPlayers);
        public int SelectableCharacterCount => _rules.AvailableCharacterCount;
        public bool RequireUniqueCharacters => MatchSettings.RequireUniqueCharacters;
        public bool LocalClientIsHost => NetworkManager != null && NetworkManager.IsHost;
        private bool CanStartMatch => _players.Count >= MinimumPlayers && AllPlayersReady;


        private bool AllMapVotesConfirmed
        {
            get
            {
                if (_players.Count < MinimumPlayers || _mapVotes.Count != _players.Count)
                {
                    return false;
                }

                for (int i = 0; i < _mapVotes.Count; i++)
                {
                    if (!_mapVotes[i].IsConfirmed)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private bool AllPlayersReady
        {
            get
            {
                if (_players.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < _players.Count; i++)
                {
                    LobbyPlayerData player = _players[i];
                    if (!player.IsReady || player.CharacterId == LobbyCharacterId.None)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private void Awake()
        {
            if (_rules == null)
            {
                Debug.LogError("NetworkLobbyState precisa de LobbyRulesDefinition no Inspector.", this);
                enabled = false;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (_rules == null)
            {
                return;
            }

            _localNameSubmitted = false;
            LocalPlayerName.Saved += HandleLocalNameSaved;
            _players.OnListChanged += HandlePlayersChanged;
            _mapVotes.OnListChanged += HandleMapVotesChanged;
            _stage.OnValueChanged += HandleStageChanged;
            _winningMap.OnValueChanged += HandleWinningMapChanged;
            _isRandomMapResult.OnValueChanged += HandleRandomMapResultChanged;
            _matchSettings.OnValueChanged += HandleMatchSettingsChanged;

            if (IsServer)
            {
                MatchSnapshot previousMatch = MatchSession.Instance != null
                    ? MatchSession.Instance.Snapshot
                    : null;

                if (previousMatch == null)
                {
                    MatchSession.Instance?.ResetMapHistory();
                }

                ResetReplicatedSessionStateServer();
                _matchSettings.Value = previousMatch != null
                    ? previousMatch.MatchSettings
                    : CreateInitialMatchSettings();
                NetworkManager.OnClientConnectedCallback += HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

                foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                {
                    AddPlayer(clientId, previousMatch);
                }

                if (previousMatch != null)
                {
                    MatchSession.Instance.Clear();
                }
            }

            SubmitLocalPlayerName();
            PlayersChanged?.Invoke();
            MapVotesChanged?.Invoke();
            StageChanged?.Invoke();
            MatchSettingsChanged?.Invoke();
        }

        public void ResetSession()
        {
            if (!IsServer)
            {
                return;
            }

            ResetReplicatedSessionStateServer();
            _matchSettings.Value = CreateInitialMatchSettings();
            MatchSession.Instance?.Clear();
            MatchSession.Instance?.ResetMapHistory();
        }

        public override void OnNetworkDespawn()
        {
            if (_rules == null)
            {
                return;
            }

            _players.OnListChanged -= HandlePlayersChanged;
            LocalPlayerName.Saved -= HandleLocalNameSaved;
            _mapVotes.OnListChanged -= HandleMapVotesChanged;
            _stage.OnValueChanged -= HandleStageChanged;
            _winningMap.OnValueChanged -= HandleWinningMapChanged;
            _isRandomMapResult.OnValueChanged -= HandleRandomMapResultChanged;
            _matchSettings.OnValueChanged -= HandleMatchSettingsChanged;

            StopMapVotingRoutine();
            StopMatchStartRoutine();

            if (NetworkManager != null && IsServer)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            PlayersChanged?.Invoke();
            MapVotesChanged?.Invoke();
            StageChanged?.Invoke();
            MatchSettingsChanged?.Invoke();
        }

        public LobbyPlayerData GetPlayer(int index)
        {
            if (index < 0 || index >= _players.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _players[index];
        }

        public LobbyMapVoteData GetMapVote(int index)
        {
            if (index < 0 || index >= _mapVotes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _mapVotes[index];
        }

        public LobbyCharacterId GetSelectableCharacter(int index)
        {
            return _rules.GetAvailableCharacter(index);
        }

        public bool TryGetPlayer(ulong clientId, out LobbyPlayerData player)
        {
            int playerIndex = FindPlayerIndex(clientId);
            if (playerIndex >= 0)
            {
                player = _players[playerIndex];
                return true;
            }

            player = default;
            return false;
        }

        public bool TryGetMapVote(ulong clientId, out LobbyMapVoteData vote)
        {
            int voteIndex = FindMapVoteIndex(clientId);
            if (voteIndex >= 0)
            {
                vote = _mapVotes[voteIndex];
                return true;
            }

            vote = default;
            return false;
        }

        public bool TryGetCharacterLock(
            LobbyCharacterId characterId,
            ulong requestingClientId,
            out int lockingPlayerSlot)
        {
            if (!RequireUniqueCharacters || !IsValidCharacter(characterId))
            {
                lockingPlayerSlot = -1;
                return false;
            }

            for (int i = 0; i < _players.Count; i++)
            {
                LobbyPlayerData player = _players[i];
                if (player.ClientId != requestingClientId
                    && player.IsReady
                    && player.CharacterId == characterId)
                {
                    lockingPlayerSlot = player.PlayerSlot;
                    return true;
                }
            }

            lockingPlayerSlot = -1;
            return false;
        }

        public void RequestCharacterSelection(LobbyCharacterId characterId)
        {
            if (!IsSpawned
                || _stage.Value != LobbyStage.CharacterSelection
                || !IsValidCharacter(characterId))
            {
                return;
            }

            SelectCharacterRpc(characterId);
        }

        public void RequestReadyState(bool isReady)
        {
            if (!IsSpawned || _stage.Value != LobbyStage.CharacterSelection)
            {
                return;
            }

            SetReadyStateRpc(isReady);
        }

        public void RequestInputDevice(LobbyInputDeviceKind inputDevice)
        {
            if (!IsSpawned || inputDevice == LobbyInputDeviceKind.Unknown)
            {
                return;
            }

            SetInputDeviceRpc(inputDevice);
        }

        public void RequestMapVote(LobbyMapId mapId)
        {
            if (!IsSpawned || _stage.Value != LobbyStage.MapVoting || !IsValidMapVoteOption(mapId))
            {
                return;
            }

            SelectMapVoteRpc(mapId);
        }

        public void RequestMapVoteConfirmation(bool isConfirmed)
        {
            if (!IsSpawned || _stage.Value != LobbyStage.MapVoting)
            {
                return;
            }

            SetMapVoteConfirmationRpc(isConfirmed);
        }

        public void RequestReturnToCharacterSelection()
        {
            if (!IsSpawned || _stage.Value != LobbyStage.MapVoting || !LocalClientIsHost)
            {
                return;
            }

            ReturnToCharacterSelectionRpc();
        }

        public void RequestMatchSettings(int durationMinutes, LobbyMatchMode mode)
        {
            if (!IsSpawned
                || _stage.Value != LobbyStage.CharacterSelection
                || !LocalClientIsHost
                || !AreValidMatchSettings(durationMinutes, mode))
            {
                return;
            }

            SetMatchSettingsRpc(durationMinutes, mode);
        }

        [Rpc(SendTo.Server)]
        private void SetPlayerNameRpc(FixedString64Bytes name, RpcParams rpcParams = default)
        {
            int playerIndex = FindPlayerIndex(rpcParams.Receive.SenderClientId);
            if (playerIndex < 0)
                return;

            LobbyPlayerData player = _players[playerIndex];
            var normalizedName = new FixedString64Bytes(LocalPlayerName.Normalize(name.ToString()));
            if (player.PlayerName.Equals(normalizedName))
                return;

            player.PlayerName = normalizedName;
            _players[playerIndex] = player;
        }

        [Rpc(SendTo.Server)]
        private void SelectCharacterRpc(LobbyCharacterId characterId, RpcParams rpcParams = default)
        {
            if (_stage.Value != LobbyStage.CharacterSelection || !IsValidCharacter(characterId))
            {
                return;
            }

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            int playerIndex = FindPlayerIndex(senderClientId);
            if (playerIndex < 0)
            {
                return;
            }

            LobbyPlayerData player = _players[playerIndex];
            if (player.IsReady || player.CharacterId == characterId)
            {
                return;
            }

            player.CharacterId = characterId;
            _players[playerIndex] = player;
        }

        [Rpc(SendTo.Server)]
        private void SetReadyStateRpc(bool isReady, RpcParams rpcParams = default)
        {
            if (_stage.Value != LobbyStage.CharacterSelection)
            {
                return;
            }

            int playerIndex = FindPlayerIndex(rpcParams.Receive.SenderClientId);
            if (playerIndex < 0)
            {
                return;
            }

            LobbyPlayerData player = _players[playerIndex];
            if (isReady && !IsValidCharacter(player.CharacterId))
            {
                return;
            }

            if (isReady
                && TryGetCharacterLock(
                    player.CharacterId,
                    player.ClientId,
                    out int lockingPlayerSlot))
            {
                CharacterReadyRejectedRpc(
                    lockingPlayerSlot,
                    RpcTarget.Single(player.ClientId, RpcTargetUse.Temp));
                return;
            }

            if (player.IsReady == isReady)
            {
                return;
            }

            player.IsReady = isReady;
            _players[playerIndex] = player;

            TryBeginMapVotingServer();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void CharacterReadyRejectedRpc(int lockingPlayerSlot, RpcParams rpcParams)
        {
            CharacterReadyRejected?.Invoke(lockingPlayerSlot);
        }

        [Rpc(SendTo.Server)]
        private void SetInputDeviceRpc(
            LobbyInputDeviceKind inputDevice,
            RpcParams rpcParams = default)
        {
            if (inputDevice != LobbyInputDeviceKind.Keyboard
                && inputDevice != LobbyInputDeviceKind.Gamepad)
            {
                return;
            }

            int playerIndex = FindPlayerIndex(rpcParams.Receive.SenderClientId);
            if (playerIndex < 0)
            {
                return;
            }

            LobbyPlayerData player = _players[playerIndex];
            if (player.InputDevice == inputDevice)
            {
                return;
            }

            player.InputDevice = inputDevice;
            _players[playerIndex] = player;
        }

        [Rpc(SendTo.Server)]
        private void SelectMapVoteRpc(LobbyMapId mapId, RpcParams rpcParams = default)
        {
            if (_stage.Value != LobbyStage.MapVoting || !IsValidMapVoteOption(mapId))
            {
                return;
            }

            int voteIndex = FindMapVoteIndex(rpcParams.Receive.SenderClientId);
            if (voteIndex < 0)
            {
                return;
            }

            LobbyMapVoteData vote = _mapVotes[voteIndex];
            if (vote.MapId == mapId && !vote.IsConfirmed)
            {
                return;
            }

            vote.MapId = mapId;
            vote.IsConfirmed = false;
            _mapVotes[voteIndex] = vote;
        }

        [Rpc(SendTo.Server)]
        private void SetMapVoteConfirmationRpc(bool isConfirmed, RpcParams rpcParams = default)
        {
            if (_stage.Value != LobbyStage.MapVoting)
            {
                return;
            }

            int voteIndex = FindMapVoteIndex(rpcParams.Receive.SenderClientId);
            if (voteIndex < 0)
            {
                return;
            }

            LobbyMapVoteData vote = _mapVotes[voteIndex];
            if (vote.IsConfirmed == isConfirmed)
            {
                return;
            }

            vote.IsConfirmed = isConfirmed;
            _mapVotes[voteIndex] = vote;

            if (isConfirmed)
            {
                TryResolveMapVoteServer();
            }
        }

        [Rpc(SendTo.Server)]
        private void ReturnToCharacterSelectionRpc(RpcParams rpcParams = default)
        {
            if (_stage.Value != LobbyStage.MapVoting
                || NetworkManager == null
                || rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
            {
                return;
            }

            ReturnToCharacterSelectionServer();
        }

        [Rpc(SendTo.Server)]
        private void SetMatchSettingsRpc(
            int durationMinutes,
            LobbyMatchMode mode,
            RpcParams rpcParams = default)
        {
            if (_stage.Value != LobbyStage.CharacterSelection
                || NetworkManager == null
                || rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId
                || !AreValidMatchSettings(durationMinutes, mode))
            {
                return;
            }

            LobbyMatchSettingsData currentSettings = _matchSettings.Value;
            _matchSettings.Value = new LobbyMatchSettingsData(
                durationMinutes,
                mode,
                currentSettings.MinimumPlayers,
                currentSettings.RequireUniqueCharacters);
        }

        private void HandlePlayersChanged(NetworkListEvent<LobbyPlayerData> changeEvent)
        {
            SubmitLocalPlayerName();
            PlayersChanged?.Invoke();
        }

        private void SubmitLocalPlayerName()
        {
            // The local entry may arrive after OnNetworkSpawn on a joining client.
            if (!IsSpawned || !IsClient || _localNameSubmitted || FindPlayerIndex(LocalClientId) < 0)
                return;

            _localNameSubmitted = true;
            SetPlayerNameRpc(new FixedString64Bytes(LocalPlayerName.Load()));
        }

        private void HandleLocalNameSaved()
        {
            _localNameSubmitted = false;
            SubmitLocalPlayerName();
        }

        private void HandleMapVotesChanged(NetworkListEvent<LobbyMapVoteData> changeEvent)
        {
            MapVotesChanged?.Invoke();
        }

        private void HandleStageChanged(LobbyStage previousStage, LobbyStage currentStage)
        {
            StageChanged?.Invoke();
        }

        private void HandleWinningMapChanged(LobbyMapId previousMap, LobbyMapId currentMap)
        {
            StageChanged?.Invoke();
        }

        private void HandleRandomMapResultChanged(bool previousValue, bool currentValue)
        {
            StageChanged?.Invoke();
        }

        private void HandleMatchSettingsChanged(
            LobbyMatchSettingsData previousSettings,
            LobbyMatchSettingsData currentSettings)
        {
            MatchSettingsChanged?.Invoke();
        }

        private static bool AreValidMatchSettings(int durationMinutes, LobbyMatchMode mode)
        {
            return durationMinutes >= 2
                && durationMinutes <= 5
                && mode == LobbyMatchMode.Solo;
        }

        private LobbyMatchSettingsData CreateInitialMatchSettings()
        {
            LobbyMatchMode initialMode = _rules.DefaultMatchMode;
            if (initialMode == LobbyMatchMode.Team)
            {
                Debug.LogWarning("Modo Equipe indisponível por enquanto. Iniciando em Solo.", this);
                initialMode = LobbyMatchMode.Solo;
            }

            return new LobbyMatchSettingsData(
                Mathf.Clamp(_rules.DefaultDurationMinutes, 2, 5),
                initialMode,
                Mathf.Clamp(_rules.MinimumPlayers, 1, _rules.MaximumPlayers),
                _rules.RequireUniqueCharacters);
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (_stage.Value != LobbyStage.CharacterSelection)
            {
                ReturnToCharacterSelectionServer();
            }

            AddPlayer(clientId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            int playerIndex = FindPlayerIndex(clientId);
            if (playerIndex >= 0)
            {
                _players.RemoveAt(playerIndex);
            }

            int voteIndex = FindMapVoteIndex(clientId);
            if (voteIndex >= 0)
            {
                _mapVotes.RemoveAt(voteIndex);
            }

            if (_stage.Value != LobbyStage.CharacterSelection && _players.Count < 2)
            {
                ReturnToCharacterSelectionServer();
                return;
            }

            if (_stage.Value == LobbyStage.CharacterSelection)
                TryBeginMapVotingServer();
            else if (_stage.Value == LobbyStage.MapVoting)
            {
                TryResolveMapVoteServer();
            }
        }

        private void AddPlayer(ulong clientId)
        {
            AddPlayer(clientId, null);
        }

        private void AddPlayer(ulong clientId, MatchSnapshot previousMatch)
        {
            if (FindPlayerIndex(clientId) >= 0)
            {
                return;
            }

            bool hasPreviousPlayer = TryFindPreviousPlayer(
                previousMatch,
                clientId,
                out LobbyPlayerData previousPlayer);
            int availableSlot = hasPreviousPlayer
                && IsSlotAvailable(previousPlayer.PlayerSlot)
                ? previousPlayer.PlayerSlot
                : FindAvailableSlot();
            if (availableSlot < 0)
            {
                NetworkManager.DisconnectClient(clientId, "Lobby cheio.");
                return;
            }

            LobbyCharacterId initialCharacter = hasPreviousPlayer
                && IsValidCharacter(previousPlayer.CharacterId)
                ? previousPlayer.CharacterId
                : FindInitialCharacter();
            LobbyInputDeviceKind inputDevice = hasPreviousPlayer
                ? previousPlayer.InputDevice
                : LobbyInputDeviceKind.Unknown;
            _players.Add(new LobbyPlayerData(
                clientId,
                availableSlot,
                initialCharacter,
                inputDevice,
                false,
                hasPreviousPlayer ? previousPlayer.PlayerName.ToString() : "Jogador"));
        }

        private static bool TryFindPreviousPlayer(
            MatchSnapshot previousMatch,
            ulong clientId,
            out LobbyPlayerData previousPlayer)
        {
            if (previousMatch != null && previousMatch.Players != null)
            {
                foreach (LobbyPlayerData player in previousMatch.Players)
                {
                    if (player.ClientId == clientId)
                    {
                        previousPlayer = player;
                        return true;
                    }
                }
            }

            previousPlayer = default;
            return false;
        }

        private bool IsSlotAvailable(int playerSlot)
        {
            int maximumPlayers = Mathf.Clamp(_rules.MaximumPlayers, 1, 4);
            if (playerSlot < 0 || playerSlot >= maximumPlayers)
            {
                return false;
            }

            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].PlayerSlot == playerSlot)
                {
                    return false;
                }
            }

            return true;
        }

        private int FindPlayerIndex(ulong clientId)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].ClientId == clientId)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindMapVoteIndex(ulong clientId)
        {
            for (int i = 0; i < _mapVotes.Count; i++)
            {
                if (_mapVotes[i].ClientId == clientId)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindAvailableSlot()
        {
            int maximumPlayers = Mathf.Clamp(_rules.MaximumPlayers, 1, 4);

            for (int slot = 0; slot < maximumPlayers; slot++)
            {
                bool isOccupied = false;
                for (int i = 0; i < _players.Count; i++)
                {
                    if (_players[i].PlayerSlot == slot)
                    {
                        isOccupied = true;
                        break;
                    }
                }

                if (!isOccupied)
                {
                    return slot;
                }
            }

            return -1;
        }

        private bool IsValidCharacter(LobbyCharacterId characterId)
        {
            if (characterId < LobbyCharacterId.Cowboy
                || characterId > LobbyCharacterId.Rebel)
            {
                return false;
            }

            return _rules.IsCharacterAvailable(characterId);
        }

        private bool IsValidMapVoteOption(LobbyMapId mapId)
        {
            if (mapId == LobbyMapId.Random)
            {
                return true;
            }

            return FindMap(mapId) != null;
        }

        private LobbyMapDefinition FindMap(LobbyMapId mapId)
        {
            if (_maps == null)
            {
                return null;
            }

            for (int i = 0; i < _maps.Length; i++)
            {
                if (_maps[i] != null && _maps[i].Id == mapId)
                {
                    return _maps[i];
                }
            }

            return null;
        }

        public LobbyMapDefinition FindWinningMap(LobbyMapId winningMapId)
        {
            return FindMap(winningMapId);
        }

        private void TryBeginMapVotingServer()
        {
            if (!IsServer || _stage.Value != LobbyStage.CharacterSelection || !CanStartMatch)
            {
                StopMapVotingRoutine();
                return;
            }
            if (_mapVotingRoutine == null)
                _mapVotingRoutine = StartCoroutine(BeginMapVotingAfterReady());
        }

        private IEnumerator BeginMapVotingAfterReady()
        {
            // Deixa a ultima bandeira descer antes de trocar a tela; cancelar o pronto interrompe a espera.
            yield return new WaitForSecondsRealtime(_characterReadyDisplaySeconds);
            _mapVotingRoutine = null;
            if (!IsServer || _stage.Value != LobbyStage.CharacterSelection || !CanStartMatch)
                yield break;

            _mapVotes.Clear();
            for (int i = 0; i < _players.Count; i++)
                _mapVotes.Add(new LobbyMapVoteData(_players[i].ClientId));

            _winningMap.Value = LobbyMapId.None;
            _isRandomMapResult.Value = false;
            _stage.Value = LobbyStage.MapVoting;
        }

        private void StopMapVotingRoutine()
        {
            if (_mapVotingRoutine == null) return;
            StopCoroutine(_mapVotingRoutine);
            _mapVotingRoutine = null;
        }

        private void TryResolveMapVoteServer()
        {
            if (!IsServer || _stage.Value != LobbyStage.MapVoting || !AllMapVotesConfirmed)
            {
                return;
            }

            LobbyMapId winningOption = ResolveWinningVoteOption();
            LobbyMapId concreteMap = winningOption == LobbyMapId.Random
                ? PickRandomConcreteMap()
                : winningOption;

            if (concreteMap == LobbyMapId.None)
            {
                Debug.LogError("The lobby cannot start because no valid map is configured.", this);
                return;
            }

            _isRandomMapResult.Value = winningOption == LobbyMapId.Random;
            _winningMap.Value = concreteMap;
            _stage.Value = LobbyStage.MatchStarting;
            StopMatchStartRoutine();
            _matchStartRoutine = StartCoroutine(RequestMatchStartAfterResult(
                concreteMap,
                _isRandomMapResult.Value));
        }

        private LobbyMapId ResolveWinningVoteOption()
        {
            Dictionary<LobbyMapId, int> voteCounts = new();
            int highestCount = 0;

            for (int i = 0; i < _mapVotes.Count; i++)
            {
                LobbyMapId mapId = _mapVotes[i].MapId;
                voteCounts.TryGetValue(mapId, out int currentCount);
                int updatedCount = currentCount + 1;
                voteCounts[mapId] = updatedCount;
                highestCount = Mathf.Max(highestCount, updatedCount);
            }

            List<LobbyMapId> tiedOptions = new();
            foreach (KeyValuePair<LobbyMapId, int> entry in voteCounts)
            {
                if (entry.Value == highestCount)
                {
                    tiedOptions.Add(entry.Key);
                }
            }

            return tiedOptions[UnityEngine.Random.Range(0, tiedOptions.Count)];
        }

        private LobbyMapId PickRandomConcreteMap()
        {
            List<LobbyMapId> validMaps = new();
            if (_maps != null)
            {
                for (int i = 0; i < _maps.Length; i++)
                {
                    LobbyMapDefinition map = _maps[i];
                    if (map != null
                        && map.Id != LobbyMapId.None
                        && map.Id != LobbyMapId.Random
                        && !validMaps.Contains(map.Id))
                    {
                        validMaps.Add(map.Id);
                    }
                }
            }

            MatchSession session = MatchSession.Instance;
            if (validMaps.Count > 1 && session != null && session.ConsecutiveMapCount >= 2)
            {
                validMaps.Remove(session.LastMapId);
            }

            return validMaps.Count > 0
                ? validMaps[UnityEngine.Random.Range(0, validMaps.Count)]
                : LobbyMapId.None;
        }

        private IEnumerator RequestMatchStartAfterResult(LobbyMapId mapId, bool isRandomResult)
        {
            if (isRandomResult)
            {
                yield return new WaitForSecondsRealtime(RandomMapDrawSeconds);
            }

            yield return new WaitForSecondsRealtime(_mapResultDisplaySeconds);

            _matchStartRoutine = null;
            if (IsServer && _stage.Value == LobbyStage.MatchStarting && _winningMap.Value == mapId)
            {
                MatchStartRequested?.Invoke(mapId);
            }
        }

        private void ReturnToCharacterSelectionServer()
        {
            if (!IsServer)
            {
                return;
            }

            StopMapVotingRoutine();
            StopMatchStartRoutine();
            _mapVotes.Clear();
            _winningMap.Value = LobbyMapId.None;
            _isRandomMapResult.Value = false;

            for (int i = 0; i < _players.Count; i++)
            {
                LobbyPlayerData player = _players[i];
                player.IsReady = false;
                _players[i] = player;
            }

            _stage.Value = LobbyStage.CharacterSelection;
        }

        private void ResetReplicatedSessionStateServer()
        {
            StopMapVotingRoutine();
            StopMatchStartRoutine();
            _players.Clear();
            _mapVotes.Clear();
            _winningMap.Value = LobbyMapId.None;
            _isRandomMapResult.Value = false;
            _stage.Value = LobbyStage.CharacterSelection;
        }

        private void StopMatchStartRoutine()
        {
            if (_matchStartRoutine == null)
            {
                return;
            }

            StopCoroutine(_matchStartRoutine);
            _matchStartRoutine = null;
        }

        private LobbyCharacterId FindInitialCharacter()
        {
            for (int characterIndex = 0; characterIndex < SelectableCharacterCount; characterIndex++)
            {
                LobbyCharacterId characterId = GetSelectableCharacter(characterIndex);
                if (!IsValidCharacter(characterId))
                {
                    continue;
                }

                bool isAlreadySelected = false;
                for (int playerIndex = 0; playerIndex < _players.Count; playerIndex++)
                {
                    if (_players[playerIndex].CharacterId == characterId)
                    {
                        isAlreadySelected = true;
                        break;
                    }
                }

                if (!isAlreadySelected)
                {
                    return characterId;
                }
            }

            LobbyCharacterId fallbackCharacter = GetSelectableCharacter(0);
            return IsValidCharacter(fallbackCharacter)
                ? fallbackCharacter
                : LobbyCharacterId.Cowboy;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _mapResultDisplaySeconds = Mathf.Max(0.1f, _mapResultDisplaySeconds);
        }
#endif
    }
}
