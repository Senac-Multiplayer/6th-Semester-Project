using System;
using Unity.Collections;
using Unity.Netcode;

namespace PiGame.Lobby
{
    // Um resultado fechado pelo servidor: os votos posteriores não mudam a roleta em andamento.
    public struct LobbyMapDrawData : INetworkSerializable, IEquatable<LobbyMapDrawData>
    {
        public const float DrawSeconds = 6.5f;
        public const float RandomWinnerHoldSeconds = 1f;
        public FixedList32Bytes<LobbyMapId> Options;
        public LobbyMapId WinningOption;
        public LobbyMapId MapId;
        public double StartsAt;

        public static LobbyMapDrawData Empty => new()
        {
            WinningOption = LobbyMapId.None,
            MapId = LobbyMapId.None
        };
        public bool HasTie => Options.Length > 1;
        public bool IsRandom => WinningOption == LobbyMapId.Random;
        public float RandomStartsAfter => HasTie ? DrawSeconds + RandomWinnerHoldSeconds : 0f;
        public float Duration => (HasTie ? DrawSeconds : 0f)
            + (IsRandom ? DrawSeconds + (HasTie ? RandomWinnerHoldSeconds : 0f) : 0f);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref WinningOption);
            serializer.SerializeValue(ref MapId);
            serializer.SerializeValue(ref StartsAt);
            int count = Options.Length;
            serializer.SerializeValue(ref count);
            if (serializer.IsReader) Options.Clear();
            for (int i = 0; i < count; i++)
            {
                LobbyMapId option = serializer.IsReader ? LobbyMapId.None : Options[i];
                serializer.SerializeValue(ref option);
                if (serializer.IsReader) Options.Add(option);
            }
        }

        public bool Equals(LobbyMapDrawData other) => WinningOption == other.WinningOption
            && MapId == other.MapId && StartsAt.Equals(other.StartsAt) && Options.Equals(other.Options);
    }
}
