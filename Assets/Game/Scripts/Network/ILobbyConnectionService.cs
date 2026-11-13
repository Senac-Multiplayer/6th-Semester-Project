using System;
using System.Collections.Generic;
using Unity.Services.Multiplayer;
using System.Threading.Tasks;

namespace PiGame.Networking
{
    public enum LobbyConnectionFailure
    {
        NetworkManagerUnavailable,
        AlreadyRunning,
        StartFailed,
        HostUnavailable,
        TransportFailure,
        ServicesUnavailable,
        AuthenticationFailed,
        InvalidJoinCode,
        SessionNotFound,
        SessionConflict,
        NoAvailableRooms
    }

    public interface ILobbyConnectionService
    {
        event Action Connected;
        event Action Disconnected;
        event Action<LobbyConnectionFailure> ConnectionFailed;

        bool IsConnected { get; }
        bool IsHost { get; }
        string JoinCode { get; }

        Task StartHostAsync(string roomName = null, bool isPrivate = true);
        Task<IList<ISessionInfo>> QueryRoomsAsync();
        Task JoinRoomAsync(string sessionId);
        Task QuickJoinAsync();
        Task StartClientAsync(string joinCode);
        Task ShutdownAsync();
    }
}
