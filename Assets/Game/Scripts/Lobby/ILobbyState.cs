using System;

namespace PiGame.Lobby
{
    public interface ILobbyState
    {
        event Action PlayersChanged;
        event Action<int> CharacterReadyRejected;
        event Action StageChanged;
        event Action MapVotesChanged;
        event Action MatchSettingsChanged;

        int PlayerCount { get; }
        int MapVoteCount { get; }
        ulong LocalClientId { get; }
        LobbyStage Stage { get; }
        LobbyMapDrawData MapDraw { get; }
        double ServerTime { get; }
        LobbyMatchSettingsData MatchSettings { get; }
        int SelectableCharacterCount { get; }
        bool RequireUniqueCharacters { get; }
        bool LocalClientIsHost { get; }

        LobbyPlayerData GetPlayer(int index);
        LobbyMapVoteData GetMapVote(int index);
        LobbyCharacterId GetSelectableCharacter(int index);
        bool TryGetPlayer(ulong clientId, out LobbyPlayerData player);
        bool TryGetMapVote(ulong clientId, out LobbyMapVoteData vote);
        bool TryGetCharacterLock(
            LobbyCharacterId characterId,
            ulong requestingClientId,
            out int lockingPlayerSlot);
        void RequestCharacterSelection(LobbyCharacterId characterId);
        void RequestInputDevice(LobbyInputDeviceKind inputDevice);
        void RequestReadyState(bool isReady);
        void RequestMapVote(LobbyMapId mapId);
        void RequestMapVoteConfirmation(bool isConfirmed);
        void RequestReturnToCharacterSelection();
        void RequestMatchSettings(int durationMinutes, LobbyMatchMode mode);
    }
}
