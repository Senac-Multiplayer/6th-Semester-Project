using System.Collections.Generic;
using PiGame.Sound;
using Unity.Netcode;
using UnityEngine;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(NetworkPlayerState))]
    public class RebeldeCombat : NetworkBehaviour, ICharacterCombat
    {
        private enum AbilityPhase : byte { Idle, Preparing, Swinging }

        [Header("Combat Settings")]
        [SerializeField] private ProjectileDefinition _projectile;
        [SerializeField, Min(0f)] private float _shotCooldownSeconds = 0.35f;
        [SerializeField, Min(0f)] private float _projectileSpawnDistance = 0.85f;
        [SerializeField] private float[] _projectileVelocityStack = { 5f, 10f, 15f };

        [Header("Ability Settings")]
        [SerializeField, Min(0f)] private float _coneDistance = 4f;
        [SerializeField, Range(0f, 180f)] private float _coneAngle = 60f;
        [SerializeField, Min(0f)] private float _projectileReflectMinimumSpeed = 3f;
        [SerializeField, Min(0.1f)] private float _maxAbilityHoldSeconds = 1.5f;
        [SerializeField, Min(0f)] private float _abilityCooldownSeconds = 2.5f;
        [SerializeField, Min(0f)] private float _playerUpwardImpulse = 3f;
        [SerializeField, Min(0f)] private float _abilityGraceSeconds = 0.12f;
        [SerializeField, Min(0.1f)] private float _abilityStrikeSeconds = 0.52f;

        [Header("2D Impact VFX Settings")]
        [SerializeField] private GameObject _ballImpactVfxObject;
        [SerializeField] private SpriteRenderer _ballImpactSpriteRenderer;
        [SerializeField] private float _impactSideOffset = 0.85f;
        [SerializeField, Min(0.01f)] private float _vfxDurationSeconds = 0.35f;

        private static readonly int AbilityPhaseParameter = Animator.StringToHash("abilityPhase");
        private readonly NetworkVariable<int> _ammo = new(1, NetworkVariableReadPermission.Everyone);
        private readonly NetworkVariable<AbilityPhase> _abilityPhase = new(AbilityPhase.Idle);
        private readonly NetworkVariable<Vector2> _abilityForward = new(Vector2.right);
        private readonly HashSet<NetworkObject> _abilityProcessedTargets = new();
        private readonly List<Collider2D> _abilityHits = new(16);
        private readonly RaycastHit2D[] _obstructionHits = new RaycastHit2D[8];

        private RebeldeProjectile _activeProjectile;
        private NetworkPlayerState _playerState;
        private RebeldeSoundManager _rebeldeSound;
        private AmmoIndicatorsView _ammoIndicatorsView;
        private PlayerAnimatorController _presentation;
        private Animator _animator;
        private SpriteRenderer _facingSprite;
        private ContactFilter2D _obstructionFilter;
        private Coroutine _hideVfxRoutine;
        private float _nextShotTime;
        private int _projectileVelocityPointer;
        private float _abilityStartedAt;
        private float _abilityHitWindowUntil;
        private float _abilityStrikeUntil;
        private float _abilityCooldownUntil;

        public bool HasShot => _ammo.Value > 0;
        public bool IsPreparingAbility => _abilityPhase.Value == AbilityPhase.Preparing;
        public bool IsSwingingAbility => _abilityPhase.Value == AbilityPhase.Swinging;
        public bool CanCollectProjectile => IsSpawned && _playerState.CanAct && _abilityPhase.Value == AbilityPhase.Idle;

        private void Awake()
        {
            _playerState = GetComponent<NetworkPlayerState>();
            _rebeldeSound = GetComponent<RebeldeSoundManager>();
            _ammoIndicatorsView = GetComponent<AmmoIndicatorsView>();
            _presentation = GetComponent<PlayerAnimatorController>();
            _animator = GetComponent<Animator>();
            _facingSprite = GetComponent<SpriteRenderer>();
            _obstructionFilter = new ContactFilter2D();
            _obstructionFilter.SetLayerMask(LayerMask.GetMask("Ground", "Wall", "Roof"));
            _obstructionFilter.useTriggers = false;

            if (_ballImpactVfxObject)
            {
                if (!_ballImpactSpriteRenderer) _ballImpactSpriteRenderer = _ballImpactVfxObject.GetComponent<SpriteRenderer>();
                HideImpactVfxImmediate();
            }

            if (!_projectile || !_projectile.Prefab || _projectile.Prefab is not RebeldeProjectile
                || _projectileVelocityStack == null || _projectileVelocityStack.Length == 0
                || System.Array.Exists(_projectileVelocityStack, speed => !(speed > 0f) || float.IsInfinity(speed)))
            {
                Debug.LogError("Configure o projétil do Rebelde e uma sequência de velocidades positivas no RebeldeCombat.", this);
                enabled = false;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!enabled) return;
            _ammo.OnValueChanged += HandleAmmoChanged;
            _abilityPhase.OnValueChanged += HandleAbilityPhaseChanged;
            _abilityForward.OnValueChanged += HandleAbilityDirectionChanged;
            _playerState.StateChanged += HandlePlayerStateChanged;
            if (IsServer)
            {
                _ammo.Value = 1;
                _playerState.SetAbilityCooldownServer(0f);
                RefreshAbilityAvailability();
            }
            ApplyAbilityPresentation();
            RefreshAmmoIndicators();
        }

        public override void OnNetworkDespawn()
        {
            _ammo.OnValueChanged -= HandleAmmoChanged;
            _abilityPhase.OnValueChanged -= HandleAbilityPhaseChanged;
            _abilityForward.OnValueChanged -= HandleAbilityDirectionChanged;
            _playerState.StateChanged -= HandlePlayerStateChanged;
            ClearProjectilesServer();
            HideImpactVfxImmediate();
            if (_presentation) _presentation.SetSkillActive(false);
            if (_animator && _animator.runtimeAnimatorController) _animator.SetInteger(AbilityPhaseParameter, (int)AbilityPhase.Idle);
            if (_ammoIndicatorsView) _ammoIndicatorsView.SetCount(0, false, Color.white);
        }

        public void ShootServer(Vector2 direction)
        {
            if (!IsServer || !IsSpawned || !enabled || !_playerState.CanAct
                || Time.time < _nextShotTime) return;
            if (!HasShot)
            {
                _rebeldeSound?.PlayRebeldeEmpty();
                return;
            }

            Vector2 shotDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector2.right;
            var projectile = (RebeldeProjectile)Instantiate(_projectile.Prefab, transform.position, Quaternion.identity);
            projectile.PlaceAtMuzzle(shotDirection, _projectileSpawnDistance);
            _activeProjectile = projectile;
            _ammo.Value = 0;
            _nextShotTime = Time.time + _shotCooldownSeconds;
            projectile.NetworkObject.Spawn(true);
            projectile.InitializeServer(OwnerClientId, shotDirection, _projectile,
                _projectileVelocityStack[_projectileVelocityPointer], _rebeldeSound);
            _projectileVelocityPointer = (_projectileVelocityPointer + 1) % _projectileVelocityStack.Length;
            _rebeldeSound?.PlayRebeldeShot();
        }

        public void RecoverProjectileServer(RebeldeProjectile projectile)
        {
            if (!IsServer || !IsSpawned || !projectile || projectile != _activeProjectile) return;
            _activeProjectile = null;
            _ammo.Value = 1;
            if (_playerState.CanAct) _rebeldeSound?.PlayRebeldeCatch();
        }

        public void ClearProjectilesServer()
        {
            if (!IsServer) return;
            RebeldeProjectile projectile = _activeProjectile;
            _activeProjectile = null;
            if (projectile && projectile.IsSpawned) projectile.NetworkObject.Despawn(true);
            if (IsSpawned) CancelAbilityServer();
        }

        public void RefillAmmoServer()
        {
            if (!IsServer || !IsSpawned || (_activeProjectile && _activeProjectile.IsSpawned)) return;
            _activeProjectile = null;
            _ammo.Value = 1;
            _projectileVelocityPointer = 0;
            _nextShotTime = 0f;
            _abilityPhase.Value = AbilityPhase.Idle;
            _abilityCooldownUntil = 0f;
            _abilityProcessedTargets.Clear();
            _playerState.SetAbilityCooldownServer(0f);
            RefreshAbilityAvailability();
        }

        public void ShootReleaseServer() { }
        public void UpdateShootAimServer(Vector2 direction) { }

        public void BeginAbilityServer(Vector2 aimDirection, Vector2 moveDirection)
        {
            if (!IsServer || !IsSpawned || !enabled || !_playerState.CanAct
                || _abilityPhase.Value != AbilityPhase.Idle || Time.time < _abilityCooldownUntil) return;
            _abilityProcessedTargets.Clear();
            _abilityForward.Value = GetFacingDirection(aimDirection, moveDirection);
            _abilityStartedAt = Time.time;
            _abilityPhase.Value = AbilityPhase.Preparing;
            RefreshAbilityAvailability();
        }

        public void EndAbilityServer(Vector2 aimDirection, Vector2 moveDirection)
        {
            if (!IsServer || _abilityPhase.Value != AbilityPhase.Preparing) return;
            if (!_playerState.CanAct) { CancelAbilityServer(); return; }
            _abilityForward.Value = GetFacingDirection(aimDirection, moveDirection);
            SwingServer();
        }

        public void CancelAbilityServer()
        {
            if (!IsServer || _abilityPhase.Value == AbilityPhase.Idle) return;
            if (_abilityPhase.Value == AbilityPhase.Preparing) StartAbilityCooldown();
            _abilityPhase.Value = AbilityPhase.Idle;
            _abilityProcessedTargets.Clear();
            RefreshAbilityAvailability();
        }

        private void SwingServer()
        {
            _abilityHitWindowUntil = Time.time + _abilityGraceSeconds;
            _abilityStrikeUntil = Time.time + Mathf.Max(_abilityStrikeSeconds, _abilityGraceSeconds);
            StartAbilityCooldown();
            _abilityPhase.Value = AbilityPhase.Swinging;
            _rebeldeSound?.PlayRebeldeSkill();
            RefreshAbilityAvailability();
            ExecuteAbilityCone();
        }

        private void StartAbilityCooldown()
        {
            _abilityCooldownUntil = Time.time + _abilityCooldownSeconds;
            _playerState.SetAbilityCooldownServer(_abilityCooldownSeconds);
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || !enabled) return;
            if (!_playerState.CanAct) { CancelAbilityServer(); RefreshAbilityAvailability(); return; }

            if (IsPreparingAbility && Time.time >= _abilityStartedAt + _maxAbilityHoldSeconds) SwingServer();
            if (IsSwingingAbility && Time.time >= _abilityStrikeUntil) _abilityPhase.Value = AbilityPhase.Idle;
            RefreshAbilityAvailability();
        }

        private void FixedUpdate()
        {
            if (IsServer && IsSpawned && _playerState.CanAct && IsSwingingAbility
                && Time.time <= _abilityHitWindowUntil) ExecuteAbilityCone();
        }

        private void RefreshAbilityAvailability()
        {
            _playerState.SetAbilityAvailabilityServer(_playerState.CanAct
                && _abilityPhase.Value == AbilityPhase.Idle && Time.time >= _abilityCooldownUntil,
                _playerState.CanAct && _abilityPhase.Value != AbilityPhase.Idle);
        }

        private void ExecuteAbilityCone()
        {
            Physics2D.SyncTransforms();
            Physics2D.OverlapCircle(transform.position, _coneDistance,
                new ContactFilter2D { useTriggers = true }, _abilityHits);
            foreach (Collider2D hit in _abilityHits)
            {
                Vector2 target = hit.ClosestPoint(transform.position);
                Vector2 toTarget = target - (Vector2)transform.position;
                if (toTarget.sqrMagnitude > 0.0001f && Vector2.Angle(_abilityForward.Value, toTarget) > _coneAngle) continue;
                if (Physics2D.Linecast(transform.position, target, _obstructionFilter, _obstructionHits) > 0) continue;

                NetworkProjectile projectile = hit.GetComponentInParent<NetworkProjectile>();
                if (projectile)
                {
                    bool ownBall = projectile == _activeProjectile;
                    ulong damageSource = projectile.DamageSourceClientId;
                    if (!projectile.IsSpawned || !projectile.IsServer
                        || (!ownBall && (damageSource == OwnerClientId
                            || !_playerState.CanReceiveHitFrom(damageSource)))
                        || !_abilityProcessedTargets.Add(projectile.NetworkObject)) continue;

                    Vector2 hitDirection = _abilityForward.Value.x < 0f ? Vector2.left : Vector2.right;
                    bool comingTowardPlayer = IsProjectileComingTowardPlayer(projectile, hitDirection);

                    if (ownBall) _activeProjectile.BatServer(hitDirection, _projectileReflectMinimumSpeed);
                    else projectile.ReflectServer(OwnerClientId, _projectileReflectMinimumSpeed);

                    if (comingTowardPlayer)
                    {
                        PlayBallImpactVfxClientRpc(hitDirection);
                    }
                    continue;
                }
                

                NetworkPlayerState player = hit.GetComponentInParent<NetworkPlayerState>();
                if (!player || !player.IsSpawned || !player.IsServer || !player.CanAct || player.IsInvulnerable
                    || player.OwnerClientId == OwnerClientId || !player.CanReceiveHitFrom(OwnerClientId)
                    || !_abilityProcessedTargets.Add(player.NetworkObject)) continue;
                if (player.TryGetComponent(out NetworkPlayerCombat combat)) combat.ApplyUpwardImpulseRpc(_playerUpwardImpulse);
            }
        }

        private bool IsProjectileComingTowardPlayer(NetworkProjectile projectile, Vector2 hitDirection)
        {
            Vector2 velocity = Vector2.zero;
            if (projectile.TryGetComponent(out Rigidbody2D rb))
            {
                velocity = rb.linearVelocity;
            }

            if (velocity.sqrMagnitude > 0.001f)
            {
                return Vector2.Dot(velocity.normalized, hitDirection) < -0.1f;
            }

            Vector2 toProjectile = (Vector2)projectile.transform.position - (Vector2)transform.position;
            return Vector2.Dot(toProjectile.normalized, hitDirection) > 0.1f;
        }

        [ClientRpc]
        private void PlayBallImpactVfxClientRpc(Vector2 hitDirection)
        {
            if (!_ballImpactVfxObject) return;

            if (_hideVfxRoutine != null)
            {
                StopCoroutine(_hideVfxRoutine);
                _hideVfxRoutine = null;
            }

            bool faceLeft = hitDirection.x < 0f;
            float sideSign = faceLeft ? -1f : 1f;

            _ballImpactVfxObject.SetActive(false);
            _ballImpactVfxObject.transform.localPosition = new Vector3(
                sideSign * Mathf.Abs(_impactSideOffset),
                _ballImpactVfxObject.transform.localPosition.y,
                _ballImpactVfxObject.transform.localPosition.z);

            if (_ballImpactSpriteRenderer)
            {
                _ballImpactSpriteRenderer.flipX = faceLeft;
            }

            _ballImpactVfxObject.SetActive(true);

            if (_vfxDurationSeconds > 0f)
            {
                _hideVfxRoutine = StartCoroutine(HideVfxAfterDelay(_vfxDurationSeconds));
            }
        }

        private System.Collections.IEnumerator HideVfxAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            HideImpactVfxImmediate();
            _hideVfxRoutine = null;
        }

        private void HideImpactVfxImmediate()
        {
            if (!_ballImpactVfxObject) return;
            _ballImpactVfxObject.SetActive(false);
        }

        private Vector2 GetFacingDirection(Vector2 aimDirection, Vector2 moveDirection)
        {
            if (Mathf.Abs(aimDirection.x) > 0.01f) return aimDirection.x < 0f ? Vector2.left : Vector2.right;
            if (Mathf.Abs(moveDirection.x) > 0.01f) return moveDirection.x < 0f ? Vector2.left : Vector2.right;
            return _facingSprite && _facingSprite.flipX ? Vector2.left : Vector2.right;
        }

        private void ApplyAbilityPresentation()
        {
            bool active = _abilityPhase.Value != AbilityPhase.Idle;
            if (_presentation) _presentation.SetSkillActive(active);
            ApplyAbilityFacing();
            if (!_animator || !_animator.runtimeAnimatorController) return;
            _animator.SetInteger(AbilityPhaseParameter, (int)_abilityPhase.Value);
        }

        private void HandleAbilityPhaseChanged(AbilityPhase previous, AbilityPhase current) => ApplyAbilityPresentation();
        private void HandleAbilityDirectionChanged(Vector2 previous, Vector2 current) => ApplyAbilityFacing();
        private void ApplyAbilityFacing()
        {
            if (_presentation) _presentation.SetAimFacing(_abilityForward.Value, _abilityPhase.Value != AbilityPhase.Idle);
        }
        private void HandleAmmoChanged(int previous, int current) => RefreshAmmoIndicators();
        private void HandlePlayerStateChanged()
        {
            if (IsServer && !_playerState.CanAct) CancelAbilityServer();
            RefreshAmmoIndicators();
        }

        private void RefreshAmmoIndicators()
        {
            if (_ammoIndicatorsView) _ammoIndicatorsView.SetCount(_ammo.Value,
                _playerState.CanAct && _playerState.IndicatorsVisible, _playerState.IndicatorColor);
        }
    }
}
