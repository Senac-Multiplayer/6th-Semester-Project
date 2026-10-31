using System;
using Unity.Netcode;

namespace PiGame.Lobby
{
    [Serializable]
    public struct LobbyMatchSettingsData :
        INetworkSerializable,
        IEquatable<LobbyMatchSettingsData>
    {
        public int DurationMinutes;
        public LobbyMatchMode Mode;
        public int MinimumPlayers;
        public bool RequireUniqueCharacters;

        public LobbyMatchSettingsData(
            int durationMinutes,
            LobbyMatchMode mode,
            int minimumPlayers,
            bool requireUniqueCharacters)
        {
            DurationMinutes = durationMinutes;
            Mode = mode;
            MinimumPlayers = minimumPlayers;
            RequireUniqueCharacters = requireUniqueCharacters;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref DurationMinutes);
            serializer.SerializeValue(ref Mode);
            serializer.SerializeValue(ref MinimumPlayers);
            serializer.SerializeValue(ref RequireUniqueCharacters);
        }

        public bool Equals(LobbyMatchSettingsData other)
        {
            return DurationMinutes == other.DurationMinutes
                && Mode == other.Mode
                && MinimumPlayers == other.MinimumPlayers
                && RequireUniqueCharacters == other.RequireUniqueCharacters;
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
