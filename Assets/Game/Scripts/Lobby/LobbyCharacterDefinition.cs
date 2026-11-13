using Unity.Netcode;
using UnityEngine;

namespace PiGame.Lobby
{
    [CreateAssetMenu(fileName = "data_character_", menuName = "Pi Game/Lobby/Character")]
    public class LobbyCharacterDefinition : ScriptableObject
    {
        [SerializeField] private LobbyCharacterId _id = LobbyCharacterId.None;
        [SerializeField] private string _displayName;
        [SerializeField] private Color _color = Color.white;
        [SerializeField] private Sprite _portrait;
        [SerializeField] private NetworkObject _playerPrefab;

        [Header("Fundo do seletor do lobby")]
        [SerializeField] private Sprite _lobbyBackground;
        [SerializeField] private Sprite _lobbyBackgroundDetails;

        public Sprite LobbyBackground => _lobbyBackground;
        public Sprite LobbyBackgroundDetails => _lobbyBackgroundDetails;
        public LobbyCharacterId Id => _id;
        public string DisplayName => _displayName;
        public Color Color => _color;
        public Sprite Portrait => _portrait;
        public NetworkObject PlayerPrefab => _playerPrefab;
    }
}
