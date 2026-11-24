using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using PiGame.Sound;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(NetworkPlayerState))]
    public class IrrigadorCombat : NetworkBehaviour, ICharacterCombat
    {
        [Header("Combat Settings")]
        [SerializeField] private ProjectileDefinition _projectile;
        [SerializeField, Min(0f)] private float _shotCooldownSeconds = 0.35f;
        [SerializeField, Min(0f)] private float _projectileSpawnDistance = 0.85f;
        [SerializeField, Min(2)] private int _maxNails = 2;
        [SerializeField] private AmmoIndicatorsView _nailIndicators;
        private NetworkVariable<int> _nails = new NetworkVariable<int>(0);
        public int Nails => _nails.Value;

        [Header("Magnet Settings")]
        [SerializeField, Min(0f)] private float _magnetRadius = 8f;
        [SerializeField, Range(0f, 1f)] private float _magnetAimThreshold = 0.5f;
        [SerializeField, Min(0f)] private float _magnetSpeed = 20f;
        [SerializeField, Min(0f)] private float _magnetStopDistance = 0.25f;
        private bool _magnetBlockedUntilRelease;

        [Header("Ability Settings")]
        [SerializeField, Min(0f)] private float _abilityRadius = 8f;
        [SerializeField, Range(0f, 1f)] private float _abilityAimThreshold = 0.5f;
        [SerializeField, Min(0f)] private float _abilityPullSpeed = 25f;
        [SerializeField] private GameObject _vortexVfxObject;

        private readonly NetworkVariable<bool> _vortexActiveNet = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly List<IrrigadorProjectile> _abilityTargets = new();
        private readonly List<Collider2D> _abilityHits = new(8);
        private bool _abilityActive;
        private Vector2 _abilityAimDirection;

        private readonly List<Collider2D> _magnetHits = new(8);
        private IrrigadorProjectile _magnetTarget;
        private readonly NetworkVariable<bool> _magnetAvailable = new(false);
        private readonly NetworkVariable<bool> _magnetActive = new(false);
        public bool IsMagnetActive => _magnetActive.Value;
        public bool CanUseMagnet => _nails.Value == 0 && _magnetAvailable.Value;

        private bool CanStartMagnet => _playerState.CanAct && _nails.Value == 0 && !_magnetBlockedUntilRelease;

        private NetworkPlayerState _playerState;
        private Rigidbody2D _rigidbody;
        private IrrigadorSoundManager _irrigadorSound;

        private float _nextShotTime;
        private readonly List<IrrigadorProjectile> _activeProjectiles = new();

        private void Awake()
        {
            _playerState = GetComponent<NetworkPlayerState>();
            _rigidbody = GetComponent<Rigidbody2D>();
            _irrigadorSound = GetComponent<IrrigadorSoundManager>();
            SetVortexVisual(false);

            if (_projectile == null || _projectile.Prefab == null)
            {
                Debug.LogError("Configure uma definição com prefab de projétil no BasicCharacterCombat.", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            _nails.OnValueChanged += HandleNailsChanged;
            _vortexActiveNet.OnValueChanged += HandleVortexActiveChanged;
            _playerState.StateChanged += RefreshNailIndicators;
            RefreshNailIndicators();
            SetVortexVisual(_vortexActiveNet.Value);

            if (!IsServer)
                return;

            _nails.Value = _maxNails;
            _vortexActiveNet.Value = false;
        }

        public override void OnNetworkDespawn()
        {
            _nails.OnValueChanged -= HandleNailsChanged;
            _vortexActiveNet.OnValueChanged -= HandleVortexActiveChanged;
            _playerState.StateChanged -= RefreshNailIndicators;
            _nailIndicators.SetCount(0, false, _playerState.IndicatorColor);
            SetVortexVisual(false);

            if (IsServer)
            {
                _vortexActiveNet.Value = false;
                _magnetAvailable.Value = false;
                _activeProjectiles.Clear();
            }
        }

        private void HandleVortexActiveChanged(bool previousValue, bool currentValue)
        {
            SetVortexVisual(currentValue);
        }

        private void SetVortexVisual(bool active)
        {
            if (_vortexVfxObject != null)
            {
                _vortexVfxObject.SetActive(active);
            }
        }

        private void HandleNailsChanged(int previousValue, int currentValue)
        {
            RefreshNailIndicators();
        }

        private void RefreshNailIndicators()
        {
            _nailIndicators.SetCount(_nails.Value, _playerState.CanAct && _playerState.IndicatorsVisible,
                _playerState.IndicatorColor);
        }

        public void ShootServer(Vector2 direction)
        {
            if (!IsServer || !_playerState.CanAct)
                return;

            if (_nails.Value <= 0)
                return;

            if (_projectile == null || _projectile.Prefab == null || Time.time < _nextShotTime)
                return;

            _nextShotTime = Time.time + _shotCooldownSeconds;

            Vector2 shotDirection = direction.sqrMagnitude > 0.01f
                ? direction.normalized
                : Vector2.right;
            NetworkProjectile projectile = Instantiate(
                _projectile.Prefab, transform.position,
                Quaternion.FromToRotation(Vector2.right, shotDirection));
            if (_irrigadorSound != null)
            {
                _irrigadorSound.PlayNailShoot();
            }
            projectile.PlaceAtMuzzle(shotDirection, _projectileSpawnDistance);
            projectile.NetworkObject.Spawn(true);
            IrrigadorProjectile irrigadorProjectile = projectile.GetComponent<IrrigadorProjectile>();
            if (irrigadorProjectile != null)
            {
                irrigadorProjectile.InitializeServer(OwnerClientId, shotDirection, _playerState.IndicatorColor, _projectile, _irrigadorSound);
                _activeProjectiles.RemoveAll(p => p == null || !p.IsSpawned);
                _activeProjectiles.Add(irrigadorProjectile);
            }
            else
            {
                projectile.InitializeServer(
                    OwnerClientId, shotDirection, _playerState.IndicatorColor, _projectile);
            }

            _nails.Value--;

            if (_nails.Value <= 0)
            {
                _magnetBlockedUntilRelease = true;
                StopMagnetServer();
            }
        }

        public void RefillAmmoServer()
        {
            if (!IsServer) return;
            _nails.Value = _maxNails;
            _magnetBlockedUntilRelease = false;
            StopMagnetServer();
        }

        public void ClearProjectilesServer()
        {
            if (!IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return;

            _activeProjectiles.RemoveAll(p => p == null || !p.IsSpawned);

            foreach (var proj in _activeProjectiles)
            {
                if (proj != null && proj.IsSpawned)
                {
                    proj.NetworkObject.Despawn(true);
                }
            }
            _activeProjectiles.Clear();
        }

        public void AddNailServer()
        {
            if (!IsServer)
                return;

            _nails.Value = Mathf.Min(_nails.Value + 1, _maxNails);
        }

        private IrrigadorProjectile FindMagnetTarget(Vector2? aimDirection = null)
        {
            IrrigadorProjectile bestTarget = null;
            float bestAlignment = _magnetAimThreshold;

            Vector2? normalizedAim = aimDirection?.normalized;
            var filter = new ContactFilter2D { useTriggers = Physics2D.queriesHitTriggers };
            filter.SetLayerMask(Physics2D.DefaultRaycastLayers);
            Physics2D.OverlapCircle(transform.position, _magnetRadius, filter, _magnetHits);

            foreach (Collider2D hit in _magnetHits)
            {
                IrrigadorProjectile nail = hit.GetComponentInParent<IrrigadorProjectile>();

                if (nail == null)
                    continue;

                if (!nail.IsStuck)
                    continue;

                if (nail.ShooterClientId != OwnerClientId)
                    continue;

                if (normalizedAim is not Vector2 aim)
                    return nail;

                Vector2 toNail = ((Vector2)nail.transform.position - (Vector2)transform.position).normalized;
                float alignment = Vector2.Dot(aim, toNail);

                if (alignment < bestAlignment)
                    continue;

                bestAlignment = alignment;
                bestTarget = nail;
            }

            return bestTarget;
        }

        private void TryActiveMagnetServer(Vector2 aimDirection)
        {
            IrrigadorProjectile target = FindMagnetTarget(aimDirection);

            if (target == null)
            {
                StopMagnetServer();
                return;
            }

            _magnetTarget = target;
            _magnetActive.Value = true;
        }

        private void StopMagnetServer()
        {
            _magnetTarget = null;
            _magnetActive.Value = false;
        }

        public void ShootReleaseServer()
        {
            if (!IsServer)
                return;

            _magnetBlockedUntilRelease = false;
            StopMagnetServer();
        }

        public void UpdateShootAimServer(Vector2 direction)
        {
            if (!IsServer)
                return;

            if (!CanStartMagnet)
            {
                if (_magnetActive.Value)
                    StopMagnetServer();
                return;
            }

            TryActiveMagnetServer(direction);
        }

        private void FixedUpdate()
        {
            if (!IsServer)
                return;

            UpdateMagnetServer();
            UpdateAbilityServer();
            _magnetAvailable.Value = CanStartMagnet && FindMagnetTarget() != null;
            _playerState.SetAbilityAvailabilityServer(
                _playerState.CanAct && _nails.Value <= _maxNails - 2,
                _playerState.CanAct && _abilityActive);
        }

        private void UpdateMagnetServer()
        {
            if (!_magnetActive.Value)
                return;
            if (_magnetTarget == null || !_magnetTarget.IsSpawned || !_magnetTarget.IsStuck || !_playerState.CanAct)
            {
                StopMagnetServer();
                return;
            }

            if (_irrigadorSound != null)
            {
                _irrigadorSound.PlayIrrigadorPull();
            }

            Vector2 toTarget =
                (Vector2)_magnetTarget.transform.position - _rigidbody.position;

            float distance = toTarget.magnitude;

            if (distance <= _magnetStopDistance)
            {
                _rigidbody.linearVelocity = Vector2.zero;
                StopMagnetServer();
                return;
            }

            _rigidbody.linearVelocity =
                toTarget.normalized * _magnetSpeed;
        }

        private void RefreshAbilityTargets()
        {
            Physics2D.OverlapCircle(transform.position, _abilityRadius,
                new ContactFilter2D { useTriggers = true }, _abilityHits);

            Vector2 normalizedAim = _abilityAimDirection;

            foreach (Collider2D hit in _abilityHits)
            {
                IrrigadorProjectile nail = hit.GetComponentInParent<IrrigadorProjectile>();

                if (nail == null)
                    continue;

                if (!nail.IsStuck)
                    continue;
                if (nail.ShooterClientId != OwnerClientId)
                    continue;
                if (_abilityTargets.Contains(nail))
                    continue;

                Vector2 toNail = ((Vector2)nail.transform.position - (Vector2)transform.position).normalized;

                float alignment = Vector2.Dot(normalizedAim, toNail);

                if (alignment < _abilityAimThreshold)
                    continue;

                _abilityTargets.Add(nail);
            }
        }

        public void BeginAbilityServer(Vector2 aimDirection, Vector2 moveDirection)
        {
            if (!IsServer || !_playerState.CanAct)
                return;

            _vortexActiveNet.Value = true;
            if (_nails.Value > _maxNails - 2)
                return;

            _abilityTargets.Clear();
            _abilityAimDirection = aimDirection.normalized;
            _abilityActive = true;
            RefreshAbilityTargets();
        }

        private void UpdateAbilityServer()
        {
            if (!_abilityActive)
                return;

            if (!_playerState.CanAct)
            {
                EndAbilityServer(_abilityAimDirection, Vector2.zero);
                return;
            }

            RefreshAbilityTargets();

            if (_abilityTargets.Count > 0 && _irrigadorSound != null)
            {
                _irrigadorSound.PlayIrrigadorSkill();
            }

            for (int i = _abilityTargets.Count - 1; i >= 0; i--)
            {
                IrrigadorProjectile nail = _abilityTargets[i];

                if (nail == null || !nail.IsSpawned || !nail.IsStuck)
                {
                    _abilityTargets.RemoveAt(i);
                    continue;
                }

                Vector2 toPlayer = (Vector2)transform.position - (Vector2)nail.transform.position;

                if (toPlayer.sqrMagnitude <= 0.01f)
                    continue;

                nail.PullServer(toPlayer.normalized, _abilityPullSpeed, true);
            }
        }

        public void EndAbilityServer(Vector2 aimDirection, Vector2 moveDirection)
        {
            if (!IsServer)
                return;

            foreach (IrrigadorProjectile nail in _abilityTargets)
            {
                if (nail != null)
                {
                    nail.StopPullServer();
                }
            }

            _abilityTargets.Clear();
            _abilityActive = false;
            _vortexActiveNet.Value = false;
        }
    }
}