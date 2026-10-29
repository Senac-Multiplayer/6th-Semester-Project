using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering.Universal;

namespace PiGame.Gameplay
{
    public class MatchIntroPresenter : MonoBehaviour
    {
        [SerializeField] private MatchController _matchController;
        [SerializeField] private Camera _sceneCamera;
        [SerializeField] private TMP_Text _countdownLabel;
        [SerializeField, Min(0.5f)] private float _closeupSize = 2.2f;
        [SerializeField, Min(0f)] private float _panDistance = 0.6f;
        [Tooltip("Ligue aqui os sons: o evento recebe 3, 2, 1 e 0 (JÁ). Executado localmente uma vez por etapa.")]
        [SerializeField] private UnityEvent<int> _countdownStepChanged = new UnityEvent<int>();

        private Vector3 _normalPosition;
        private float _normalSize;
        private Vector3[] _playerPositions;
        private int _lastStep = -1;
        private bool _configured;
        private bool _cameraRestored = true;
        private PixelPerfectCamera _pixelPerfect;
        private bool _pixelPerfectWasEnabled;

        private void Awake()
        {
            if (!_matchController || !_sceneCamera || !_countdownLabel || !_sceneCamera.orthographic)
            {
                Debug.LogError("Configure MatchController, câmera ortográfica e texto da contagem no MatchIntroPresenter.", this);
                enabled = false;
                return;
            }
            _configured = true;
            _normalPosition = _sceneCamera.transform.position;
            _normalSize = _sceneCamera.orthographicSize;
            _pixelPerfect = _sceneCamera.GetComponent<PixelPerfectCamera>();
            _pixelPerfectWasEnabled = _pixelPerfect && _pixelPerfect.enabled;
            _countdownLabel.enabled = false;
        }

        private IEnumerator Start()
        {
            if (!_configured) yield break;
            // Cada client confirma apenas quando já recebeu os personagens e seus slots.
            while (!_matchController.IsSpawned || _matchController.ScoreCount == 0
                || FindObjectsByType<NetworkPlayerState>(FindObjectsSortMode.None)
                    .Count(p => p.IsSpawned && p.PlayerSlot >= 0) != _matchController.ScoreCount)
                yield return null;

            _playerPositions = FindObjectsByType<NetworkPlayerState>(FindObjectsSortMode.None)
                .Where(p => p.IsSpawned && p.PlayerSlot >= 0).OrderBy(p => p.PlayerSlot)
                .Select(p => p.transform.position).ToArray();
            _matchController.ReadyForIntroRpc();
        }

        private void LateUpdate()
        {
            if (!_matchController.IsSpawned || _playerPositions == null) return;
            float elapsed = _matchController.IntroElapsed;
            if (_matchController.Phase == MatchPhase.PresentingPlayers && elapsed >= 0f)
            {
                PresentPlayers(elapsed);
                return;
            }

            RestoreCamera();
            float countdown = elapsed - _matchController.PresentationDuration;
            bool counting = _matchController.Phase == MatchPhase.Countdown;
            bool showGo = _matchController.Phase == MatchPhase.Playing && countdown >= 3f && countdown < 3.65f;
            _countdownLabel.enabled = counting || showGo;
            if (!_countdownLabel.enabled) return;

            int step = counting ? Mathf.Clamp(3 - Mathf.FloorToInt(countdown), 1, 3) : 0;
            if (step != _lastStep)
            {
                _lastStep = step;
                _countdownLabel.text = step == 0 ? "JÁ" : step.ToString();
                _countdownLabel.color = step == 0 ? new Color(0.4f, 0.95f, 0.55f)
                    : new Color(0.95f, 0.74f, 0.37f);
                // O Prado pode conectar seus métodos de áudio pelo Inspector, sem alterar o fluxo de rede.
                _countdownStepChanged.Invoke(step);
            }
            float pop = Mathf.SmoothStep(1.35f, 1f, Mathf.Clamp01((countdown % 1f) / 0.3f));
            _countdownLabel.rectTransform.localScale = Vector3.one * pop;
        }

        private void PresentPlayers(float elapsed)
        {
            _cameraRestored = false;
            // O Pixel Perfect controla o tamanho da câmera durante o render; liberar apenas o zoom da apresentação.
            if (_pixelPerfect && _pixelPerfect.enabled) _pixelPerfect.enabled = false;
            _countdownLabel.enabled = false;
            float visitsDuration = _playerPositions.Length * _matchController.SecondsPerPlayer;
            if (elapsed >= visitsDuration)
            {
                float t = Mathf.SmoothStep(0f, 1f, (elapsed - visitsDuration) / _matchController.CameraReturnSeconds);
                _sceneCamera.transform.position = Vector3.Lerp(Focus(_playerPositions.Length - 1, 1f), _normalPosition, t);
                _sceneCamera.orthographicSize = Mathf.Lerp(_closeupSize, _normalSize, t);
                return;
            }

            int index = Mathf.Min((int)(elapsed / _matchController.SecondsPerPlayer), _playerPositions.Length - 1);
            float localTime = elapsed - index * _matchController.SecondsPerPlayer;
            float approach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(localTime / 0.65f));
            Vector3 previous = index == 0 ? _normalPosition : Focus(index - 1, 1f);
            float pan = Mathf.Clamp01((localTime - 0.65f) / Mathf.Max(0.1f, _matchController.SecondsPerPlayer - 0.65f));
            _sceneCamera.transform.position = Vector3.Lerp(previous, Focus(index, pan), approach);
            _sceneCamera.orthographicSize = Mathf.Lerp(index == 0 ? _normalSize : _closeupSize, _closeupSize, approach);
        }

        private Vector3 Focus(int index, float pan)
        {
            Vector3 position = _playerPositions[index] + Vector3.up * 0.35f;
            position.x += Mathf.Lerp(-_panDistance * 0.5f, _panDistance * 0.5f, pan);
            position.z = _normalPosition.z;
            return position;
        }

        private void RestoreCamera()
        {
            if (_cameraRestored || !_sceneCamera) return;
            _sceneCamera.transform.position = _normalPosition;
            _sceneCamera.orthographicSize = _normalSize;
            if (_pixelPerfect) _pixelPerfect.enabled = _pixelPerfectWasEnabled;
            _cameraRestored = true;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (!_configured) return;
            RestoreCamera();
            _countdownLabel.enabled = false;
        }
    }
}
