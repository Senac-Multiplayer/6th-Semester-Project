using System;
using TMPro;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace PiGame.UI
{
    public class RoomSlotUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text _roomName;
        [SerializeField] private TMP_Text _players;
        [SerializeField] private Button _joinButton;
        private string _sessionId;
        public event Action<string> JoinRequested;

        private void Awake()
        {
            if (!_roomName || !_players || !_joinButton)
            {
                Debug.LogError("Configure nome, jogadores e botão do RoomSlotUI no prefab.", this);
                enabled = false;
                return;
            }
            _joinButton.onClick.AddListener(Join);
        }

        public void Render(ISessionInfo room)
        {
            _sessionId = room.Id;
            _roomName.text = room.Name;
            _players.text = $"{room.MaxPlayers - room.AvailableSlots}/{room.MaxPlayers}";
            _joinButton.interactable = !room.IsLocked && room.AvailableSlots > 0;
        }

        public void Focus() => EventSystem.current.SetSelectedGameObject(_joinButton.gameObject);

        private void Join() => JoinRequested?.Invoke(_sessionId);
    }
}
