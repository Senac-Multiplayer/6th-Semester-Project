using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using PiGame.Sound;


namespace PiGame.Gameplay
{
    [RequireComponent(typeof(NetworkPlayerState))]
        
    public class RebeldeCombat : NetworkBehaviour, ICharacterCombat
    {
        [Header("Combat Settings")]
        [SerializeField] private ProjectileDefinition _projectile;
        [SerializeField, Min(0f)] private float _shotCooldownSeconds = 0.35f;
        [SerializeField, Min(0f)] private float _projectileSpawnDistance = 0.85f;

        [SerializeField] private float[] _projectileVelocityStack = {5f, 10f, 15f};

        [Header("Ability Settings")]
        [SerializeField, Min(0f)] private float _coneDistance = 4f;
        [SerializeField, Range(0f, 180f)] private float _coneAngle = 60f;
        [SerializeField, Min(0f)] private float _projectileReflectMinimumSpeed = 3f;

        [SerializeField, Min(0.1f)] private float _maxAbilityHoldSeconds = 1.5f;
        [SerializeField, Min(0f)] private float _abilityCooldownSeconds = 4f;
        [SerializeField, Min(0f)] private float _playerUpwardImpulse = 3f;
        [SerializeField, Min(0f)] private float _abilityGraceSeconds = 0.12f;

        private bool _abilityHitWindowActive;
        private float _abilityHitWindowUntil;
        private readonly HashSet<NetworkObject> _abilityProcessedTargets = new();
        private bool _abilityActive;
        private Vector2 _abilityForward = Vector2.right;
        private float _abilityStartedAt;
        private float _abilityCooldownUntil;
        private SpriteRenderer _facingSprite;
        private RebeldeSoundManager _rebeldeSound;

        private float _projectileVelocity;
        private int _projectileVelocityPointer = 0;

        private AmmoIndicatorsView _ammoIndicatorsView;

        private NetworkVariable<int> _ammo = new NetworkVariable<int>(1,NetworkVariableReadPermission.Everyone);
            
        private NetworkPlayerState _playerState;
        private NetworkVariable<bool> _hasShot = new NetworkVariable<bool>(true);
        public bool HasShot => _hasShot.Value;
        private float _nextShotTime;
        private RebeldeProjectile _activeRebeldeProjectile;

        public void Awake()
        {
            _playerState = GetComponent<NetworkPlayerState>();
            _rebeldeSound = GetComponent<RebeldeSoundManager>();

            if (_projectile == null || _projectile.Prefab == null)
            {
                Debug.LogError("Configure uma definição com prefab de projétil no BasicCharacterCombat.", this);
            }
            _facingSprite = GetComponent<SpriteRenderer>();
            _projectileVelocity = _projectileVelocityStack[0];

            _ammoIndicatorsView = GetComponent<AmmoIndicatorsView>();
        }
        public void ShootServer(Vector2 direction)
        {
            if (!IsServer || !_playerState.CanAct)
            {
                return;
            }

            if(_ammo.Value <= 0)
            {
                if(_rebeldeSound != null)
                {
                    _rebeldeSound.PlayRebeldeEmpty();
                    return;
                }
            }

            if(_projectile == null || !_hasShot.Value || _projectile.Prefab == null || Time.time < _nextShotTime)
            {
                return;
            }

            _nextShotTime = Time.time + _shotCooldownSeconds;

            Vector2 shotDirection = direction.sqrMagnitude > 0.01f
                ? direction.normalized
                : Vector2.right;
                    
            NetworkProjectile projectile = Instantiate(
                _projectile.Prefab, transform.position, Quaternion.identity);
            projectile.PlaceAtMuzzle(shotDirection, _projectileSpawnDistance);

            projectile.NetworkObject.Spawn(true);

            RebeldeProjectile rebeldeProjectile = projectile.GetComponent<RebeldeProjectile>();
            if(rebeldeProjectile != null)
            {
                if(_rebeldeSound != null)
                {
                    _rebeldeSound.PlayRebeldeShot();
                }
                rebeldeProjectile.InitializeServer(OwnerClientId, shotDirection, _projectile,_projectileVelocity, _rebeldeSound);
                _hasShot.Value = false;
                _activeRebeldeProjectile = rebeldeProjectile;
            }
            _ammo.Value = 0;
            GetNextShot();
        }
        public void RefillAmmoServer()
        {
            if (!IsServer) return;
            _ammo.Value = 1;
            _hasShot.Value = true;
        }

