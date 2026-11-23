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
        [SerializeField] private Sprite _passiveAbilityIcon;
        [SerializeField] private TextMeshPro _localHintLabel;
        [SerializeField, Min(0f)] private float _localHintSeconds = 3f;
        [SerializeField, Range(0f, 1f)] private float _abilityOpacity = 0.7f;

        private NetworkPlayerState _playerState;
        private IrrigadorCombat _irrigadorCombat;
        private Sprite _normalAbilityIcon;
        private bool _wasVisible;
        private bool _showLocalHint;
        private float _localHintEndsAt;
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private MaterialPropertyBlock _abilityProperties;

        private void Awake()
        {
            _playerState = GetComponent<NetworkPlayerState>();
            _irrigadorCombat = GetComponent<IrrigadorCombat>();
            if (_marker == null || _playerLabel == null || _abilityBackground == null || _abilityFill == null || _localHintLabel == null)
            {
                Debug.LogError("Configure marcador, nome, barra de habilidade e aviso local no PlayerIndicator do prefab.", this);
                enabled = false;
                return;
            }
            _abilityProperties = new MaterialPropertyBlock();
            _normalAbilityIcon = _abilityFill.sprite;
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
            _abilityBackground.enabled = false;
            _abilityFill.enabled = visible;
            if (!visible) return;
            _abilityFill.sprite = _irrigadorCombat != null && _irrigadorCombat.IsMagnetActive
                && _passiveAbilityIcon != null ? _passiveAbilityIcon : _normalAbilityIcon;

            // Uma carga disponível mantém o ícone pronto enquanto a carga extra recarrega.
            float progress = availability > 0 ? 1f
                : remaining > 0f ? _playerState.AbilityCooldownProgress
                : availability == 0 ? 0f : 1f;
            _abilityFill.color = new Color(1f, 1f, 1f, _abilityOpacity);
            _abilityFill.GetPropertyBlock(_abilityProperties);
            _abilityProperties.SetFloat(ProgressId, progress);
            _abilityFill.SetPropertyBlock(_abilityProperties);
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
