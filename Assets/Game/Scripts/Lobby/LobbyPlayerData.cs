using System;
using Unity.Collections;
using Unity.Netcode;

namespace PiGame.Lobby
{
    public struct LobbyPlayerData : INetworkSerializable, IEquatable<LobbyPlayerData>
    {
        public ulong ClientId;
        public int PlayerSlot;
        public LobbyTeam Team;
        public LobbyCharacterId CharacterId;
        public LobbyInputDeviceKind InputDevice;
        public bool IsReady;
        public FixedString64Bytes PlayerName;

        public LobbyPlayerData(
            ulong clientId,
            int playerSlot,
            LobbyCharacterId characterId = LobbyCharacterId.None,
            LobbyInputDeviceKind inputDevice = LobbyInputDeviceKind.Unknown,
            bool isReady = false,
            string playerName = "Jogador",
            LobbyTeam team = LobbyTeam.None)
        {
            ClientId = clientId;
            PlayerSlot = playerSlot;
            Team = team;
            CharacterId = characterId;
            InputDevice = inputDevice;
            IsReady = isReady;
            PlayerName = new FixedString64Bytes(LocalPlayerName.Normalize(playerName));
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref PlayerSlot);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref CharacterId);
            serializer.SerializeValue(ref InputDevice);
            serializer.SerializeValue(ref IsReady);
            serializer.SerializeValue(ref PlayerName);
        }

        public bool Equals(LobbyPlayerData other)
        {
            return ClientId == other.ClientId
                && PlayerSlot == other.PlayerSlot
                && Team == other.Team
                && CharacterId == other.CharacterId
                && InputDevice == other.InputDevice
                && IsReady == other.IsReady
                && PlayerName.Equals(other.PlayerName);
        }

        public override bool Equals(object obj)
        {
            return obj is LobbyPlayerData other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = ClientId.GetHashCode();
                hashCode = (hashCode * 397) ^ PlayerSlot;
                hashCode = (hashCode * 397) ^ (int)Team;
                hashCode = (hashCode * 397) ^ (int)CharacterId;
                hashCode = (hashCode * 397) ^ (int)InputDevice;
                hashCode = (hashCode * 397) ^ IsReady.GetHashCode();
                hashCode = (hashCode * 397) ^ PlayerName.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(LobbyPlayerData left, LobbyPlayerData right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(LobbyPlayerData left, LobbyPlayerData right)
        {
            return !left.Equals(right);
        }
    }
}
