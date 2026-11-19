using System;
using UnityEngine;

namespace PiGame.Lobby
{
    [CreateAssetMenu(
        fileName = "data_lobby_rules",
        menuName = "Pi Game/Lobby/Rules Definition")]
    public class LobbyRulesDefinition : ScriptableObject
    {
        [Header("Players")]
        [SerializeField, Range(1, 4)] private int _minimumPlayers = 2;
        [SerializeField, Range(1, 4)] private int _maximumPlayers = 4;

        [Header("Characters")]
        [SerializeField] private bool _requireUniqueCharacters = true;
        [SerializeField] private LobbyCharacterId[] _availableCharacters =
        {
            LobbyCharacterId.Cowboy,
            LobbyCharacterId.Irrigator
        };

        [Header("Match defaults")]
        [SerializeField, Range(2, 10)] private int _defaultDurationMinutes = 3;
        [SerializeField] private LobbyMatchMode _defaultMatchMode = LobbyMatchMode.Solo;

        public int MinimumPlayers => _minimumPlayers;
        public int MaximumPlayers => _maximumPlayers;
        public bool RequireUniqueCharacters => _requireUniqueCharacters;
        public int AvailableCharacterCount => _availableCharacters?.Length ?? 0;
        public int DefaultDurationMinutes => _defaultDurationMinutes;
        public LobbyMatchMode DefaultMatchMode => _defaultMatchMode;

        public LobbyCharacterId GetAvailableCharacter(int index)
        {
            if (_availableCharacters == null
                || index < 0
                || index >= _availableCharacters.Length)
            {
                return LobbyCharacterId.None;
            }

            return _availableCharacters[index];
        }

        public bool IsCharacterAvailable(LobbyCharacterId characterId)
        {
            if (_availableCharacters == null)
            {
                return false;
            }

            return Array.IndexOf(_availableCharacters, characterId) >= 0;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _minimumPlayers = Mathf.Clamp(_minimumPlayers, 1, 4);
            _maximumPlayers = Mathf.Clamp(_maximumPlayers, _minimumPlayers, 4);
            _defaultDurationMinutes = Mathf.Clamp(_defaultDurationMinutes, 2, 10);

            if (_availableCharacters == null || _availableCharacters.Length == 0)
            {
                _availableCharacters = new[]
                {
                    LobbyCharacterId.Cowboy,
                    LobbyCharacterId.Irrigator
                };
            }

            if (_defaultMatchMode != LobbyMatchMode.Team
                && _defaultMatchMode != LobbyMatchMode.Solo)
            {
                _defaultMatchMode = LobbyMatchMode.Solo;
            }
        }
#endif
    }
}
