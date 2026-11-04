using UnityEngine;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class DiagnosticsOptionsUI : MonoBehaviour
    {
        [SerializeField] private Toggle _showPing;
        [SerializeField] private Toggle _showFps;

        private void Awake()
        {
            if (!_showPing || !_showFps)
            {
                Debug.LogError("Configure os toggles de ping e FPS no DiagnosticsOptionsUI.", this);
                enabled = false;
                return;
            }
            _showPing.onValueChanged.AddListener(SetPing);
            _showFps.onValueChanged.AddListener(SetFps);
        }

        private void OnEnable()
        {
            _showPing.SetIsOnWithoutNotify(GameDisplaySettings.ShowPing);
            _showFps.SetIsOnWithoutNotify(GameDisplaySettings.ShowFps);
        }

        private void OnDestroy()
        {
            if (_showPing) _showPing.onValueChanged.RemoveListener(SetPing);
            if (_showFps) _showFps.onValueChanged.RemoveListener(SetFps);
        }

        private void SetPing(bool value) => GameDisplaySettings.ShowPing = value;
        private void SetFps(bool value) => GameDisplaySettings.ShowFps = value;
    }
}
