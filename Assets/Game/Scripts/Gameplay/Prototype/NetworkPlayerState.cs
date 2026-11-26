using System.Collections;
using System;
using PiGame.Lobby;
using Unity.Netcode;
using Unity.Netcode.Components;
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
        private struct LifeState : INetworkSerializeByMemcpy, IEquatable<LifeState>
        {
            public bool Alive;
            public Vector3 SpawnPosition;

            public LifeState(bool alive, Vector3 spawnPosition = default)
            {
                Alive = alive;
                SpawnPosition = spawnPosition;
            }

            public bool Equals(LifeState other) => Alive == other.Alive && SpawnPosition.Equals(other.SpawnPosition);
        }

        private readonly NetworkVariable<LifeState> _life = new(new LifeState(true));
        private readonly NetworkVariable<double> _respawnInvulnerabilityEndsAt = new NetworkVariable<double>();
        private readonly NetworkVariable<bool> _matchActive = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<bool> _indicatorsVisible = new NetworkVariable<bool>(true);
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
        private NetworkTransform _networkTransform;
        private bool _waitingForRespawnTeleport;
        private bool _receivedTeleport;
        private Vector2 _lastTeleportPosition;
        private GeneralPlayerSoundManager _playerSound;
        private Animator _animator;
        private BoxCollider2D _bodyCollider;
        public InitialSpawnPlacement.Body StandingSpawnBody { get; private set; }
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
        public bool IsAlive => _life.Value.Alive;
        public Vector3 PositionForSpawnClearance => _waitingForRespawnTeleport ? _life.Value.SpawnPosition : transform.position;
        public bool IndicatorsVisible => _indicatorsVisible.Value && _remoteHudRevealAt <= 0f && !_waitingForRespawnTeleport;
        public bool CanAct => IsAlive && _matchActive.Value && !_waitingForRespawnTeleport;
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
            _networkTransform = GetComponent<NetworkTransform>();
            _animator = GetComponent<Animator>();
            _bodyCollider = GetComponent<BoxCollider2D>();
            if (_bodyCollider) StandingSpawnBody = new InitialSpawnPlacement.Body(_bodyCollider);
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
            _life.OnValueChanged += HandleLifeChanged;
            _matchActive.OnValueChanged += HandleBoolChanged;
            _indicatorsVisible.OnValueChanged += HandleIndicatorsChanged;
            _playerName.OnValueChanged += HandleNameChanged;
            _initialSpawnPrepared.OnValueChanged += HandleSpawnPreparedChanged;

            ApplyAlivePresentation(IsAlive);
            ApplyHealthPresentation();
            UpdateDeathAnimation(IsAlive);
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
            _life.OnValueChanged -= HandleLifeChanged;
            _waitingForRespawnTeleport = false;
            _receivedTeleport = false;
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
            _life.Value = new LifeState(true);
            _matchActive.Value = false;
            _isInvulnerable = false;
            _respawnInvulnerabilityEndsAt.Value = 0d;
        }

        public IEnumerator PrepareInitialSpawn(float delaySeconds)
        {
            if (!IsOwner || !IsSpawned || IsInitialSpawnPrepared) yield break;

            _initialSpawnSimulationAllowed = true;
            RefreshSpawnPhysics();

            // A espera acompanha os passos da física, inclusive quando o cliente carrega devagar.
            var physicsStep = new WaitForFixedUpdate();
            int steps = Mathf.CeilToInt(delaySeconds / Time.fixedDeltaTime);
            for (int step = 0; step < steps; step++)
            {
                yield return physicsStep;
                if (!IsSpawned || !IsOwner) yield break;
            }

            ReplicateSpawnPosition();
            _initialSpawnPrepared.Value = true;
            _initialSpawnSimulationAllowed = false;
            RefreshSpawnPhysics();
        }

        private void HandleSpawnPreparedChanged(bool previous, bool current) => RefreshSpawnPhysics();

        private void RefreshSpawnPhysics()
        {
            if (_rigidbody != null)
                _rigidbody.simulated = IsAlive && !_waitingForRespawnTeleport
                    && (_initialSpawnSimulationAllowed || IsInitialSpawnPrepared);
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

            DefeatServer(attackerClientId, showDeathProp: true);
        }

        public void HandleArenaExitServer()
        {
            if (!IsServer || !IsSpawned || !CanAct) return;
            _currentHealth.Value = 0;
            DefeatServer(ulong.MaxValue, showDeathProp: false);
        }

        private void DefeatServer(ulong attackerClientId, bool showDeathProp)
        {
            _life.Value = new LifeState(false);
            _matchActive.Value = false;
            if (showDeathProp) SpawnDeathProp();
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
            
            if(_playerSound != null)
            {
                _playerSound.PlayRespawn();
            }

            _currentHealth.Value = _maximumHealth;
            _respawnInvulnerabilityEndsAt.Value = NetworkManager.ServerTime.Time
                + Mathf.Max(0f, _respawnInvulnerabilitySeconds);
            _isInvulnerable = false;
            _life.Value = new LifeState(true, position);
            _matchActive.Value = true;
            if (TryGetComponent<ICharacterCombat>(out var characterCombat))
            {
                characterCombat.RefillAmmoServer();
            }
        }
#if UNITY_EDITOR
        public void TeleportForMapPlaytest(Vector3 position)
        {
            if (!IsServer || !IsOwner || !IsSpawned) return;

            if (IsAlive)
            {
                PlaceAtSpawn(position);
                ReplicateSpawnPosition();
            }
            RespawnServer(position);
        }
#endif

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

        private void PlaceAtSpawn(Vector3 position)
        {
            transform.position = position;
            if (!_rigidbody) return;
            _rigidbody.position = position;
            _rigidbody.linearVelocity = Vector2.zero;
            _rigidbody.angularVelocity = 0f;
        }

        private void ReplicateSpawnPosition()
        {
            if (_networkTransform && _networkTransform.CanCommitToTransform)
                _networkTransform.Teleport(transform.position, transform.rotation, transform.localScale);
        }

        private void HandleLifeChanged(LifeState previous, LifeState current)
        {
            if (previous.Alive == current.Alive) return;

            if (!current.Alive)
            {
                _receivedTeleport = false;
                _waitingForRespawnTeleport = false;
            }
            else if (_networkTransform is PlayerNetworkTransform && !_networkTransform.CanCommitToTransform)
            {
                // Observers wait for the teleport to clear the old interpolation state.
                _waitingForRespawnTeleport = !HasReceivedRespawnTeleport(current.SpawnPosition);
                if (!_waitingForRespawnTeleport) PlaceAtSpawn(current.SpawnPosition);
            }
            else
            {
                PlaceAtSpawn(current.SpawnPosition);
                ReplicateSpawnPosition();
            }

            RefreshLifePresentation();
        }

        public void NotifyPositionTeleported(Vector3 position)
        {
            _lastTeleportPosition = position;
            _receivedTeleport = true;
            if (!_waitingForRespawnTeleport || !HasReceivedRespawnTeleport(_life.Value.SpawnPosition)) return;

            PlaceAtSpawn(_life.Value.SpawnPosition);
            _waitingForRespawnTeleport = false;
            RefreshLifePresentation();
        }

        private bool HasReceivedRespawnTeleport(Vector3 position) =>
            _receivedTeleport && (_lastTeleportPosition - (Vector2)position).sqrMagnitude < 0.0001f;

        private void RefreshLifePresentation()
        {
            bool visible = IsAlive && !_waitingForRespawnTeleport;
            ApplyAlivePresentation(visible);
            UpdateDeathAnimation(visible);
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
                playerCollider.enabled = isAlive;

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
