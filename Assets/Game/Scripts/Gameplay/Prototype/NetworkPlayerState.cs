using System.Collections;
using System;
using PiGame.Lobby;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using PiGame.Sound;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(NetworkObject), typeof(SpriteRenderer))]
    public class NetworkPlayerState : NetworkBehaviour
    {
        [SerializeField] private int _maximumHealth = 3;

        private readonly NetworkVariable<int> _playerSlot = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<LobbyCharacterId> _characterId =
            new NetworkVariable<LobbyCharacterId>(LobbyCharacterId.None);
        private readonly NetworkVariable<LobbyInputDeviceKind> _inputDevice =
            new NetworkVariable<LobbyInputDeviceKind>(LobbyInputDeviceKind.Unknown);
        private readonly NetworkVariable<Color> _indicatorColor =
            new NetworkVariable<Color>(Color.white);
        private readonly NetworkVariable<int> _currentHealth = new NetworkVariable<int>();
        private readonly NetworkVariable<bool> _isAlive = new NetworkVariable<bool>(true);
        private readonly NetworkVariable<bool> _matchActive = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<bool> _indicatorsVisible = new NetworkVariable<bool>(true);
        private readonly NetworkVariable<FixedString64Bytes> _playerName = new NetworkVariable<FixedString64Bytes>();
        private readonly NetworkVariable<double> _abilityReadyAt = new NetworkVariable<double>(-1d);
        private readonly NetworkVariable<float> _abilityCooldownDuration = new NetworkVariable<float>();
        // -1: sem indicador; 0: indisponível; 1: disponível; 2: em uso (habilidade sem timer).
        private readonly NetworkVariable<int> _abilityAvailability = new NetworkVariable<int>(-1);

        private SpriteRenderer[] _spriteRenderers;
        private SpriteRenderer _bodySpriteRenderer;
        private Collider2D[] _colliders;
        private Rigidbody2D _rigidbody;
        private GeneralPlayerSoundManager _playerSound;
        private Animator _animator;
        private bool _isInvulnerable;
        private Coroutine _knockbackRoutine;
        private Coroutine _hitAnimationRoutine;

        public event Action StateChanged;
        public event Action<NetworkPlayerState, ulong> Died;

        public int PlayerSlot => _playerSlot.Value;
        public LobbyCharacterId CharacterId => _characterId.Value;
        public LobbyInputDeviceKind InputDevice => _inputDevice.Value;
        public Color IndicatorColor => _indicatorColor.Value;
        public int CurrentHealth => _currentHealth.Value;
        public int MaximumHealth => _maximumHealth;
        public bool IsAlive => _isAlive.Value;
        public bool IndicatorsVisible => _indicatorsVisible.Value;
        public bool CanAct => _isAlive.Value && _matchActive.Value;
        public bool IsInvulnerable => _isInvulnerable;
        public string PlayerName => _playerName.Value.ToString();
        // Negativo indica que a habilidade atual não tem recarga por tempo.
        public float AbilityCooldownRemaining => _abilityReadyAt.Value < 0d ? -1f
            : IsSpawned ? Mathf.Max(0f, (float)(_abilityReadyAt.Value - NetworkManager.ServerTime.Time)) : 0f;

        public float AbilityCooldownProgress => _abilityCooldownDuration.Value <= 0f ? 1f
            : 1f - Mathf.Clamp01(AbilityCooldownRemaining / _abilityCooldownDuration.Value);
        public int AbilityAvailability => _abilityAvailability.Value;

        private void Awake()
        {
            _bodySpriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            _colliders = GetComponentsInChildren<Collider2D>(true);
            _rigidbody = GetComponent<Rigidbody2D>();
            _animator = GetComponent<Animator>();
            _playerSound = GetComponent<GeneralPlayerSoundManager>();
        }

        public override void OnNetworkSpawn()
        {
            _playerSlot.OnValueChanged += HandleIntChanged;
            _characterId.OnValueChanged += HandleCharacterChanged;
            _inputDevice.OnValueChanged += HandleInputChanged;
            _indicatorColor.OnValueChanged += HandleColorChanged;
            _currentHealth.OnValueChanged += HandleIntChanged;
            _isAlive.OnValueChanged += HandleAliveChanged;
            _matchActive.OnValueChanged += HandleBoolChanged;
            _indicatorsVisible.OnValueChanged += HandleBoolChanged;
            _playerName.OnValueChanged += HandleNameChanged;

            ApplyAlivePresentation(_isAlive.Value);
            ApplyHealthPresentation();
            UpdateDeathAnimation(_isAlive.Value);
            StateChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            _playerSlot.OnValueChanged -= HandleIntChanged;
            _characterId.OnValueChanged -= HandleCharacterChanged;
            _inputDevice.OnValueChanged -= HandleInputChanged;
            _indicatorColor.OnValueChanged -= HandleColorChanged;
            _currentHealth.OnValueChanged -= HandleIntChanged;
            _isAlive.OnValueChanged -= HandleAliveChanged;
            _matchActive.OnValueChanged -= HandleBoolChanged;
            _indicatorsVisible.OnValueChanged -= HandleBoolChanged;
            _playerName.OnValueChanged -= HandleNameChanged;
        }

        public void InitializeServer(LobbyPlayerData playerData, Color indicatorColor)
        {
            if (!IsServer)
            {
                return;
            }

            _playerSlot.Value = playerData.PlayerSlot;
            _playerName.Value = playerData.PlayerName;
            _characterId.Value = playerData.CharacterId;
            _inputDevice.Value = playerData.InputDevice;
            _indicatorColor.Value = indicatorColor;
            _currentHealth.Value = _maximumHealth;
            _isAlive.Value = true;
            _matchActive.Value = false;
            _isInvulnerable = false;
        }

        public void SetInvulnerableServer(bool isInvulnerable)
        {
            if (IsServer)
            {
                _isInvulnerable = isInvulnerable;
            }
        }

        public void SetAbilityCooldownServer(float seconds)
        {
            if (!IsServer) return;
            _abilityCooldownDuration.Value = Mathf.Max(0f, seconds);
            _abilityReadyAt.Value = NetworkManager.ServerTime.Time + _abilityCooldownDuration.Value;
        }

        public void SetAbilityAvailabilityServer(bool available, bool active)
        {
            if (IsServer) _abilityAvailability.Value = active ? 2 : available ? 1 : 0;
        }

        private void HandleNameChanged(FixedString64Bytes previousValue, FixedString64Bytes currentValue)
        {
            StateChanged?.Invoke();
        }

        [Rpc(SendTo.Owner)]
        public void ApplyKnockbackOwnerRpc(Vector2 velocity)
        {
            if (!IsOwner || _rigidbody == null)
            {
                return;
            }

            _rigidbody.linearVelocity = velocity;
            if (_knockbackRoutine != null)
            {
                StopCoroutine(_knockbackRoutine);
            }

            _knockbackRoutine = StartCoroutine(DisableMovementRoutine(0.25f));
        }

        private IEnumerator DisableMovementRoutine(float duration)
        {
            PlayerMove playerMove = GetComponent<PlayerMove>();
            if (playerMove == null)
            {
                yield break;
            }

            playerMove.enabled = false;
            yield return new WaitForSeconds(duration);
            playerMove.enabled = true;
            _knockbackRoutine = null;
        }

        public void SetMatchActiveServer(bool isActive, bool indicatorsVisible = true)
        {
            if (!IsServer)
            {
                return;
            }

            _matchActive.Value = isActive;
            _indicatorsVisible.Value = indicatorsVisible;
            if (!isActive && _rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector2.zero;
            }
        }

        public void ApplyDamageServer(int amount, ulong attackerClientId)
        {
            if (!IsServer || !CanAct || _isInvulnerable || amount <= 0)
            {
                return;
            }

            _currentHealth.Value = Mathf.Max(0, _currentHealth.Value - amount);

            if (_currentHealth.Value > 0)
            {
                if(_playerSound != null)
                {
                    _playerSound.PlayHit();
                }
                return;
            }


            if(_playerSound != null)
            {
                _playerSound.PlayDeathHit();
            }

            _isAlive.Value = false;
            _matchActive.Value = false;
            if (TryGetComponent<ICharacterCombat>(out var characterCombat))
            {
                characterCombat.ClearProjectilesServer();
            }
            Died?.Invoke(this, attackerClientId);
        }

        public void RespawnServer(Vector3 position)
        {
            if (!IsServer)
            {
                return;
            }

            transform.position = position;
            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector2.zero;
            }

            TeleportOwnerRpc(
                position,
                RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
            
            if(_playerSound != null)
            {
                _playerSound.PlayRespawn();
            }

            _currentHealth.Value = _maximumHealth;
            _isAlive.Value = true;
            _matchActive.Value = true;
            _isInvulnerable = false;
            if (TryGetComponent<ICharacterCombat>(out var characterCombat))
            {
                characterCombat.RefillAmmoServer();
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void TeleportOwnerRpc(Vector3 position, RpcParams rpcParams)
        {
            transform.position = position;
            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector2.zero;
            }
        }

        private void HandleAliveChanged(bool previousValue, bool currentValue)
        {
            ApplyAlivePresentation(currentValue);
            UpdateDeathAnimation(currentValue);
            StateChanged?.Invoke();
        }

        private void UpdateDeathAnimation(bool isAlive)
        {
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.SetBool("isDead", !isAlive);
            }
        }

        private void ApplyAlivePresentation(bool isAlive)
        {
            foreach (SpriteRenderer spriteRenderer in _spriteRenderers)
            {
                spriteRenderer.enabled = isAlive;
            }

            foreach (Collider2D playerCollider in _colliders)
            {
                playerCollider.enabled = isAlive;
            }

            if (_rigidbody != null)
            {
                _rigidbody.simulated = isAlive;
            }
        }

        private void HandleIntChanged(int previousValue, int currentValue)
        {
            ApplyHealthPresentation();
            if (currentValue < previousValue && IsAlive)
            {
                PlayHitAnimation();
            }
            StateChanged?.Invoke();
        }

        private void PlayHitAnimation()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
            {
                return;
            }

            _animator.SetBool("isHit", true);
            if (_hitAnimationRoutine != null)
            {
                StopCoroutine(_hitAnimationRoutine);
            }

            _hitAnimationRoutine = StartCoroutine(ResetHitAnimationRoutine());
        }

        private IEnumerator ResetHitAnimationRoutine()
        {
            yield return null;
            if (_animator != null)
            {
                _animator.SetBool("isHit", false);
            }

            _hitAnimationRoutine = null;
        }

        private void ApplyHealthPresentation()
        {
            float healthRatio = _maximumHealth > 0
                ? (float)_currentHealth.Value / _maximumHealth
                : 0f;
            Color damageColor = new Color(0.55f, 0.18f, 0.18f, 1f);
            Color playerColor = Color.Lerp(damageColor, Color.white, healthRatio);

            _bodySpriteRenderer.color = playerColor;
        }

        private void HandleCharacterChanged(
            LobbyCharacterId previousValue,
            LobbyCharacterId currentValue)
        {
            StateChanged?.Invoke();
        }

        private void HandleInputChanged(
            LobbyInputDeviceKind previousValue,
            LobbyInputDeviceKind currentValue)
        {
            StateChanged?.Invoke();
        }

        private void HandleColorChanged(Color previousValue, Color currentValue)
        {
            StateChanged?.Invoke();
        }

        private void HandleBoolChanged(bool previousValue, bool currentValue)
        {
            StateChanged?.Invoke();
        }
    }
}
