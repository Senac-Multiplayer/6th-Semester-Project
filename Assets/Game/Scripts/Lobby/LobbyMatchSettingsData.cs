using System;
using Unity.Netcode;

namespace PiGame.Lobby
{
    public struct LobbyMatchSettingsData :
        INetworkSerializable,
        IEquatable<LobbyMatchSettingsData>
    {
        public int DurationMinutes;
        public LobbyMatchMode Mode;
        public int MinimumPlayers;
        public bool RequireUniqueCharacters;
        public int KillLimit;

        public LobbyMatchSettingsData(
            int durationMinutes,
            LobbyMatchMode mode,
            int minimumPlayers,
            bool requireUniqueCharacters,
            int killLimit = 3)
        {
            DurationMinutes = durationMinutes;
            Mode = mode;
            MinimumPlayers = minimumPlayers;
            RequireUniqueCharacters = requireUniqueCharacters;
            KillLimit = killLimit;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref DurationMinutes);
            serializer.SerializeValue(ref Mode);
            serializer.SerializeValue(ref MinimumPlayers);
            serializer.SerializeValue(ref RequireUniqueCharacters);
            serializer.SerializeValue(ref KillLimit);
        }

        public bool Equals(LobbyMatchSettingsData other)
        {
            return DurationMinutes == other.DurationMinutes
                && Mode == other.Mode
                && MinimumPlayers == other.MinimumPlayers
                && RequireUniqueCharacters == other.RequireUniqueCharacters
                && KillLimit == other.KillLimit;
        }

        public override bool Equals(object obj)
        {
            return obj is LobbyMatchSettingsData other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = DurationMinutes;
                hashCode = (hashCode * 397) ^ (int)Mode;
                hashCode = (hashCode * 397) ^ MinimumPlayers;
                hashCode = (hashCode * 397) ^ RequireUniqueCharacters.GetHashCode();
                hashCode = (hashCode * 397) ^ KillLimit;
                return hashCode;
            }
        }

        public static bool operator ==(
            LobbyMatchSettingsData left,
            LobbyMatchSettingsData right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            LobbyMatchSettingsData left,
            LobbyMatchSettingsData right)
        {
            return !left.Equals(right);
        }
    }
}
