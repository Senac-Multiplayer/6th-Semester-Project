using System.Collections.Generic;
using System.Linq;
using PiGame.Lobby;
using PiGame.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class LobbyConnectionView : MonoBehaviour
    {
        [SerializeField] private GameObject[] _roots;
        [SerializeField] private Text[] _labels;
        [Tooltip("Três barras por jogador, da menor para a maior.")]
        [SerializeField] private Image[] _bars;
        [SerializeField] private Color _goodColor = new(0.35f, 0.95f, 0.55f);
        [SerializeField] private Color _fairColor = new(1f, 0.8f, 0.3f);
        [SerializeField] private Color _badColor = new(1f, 0.35f, 0.35f);
        [SerializeField] private Color _emptyColor = new(0.3f, 0.32f, 0.36f);
        private readonly ulong[] _clients = new ulong[4];
        private readonly bool[] _occupied = new bool[4];
        private float _nextRefresh;

        private void Awake()
        {
            if (_roots == null || _labels == null || _bars == null
                || _roots.Length != 4 || _labels.Length != 4 || _bars.Length != 12
                || _roots.Any(x => !x) || _labels.Any(x => !x) || _bars.Any(x => !x))
            {
                Debug.LogError("Configure quatro slots, quatro textos e doze barras no LobbyConnectionView.", this);
                enabled = false;
            }
        }

        public void SetPlayers(IReadOnlyList<LobbyPlayerData> players)
        {
            if (!enabled) return;
            for (int i = 0; i < 4; i++) _occupied[i] = false;
            foreach (var player in players)
            {
                _clients[player.PlayerSlot] = player.ClientId;
                _occupied[player.PlayerSlot] = true;
            }
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.5f;
            Refresh();
        }

        private void Refresh()
        {
            var monitor = NetworkConnectionMonitor.Instance;
            for (int i = 0; i < 4; i++)
            {
                _roots[i].SetActive(_occupied[i] && GameDisplaySettings.ShowPing);
                if (!_occupied[i]) continue;
                bool host = _clients[i] == NetworkManager.ServerClientId;
                int ping = -1;
                ConnectionQuality quality = ConnectionQuality.Unknown;
                if (monitor != null) monitor.TryGetConnection(_clients[i], out ping, out quality);
                _labels[i].text = host ? "HOST" : ping < 0 ? "-- ms" : $"{ping} ms";
                _labels[i].color = host && monitor != null && monitor.HostUnstable ? _badColor : Color.white;
                Color color = quality == ConnectionQuality.Good ? _goodColor
                    : quality == ConnectionQuality.Fair ? _fairColor : _badColor;
                for (int b = 0; b < 3; b++)
                {
                    _bars[i * 3 + b].enabled = !host;
                    _bars[i * 3 + b].color = b < (int)quality ? color : _emptyColor;
                }
            }
        }
    }
}
