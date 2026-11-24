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
        [SerializeField, Min(0f)] private float _respawnInvulnerabilitySeconds = 2f;
        [SerializeField, Min(0.01f)] private float _respawnBlinkInterval = 0.1f;
        [SerializeField] private GameObject _deathPropPrefab;
        private GameObject _activeDeathProp;

        private readonly NetworkVariable<int> _playerSlot = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<LobbyCharacterId> _characterId =
            new NetworkVariable<LobbyCharacterId>(LobbyCharacterId.None);
        private readonly NetworkVariable<LobbyInputDeviceKind> _inputDevice =
            new NetworkVariable<LobbyInputDeviceKind>(LobbyInputDeviceKind.Unknown);
        private readonly NetworkVariable<Color> _indicatorColor =
            new NetworkVariable<Color>(Color.white);
        private readonly NetworkVariable<int> _currentHealth = new NetworkVariable<int>();
        private readonly NetworkVariable<bool> _isAlive = new NetworkVariable<bool>(true);
        private readonly NetworkVariable<double> _respawnInvulnerabilityEndsAt = new NetworkVariable<double>();
        private readonly NetworkVariable<bool> _matchActive = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<bool> _indicatorsVisible = new NetworkVariable<bool>(true);
        private readonly NetworkVariable<Vector3> _initialSpawnPosition = new();
        private readonly NetworkVariable<bool> _initialSpawnAssigned = new();
        private readonly NetworkVariable<bool> _initialSpawnPrepared = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<FixedString64Bytes> _playerName = new NetworkVariable<FixedString64Bytes>();
        private readonly NetworkVariable<double> _abilityReadyAt = new NetworkVariable<double>(-1d);
        private readonly NetworkVariable<float> _abilityCooldownDuration = new NetworkVariable<float>();
        // -1: sem indicador; 0: indisponível; 1: disponível; 2: em uso (habilidade sem timer).
        private readonly NetworkVariable<int> _abilityAvailability = new NetworkVariable<int>(-1);

        private readonly NetworkVariable<LobbyTeam> _team = new(LobbyTeam.None);
        private MatchController _matchController;
        public LobbyTeam Team => _team.Value;
        public bool CanReceiveHitFrom(ulong attackerClientId)
        {
            if (!_matchController) _matchController = FindFirstObjectByType<MatchController>();
            return !_matchController || !_matchController.AreTeammates(attackerClientId, OwnerClientId);
        }

        private SpriteRenderer[] _spriteRenderers;
        private const float RespawnTintMultiplier = 0.8f;
        private const float RemoteHudRevealDelaySeconds = 0.35f;
        private float _remoteHudRevealAt;
        private SpriteRenderer _bodySpriteRenderer;
        private Color _healthTint = Color.white;
        private bool _respawnTintDark;
        private Collider2D[] _colliders;
        private Rigidbody2D _rigidbody;
        private GeneralPlayerSoundManager _playerSound;
        private Animator _animator;
        private BoxCollider2D _bodyCollider;
        private bool _initialSpawnSimulationAllowed;
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
        public bool IndicatorsVisible => _indicatorsVisible.Value && _remoteHudRevealAt <= 0f;
        public bool CanAct => _isAlive.Value && _matchActive.Value;
        public bool IsInitialSpawnPrepared => _initialSpawnPrepared.Value;
        public bool IsInvulnerable => _isInvulnerable
            || (IsServer && IsSpawned && NetworkManager.ServerTime.Time < _respawnInvulnerabilityEndsAt.Value);
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
            _bodyCollider = GetComponent<BoxCollider2D>();
            if (_rigidbody != null) _rigidbody.simulated = false;
            _playerSound = GetComponent<GeneralPlayerSoundManager>();
        }

        public override void OnNetworkSpawn()
        {
            _playerSlot.OnValueChanged += HandleIntChanged;
            _team.OnValueChanged += HandleTeamChanged;
            _characterId.OnValueChanged += HandleCharacterChanged;
            _inputDevice.OnValueChanged += HandleInputChanged;
            _indicatorColor.OnValueChanged += HandleColorChanged;
            _currentHealth.OnValueChanged += HandleIntChanged;
            _isAlive.OnValueChanged += HandleAliveChanged;
            _matchActive.OnValueChanged += HandleBoolChanged;
            _indicatorsVisible.OnValueChanged += HandleIndicatorsChanged;
            _playerName.OnValueChanged += HandleNameChanged;
            _initialSpawnPrepared.OnValueChanged += HandleSpawnPreparedChanged;

            ApplyAlivePresentation(_isAlive.Value);
            ApplyHealthPresentation();
            UpdateDeathAnimation(_isAlive.Value);
            StateChanged?.Invoke();
        }

        private void LateUpdate()
        {
            if (_remoteHudRevealAt > 0f && Time.unscaledTime >= _remoteHudRevealAt)
            {
                _remoteHudRevealAt = 0f;
                StateChanged?.Invoke();
            }

            bool dark = false;
            if (IsSpawned && CanAct)
            {
                double remaining = _respawnInvulnerabilityEndsAt.Value - NetworkManager.ServerTime.Time;
                dark = remaining > 0d
                    && (long)(remaining / Mathf.Max(0.01f, _respawnBlinkInterval)) % 2 == 0;
            }

            if (_respawnTintDark == dark) return;
            _respawnTintDark = dark;
            ApplyBodyTint();
        }

        public override void OnNetworkDespawn()
        {
            _respawnTintDark = false;
            ApplyBodyTint();
            _playerSlot.OnValueChanged -= HandleIntChanged;
            _team.OnValueChanged -= HandleTeamChanged;
            _characterId.OnValueChanged -= HandleCharacterChanged;
            _inputDevice.OnValueChanged -= HandleInputChanged;
            _indicatorColor.OnValueChanged -= HandleColorChanged;
            _currentHealth.OnValueChanged -= HandleIntChanged;
            _isAlive.OnValueChanged -= HandleAliveChanged;
            _matchActive.OnValueChanged -= HandleBoolChanged;
            _indicatorsVisible.OnValueChanged -= HandleIndicatorsChanged;
            _remoteHudRevealAt = 0f;
            _playerName.OnValueChanged -= HandleNameChanged;
            _initialSpawnPrepared.OnValueChanged -= HandleSpawnPreparedChanged;
            _initialSpawnSimulationAllowed = false;

            CleanupDeathProp();
        }

        private void HandleTeamChanged(LobbyTeam previous, LobbyTeam current) => StateChanged?.Invoke();

        public void InitializeServer(LobbyPlayerData playerData, Color indicatorColor)
        {
            if (!IsServer)
            {
                return;
            }

            _playerSlot.Value = playerData.PlayerSlot;
            _team.Value = playerData.Team;
            _playerName.Value = playerData.PlayerName;
            _characterId.Value = playerData.CharacterId;
            _inputDevice.Value = playerData.InputDevice;
            _indicatorColor.Value = LobbyTeams.IsValid(playerData.Team) ? LobbyTeams.Color(playerData.Team) : indicatorColor;
            _currentHealth.Value = _maximumHealth;
            _isAlive.Value = true;
            _matchActive.Value = false;
            _isInvulnerable = false;
            _respawnInvulnerabilityEndsAt.Value = 0d;
            _initialSpawnPosition.Value = transform.position;
            _initialSpawnAssigned.Value = true;
        }

        internal IEnumerator PrepareInitialSpawn(float attemptTimeoutSeconds)
        {
            if (!IsOwner || _rigidbody == null || _bodyCollider == null) yield break;
            var physicsStep = new WaitForFixedUpdate();
            for (int attempt = 0; attempt < 3 && IsSpawned; attempt++)
            {
                _initialSpawnSimulationAllowed = false;
                RefreshSpawnPhysics();
                int stableSteps = 0;
                float deadline = Time.unscaledTime + Mathf.Max(0.5f, attemptTimeoutSeconds);
                while (IsSpawned && Time.unscaledTime < deadline)
                {
                    if (!_initialSpawnSimulationAllowed && _initialSpawnAssigned.Value
                        && gameObject.scene.isLoaded)
                    {
                        Physics2D.SyncTransforms();
                        if (InitialSpawnPlacement.TryResolve(_bodyCollider, _initialSpawnPosition.Value, out Vector3 position))
                        {
                            transform.position = position;
                            _rigidbody.position = position;
                            _rigidbody.linearVelocity = Vector2.zero;
                            _initialSpawnSimulationAllowed = true;
                            RefreshSpawnPhysics();
                            Physics2D.SyncTransforms();
                        }
                    }

                    yield return physicsStep;
                    if (!IsSpawned) yield break;
                    bool safe = _initialSpawnSimulationAllowed
                        && Mathf.Abs(_rigidbody.linearVelocity.y) <= 0.1f
                        && InitialSpawnPlacement.IsSupportedAndClear(_bodyCollider);
                    stableSteps = safe ? stableSteps + 1 : 0;
                    if (stableSteps < 3) continue;
                    _initialSpawnPrepared.Value = true;
                    yield break;
                }
                Debug.LogWarning($"P{PlayerSlot + 1}: nova tentativa de preparar o spawn ({attempt + 1}/3).", this);
            }
            _initialSpawnSimulationAllowed = false;
            RefreshSpawnPhysics();
        }

        private void HandleSpawnPreparedChanged(bool previous, bool current) => RefreshSpawnPhysics();

        private void RefreshSpawnPhysics()
        {
            if (_rigidbody != null)
                _rigidbody.simulated = IsAlive && (_initialSpawnSimulationAllowed || IsInitialSpawnPrepared);
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
            if (!IsServer || !CanAct || IsInvulnerable || amount <= 0 || !CanReceiveHitFrom(attackerClientId))
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
            SpawnDeathProp();
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
            CleanupDeathProp();
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
            _respawnInvulnerabilityEndsAt.Value = NetworkManager.ServerTime.Time
                + Mathf.Max(0f, _respawnInvulnerabilitySeconds);
            _isAlive.Value = true;
            _matchActive.Value = true;
            _isInvulnerable = false;
            if (TryGetComponent<ICharacterCombat>(out var characterCombat))
            {
                characterCombat.RefillAmmoServer();
            }
        }
        private void SpawnDeathProp()
        {
            if (_deathPropPrefab != null && _activeDeathProp == null)
            {
                _activeDeathProp = Instantiate(_deathPropPrefab, transform.position, Quaternion.identity);

                var netObj = _activeDeathProp.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    netObj.Spawn();
                }
            }
        }

        private void CleanupDeathProp()
        {
            if (_activeDeathProp != null)
            {
                var netObj = _activeDeathProp.GetComponent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                {
                    netObj.Despawn(true);
                }
                else
                {
                    Destroy(_activeDeathProp);
                }
                _activeDeathProp = null;
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
                Debug.Log("Sumiu Colisou");
            }

            if (_rigidbody != null)
            {
                RefreshSpawnPhysics();
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
            _healthTint = Color.Lerp(damageColor, Color.white, healthRatio);
            ApplyBodyTint();
        }

        private void ApplyBodyTint()
        {
            float multiplier = _respawnTintDark ? RespawnTintMultiplier : 1f;
            _bodySpriteRenderer.color = new Color(
                _healthTint.r * multiplier,
                _healthTint.g * multiplier,
                _healthTint.b * multiplier,
                _healthTint.a);
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

        private void HandleIndicatorsChanged(bool previousValue, bool currentValue)
        {
            _remoteHudRevealAt = currentValue && !IsOwner ? Time.unscaledTime + RemoteHudRevealDelaySeconds : 0f;
            StateChanged?.Invoke();
        }

        private void HandleBoolChanged(bool previousValue, bool currentValue)
        {
            StateChanged?.Invoke();
        }
    }
}
