using PiGame.Sound;
using Unity.Netcode;
using UnityEngine;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(NetworkPlayerState), typeof(PlayerMove), typeof(Collider2D))]
    public class CowboyCombat : NetworkBehaviour, ICharacterCombat
    {
        [SerializeField] private ProjectileDefinition _projectile;
        [SerializeField, Min(1)] private int _maximumAmmo = 6;
        [SerializeField, Min(0.01f)] private float _reloadSeconds = 1.5f;
        [SerializeField, Min(0f)] private float _shotCooldownSeconds = 0.35f;
        [SerializeField, Min(0f)] private float _projectileSpawnDistance = 0.5f;
        [SerializeField, Min(0.01f)] private float _rollSeconds = 0.2f;
        [SerializeField, Min(0f)] private float _rollSpeed = 16f;
        [SerializeField] private LayerMask _rollPassThroughLayers;
        [SerializeField] private SpriteRenderer[] _ammoIndicators;
        [SerializeField] private Color _rollTint = new Color(0.45f, 0.45f, 0.45f, 0.65f);

        private readonly NetworkVariable<int> _ammo = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone);
        private readonly NetworkVariable<float> _rollDirectionX = new NetworkVariable<float>(1f);
        private readonly NetworkVariable<bool> _isRolling = new NetworkVariable<bool>();

        private NetworkPlayerState _playerState;
        private PlayerMove _playerMove;
        private Collider2D _bodyCollider;
        private SpriteRenderer _bodySprite;
        private Color _colorBeforeRoll;
        private CowboySoundManager _cowboySound;
        private bool _visualRollActive;
        private LayerMask _originalExcludedLayers;
        private bool _isReloading;
        private bool _couldActLastFrame;
        private float _reloadEndsAt;
        private float _rollEndsAt;
        private float _nextShotAt;

        private void Awake()
        {
            _playerState = GetComponent<NetworkPlayerState>();
            _playerMove = GetComponent<PlayerMove>();
            _bodyCollider = GetComponent<Collider2D>();
            _bodySprite = GetComponent<SpriteRenderer>();
            _cowboySound = GetComponent<CowboySoundManager>();
            _originalExcludedLayers = _bodyCollider.excludeLayers;

            if (_projectile == null || _projectile.Prefab == null
                || _rollPassThroughLayers.value == 0
                || _ammoIndicators == null || _ammoIndicators.Length != _maximumAmmo)
            {
                Debug.LogError("Configure o projétil, as camadas e os indicadores de munição no CowboyCombat.", this);
                enabled = false;
                return;
            }

            foreach (SpriteRenderer indicator in _ammoIndicators)
            {
                if (indicator != null)
                {
                    continue;
                }

                Debug.LogError("Todos os indicadores de munição devem estar ligados no CowboyCombat.", this);
                enabled = false;
                return;
            }
        }
        public void ClearProjectilesServer() { }
        public override void OnNetworkSpawn()
        {
            if (!enabled)
            {
                return;
            }

            _ammo.OnValueChanged += HandleAmmoChanged;
            _rollDirectionX.OnValueChanged += HandleRollDirectionChanged;
            _isRolling.OnValueChanged += HandleRollingChanged;
            _playerState.StateChanged += RefreshAmmoIndicators;

            if (IsServer)
            {
                _ammo.Value = _maximumAmmo;
                _couldActLastFrame = _playerState.CanAct;
            }

            foreach (SpriteRenderer indicator in _ammoIndicators)
            {
                indicator.gameObject.SetActive(true);
            }

            ApplyRollState();
            RefreshAmmoIndicators();
        }

        public override void OnNetworkDespawn()
        {
            _ammo.OnValueChanged -= HandleAmmoChanged;
            _rollDirectionX.OnValueChanged -= HandleRollDirectionChanged;
            _isRolling.OnValueChanged -= HandleRollingChanged;
            _playerState.StateChanged -= RefreshAmmoIndicators;

            if (IsServer)
            {
                _playerState.SetInvulnerableServer(false);
            }

            _bodyCollider.excludeLayers = _originalExcludedLayers;
            if (_visualRollActive)
            {
                _bodySprite.color = _colorBeforeRoll;
                _visualRollActive = false;
            }
            if (IsOwner)
            {
                _playerMove.EndHorizontalMovementOverride();
            }
        }

        private void Update()
        {
            if (!IsServer || !enabled)
            {
                return;
            }

            bool canAct = _playerState.CanAct;
            if (!canAct)
            {
                if (_isRolling.Value)
                {
                    EndRollServer();
                }

                _couldActLastFrame = false;
                return;
            }

            if (!_couldActLastFrame)
            {
                _ammo.Value = _maximumAmmo;
                _isReloading = false;
                _nextShotAt = 0f;
                _couldActLastFrame = true;
            }

            if (_isRolling.Value && Time.time >= _rollEndsAt)
            {
                EndRollServer();
            }

            if (_isReloading && Time.time >= _reloadEndsAt)
            {
                if(_cowboySound != null)
                {
                    _cowboySound.PlayCowboyReload();
                }
                _ammo.Value = _maximumAmmo;
                _isReloading = false;
            }
        }
        public void RefillAmmoServer()
        {
            if (!IsServer) return;
            _ammo.Value = _maximumAmmo;
            _isReloading = false;
        }
        public void ShootServer(Vector2 direction)
        {
            if (!IsServer || !enabled) 
            {
                return;
            }

            if( _ammo.Value <= 0)
            {
                if(_cowboySound != null)
                {
                    _cowboySound.PlayCowboyEmpty();
                    return;
                }
            }

            if(!_playerState.CanAct || _isRolling.Value || _isReloading || Time.time < _nextShotAt)
            {
                return;
            }
            
            Vector2 shotDirection = direction.sqrMagnitude > 0.01f
                ? direction.normalized
                : Vector2.right;
            Vector3 spawnPosition = transform.position
                + (Vector3)(shotDirection * _projectileSpawnDistance);

            NetworkProjectile projectile = Instantiate(
                _projectile.Prefab, spawnPosition, Quaternion.identity);
            projectile.NetworkObject.Spawn(true);
            projectile.InitializeServer(
                OwnerClientId, shotDirection, _playerState.IndicatorColor, _projectile);

            _ammo.Value--;

            if(_cowboySound != null)
            {
                _cowboySound.PlayCowboyShoot();
            }
            _nextShotAt = Time.time + _shotCooldownSeconds;
            if (_ammo.Value == 0)
            {
                _isReloading = true;
                _reloadEndsAt = Time.time + _reloadSeconds;
            }
        }

        public void ShootReleaseServer()
        {
        }

        public void UpdateShootAimServer(Vector2 direction)
        {
        }

        public void BeginAbilityServer(Vector2 aimDirection, Vector2 moveDirection)
        {
            if (!IsServer || !enabled || !_playerState.CanAct || _isRolling.Value
                || !_playerMove.CanBeginWallRestrictedAbility())
            {
                return;
            }

            if(_cowboySound != null)
            {
                _cowboySound.PlayCowboySkill();
            }
            _ammo.Value = Mathf.Min(_maximumAmmo, _ammo.Value + 2);
            _isReloading = false;
            _rollDirectionX.Value = moveDirection.x < 0f ? -1f : 1f;
            _rollEndsAt = Time.time + _rollSeconds;
            _playerState.SetInvulnerableServer(true);
            _isRolling.Value = true;
        }

        public void EndAbilityServer(Vector2 aimDirection, Vector2 moveDirection)
        {
            // O rolamento tem duração fixa; soltar o botão não o interrompe.
        }

        private void EndRollServer()
        {
            _isRolling.Value = false;
            _playerState.SetInvulnerableServer(false);
        }

        private void HandleRollDirectionChanged(float previousValue, float currentValue)
        {
            if (_isRolling.Value)
            {
                ApplyRollState();
            }
        }

        private void HandleRollingChanged(bool previousValue, bool currentValue)
        {
            ApplyRollState();
        }

        private void HandleAmmoChanged(int previousValue, int currentValue)
        {
            RefreshAmmoIndicators();
        }

        private void RefreshAmmoIndicators()
        {
            for (int index = 0; index < _ammoIndicators.Length; index++)
            {
                bool visible = _playerState.CanAct
                    && index < _ammo.Value;
                _ammoIndicators[index].color = _playerState.IndicatorColor;
                _ammoIndicators[index].enabled = visible;
                _ammoIndicators[index].gameObject.SetActive(visible);
            }
        }

        private void ApplyRollState()
        {
            bool rolling = _isRolling.Value;
            if (rolling && !_visualRollActive)
            {
                _colorBeforeRoll = _bodySprite.color;
                _visualRollActive = true;
                _bodySprite.color = new Color(
                    _colorBeforeRoll.r * _rollTint.r,
                    _colorBeforeRoll.g * _rollTint.g,
                    _colorBeforeRoll.b * _rollTint.b,
                    _colorBeforeRoll.a * _rollTint.a);
            }
            else if (!rolling && _visualRollActive)
            {
                _bodySprite.color = _colorBeforeRoll;
                _visualRollActive = false;
            }
            _bodyCollider.excludeLayers = rolling
                ? _originalExcludedLayers.value | _rollPassThroughLayers.value
                : _originalExcludedLayers.value;

            if (!IsOwner)
            {
                return;
            }

            if (rolling)
            {
                _playerMove.BeginHorizontalMovementOverride(_rollDirectionX.Value * _rollSpeed);
            }
            else
            {
                _playerMove.EndHorizontalMovementOverride();
            }
        }
    }
}
