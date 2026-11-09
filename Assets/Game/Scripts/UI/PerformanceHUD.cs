using PiGame.Networking;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PiGame.UI
{
    public class PerformanceHUD : MonoBehaviour
    {
        [SerializeField] private TMP_Text _pingLabel;
        [SerializeField] private TMP_Text _fpsLabel;
        [Tooltip("No lobby, o ping já aparece nos slots dos jogadores.")]
        [SerializeField] private bool _showConnection = true;
        [SerializeField] private Color _normalColor = Color.white;
        [SerializeField] private Color _warningColor = new(1f, 0.35f, 0.35f);
        private float _seconds;
        private int _frames;

        private void Awake()
        {
            if (!_pingLabel || !_fpsLabel)
            {
                Debug.LogError("Configure os textos de ping e FPS no PerformanceHUD.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            _seconds += Time.unscaledDeltaTime;
            _frames++;
            if (_seconds < 0.5f) return;
            bool currentScene = SceneManager.GetActiveScene() == gameObject.scene;
            _fpsLabel.enabled = currentScene && GameDisplaySettings.ShowFps;
            _fpsLabel.text = $"FPS: {Mathf.RoundToInt(_frames / _seconds)}";
            _frames = 0;
            _seconds = 0f;

            var network = NetworkManager.Singleton;
            _pingLabel.enabled = currentScene && _showConnection && GameDisplaySettings.ShowPing
                && network != null && network.IsConnectedClient;
            if (!_pingLabel.enabled) return;
            var monitor = NetworkConnectionMonitor.Instance;
            bool poor = false;
            if (network.IsHost)
            {
                poor = monitor != null && monitor.HostUnstable;
                _pingLabel.text = poor ? "HOST - CONEXÃO INSTÁVEL" : "HOST";
            }
            else if (monitor != null && monitor.TryGetConnection(network.LocalClientId, out int ping, out ConnectionQuality quality))
            {
                poor = quality == ConnectionQuality.Poor;
                _pingLabel.text = ping >= 0 ? $"PING: {ping} ms" : "PING: -- ms";
            }
            else _pingLabel.text = "PING: -- ms";
            _pingLabel.color = poor ? _warningColor : _normalColor;
        }
    }
}
