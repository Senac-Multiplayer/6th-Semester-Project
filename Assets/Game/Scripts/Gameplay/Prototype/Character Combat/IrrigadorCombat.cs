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
        [SerializeField, Min(1f)] private int _maxNails = 4;
        [SerializeField] private AmmoIndicatorsView _nailIndicators;
        private NetworkVariable<int> _nails = new NetworkVariable<int>(4);
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

        private readonly List<IrrigadorProjectile> _abilityTargets = new();
        private readonly List<Collider2D> _abilityHits = new(8);
        private bool _abilityActive;
        private Vector2 _abilityAimDirection;

        private IrrigadorProjectile _magnetTarget;
        private bool _magnetActive;

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
            if (_projectile == null || _projectile.Prefab == null)
            {
                Debug.LogError("Configure uma definição com prefab de projétil no BasicCharacterCombat.", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            _nails.OnValueChanged += HandleNailsChanged;
            _playerState.StateChanged += RefreshNailIndicators;
            RefreshNailIndicators();

            if(!IsServer)
                return;
            
            _nails.Value = _maxNails;
        }

        public override void OnNetworkDespawn()
        {
            _nails.OnValueChanged -= HandleNailsChanged;
            _playerState.StateChanged -= RefreshNailIndicators;
            _nailIndicators.SetCount(0, false, _playerState.IndicatorColor);

            //Limpa quando o jogador desconectar do jogo
            if (IsServer)
            {
                _activeProjectiles.Clear();
            }
        }

        private void HandleNailsChanged(int previousValue, int currentValue)
        {
            RefreshNailIndicators();
        }

        private void RefreshNailIndicators()
        {
            _nailIndicators.SetCount(_nails.Value, _playerState.CanAct,
                _playerState.IndicatorColor);
        }

        public void ShootServer(Vector2 direction)
        {
            if (!IsServer || !_playerState.CanAct)
                return;

            if(_nails.Value <= 0)
                return;

            if(_projectile == null || _projectile.Prefab == null || Time.time < _nextShotTime)
                return;
            
            _nextShotTime = Time.time + _shotCooldownSeconds;

            Vector2 shotDirection = direction.sqrMagnitude > 0.01f
                ? direction.normalized
                : Vector2.right;
            NetworkProjectile projectile = Instantiate(
                _projectile.Prefab, transform.position,
                Quaternion.FromToRotation(Vector2.right, shotDirection));
            if(_irrigadorSound != null)
            {
                _irrigadorSound.PlayNailShoot();
            }
            projectile.PlaceAtMuzzle(shotDirection, _projectileSpawnDistance);
            projectile.NetworkObject.Spawn(true);
            IrrigadorProjectile irrigadorProjectile = projectile.GetComponent<IrrigadorProjectile>();
            if(irrigadorProjectile != null)
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

            if(_nails.Value <= 0)
            {
                _magnetBlockedUntilRelease = true;
                StopMagnetServer();
            }
        }
        public void RefillAmmoServer()
        {
            if (!IsServer) return;
            _nails.Value = _maxNails;
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
            if(!IsServer)
                return;
            
            _nails.Value = Mathf.Min(_nails.Value + 1, _maxNails);
        }

        private IrrigadorProjectile FindMagnetTarget(Vector2 aimDirection)
        {
            IrrigadorProjectile bestTarget = null;
            float bestAlignment = _magnetAimThreshold;

            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, _magnetRadius);

            foreach(Collider2D hit in hits)
            {
                IrrigadorProjectile nail = hit.GetComponentInParent<IrrigadorProjectile>();

                if(nail == null)
                    continue;
                
                if(!nail.IsStuck)
                    continue;
                
                if(nail.ShooterClientId != OwnerClientId)
                    continue;
                
                Vector2 toNail = ((Vector2)nail.transform.position - (Vector2)transform.position).normalized;

                float alignment = Vector2.Dot(aimDirection.normalized, toNail);

                if(alignment < bestAlignment)
                    continue;
                
                bestAlignment = alignment;
                bestTarget = nail;
            }

            return bestTarget;
        }
        
        private void TryActiveMagnetServer(Vector2 aimDirection)
        {
            IrrigadorProjectile target = FindMagnetTarget(aimDirection);

            if(target == null)
            {
                StopMagnetServer();
                return;
            }

            _magnetTarget = target;
            _magnetActive = true;
        }
        private void StopMagnetServer()
        {
            _magnetTarget = null;
            _magnetActive = false;
        }

        public void ShootReleaseServer()
        {
           if(!IsServer)
            return;

            _magnetBlockedUntilRelease = false;
            StopMagnetServer(); 
        }

        public void UpdateShootAimServer(Vector2 direction)
        {
            if(!IsServer)
                return;

            if(_nails.Value > 0 || _magnetBlockedUntilRelease)
            {
                if (_magnetActive)
                    StopMagnetServer();
                return;
            }

            TryActiveMagnetServer(direction);
        }
        private void FixedUpdate()
        {
            if(!IsServer)
                return;
            
            UpdateMagnetServer();
            UpdateAbilityServer();
            // Apenas apresenta as condições existentes; não altera o uso da habilidade.
            _playerState.SetAbilityAvailabilityServer(
                _playerState.CanAct && _nails.Value <= _maxNails - 2,
                _playerState.CanAct && _abilityActive);
        }

        private void UpdateMagnetServer()
        {
            if (!_magnetActive || _magnetTarget == null)
                return;

            if(_irrigadorSound != null)
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

            foreach(Collider2D hit in _abilityHits)
            {
                IrrigadorProjectile nail = hit.GetComponentInParent<IrrigadorProjectile>();

                if(nail == null)
                    continue;

                if(!nail.IsStuck)
                    continue;
                if(nail.ShooterClientId != OwnerClientId)
                    continue;
                if(_abilityTargets.Contains(nail))
                    continue;
                    
                Vector2 toNail = ((Vector2)nail.transform.position - (Vector2)transform.position).normalized;

                float alignment = Vector2.Dot(normalizedAim,toNail);

                if(alignment < _abilityAimThreshold)
                    continue;
                
                _abilityTargets.Add(nail);
            }
        }

        public void BeginAbilityServer(Vector2 aimDirection, Vector2 moveDirection)
        {
            if(!IsServer || !_playerState.CanAct)
                return;
            
            if(_nails.Value > _maxNails - 2)
                return;
            
            _abilityTargets.Clear();
            _abilityAimDirection = aimDirection.normalized;
            _abilityActive = true;
            RefreshAbilityTargets();
        }

        private void UpdateAbilityServer()
        {
            if(!_abilityActive)
                return;

            if(!_playerState.CanAct)
            {
                EndAbilityServer(_abilityAimDirection, Vector2.zero);
                return;
            }

            // Incluir também os pregos que fincam depois de apertar a habilidade.
            RefreshAbilityTargets();

            if(_abilityTargets.Count > 0 && _irrigadorSound != null)
            {
                _irrigadorSound.PlayIrrigadorSkill();
            }
            
            for(int i = _abilityTargets.Count - 1; i >=0; i--)
            {
                IrrigadorProjectile nail = _abilityTargets[i];

                if(nail == null || !nail.IsSpawned || !nail.IsStuck)
                {
                    _abilityTargets.RemoveAt(i);
                    continue;
                }

                Vector2 toPlayer = (Vector2)transform.position - (Vector2)nail.transform.position;
                
                if(toPlayer.sqrMagnitude <= 0.01f)
                    continue;
                
                nail.PullServer(toPlayer.normalized, _abilityPullSpeed,true);
            }
        }

        public void EndAbilityServer(Vector2 aimDirection, Vector2 moveDirection)
        {
            if(!IsServer)
                return;

            foreach(IrrigadorProjectile nail in _abilityTargets)
            {
                if(nail != null)
                {
                    nail.StopPullServer();
                }
            }

            _abilityTargets.Clear();
            _abilityActive = false;
        }


    }
}

