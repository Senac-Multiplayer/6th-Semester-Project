using System.Collections;
using System.Linq;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering.Universal;

namespace PiGame.Gameplay
{
    public class MatchIntroPresenter : MonoBehaviour
    {
        [SerializeField] private MatchController _matchController;
        [SerializeField] private Camera _sceneCamera;
        [SerializeField] private CinemachineBrain _brain;
        [SerializeField] private CinemachineCamera _mapCamera;
        [SerializeField] private CinemachineCamera[] _playerCameras;
        [SerializeField] private TMP_Text _countdownLabel;
        [SerializeField, Min(0.5f)] private float _closeupSize = 2.2f;
        [SerializeField, Min(0f)] private float _panDistance = 0.6f;
        [Tooltip("Ligue aqui os sons: o evento recebe 3, 2, 1 e 0 (JÁ). Executado localmente uma vez por etapa.")]
        [SerializeField] private UnityEvent<int> _countdownStepChanged = new UnityEvent<int>();

        private Vector3 _normalPosition;
        private Quaternion _normalRotation;
        private float _normalSize;
        private Vector3[] _playerPositions;
        private int _lastStep = -1;
        private int _selectedCamera = -1;
        private bool _configured;
        private bool _prepared;
        private bool _cameraRestored = true;
        private PixelPerfectCamera _pixelPerfect;
        private bool _pixelPerfectWasEnabled;
        private float _presentationElapsed = -1f;
        private float _approachSeconds;

        private void Awake()
        {
            if (!_matchController || !_sceneCamera || !_brain || !_mapCamera || !_countdownLabel
                || !_sceneCamera.orthographic || _playerCameras == null || _playerCameras.Length != 4
                || _playerCameras.Any(c => !c))
            {
                Debug.LogError("Configure MatchController, câmera ortográfica, Brain, câmera do mapa, quatro câmeras dos jogadores e contagem no MatchIntroPresenter.", this);
                enabled = false;
                return;
            }
            _configured = true;
            _brain.enabled = false;
            _pixelPerfect = _sceneCamera.GetComponent<PixelPerfectCamera>();
            _countdownLabel.enabled = false;
        }

        private IEnumerator Start()
        {
            if (!_configured) yield break;
            while (!_matchController.IsSpawned || _matchController.ScoreCount == 0
                || FindObjectsByType<NetworkPlayerState>(FindObjectsSortMode.None)
                    .Count(p => p.IsSpawned && p.PlayerSlot >= 0) != _matchController.ScoreCount)
                yield return null;

            _playerPositions = FindObjectsByType<NetworkPlayerState>(FindObjectsSortMode.None)
                .Where(p => p.IsSpawned && p.PlayerSlot >= 0).OrderBy(p => p.PlayerSlot)
                .Select(p => p.transform.position).ToArray();
            _normalPosition = _sceneCamera.transform.position;
            _normalRotation = _sceneCamera.transform.rotation;
            _pixelPerfectWasEnabled = _pixelPerfect && _pixelPerfect.enabled;
            _approachSeconds = _brain.DefaultBlend.BlendTime;
            ConfigureCamera(_mapCamera, _normalPosition, CurrentMapSize());
            _mapCamera.Priority = 10;
            for (int i = 0; i < _playerCameras.Length; i++)
            {
                ConfigureCamera(_playerCameras[i], i < _playerPositions.Length ? Focus(i, 0f) : _normalPosition, _closeupSize);
                _playerCameras[i].Priority = 0;
            }
            _normalSize = CurrentMapSize();
            _brain.enabled = true;
            _prepared = true;
            _cameraRestored = false;
            // Deixar o Brain assumir o mesmo enquadramento do mapa antes de iniciar qualquer blend.
            yield return null;
            _matchController.ReadyForIntroRpc();
        }

        private float CurrentMapSize() => _pixelPerfectWasEnabled
            ? _pixelPerfect.orthographicSize : _sceneCamera.orthographicSize;

        private void ConfigureCamera(CinemachineCamera camera, Vector3 position, float size)
        {
            camera.transform.SetPositionAndRotation(position, _normalRotation);
            var lens = LensSettings.FromCamera(_sceneCamera);
            lens.OrthographicSize = size;
            camera.Lens = lens;
        }

        private void LateUpdate()
        {
            if (!_matchController.IsSpawned || !_prepared) return;
            float elapsed = _matchController.IntroElapsed;
            if (_matchController.Phase == MatchPhase.WaitingForPlayers
                || (_matchController.Phase == MatchPhase.PresentingPlayers && elapsed < 0f && _presentationElapsed < 0f))
            {
                _normalSize = CurrentMapSize();
                ConfigureCamera(_mapCamera, _normalPosition, _normalSize);
                _brain.ManualUpdate(Time.frameCount, 0f);
                return;
            }
            if (_matchController.Phase == MatchPhase.PresentingPlayers)
            {
                float previous = Mathf.Max(0f, _presentationElapsed);
                _presentationElapsed = Mathf.Max(previous, elapsed);
                PresentPlayers(_presentationElapsed, _presentationElapsed - previous);
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
                _countdownLabel.color = step == 0 ? new Color(0.4f, 0.95f, 0.55f) : new Color(0.95f, 0.74f, 0.37f);
                // O Prado pode conectar seus métodos de áudio pelo Inspector, sem alterar o fluxo de rede.
                _countdownStepChanged.Invoke(step);
            }
            float pop = Mathf.SmoothStep(1.35f, 1f, Mathf.Clamp01((countdown % 1f) / 0.3f));
            _countdownLabel.rectTransform.localScale = Vector3.one * pop;
        }

        private void PresentPlayers(float elapsed, float delta)
        {
            if (_pixelPerfect && _pixelPerfect.enabled) _pixelPerfect.enabled = false;
            _countdownLabel.enabled = false;
            float visitsDuration = _playerPositions.Length * _matchController.SecondsPerPlayer;
            int index = elapsed >= visitsDuration ? -1
                : Mathf.Min((int)(elapsed / _matchController.SecondsPerPlayer), _playerPositions.Length - 1);
            if (index != _selectedCamera)
            {
                _selectedCamera = index;
                _brain.DefaultBlend.Time = index < 0 ? _matchController.CameraReturnSeconds : _approachSeconds;
                _mapCamera.Priority = index < 0 ? 20 : 0;
                for (int i = 0; i < _playerCameras.Length; i++) _playerCameras[i].Priority = i == index ? 20 : 0;
                (index < 0 ? _mapCamera : _playerCameras[index]).Prioritize();
            }
            if (index >= 0)
            {
                float localTime = elapsed - index * _matchController.SecondsPerPlayer;
                float pan = Mathf.Clamp01((localTime - _approachSeconds) / Mathf.Max(0.1f, _matchController.SecondsPerPlayer - _approachSeconds));
                _playerCameras[index].transform.position = Focus(index, pan);
            }
            // O Cinemachine faz os blends; o delta vem do relógio da partida e nunca volta para trás.
            _brain.ManualUpdate(Time.frameCount, delta);
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
            if (_brain) _brain.enabled = false;
            _sceneCamera.transform.SetPositionAndRotation(_normalPosition, _normalRotation);
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
