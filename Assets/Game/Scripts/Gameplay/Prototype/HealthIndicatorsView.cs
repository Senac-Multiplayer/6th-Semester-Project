using System;
using UnityEngine;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(NetworkPlayerState))]
    public class HealthIndicatorsView : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private SpriteRenderer[] _segments;
        [SerializeField] private Color _healthyColor = new Color(0.2f, 0.95f, 0.35f);
        [SerializeField] private Color _lowHealthColor = new Color(1f, 0.72f, 0.15f);
        [SerializeField] private Color _emptyColor = new Color(0.15f, 0.15f, 0.15f);

        private NetworkPlayerState _playerState;

        private void Awake()
        {
            _playerState = GetComponent<NetworkPlayerState>();
            if (!_root || _segments == null || _segments.Length != _playerState.MaximumHealth
                || Array.Exists(_segments, segment => !segment))
            {
                Debug.LogError("Configure a raiz e um segmento por ponto de vida no HealthIndicatorsView.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            _playerState.StateChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            _playerState.StateChanged -= Refresh;
        }

        private void Refresh()
        {
            _root.SetActive(_playerState.IsSpawned && _playerState.IsAlive && _playerState.PlayerSlot >= 0
                && _playerState.IndicatorsVisible);
            Color filledColor = _playerState.CurrentHealth == 1 ? _lowHealthColor : _healthyColor;
            for (int i = 0; i < _segments.Length; i++)
                _segments[i].color = i < _playerState.CurrentHealth ? filledColor : _emptyColor;
        }
    }
}
