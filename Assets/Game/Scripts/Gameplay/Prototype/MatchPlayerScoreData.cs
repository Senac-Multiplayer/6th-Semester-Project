using System;
using PiGame.Lobby;
using Unity.Collections;
using Unity.Netcode;

namespace PiGame.Gameplay
{
    public struct MatchPlayerScoreData :
        INetworkSerializable,
        IEquatable<MatchPlayerScoreData>
    {
        public ulong ClientId;
        public int PlayerSlot;
        public LobbyTeam Team;
        public int Kills;
        public LobbyCharacterId CharacterId;
        public FixedString64Bytes PlayerName;

        public MatchPlayerScoreData(LobbyPlayerData player)
        {
            ClientId = player.ClientId;
            PlayerSlot = player.PlayerSlot;
            Team = player.Team;
            Kills = 0;
            CharacterId = player.CharacterId;
            PlayerName = player.PlayerName;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref PlayerSlot);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref Kills);
            serializer.SerializeValue(ref CharacterId);
            serializer.SerializeValue(ref PlayerName);
        }

        public bool Equals(MatchPlayerScoreData other)
        {
            return ClientId == other.ClientId
                && PlayerSlot == other.PlayerSlot
                && Team == other.Team
                && Kills == other.Kills
                && CharacterId == other.CharacterId
                && PlayerName.Equals(other.PlayerName);
        }

        public override bool Equals(object obj)
        {
            return obj is MatchPlayerScoreData other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = ClientId.GetHashCode();
                hashCode = (hashCode * 397) ^ PlayerSlot;
                hashCode = (hashCode * 397) ^ (int)Team;
                hashCode = (hashCode * 397) ^ Kills;
                hashCode = (hashCode * 397) ^ (int)CharacterId;
                hashCode = (hashCode * 397) ^ PlayerName.GetHashCode();
                return hashCode;
            }
        }
    }
}