        public void ClearProjectilesServer()
        {
            if (!IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return;

            if (_activeRebeldeProjectile != null && _activeRebeldeProjectile.IsSpawned)
            {
                _activeRebeldeProjectile.NetworkObject.Despawn(true);
            }
            _activeRebeldeProjectile = null;
        }

        private void Update()
        {
            if (!IsServer)
                return;

            if (_abilityActive)
            {
                float heldTime = Time.time - _abilityStartedAt;

                if (heldTime >= _maxAbilityHoldSeconds)
                {
                    _abilityActive = false;
                    _abilityCooldownUntil = Time.time + _abilityCooldownSeconds;
                    _playerState.SetAbilityCooldownServer(_abilityCooldownSeconds);
                }


            }
            if (_abilityHitWindowActive)
            {
                if (Time.time <= _abilityHitWindowUntil)
                {
                    // Continua verificando o cone durante a janela adicional.
                    ExecuteAbilityCone();
                }
                else
                {
                    // A janela expirou; não processa mais alvos.
                    _abilityHitWindowActive = false;
                }
            }

            if (!_abilityActive &&
                _abilityCooldownUntil > 0f &&
                Time.time >= _abilityCooldownUntil)
            {
                _abilityCooldownUntil = 0f;
            }
        }
        public override void OnNetworkSpawn()
        {
            _ammo.OnValueChanged += HandleAmmoChanged;
            _playerState.StateChanged += RefreshAmmoIndicators;

            if (IsServer)
            {
                _ammo.Value = 1;
                _hasShot.Value = true;
                _playerState.SetAbilityCooldownServer(0f);
            }

            RefreshAmmoIndicators();
        }

        public override void OnNetworkDespawn()
        {
            _ammo.OnValueChanged -= HandleAmmoChanged;
            _playerState.StateChanged -= RefreshAmmoIndicators;
            if (IsServer)
            {
                _activeRebeldeProjectile = null;
            }
        }

        private void HandleAmmoChanged(int previousValue, int currentValue)
        {
            RefreshAmmoIndicators();
        }

        private void RefreshAmmoIndicators()
        {
            if (_ammoIndicatorsView == null)
                return;

            _ammoIndicatorsView.SetCount(
                _ammo.Value,
                _playerState.CanAct,
                _playerState.IndicatorColor
            );
        }
        public void AddShotServer()
        {
            if (!IsServer)
                return;
            
            if(_rebeldeSound != null)
            {
                _rebeldeSound.PlayRebeldeCatch();
            }

            _hasShot.Value = true;

            _ammo.Value = 1;
        }
        private void GetNextShot()
        {
            _projectileVelocityPointer++;
            if(_projectileVelocityPointer >= _projectileVelocityStack.Length)
            {
                _projectileVelocityPointer = 0;
            }

            _projectileVelocity = _projectileVelocityStack[_projectileVelocityPointer];
        }

        //Esse personagem não precisa destes métodos, mas estão aqui pela implementação da interface
        public void ShootReleaseServer()
        {

        }
        public void UpdateShootAimServer(Vector2 direction)
        {

        }


        public void BeginAbilityServer(Vector2 aimDirection, Vector2 moveDirection)
        {
            if (!IsServer || !_playerState.CanAct || _abilityActive ||  _abilityHitWindowActive)
                return;

            if (Time.time < _abilityCooldownUntil)
            {
                return;
            }
            _abilityProcessedTargets.Clear();

            _abilityActive = true;
            _abilityStartedAt = Time.time;

            _rebeldeSound.PlayRebeldeSkill();
            Debug.Log($"[Rebelde] Skill iniciada em {Time.time}");
            
            _abilityForward = GetFacingDirection(aimDirection, moveDirection);
        }
        public void EndAbilityServer(Vector2 aimDirection, Vector2 moveDirection)
        {
            if (!IsServer)
                return;

            if (!_abilityActive)
            {
                return;
            }

            Debug.Log($"[Rebelde] End | IsServer={IsServer} | Time={Time.time}");

            float heldTime = Time.time - _abilityStartedAt;

            _abilityActive = false;

            if (heldTime >= _maxAbilityHoldSeconds)
            {
                _abilityCooldownUntil = Time.time + _abilityCooldownSeconds;
                _playerState.SetAbilityCooldownServer(_abilityCooldownSeconds);

                return;
            }

            _abilityCooldownUntil = Time.time + _abilityCooldownSeconds;
            _playerState.SetAbilityCooldownServer(_abilityCooldownSeconds);
            _abilityForward = GetFacingDirection(aimDirection, moveDirection);

            _abilityHitWindowActive = true;
            _abilityHitWindowUntil = Time.time + _abilityGraceSeconds;

            ExecuteAbilityCone();
        }

        private bool IsInsideAbilityCone(Vector2 targetPosition, Vector2 forward)
        {
            Vector2 toTarget = targetPosition - (Vector2)transform.position;

            float distance = toTarget.magnitude;

            if(distance > _coneDistance)
            {
                return false;
            }

            if(distance <= 0.001f)
            {
                return true;
            }

            float angle =
                Vector2.Angle(
                    forward,
                    toTarget.normalized);

            return angle <= _coneAngle;
        }
        private void ExecuteAbilityCone()
        {
                Collider2D[] hits = Physics2D.OverlapCircleAll(
                transform.position,
                _coneDistance);

            foreach (Collider2D hit in hits)
            {
                if (!IsInsideAbilityCone(hit.bounds.center, _abilityForward))
                    continue;

                // Primeiro, verifica se o alvo é um projétil.
                NetworkProjectile projectile =
                    hit.GetComponentInParent<NetworkProjectile>();

                if (projectile != null)
                {
                    // Não reflete projéteis disparados pelo próprio Rebelde.
                    if (projectile.ShooterClientId == OwnerClientId)
                        continue;

                    // Impede refletir o mesmo projétil em vários frames da janela.
                    if (!_abilityProcessedTargets.Add(projectile.NetworkObject))
                        continue;

                    projectile.ReflectServer(_projectileReflectMinimumSpeed);
                    continue;
                }

                // Se não for projétil, verifica se é um jogador.
                NetworkPlayerState player =
                    hit.GetComponentInParent<NetworkPlayerState>();

                if (player == null)
                    continue;

                // Não afeta o próprio Rebelde.
                if (player.OwnerClientId == OwnerClientId)
                    continue;

                // Impede aplicar o impulso repetidamente ao mesmo jogador.
                if (!_abilityProcessedTargets.Add(player.NetworkObject))
                    continue;

                ApplyUpwardImpulse(player);
            }
        }

        private void ApplyUpwardImpulse(NetworkPlayerState targetPlayer)
        {
            if (!IsServer || targetPlayer == null)
                return;

            NetworkPlayerCombat targetCombat =
                targetPlayer.GetComponent<NetworkPlayerCombat>();

            if (targetCombat == null)
            {
                Debug.LogWarning(
                    $"[Impulse] RebeldeCombat não encontrado em {targetPlayer.name}.",
                    targetPlayer);
                return;
            }

            targetCombat.ApplyUpwardImpulseRpc(_playerUpwardImpulse);
        }

        private Vector2 GetFacingDirection(Vector2 aimDirection, Vector2 moveDirection)
        {
            if(Mathf.Abs(aimDirection.x) > 0.01f)
            {
                return aimDirection.x < 0f ? Vector2.left : Vector2.right;
            }
            else if(Mathf.Abs(moveDirection.x) > 0.01f)
            {
                return moveDirection.x < 0f ?Vector2.left : Vector2.right;
            }

            if(_facingSprite != null)
            {
                return _facingSprite.flipX ? Vector2.left : Vector2.right;
            }

            return _abilityForward;
        }
    }
}
