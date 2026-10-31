using PiGame.Lobby;
using PiGame.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class LobbyUIController : MonoBehaviour
    {
        private enum ConfirmationAction
        {
            None,
            Quit,
            Disconnect
        }

        [Header("Panels")]
        [SerializeField] private MainMenuUI _mainMenu;
        [SerializeField] private ConnectionPanelUI _connectionPanel;
        [SerializeField] private GameObject _lobbyPanel;
        [SerializeField] private LobbyCharacterSelectionController _characterSelectionController;
        [SerializeField] private LobbyMapVotingController _mapVotingController;
        [SerializeField] private ConfirmationPanelUI _confirmationPanel;
        [SerializeField] private MatchSettingsPanelUI _matchSettingsPanel;
        [FormerlySerializedAs("_localNetworkAddressText")]
        [SerializeField] private Text _sessionCodeText;
        [SerializeField] private PauseMenuUI _lobbyPause;
        [SerializeField] private PlayerNamePanelUI _playerNamePanel;

        [Header("Services")]
        [SerializeField] private NetcodeLobbyConnectionService _connectionService;

        private ILobbyConnectionService _connectionServiceContract;
        private ConfirmationAction _pendingConfirmation;
        private bool _returnToOptionsAfterName;
        private int _ignoreMenuInputFrame = -1;

        private void Awake()
        {
            if (NetcodeLobbyConnectionService.Instance != null)
            {
                _connectionService = NetcodeLobbyConnectionService.Instance;
            }

            _connectionServiceContract = _connectionService;
            if (_mainMenu == null || _connectionPanel == null || _playerNamePanel == null)
            {
                Debug.LogError("LobbyUIController precisa dos painéis de menu, conexão e nome no Inspector.", this);
                enabled = false;
                return;
            }

            if (!_connectionPanel.Initialize())
            {
                enabled = false;
                return;
            }

            InitializeLobbyPause();
        }

        private void OnEnable()
        {
            _connectionPanel.HostRequested += HandleHostRequested;
            _connectionPanel.ClientRequested += HandleClientRequested;
            _connectionPanel.CancelConnectionRequested += HandleCancelConnectionRequested;
            _mainMenu.PlayRequested += HandlePlayRequested;
            _mainMenu.OptionsRequested += HandleOptionsRequested;
            _mainMenu.QuitRequested += HandleQuitRequested;
            _connectionPanel.BackRequested += ShowMainMenu;
            _connectionPanel.OptionsRequested += HandleOptionsRequested;
            _mapVotingController.DisconnectRequested += HandleDisconnectRequested;
            _mapVotingController.VisibilityChanged += HandleMapVotingVisibilityChanged;
            _confirmationPanel.Confirmed += HandleConfirmationConfirmed;
            _confirmationPanel.Canceled += HandleConfirmationCanceled;
            _matchSettingsPanel.Opened += HandleMatchSettingsOpened;
            _matchSettingsPanel.Closed += HandleMatchSettingsClosed;
            _lobbyPause.ResumeRequested += CloseLobbyPause;
            _lobbyPause.ExitRequested += _lobbyPause.ShowConfirmation;
            _lobbyPause.ExitConfirmed += HandleLobbyPauseExitConfirmed;
            _lobbyPause.ExitCanceled += _lobbyPause.HideConfirmation;
            _lobbyPause.ControlsClosed += HandleControlsClosed;
            _lobbyPause.PlayerNameRequested += HandlePlayerNameRequested;
            _playerNamePanel.Closed += HandlePlayerNameClosed;
            _confirmationPanel.Hide();

            if (_connectionServiceContract == null)
            {
                ShowConnectionError("SERVIÇO DE REDE NÃO CONFIGURADO");
                Debug.LogError("LobbyUIController requires a connection service reference.", this);
                return;
            }

            _connectionServiceContract.Connected += HandleConnected;
            _connectionServiceContract.Disconnected += HandleDisconnected;
            _connectionServiceContract.ConnectionFailed += HandleConnectionFailed;

            if (_connectionService.IsRecoveringConnection)
            {
                ShowRecoveringConnection();
                return;
            }

            if (_connectionServiceContract.IsConnected)
            {
                ShowLobbyPanel();
                return;
            }

            ShowMainMenu();
            if (!LocalPlayerName.HasSavedName)
            {
                _returnToOptionsAfterName = false;
                SetOfflineInteractionEnabled(false);
                _playerNamePanel.Show();
            }
        }

        private void OnDisable()
        {
            _connectionPanel.HostRequested -= HandleHostRequested;
            _connectionPanel.ClientRequested -= HandleClientRequested;
            _connectionPanel.CancelConnectionRequested -= HandleCancelConnectionRequested;
            _mainMenu.PlayRequested -= HandlePlayRequested;
            _mainMenu.OptionsRequested -= HandleOptionsRequested;
            _mainMenu.QuitRequested -= HandleQuitRequested;
            _connectionPanel.BackRequested -= ShowMainMenu;
            _connectionPanel.OptionsRequested -= HandleOptionsRequested;
            _mapVotingController.DisconnectRequested -= HandleDisconnectRequested;
            _mapVotingController.VisibilityChanged -= HandleMapVotingVisibilityChanged;
            _confirmationPanel.Confirmed -= HandleConfirmationConfirmed;
            _confirmationPanel.Canceled -= HandleConfirmationCanceled;
            _matchSettingsPanel.Opened -= HandleMatchSettingsOpened;
            _matchSettingsPanel.Closed -= HandleMatchSettingsClosed;
            if (_lobbyPause != null)
            {
                _lobbyPause.ResumeRequested -= CloseLobbyPause;
                _lobbyPause.ExitRequested -= _lobbyPause.ShowConfirmation;
                _lobbyPause.ExitConfirmed -= HandleLobbyPauseExitConfirmed;
                _lobbyPause.ExitCanceled -= _lobbyPause.HideConfirmation;
                _lobbyPause.ControlsClosed -= HandleControlsClosed;
                _lobbyPause.PlayerNameRequested -= HandlePlayerNameRequested;
            }
            _playerNamePanel.Closed -= HandlePlayerNameClosed;

            if (_connectionServiceContract == null)
            {
                return;
            }

            _connectionServiceContract.Connected -= HandleConnected;
            _connectionServiceContract.Disconnected -= HandleDisconnected;
            _connectionServiceContract.ConnectionFailed -= HandleConnectionFailed;
        }

        private void Update()
        {
            if (_connectionService.IsRecoveringConnection)
            {
                if (!_connectionPanel.gameObject.activeSelf) ShowRecoveringConnection();
                return;
            }
            if (_lobbyPause == null || Time.frameCount == _ignoreMenuInputFrame)
            {
                return;
            }

            bool keyboardBack = Keyboard.current != null
                && Keyboard.current.escapeKey.wasPressedThisFrame;
            bool gamepadMenu = Gamepad.current != null
                && Gamepad.current.startButton.wasPressedThisFrame;
            bool gamepadBack = Gamepad.current != null
                && Gamepad.current.buttonEast.wasPressedThisFrame;

            if (_playerNamePanel.IsVisible)
            {
                if (keyboardBack || gamepadMenu || gamepadBack)
                    _playerNamePanel.HandleBack();
                return;
            }

            if (_lobbyPause.IsInlineControlsBusy && (keyboardBack || gamepadMenu || gamepadBack))
            {
                _lobbyPause.HandleControlsBack();
                return;
            }

            if (_lobbyPause.IsControlsVisible)
            {
                if ((keyboardBack || gamepadMenu || gamepadBack)
                    && !_lobbyPause.BlocksControlsBackShortcut)
                {
                    _lobbyPause.HandleControlsBack();
                }

                return;
            }

            if (_lobbyPause.IsVisible)
            {
                if (keyboardBack || gamepadMenu)
                {
                    if (_lobbyPause.IsConfirmationVisible)
                    {
                        _lobbyPause.HideConfirmation();
                    }
                    else
                    {
                        CloseLobbyPause();
                    }
                }
                else if (gamepadBack)
                {
                    if (_lobbyPause.IsConfirmationVisible)
                    {
                        _lobbyPause.HideConfirmation();
                    }
                    else
                    {
                        CloseLobbyPause();
                    }
                }

                return;
            }

            if (_pendingConfirmation != ConfirmationAction.None) return;
            if ((_connectionServiceContract == null || !_connectionServiceContract.IsConnected)
                && (keyboardBack || gamepadMenu || gamepadBack))
            {
                if (_mainMenu.IsSubpanelVisible)
                {
                    _mainMenu.HandleBack();
                    _ignoreMenuInputFrame = Time.frameCount;
                }
                else if (_mainMenu.IsHomeVisible) HandleQuitRequested();
                return;
            }

            bool keyboardSettings = Keyboard.current != null
                && Keyboard.current.tabKey.wasPressedThisFrame;
            bool gamepadSettings = Gamepad.current != null
                && Gamepad.current.buttonNorth.wasPressedThisFrame;
            if ((keyboardSettings || gamepadSettings)
                && _connectionServiceContract != null
                && _connectionServiceContract.IsHost
                && _pendingConfirmation == ConfirmationAction.None
                && !_mapVotingController.IsVisible
                && _lobbyPanel.activeInHierarchy)
            {
                if (_matchSettingsPanel.IsOpen)
                {
                    _matchSettingsPanel.Hide();
                }
                else
                {
                    _matchSettingsPanel.Show();
                }

                return;
            }

            if ((keyboardBack || gamepadMenu)
                && _connectionServiceContract != null
                && _connectionServiceContract.IsConnected
                && _pendingConfirmation == ConfirmationAction.None
                && !_matchSettingsPanel.IsOpen)
            {
                OpenLobbyPause();
            }
        }

        private void HandlePlayRequested()
        {
            ShowConnectionPanel();
            _connectionPanel.ShowIdle();
        }

        private void ShowMainMenu()
        {
            _playerNamePanel.Hide();
            _lobbyPause.SetVisible(false);
            _confirmationPanel.Hide();
            _pendingConfirmation = ConfirmationAction.None;
            _matchSettingsPanel.ResetView();
            _connectionPanel.gameObject.SetActive(false);
            _lobbyPanel.SetActive(false);
            _mainMenu.ShowHome();
            _ignoreMenuInputFrame = Time.frameCount;
        }

        private void SetOfflineInteractionEnabled(bool value)
        {
            _connectionPanel.SetInteractionEnabled(value);
            _mainMenu.SetInteractionEnabled(value);
        }

        private void RestoreOfflineInteraction()
        {
            SetOfflineInteractionEnabled(true);
            if (_connectionPanel.gameObject.activeInHierarchy)
                _connectionPanel.FocusDefaultButton();
            else
                _mainMenu.FocusDefaultButton();
        }

        private async void HandleHostRequested()
        {
            if (_connectionServiceContract == null)
            {
                ShowConnectionError("SERVIÇO DE REDE NÃO CONFIGURADO");
                return;
            }

            _connectionPanel.ShowConnecting("CRIANDO SALA...");
            await _connectionServiceContract.StartHostAsync();
        }

        private async void HandleClientRequested(string joinCode)
        {
            if (_connectionServiceContract == null)
            {
                ShowConnectionError("SERVIÇO DE REDE NÃO CONFIGURADO");
                return;
            }

            _connectionPanel.ShowConnecting("ENTRANDO NA SALA...");
            await _connectionServiceContract.StartClientAsync(joinCode);
        }

        private async void HandleCancelConnectionRequested()
        {
            if (_connectionServiceContract != null)
            {
                await _connectionServiceContract.ShutdownAsync();
            }

            ShowConnectionPanel();
            _connectionPanel.ShowIdle();
        }

        private void HandleQuitRequested()
        {
            OpenConfirmation(ConfirmationAction.Quit, "DESEJA REALMENTE SAIR?");
        }

        private void HandleOptionsRequested()
        {
            OpenLobbyPause();
        }

        private void HandlePlayerNameRequested()
        {
            _returnToOptionsAfterName = true;
            _lobbyPause.SetVisible(false);
            _playerNamePanel.Show();
        }

        private void HandlePlayerNameClosed()
        {
            _ignoreMenuInputFrame = Time.frameCount;
            if (_returnToOptionsAfterName)
            {
                OpenLobbyPause();
                return;
            }

            RestoreOfflineInteraction();
        }

        private void HandleDisconnectRequested()
        {
            string message = _connectionServiceContract != null && _connectionServiceContract.IsHost
                ? "ENCERRAR LOBBY?"
                : "DESCONECTAR DO LOBBY?";
            OpenConfirmation(ConfirmationAction.Disconnect, message);
        }

        private async void HandleConfirmationConfirmed()
        {
            ConfirmationAction confirmedAction = _pendingConfirmation;
            _pendingConfirmation = ConfirmationAction.None;

            if (confirmedAction == ConfirmationAction.Quit)
            {
                QuitApplication();
                return;
            }

            if (confirmedAction != ConfirmationAction.Disconnect)
            {
                return;
            }

            _confirmationPanel.Hide();
            ShowConnectionPanel();
            _connectionPanel.ShowConnecting("ENCERRANDO SALA...");
            if (_connectionServiceContract != null)
            {
                await _connectionServiceContract.ShutdownAsync();
            }

            ShowMainMenu();
        }

        private static void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void HandleConfirmationCanceled()
        {
            _ignoreMenuInputFrame = Time.frameCount;
            ConfirmationAction canceledAction = _pendingConfirmation;
            _pendingConfirmation = ConfirmationAction.None;
            _confirmationPanel.Hide();

            if (canceledAction == ConfirmationAction.Disconnect)
            {
                SetLobbyInteractionEnabled(true);
                return;
            }

            RestoreOfflineInteraction();
        }

        private void HandleConnected()
        {
            ShowLobbyPanel();
        }

        private void ShowRecoveringConnection()
        {
            ShowConnectionPanel();
            _connectionPanel.ShowConnecting("RECUPERANDO A SALA...");
        }

        private void HandleDisconnected()
        {
            ShowMainMenu();
        }

        private void HandleConnectionFailed(LobbyConnectionFailure failure)
        {
            string message = failure switch
            {
                LobbyConnectionFailure.NetworkManagerUnavailable => "NETWORK MANAGER NÃO CONFIGURADO",
                LobbyConnectionFailure.AlreadyRunning => "UMA CONEXÃO JÁ ESTÁ ATIVA",
                LobbyConnectionFailure.HostUnavailable => "HOST NÃO ENCONTRADO",
                LobbyConnectionFailure.TransportFailure => "FALHA NO TRANSPORTE DE REDE",
                LobbyConnectionFailure.ServicesUnavailable => "SERVIÇOS UNITY INDISPONÍVEIS",
                LobbyConnectionFailure.AuthenticationFailed => "FALHA NA AUTENTICAÇÃO",
                LobbyConnectionFailure.InvalidJoinCode => "CÓDIGO INVÁLIDO",
                LobbyConnectionFailure.SessionNotFound => "SALA NÃO ENCONTRADA",
                LobbyConnectionFailure.SessionConflict => "SESSÃO ANTERIOR AINDA ATIVA",
                _ => "NÃO FOI POSSÍVEL INICIAR A CONEXÃO"
            };

            ShowConnectionError(message);
        }

        private void ShowConnectionPanel()
        {
            _playerNamePanel.Hide();
            _lobbyPause.SetVisible(false);
            _pendingConfirmation = ConfirmationAction.None;
            _confirmationPanel.Hide();
            _matchSettingsPanel.ResetView();
            _mainMenu.ShowConnection();
            _connectionPanel.gameObject.SetActive(true);
            _connectionPanel.SetInteractionEnabled(true);
            _lobbyPanel.SetActive(false);
        }

        private void ShowLobbyPanel()
        {
            _pendingConfirmation = ConfirmationAction.None;
            _confirmationPanel.Hide();
            _mainMenu.Hide();
            _connectionPanel.gameObject.SetActive(false);
            _lobbyPanel.SetActive(true);
            _matchSettingsPanel.ResetView();
            _matchSettingsPanel.SetMenuVisible(
                _connectionServiceContract.IsHost && !_mapVotingController.IsVisible);
            RefreshSessionCode();
            SetLobbyInteractionEnabled(true);
        }

        private void RefreshSessionCode()
        {
            if (_sessionCodeText == null || _connectionServiceContract == null)
            {
                return;
            }

            bool showCode = _connectionServiceContract.IsHost
                && !string.IsNullOrWhiteSpace(_connectionServiceContract.JoinCode);
            _sessionCodeText.gameObject.SetActive(showCode);
            if (showCode)
            {
                _sessionCodeText.text =
                    $"CÓDIGO DA SALA: <size=26>"
                    + $"{_connectionServiceContract.JoinCode}</size>";
            }
        }

        private void HandleMatchSettingsOpened()
        {
            SetLobbyInteractionEnabled(false);
        }

        private void HandleMatchSettingsClosed()
        {
            SetLobbyInteractionEnabled(true);
        }

        private void HandleMapVotingVisibilityChanged(bool isVisible)
        {
            _matchSettingsPanel.SetMenuVisible(
                !isVisible
                && _connectionServiceContract != null
                && _connectionServiceContract.IsHost);
            SetLobbyInteractionEnabled(true);
        }

        private void ShowConnectionError(string message)
        {
            ShowConnectionPanel();
            _connectionPanel.ShowError(message);
        }

        private void OpenConfirmation(ConfirmationAction action, string message)
        {
            _pendingConfirmation = action;

            if (action == ConfirmationAction.Disconnect)
            {
                SetLobbyInteractionEnabled(false);
            }
            else
            {
                SetOfflineInteractionEnabled(false);
            }

            _confirmationPanel.Show(message);
        }

        private void SetLobbyInteractionEnabled(bool isEnabled)
        {
            bool mapVotingIsVisible = _mapVotingController.IsVisible;
            _characterSelectionController.SetInteractionEnabled(isEnabled && !mapVotingIsVisible);
            _mapVotingController.SetInteractionEnabled(isEnabled && mapVotingIsVisible);
        }

        private void InitializeLobbyPause()
        {
            if (_lobbyPause == null)
            {
                Debug.LogError(
                    "LobbyUIController precisa de uma referência para o pause do lobby.",
                    this);
                enabled = false;
                return;
            }

            InputActionAsset actions = FindFirstObjectByType<InputSystemUIInputModule>()?.actionsAsset;
            bool inLobby = _connectionServiceContract != null && _connectionServiceContract.IsConnected;
            if (!_lobbyPause.Initialize(
                actions,
                inLobby ? "PAUSE" : "OPÇÕES",
                "VOLTAR",
                inLobby ? "DESCONECTAR" : "SAIR DO JOGO",
                inLobby ? "DESCONECTAR DO LOBBY?" : "DESEJA SAIR DO JOGO?"))
            {
                enabled = false;
                return;
            }

            _lobbyPause.SetVisible(false);
        }

        private void OpenLobbyPause()
        {
            InitializeLobbyPause();
            if (_connectionServiceContract != null && _connectionServiceContract.IsConnected)
                SetLobbyInteractionEnabled(false);
            else
                SetOfflineInteractionEnabled(false);
            _lobbyPause.SetVisible(true);
        }

        private void CloseLobbyPause()
        {
            if (_lobbyPause == null
                || (!_lobbyPause.IsVisible && !_lobbyPause.IsControlsVisible))
            {
                return;
            }

            _lobbyPause.SetVisible(false);
            _ignoreMenuInputFrame = Time.frameCount;
            if (_connectionServiceContract != null && _connectionServiceContract.IsConnected)
                SetLobbyInteractionEnabled(true);
            else
            {
                RestoreOfflineInteraction();
            }
        }

        private async void HandleLobbyPauseExitConfirmed()
        {
            if (_connectionServiceContract == null || !_connectionServiceContract.IsConnected)
            {
                QuitApplication();
                return;
            }
            _lobbyPause.SetBusy(true);
            _lobbyPause.SetVisible(false);
            ShowConnectionPanel();
            _connectionPanel.ShowConnecting("ENCERRANDO SALA...");
            if (_connectionServiceContract != null)
            {
                await _connectionServiceContract.ShutdownAsync();
            }

            _lobbyPause.SetBusy(false);
            ShowMainMenu();
        }

        private void HandleControlsClosed()
        {
            if (_connectionServiceContract != null && _connectionServiceContract.IsConnected)
            {
                if (_lobbyPause.IsVisible)
                {
                    SetLobbyInteractionEnabled(false);
                }

                return;
            }

            SetOfflineInteractionEnabled(!_lobbyPause.IsVisible);
        }
    }
}
