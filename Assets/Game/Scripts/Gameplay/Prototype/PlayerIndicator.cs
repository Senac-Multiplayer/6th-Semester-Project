using TMPro;
using UnityEngine;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(NetworkPlayerState))]
    public class PlayerIndicator : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _marker;
        [SerializeField] private TextMeshPro _playerLabel;
        [SerializeField] private SpriteRenderer _abilityBackground;
        [SerializeField] private SpriteRenderer _abilityFill;
        [SerializeField] private TextMeshPro _localHintLabel;
        [SerializeField, Min(0f)] private float _localHintSeconds = 3f;

        private NetworkPlayerState _playerState;
        private bool _wasVisible;
        private bool _showLocalHint;
        private float _localHintEndsAt;
        private Vector3 _abilityFillScale;
        private Vector3 _abilityFillPosition;

        private void Awake()
        {
            _playerState = GetComponent<NetworkPlayerState>();
            if (_marker == null || _playerLabel == null || _abilityBackground == null || _abilityFill == null || _localHintLabel == null)
            {
                Debug.LogError("Configure marcador, nome, barra de habilidade e aviso local no PlayerIndicator do prefab.", this);
                enabled = false;
                return;
            }
            _abilityFillScale = _abilityFill.transform.localScale;
            _abilityFillPosition = _abilityFill.transform.localPosition;
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

        private void Update()
        {
            if (_showLocalHint && Time.unscaledTime >= _localHintEndsAt)
            {
                _showLocalHint = false;
                _localHintLabel.enabled = false;
            }

            RefreshAbility();
        }

        private void RefreshAbility()
        {
            float remaining = _playerState.AbilityCooldownRemaining;
            int availability = _playerState.AbilityAvailability;
            bool visible = _wasVisible && (remaining >= 0f || availability >= 0);
            _abilityBackground.enabled = visible;
            _abilityFill.enabled = visible;
            if (!visible) return;

            float progress = remaining >= 0f ? _playerState.AbilityCooldownProgress : 1f;
            _abilityFill.transform.localScale = new Vector3(_abilityFillScale.x * progress, _abilityFillScale.y, _abilityFillScale.z);
            _abilityFill.transform.localPosition = _abilityFillPosition
                + Vector3.left * (_abilityFillScale.x * (1f - progress) * 0.5f);
            _abilityFill.color = availability == 2 ? new Color(0.3f, 0.85f, 1f)
                : remaining > 0f ? new Color(0.95f, 0.74f, 0.37f)
                : availability == 0 ? new Color(0.32f, 0.32f, 0.32f)
                : new Color(0.4f, 0.95f, 0.55f);
        }

        private void Refresh()
        {
            bool visible = _playerState.IsSpawned && _playerState.IsAlive && _playerState.PlayerSlot >= 0
                && _playerState.IndicatorsVisible;
            if (visible && !_wasVisible && _playerState.IsOwner)
            {
                _showLocalHint = true;
                _localHintEndsAt = Time.unscaledTime + _localHintSeconds;
            }
            _wasVisible = visible;
            _marker.enabled = visible;
            _playerLabel.enabled = visible;
            RefreshAbility();
            _localHintLabel.enabled = visible && _showLocalHint;
            _marker.color = _playerState.IndicatorColor;
            _playerLabel.color = _playerState.IndicatorColor;
            RefreshName();
        }

        private void RefreshName()
        {
            _playerLabel.text = $"P{_playerState.PlayerSlot + 1}\n<size=75%>{_playerState.PlayerName}</size>";
        }
    }
}
