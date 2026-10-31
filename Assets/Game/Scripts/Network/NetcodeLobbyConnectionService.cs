using System;
using System.Threading;
using System.Threading.Tasks;
using PiGame.Lobby;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PiGame.Networking
{
    public class NetcodeLobbyConnectionService : MonoBehaviour, ILobbyConnectionService
    {
        private const string ServicesProfileArgument = "-ugs-profile";
        private const string ServicesProfileMutexPrefix =
            "PiGame-6thSemesterProject-UGS-";
        private const int MaximumLocalProfiles = 4;
        private const string LobbySceneName = "scene_lobby";

        private enum SessionRole
        {
            None,
            Host,
            Client
        }

        [SerializeField] private NetworkManager _networkManager;
        [SerializeField, Range(2, 4)] private int _maxPlayers = 4;
        [SerializeField, Min(5f)] private float _hostRecoveryTimeoutSeconds = 45f;

        private bool _isDuplicate;
        private SessionRole _sessionRole;
        private ISession _session;
        private int _operationVersion;
        private bool _isStarting;
        private bool _wasConnected;
        private bool _terminationReported;
        private bool _callbacksRegistered;
        private bool _isRecoveringSession;
        private bool _hostMigrationPending;
        private LobbyMatchSettingsData? _recoveredSettings;
        private LobbyMatchSettingsData? _lastSyncedSettings;
        private static bool _authenticationIdentityLogged;
        private static string _selectedServicesProfile;
        private static Mutex _servicesProfileMutex;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<LobbyConnectionFailure> ConnectionFailed;

        public static NetcodeLobbyConnectionService Instance { get; private set; }
        public bool IsConnected => _networkManager != null && _networkManager.IsConnectedClient;
        public bool IsHost => _session?.IsHost ?? (_networkManager != null && _networkManager.IsHost);
        public string JoinCode => _session?.Code ?? string.Empty;
        public bool IsRecoveringConnection => _hostMigrationPending;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                _isDuplicate = true;
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _networkManager ??= GetComponent<NetworkManager>();
        }

        private void OnEnable()
        {
            if (_isDuplicate)
            {
                return;
            }

            RegisterCallbacks();
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
        }

        private void OnDestroy()
        {
            DetachSession();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public async Task StartHostAsync()
        {
            if (!TryBeginSession(SessionRole.Host, out int operationVersion))
            {
                return;
            }

            try
            {
                await EnsureServicesReadyAsync();
                if (!IsOperationCurrent(operationVersion))
                {
                    return;
                }

                SessionOptions options = new SessionOptions
                {
                    MaxPlayers = _maxPlayers,
                    IsPrivate = true,
                    Name = "PI6 Lobby"
                }.WithRelayNetwork().WithHostMigration(CreateMigrationDataHandler());

                ISession createdSession = await MultiplayerService.Instance.CreateSessionAsync(options);
                await CompleteOrDiscardSessionAsync(createdSession, operationVersion);
            }
            catch (SessionException exception)
            {
                HandleSessionException(exception, operationVersion);
            }
            catch (AuthenticationException exception)
            {
                HandleServiceException(
                    exception,
                    LobbyConnectionFailure.AuthenticationFailed,
                    operationVersion);
            }
            catch (RequestFailedException exception)
            {
                HandleServiceException(
                    exception,
                    LobbyConnectionFailure.ServicesUnavailable,
                    operationVersion);
            }
            catch (Exception exception)
            {
                HandleServiceException(
                    exception,
                    LobbyConnectionFailure.ServicesUnavailable,
                    operationVersion);
            }
        }

        public async Task StartClientAsync(string joinCode)
        {
            if (!TryNormalizeJoinCode(joinCode, out string normalizedJoinCode))
            {
                ConnectionFailed?.Invoke(LobbyConnectionFailure.InvalidJoinCode);
                return;
            }

            if (!TryBeginSession(SessionRole.Client, out int operationVersion))
            {
                return;
            }

            try
            {
                await EnsureServicesReadyAsync();
                if (!IsOperationCurrent(operationVersion))
                {
                    return;
                }

                ISession joinedSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(
                    normalizedJoinCode, new JoinSessionOptions().WithHostMigration(CreateMigrationDataHandler()));
                await CompleteOrDiscardSessionAsync(joinedSession, operationVersion);
            }
            catch (SessionException exception)
            {
                if (exception.Error == SessionError.SessionConflict
                    && IsOperationCurrent(operationVersion))
                {
                    ISession recoveredSession = await TryRecoverJoinedSessionAsync(
                        normalizedJoinCode,
                        operationVersion);
                    if (recoveredSession != null)
                    {
                        await CompleteOrDiscardSessionAsync(
                            recoveredSession,
                            operationVersion);
                        return;
                    }
                }

                HandleSessionException(exception, operationVersion);
            }
            catch (AuthenticationException exception)
            {
                HandleServiceException(
                    exception,
                    LobbyConnectionFailure.AuthenticationFailed,
                    operationVersion);
            }
            catch (RequestFailedException exception)
            {
                HandleServiceException(
                    exception,
                    LobbyConnectionFailure.ServicesUnavailable,
                    operationVersion);
            }
            catch (Exception exception)
            {
                HandleServiceException(
                    exception,
                    LobbyConnectionFailure.ServicesUnavailable,
                    operationVersion);
            }
        }

        public async Task ShutdownAsync()
        {
            _operationVersion++;
            _terminationReported = true;
            _isStarting = false;
            _wasConnected = false;
            _sessionRole = SessionRole.None;
            _hostMigrationPending = false;

            ISession sessionToLeave = _session;
            DetachSession();

            if (sessionToLeave != null)
            {
                await LeaveSessionQuietlyAsync(sessionToLeave);
            }

            MatchSession.Instance?.Clear();
            MatchSession.Instance?.ResetMapHistory();

            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }
        }

        private bool TryBeginSession(SessionRole role, out int operationVersion)
        {
            operationVersion = _operationVersion;
            if (_networkManager == null)
            {
                ConnectionFailed?.Invoke(LobbyConnectionFailure.NetworkManagerUnavailable);
                return false;
            }

            RegisterCallbacks();

            if (_networkManager.IsListening || _isStarting)
            {
                ConnectionFailed?.Invoke(LobbyConnectionFailure.AlreadyRunning);
                return false;
            }

            operationVersion = ++_operationVersion;
            _sessionRole = role;
            _isStarting = true;
            _wasConnected = false;
            _terminationReported = false;
            _hostMigrationPending = false;
            _recoveredSettings = null;
            _lastSyncedSettings = null;
            return true;
        }

        private static async Task EnsureServicesReadyAsync()
        {
            string requestedProfile = FindServicesProfileArgument();

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                string selectedProfile = SelectServicesProfile(requestedProfile);
                InitializationOptions options = new InitializationOptions();
                options.SetProfile(selectedProfile);

                await UnityServices.InitializeAsync(options);
            }
            else
            {
                string activeProfile = AuthenticationService.Instance.Profile;
                if (!string.IsNullOrWhiteSpace(requestedProfile)
                    && !string.Equals(
                        activeProfile,
                        requestedProfile,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unity Services is already using profile "
                        + $"'{activeProfile}', but profile "
                        + $"'{requestedProfile}' was requested.");
                }

                EnsureActiveProfileLease(activeProfile);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            if (!_authenticationIdentityLogged)
            {
                _authenticationIdentityLogged = true;
                Debug.Log(
                    $"Unity Services authenticated with profile "
                    + $"'{AuthenticationService.Instance.Profile}' and player "
                    + $"'{AuthenticationService.Instance.PlayerId}'.");
            }
        }

        private static string SelectServicesProfile(string requestedProfile)
        {
            if (!string.IsNullOrWhiteSpace(_selectedServicesProfile))
            {
                if (!string.IsNullOrWhiteSpace(requestedProfile)
                    && !string.Equals(
                        _selectedServicesProfile,
                        requestedProfile,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unity Services profile '{_selectedServicesProfile}' "
                        + $"is already selected for this process.");
                }

                return _selectedServicesProfile;
            }

            if (!string.IsNullOrWhiteSpace(requestedProfile))
            {
                ValidateServicesProfile(requestedProfile);
                if (!TryAcquireServicesProfile(requestedProfile))
                {
                    throw new InvalidOperationException(
                        $"Unity Services profile '{requestedProfile}' is already "
                        + "being used by another local game instance.");
                }

                return requestedProfile;
            }

            if (!UsesLocalProfileLeases())
            {
                _selectedServicesProfile = "default";
                return _selectedServicesProfile;
            }

            for (int profileIndex = 0; profileIndex < MaximumLocalProfiles; profileIndex++)
            {
                string candidate = profileIndex == 0
                    ? "default"
                    : $"local{profileIndex + 1}";
                if (TryAcquireServicesProfile(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                $"All {MaximumLocalProfiles} local Unity Services profiles are in use.");
        }

        private static void EnsureActiveProfileLease(string activeProfile)
        {
            if (!string.IsNullOrWhiteSpace(_selectedServicesProfile))
            {
                return;
            }

            string profile = string.IsNullOrWhiteSpace(activeProfile)
                ? "default"
                : activeProfile;
            if (!UsesLocalProfileLeases())
            {
                _selectedServicesProfile = profile;
                return;
            }

            if (!TryAcquireServicesProfile(profile))
            {
                throw new InvalidOperationException(
                    $"Unity Services was initialized with profile '{profile}', "
                    + "which is already being used by another local game instance.");
            }
        }

        private static bool TryAcquireServicesProfile(string profile)
        {
            Mutex profileMutex = new Mutex(
                false,
                ServicesProfileMutexPrefix + profile);
            bool acquired;

            try
            {
                acquired = profileMutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                profileMutex.Dispose();
                return false;
            }

            _servicesProfileMutex = profileMutex;
            _selectedServicesProfile = profile;
            Application.quitting -= ReleaseServicesProfileLease;
            Application.quitting += ReleaseServicesProfileLease;
            return true;
        }

        private static void ReleaseServicesProfileLease()
        {
            Application.quitting -= ReleaseServicesProfileLease;
            if (_servicesProfileMutex == null)
            {
                return;
            }

            try
            {
                _servicesProfileMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The operating system already released the profile lease.
            }

            _servicesProfileMutex.Dispose();
            _servicesProfileMutex = null;
        }

        private static bool UsesLocalProfileLeases()
        {
            return Application.platform == RuntimePlatform.WindowsPlayer
                || Application.platform == RuntimePlatform.WindowsEditor;
        }

        private static void ValidateServicesProfile(string profile)
        {
            if (profile.Length > 30)
            {
                throw new ArgumentException(
                    "Unity Services profile names cannot exceed 30 characters.",
                    nameof(profile));
            }

            foreach (char character in profile)
            {
                bool isAsciiLetter = character >= 'A' && character <= 'Z'
                    || character >= 'a' && character <= 'z';
                bool isDigit = character >= '0' && character <= '9';
                if (!isAsciiLetter && !isDigit && character != '-' && character != '_')
                {
                    throw new ArgumentException(
                        "Unity Services profile names only support letters, numbers, "
                        + "hyphens, and underscores.",
                        nameof(profile));
                }
            }
        }

        private static string FindServicesProfileArgument()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string inlinePrefix = ServicesProfileArgument + "=";

            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];
                if (string.Equals(
                        argument,
                        ServicesProfileArgument,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1 < arguments.Length
                        ? arguments[i + 1]
                        : string.Empty;
                }

                if (argument.StartsWith(inlinePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return argument.Substring(inlinePrefix.Length);
                }
            }

            return string.Empty;
        }

        private async Task<ISession> TryRecoverJoinedSessionAsync(
            string joinCode,
            int operationVersion)
        {
            _isRecoveringSession = true;

            try
            {
                var joinedSessionIds =
                    await MultiplayerService.Instance.GetJoinedSessionIdsAsync();

                foreach (string sessionId in joinedSessionIds)
                {
                    if (!IsOperationCurrent(operationVersion))
                    {
                        return null;
                    }

                    ISession recoveredSession = null;
                    try
                    {
                        recoveredSession = await MultiplayerService.Instance
                            .ReconnectToSessionAsync(sessionId);

                        if (string.Equals(
                            recoveredSession.Code,
                            joinCode,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return recoveredSession;
                        }

                        await LeaveSessionQuietlyAsync(recoveredSession);
                    }
                    catch (Exception recoveryException)
                    {
                        Debug.LogWarning(
                            $"Could not recover session {sessionId}: "
                            + recoveryException.Message,
                            this);

                        if (recoveredSession != null)
                        {
                            await LeaveSessionQuietlyAsync(recoveredSession);
                        }
                    }
                }
            }
            catch (SessionException exception)
            {
                Debug.LogWarning(
                    $"Could not list joined sessions: "
                    + $"{exception.Error} - {exception.Message}",
                    this);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not list joined sessions: {exception.Message}",
                    this);
            }
            finally
            {
                _isRecoveringSession = false;
            }

            return null;
        }

        private async Task CompleteOrDiscardSessionAsync(
            ISession session,
            int operationVersion)
        {
            if (!IsOperationCurrent(operationVersion))
            {
                await LeaveSessionQuietlyAsync(session);
                return;
            }

            _session = session;
            AttachSession();
            _isStarting = false;
            _wasConnected = true;
            Connected?.Invoke();
        }

        private bool IsOperationCurrent(int operationVersion)
        {
            return operationVersion == _operationVersion && !_terminationReported;
        }

        private void AttachSession()
        {
            if (_session == null)
            {
                return;
            }

            _session.StateChanged += HandleSessionStateChanged;
            _session.RemovedFromSession += HandleRemovedFromSession;
            _session.Deleted += HandleSessionDeleted;
            _session.SessionHostChanged += HandleSessionHostChanged;
            _session.SessionMigrated += HandleSessionMigrated;
            _session.Network.StateChanged += HandleNetworkStateChanged;
            _session.Network.StartFailed += HandleNetworkStartFailed;
            _session.Network.MigrationFailed += HandleMigrationFailed;
        }

        private void DetachSession()
        {
            if (_session == null)
            {
                return;
            }

            _session.StateChanged -= HandleSessionStateChanged;
            _session.RemovedFromSession -= HandleRemovedFromSession;
            _session.Deleted -= HandleSessionDeleted;
            _session.SessionHostChanged -= HandleSessionHostChanged;
            _session.SessionMigrated -= HandleSessionMigrated;
            _session.Network.StateChanged -= HandleNetworkStateChanged;
            _session.Network.StartFailed -= HandleNetworkStartFailed;
            _session.Network.MigrationFailed -= HandleMigrationFailed;
            _session = null;
        }

        private SessionLobbyMigrationData CreateMigrationDataHandler()
        {
            return new SessionLobbyMigrationData(
                () => FindFirstObjectByType<NetworkLobbyState>().MatchSettings,
                settings => _recoveredSettings = _lastSyncedSettings ?? settings);
        }

        public void RememberMatchSettings(LobbyMatchSettingsData settings)
        {
            // O upload periódico do SDK pode estar alguns segundos atrás do último ajuste.
            if (!_hostMigrationPending) _lastSyncedSettings = settings;
        }

        public bool TryConsumeRecoveredSettings(out LobbyMatchSettingsData settings)
        {
            LobbyMatchSettingsData? saved = _recoveredSettings
                ?? (_hostMigrationPending ? _lastSyncedSettings : null);
            settings = saved.GetValueOrDefault();
            bool recovered = saved.HasValue;
            _recoveredSettings = null;
            return recovered;
        }

        private bool TryRecoverHost()
        {
            if (_terminationReported || !_wasConnected || _session == null || !_session.IsMember)
                return false;
            if (!_hostMigrationPending)
            {
                _hostMigrationPending = true;
                LobbySceneFlow.Instance?.ShowLobbyLocally();
                _ = WaitForHostRecoveryAsync(_operationVersion);
            }
            return true;
        }

        private async Task WaitForHostRecoveryAsync(int operationVersion)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + _hostRecoveryTimeoutSeconds;
            while (_hostMigrationPending && IsOperationCurrent(operationVersion)
                && Time.realtimeSinceStartupAsDouble < deadline)
                await Task.Delay(250);

            if (_hostMigrationPending && IsOperationCurrent(operationVersion))
            {
                Debug.LogWarning("Não foi possível recuperar a sala após a saída do host.", this);
                ReportDisconnection();
            }
        }

        private void HandleSessionHostChanged(string hostId)
        {
            TryRecoverHost();
        }

        private async void HandleSessionMigrated()
        {
            if (_terminationReported) return;
            int operationVersion = _operationVersion;
            _sessionRole = _session.IsHost ? SessionRole.Host : SessionRole.Client;
            _wasConnected = true;
            // O SDK avisa o novo host antes de os clients concluírem a sincronização de cenas.
            // Não descarregar o mapa durante essa sincronização.
            if (_networkManager.IsServer)
            {
                double deadline = Time.realtimeSinceStartupAsDouble + _hostRecoveryTimeoutSeconds;
                while (IsOperationCurrent(operationVersion) && _session != null
                    && _networkManager.ConnectedClientsIds.Count < _session.PlayerCount
                    && Time.realtimeSinceStartupAsDouble < deadline)
                    await Task.Delay(100);
            }
            if (!IsOperationCurrent(operationVersion)) return;
            LobbySceneFlow flow = LobbySceneFlow.Instance;
            if (flow != null)
            {
                flow.ShowLobbyLocally();
                if (_networkManager.IsServer)
                {
                    flow.PrepareNextMatchServer();
                    flow.UnloadMapServer();
                }
            }
            _hostMigrationPending = false;
            Connected?.Invoke();
        }

        private void HandleMigrationFailed(SessionError error)
        {
            Debug.LogWarning($"Falha na recuperação da sala: {error}.", this);
            ReportDisconnection();
        }

        private void RegisterCallbacks()
        {
            if (_networkManager == null || _callbacksRegistered)
            {
                return;
            }

            _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            _networkManager.OnTransportFailure += HandleTransportFailure;
            _networkManager.OnServerStopped += HandleSessionStopped;
            _networkManager.OnClientStopped += HandleSessionStopped;
            _callbacksRegistered = true;
        }

        private void UnregisterCallbacks()
        {
            if (_networkManager == null || !_callbacksRegistered)
            {
                return;
            }

            _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            _networkManager.OnTransportFailure -= HandleTransportFailure;
            _networkManager.OnServerStopped -= HandleSessionStopped;
            _networkManager.OnClientStopped -= HandleSessionStopped;
            _callbacksRegistered = false;
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (_terminationReported
                || _isRecoveringSession
                || clientId != _networkManager.LocalClientId)
            {
                return;
            }

            if (_wasConnected)
            {
                if (TryRecoverHost()) return;
                ReportDisconnection();
                return;
            }

            if (_isStarting)
            {
                LobbyConnectionFailure failure = _sessionRole == SessionRole.Client
                    ? LobbyConnectionFailure.HostUnavailable
                    : LobbyConnectionFailure.StartFailed;
                ReportFailure(failure);
            }
        }

        private void HandleTransportFailure()
        {
            if (_isRecoveringSession)
            {
                return;
            }

            if (_wasConnected)
            {
                if (TryRecoverHost()) return;
                ReportDisconnection();
                return;
            }

            ReportFailure(LobbyConnectionFailure.TransportFailure);
        }

        private void HandleSessionStopped(bool isHost)
        {
            if (_terminationReported || _isRecoveringSession)
            {
                return;
            }

            if (_wasConnected)
            {
                if (TryRecoverHost()) return;
                ReportDisconnection();
                return;
            }

            if (_isStarting)
            {
                LobbyConnectionFailure failure = _sessionRole == SessionRole.Client
                    ? LobbyConnectionFailure.HostUnavailable
                    : LobbyConnectionFailure.StartFailed;
                ReportFailure(failure);
            }
        }

        private void HandleSessionStateChanged(SessionState state)
        {
            if (state == SessionState.Disconnected || state == SessionState.Deleted)
            {
                ReportDisconnection();
            }
        }

        private void HandleRemovedFromSession()
        {
            ReportDisconnection();
        }

        private void HandleSessionDeleted()
        {
            ReportDisconnection();
        }

        private void HandleNetworkStateChanged(NetworkState state)
        {
            if (state == NetworkState.Migrating)
            {
                TryRecoverHost();
                return;
            }
            if (state == NetworkState.Stopped && _wasConnected)
            {
                if (TryRecoverHost()) return;
                ReportDisconnection();
            }
        }

        private void HandleNetworkStartFailed(SessionError error)
        {
            if (_isStarting)
            {
                ReportFailure(MapSessionError(error));
            }
        }

        private void ReportDisconnection()
        {
            if (_terminationReported)
            {
                return;
            }

            _terminationReported = true;
            _hostMigrationPending = false;
            _isStarting = false;
            _wasConnected = false;
            _sessionRole = SessionRole.None;
            ISession disconnectedSession = _session;
            DetachSession();
            if (disconnectedSession != null)
            {
                _ = LeaveSessionQuietlyAsync(disconnectedSession);
            }

            Disconnected?.Invoke();
            ReturnToLobbySceneAfterUnexpectedDisconnect();
        }

        private void ReturnToLobbySceneAfterUnexpectedDisconnect()
        {
            if (!isActiveAndEnabled
                || (SceneManager.GetActiveScene().name == LobbySceneName && SceneManager.sceneCount == 1))
            {
                return;
            }

            MatchSession.Instance?.Clear();
            StartCoroutine(LoadLobbySceneNextFrame());
        }

        private static System.Collections.IEnumerator LoadLobbySceneNextFrame()
        {
            yield return null;
            SceneManager.LoadScene(LobbySceneName, LoadSceneMode.Single);
        }

        private void ReportFailure(LobbyConnectionFailure failure)
        {
            if (_terminationReported)
            {
                return;
            }

            _terminationReported = true;
            _isStarting = false;
            _wasConnected = false;
            _sessionRole = SessionRole.None;

            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }

            ConnectionFailed?.Invoke(failure);
        }

        private void HandleSessionException(
            SessionException exception,
            int operationVersion)
        {
            if (!IsOperationCurrent(operationVersion))
            {
                return;
            }

            Debug.LogWarning(
                $"Multiplayer session failed: {exception.Error} - {exception.Message}",
                this);
            LobbyConnectionFailure failure = exception.Error == SessionError.Unknown
                && _sessionRole == SessionRole.Client
                    ? LobbyConnectionFailure.SessionNotFound
                    : MapSessionError(exception.Error);
            ReportFailure(failure);
        }

        private void HandleServiceException(
            Exception exception,
            LobbyConnectionFailure failure,
            int operationVersion)
        {
            if (!IsOperationCurrent(operationVersion))
            {
                return;
            }

            Debug.LogWarning(
                $"Unity Multiplayer Services failed: {exception.Message}",
                this);
            ReportFailure(failure);
        }

        private static LobbyConnectionFailure MapSessionError(SessionError error)
        {
            return error switch
            {
                SessionError.InvalidParameter => LobbyConnectionFailure.InvalidJoinCode,
                SessionError.InvalidSessionIdentifier => LobbyConnectionFailure.InvalidJoinCode,
                SessionError.SessionNotFound => LobbyConnectionFailure.SessionNotFound,
                SessionError.SessionDeleted => LobbyConnectionFailure.SessionNotFound,
                SessionError.AllocationNotFound => LobbyConnectionFailure.SessionNotFound,
                SessionError.SessionConflict => LobbyConnectionFailure.SessionConflict,
                SessionError.NotAuthorized => LobbyConnectionFailure.AuthenticationFailed,
                SessionError.Forbidden => LobbyConnectionFailure.AuthenticationFailed,
                SessionError.NetworkManagerNotInitialized =>
                    LobbyConnectionFailure.NetworkManagerUnavailable,
                SessionError.NetworkManagerStartFailed => LobbyConnectionFailure.TransportFailure,
                SessionError.NetworkSetupFailed => LobbyConnectionFailure.TransportFailure,
                SessionError.TransportComponentMissing => LobbyConnectionFailure.TransportFailure,
                SessionError.TransportInvalid => LobbyConnectionFailure.TransportFailure,
                SessionError.QoSMeasurementFailed => LobbyConnectionFailure.TransportFailure,
                _ => LobbyConnectionFailure.ServicesUnavailable
            };
        }

        private static bool TryNormalizeJoinCode(string joinCode, out string normalizedJoinCode)
        {
            normalizedJoinCode = string.Empty;
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                return false;
            }

            string trimmedCode = joinCode.Trim();
            foreach (char character in trimmedCode)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    return false;
                }
            }

            normalizedJoinCode = trimmedCode.ToUpperInvariant();
            return true;
        }

        private static async Task LeaveSessionQuietlyAsync(ISession session)
        {
            try
            {
                if (session.IsMember)
                {
                    await session.LeaveAsync();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not leave multiplayer session cleanly: {exception.Message}");
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _maxPlayers = Mathf.Clamp(_maxPlayers, 2, 4);
        }
#endif
    }
}
