using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using PiGame.Sound;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Collider2D))]
    public class IrrigadorProjectile : NetworkProjectile
    {
        [Header("Projectile Settings")]
        [SerializeField] private Collider2D _collectionTrigger;
        [SerializeField] private Collider2D _tipCollider;
        [SerializeField] private Transform _tip;
        [SerializeField] private LayerMask _surfaceLayer;
        public LayerMask SurfaceLayer => _surfaceLayer;

        [Header("Safety")]
        [SerializeField] private float _despawnBelowY = -20f;
        [SerializeField, Min(1f)] private float _automaticReturnSeconds = 10f;
        private float _automaticReturnAt;
        private bool _isReturningToShooter;

        [Header("CharacterHit Settings")]
        [SerializeField] private float _fallGravity = 20f;
        [SerializeField] private float _fallStartOffset = 0.1f;
        private bool _falling;
        private Vector2 _fallVelocity;

        private bool _IsStuck;
        public bool IsStuck => _IsStuck;

        private Collider2D _projectileCollider;
        private BoxCollider2D _tipBox;
        private readonly RaycastHit2D[] _tipSurfaceHits = new RaycastHit2D[8];
        private IrrigadorSoundManager _ownerSound;
        private readonly List<Collider2D> _collectionHits = new(8);
        private readonly List<RaycastHit2D> _collectionCastHits = new(8);

        public bool _abilityPull;
        private bool _beingPulled;

        private ulong _lastHitClientId;

        private Vector2 _pullDirection;
        private float _pullSpeed;

        private Collider2D _stuckSurface;
        private Vector2 _stuckSurfaceNormal;

        private bool _hasHitPlayer;

        private void Awake()
        {
            _projectileCollider = GetComponent<Collider2D>();
            _tipBox = (BoxCollider2D)_tipCollider;
            _useLifetime = false;
            UpdateColliders();
        }

        public void InitializeServer(
            ulong shooterClientId,
            Vector2 direction,
            Color color,
            ProjectileDefinition definition, IrrigadorSoundManager ownerSound)
        {
            if (!IsServer)
                return;

            base.InitializeServer(shooterClientId, direction, color, definition);
            transform.rotation = Quaternion.FromToRotation(Vector2.right, _direction);

            _IsStuck = false;
            _hasHitPlayer = false;
            _lastHitClientId = ulong.MaxValue;

            _ownerSound = ownerSound;
            _automaticReturnAt = Time.time + Mathf.Max(1f, _automaticReturnSeconds);
            _isReturningToShooter = false;

            UpdateColliders();
        }

        protected override void Update()
        {
            if (!IsServer || !IsSpawned || _isReturningToShooter)
                return;

            // Verificar o prazo antes de ignorar os pregos presos em superfícies.
            if (Time.time >= _automaticReturnAt)
            {
                ReturnToShooterServer();
                return;
            }

            if (_IsStuck && !_beingPulled && !_falling)
                return;
            
            if (TryDespawnOutOfBounds())
                return;

            Vector2 direction;
            float speed;

            if (_falling)
            {
                _fallVelocity.y -= _fallGravity * Time.deltaTime;

                direction = _fallVelocity.normalized;
                speed = _fallVelocity.magnitude;
            }
            else
            {
                direction = _beingPulled ? _pullDirection : _direction;

                speed = _beingPulled ? _pullSpeed : _speed;

            }

            Vector2 movement = direction * speed * Time.deltaTime;

            if (_beingPulled)
                Physics2D.SyncTransforms();

            Vector2 tipSize = GetWorldBoxSize(_tipBox);
            Vector2 tipCenter = _tip.TransformPoint(_tipBox.offset);
            float angle = _tip.eulerAngles.z;
            Vector2 directionOfTravel = movement.normalized;
            var surfaceFilter = new ContactFilter2D { useTriggers = false };
            surfaceFilter.SetLayerMask(_surfaceLayer);

            if (_beingPulled && _stuckSurface && Vector2.Dot(directionOfTravel, _stuckSurfaceNormal) > 0f)
            {
                Vector2 separation = _stuckSurfaceNormal * (Physics2D.defaultContactOffset * 2f);
                if (IsTerrainClear(tipCenter + separation, tipSize, angle, _surfaceLayer))
                {
                    transform.position += (Vector3)separation;
                    tipCenter += separation;
                    _stuckSurface = null;
                }
            }

            int surfaceCount = Physics2D.BoxCast(tipCenter, tipSize, angle, directionOfTravel,
                surfaceFilter, _tipSurfaceHits, movement.magnitude);
            RaycastHit2D hit = surfaceCount > 0 ? _tipSurfaceHits[0] : default;
            bool blockedBySurface = hit.collider != null;

            // Verificar também o trajeto evita atravessar o dono entre dois frames.
            Vector2 collectionMovement = blockedBySurface
                ? movement.normalized * hit.distance
                : movement;
            if (_beingPulled && TryCollectOwner(collectionMovement))
                return;

            if (blockedBySurface)
            {
                transform.position += (Vector3)(movement.normalized * hit.distance
                    + hit.normal * Physics2D.defaultContactOffset);
                StickToSurface(hit.collider, hit.normal);
                return;
            }

            transform.position += (Vector3)movement;

            if (!_beingPulled && Time.time >= _despawnAt)
            {
                ReturnToShooterServer();
            }
        }

        public void HandleTipSurfaceContact(Collider2D other)
        {
            if (!IsServer || _IsStuck || other.isTrigger)
                return;

            StickToSurface(other);
        }

        private void StickToSurface(Collider2D surface, Vector2 surfaceNormal = default)
        {
            if(!_falling)
            {
                Vector2 stickDirection = _beingPulled ? _pullDirection : _direction;

                Vector3 previousTip = _tip.TransformPoint(_tipBox.offset);
                transform.rotation = Quaternion.FromToRotation(Vector2.right, stickDirection);
                transform.position += previousTip - _tip.TransformPoint(_tipBox.offset);
            }

            if(_ownerSound != null)
            {
                _ownerSound.PlayStuckNail();
            }


            _IsStuck = true;
            _falling = false;
            _beingPulled = false;
            _abilityPull = false;
            _stuckSurface = surface;
            _stuckSurfaceNormal = surfaceNormal;

            // O trigger também pode avisar depois de um passo da física. Só despenetrar:
            // aproximar colliders já separados puxava o prego para dentro das bordas.
            Physics2D.SyncTransforms();
            ColliderDistance2D distance = _tipCollider.Distance(surface);
            if (distance.isValid)
            {
                if (_stuckSurfaceNormal == Vector2.zero) _stuckSurfaceNormal = -distance.normal;
                if (distance.isOverlapped)
                    transform.position += (Vector3)(distance.normal
                        * (distance.distance - Physics2D.defaultContactOffset));
            }

            UpdateColliders();
        }

        protected override void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsServer || !IsSpawned || _falling)
                return;

            NetworkPlayerState playerState =
                other.GetComponentInParent<NetworkPlayerState>();

            if (playerState == null)
                return;

            if (!_IsStuck || _beingPulled)
            {
                TryDamagePlayer(playerState);
                return;
            }

            if (playerState.OwnerClientId != _shooterClientId)
                return;

            IrrigadorCombat combat =
                playerState.GetComponent<IrrigadorCombat>();

            if (combat == null)
                return;

            ReturnToShooterServer(combat);
        }

        public override void ReflectServer(ulong reflectorClientId, float minimumSpeed)
        {
            if (_IsStuck || _falling || _isReturningToShooter) return;
            base.ReflectServer(reflectorClientId, minimumSpeed);
        }

        public void PullServer(Vector2 direction, float speed, bool abilityPull = false)
        {
            if (!IsServer || !IsStuck)
                return;

            DamageSourceClientId = ShooterClientId;
            _beingPulled = true;
            _abilityPull = abilityPull;

            _hasHitPlayer = false;
            _lastHitClientId = ulong.MaxValue;

            _pullDirection = direction.normalized;
            _pullSpeed = speed;

            UpdateColliders();
        }
        public void StopPullServer()
        {
            if (_abilityPull)
                return;

            _beingPulled = false;

            UpdateColliders();
        }

        private void UpdateColliders()
        {
            bool isFlying = !_IsStuck && !_beingPulled && !_falling;
            bool IsStuck = _IsStuck && !_beingPulled;
            bool isBeingPulled = _beingPulled;

            if (_projectileCollider != null)
            {
                _projectileCollider.enabled = isFlying || isBeingPulled;
            }
            if (_tipCollider != null)
            {
                _tipCollider.enabled = isFlying || isBeingPulled;
            }
            if (_collectionTrigger != null)
            {
                _collectionTrigger.enabled = IsStuck || isBeingPulled;
            }
        }

        private bool TryCollectOwner(Vector2 movement)
        {
            ContactFilter2D filter = new ContactFilter2D
            {
                useTriggers = true
            };

            // A lista cresce se vários pregos se sobrepõem; não descartar o dono
            // quando os colliders dos próprios pregos ocupam os primeiros resultados.
            int hitCount = _collectionTrigger.Overlap(filter, _collectionHits);

            for (int i = 0; i < hitCount; i++)
            {
                if (TryCollectFrom(_collectionHits[i]))
                    return true;
            }

            if (movement.sqrMagnitude > 0f)
            {
                int castCount = _collectionTrigger.Cast(
                    movement.normalized, filter, _collectionCastHits, movement.magnitude);
                for (int i = 0; i < castCount; i++)
                {
                    if (TryCollectFrom(_collectionCastHits[i].collider))
                        return true;
                }
            }

            return false;
        }

        private bool TryCollectFrom(Collider2D hit)
        {
            NetworkPlayerState playerState = hit.GetComponentInParent<NetworkPlayerState>();
            if (playerState == null || playerState.OwnerClientId != _shooterClientId)
                return false;

            IrrigadorCombat combat = playerState.GetComponent<IrrigadorCombat>();
            if (combat == null)
                return false;

            ReturnToShooterServer(combat);
            return true;
        }

        private void StartFalling()
        {
            Vector3 previousTip = _tip.TransformPoint(_tipBox.offset);
            transform.rotation = Quaternion.Euler(0f, 0f, -90f);
            Vector2 size = GetWorldBoxSize(_tipBox);
            float angle = _tip.eulerAngles.z;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_surfaceLayer);
            float lift = Mathf.Max(0f, _fallStartOffset);
            int hits = Physics2D.BoxCast(previousTip, size, angle, Vector2.up, filter, _tipSurfaceHits, lift);
            if (hits > 0)
                lift = Mathf.Max(0f, _tipSurfaceHits[0].distance - Physics2D.defaultContactOffset);

            transform.position += previousTip - _tip.TransformPoint(_tipBox.offset) + Vector3.up * lift;
            if (!IsTerrainClear(_tip.TransformPoint(_tipBox.offset), size, angle, _surfaceLayer))
            {
                ReturnToShooterServer();
                return;
            }

            _IsStuck = false;
            _falling = true;
            _beingPulled = false;
            _abilityPull = false;
            _stuckSurface = null;
            _fallVelocity = Vector2.down * 2f;
            UpdateColliders();
        }

        private void TryDamagePlayer(NetworkPlayerState playerState)
        {
            if (!IsServer || !IsSpawned || playerState == null)
                return;

            if (_falling)
                return;

            if (!IsEnemyOfDamageSource(playerState)) return;

            if (_hasHitPlayer &&
                _lastHitClientId == playerState.OwnerClientId)
            {
                return;
            }

            _hasHitPlayer = true;
            _lastHitClientId = playerState.OwnerClientId;
            

            StartFalling();
            

            playerState.ApplyDamageServer(
                _damage,
                DamageSourceClientId
            );
        }

        private bool TryDespawnOutOfBounds()
        {
            Vector3 position = transform.position;

            if (position.y <= _despawnBelowY)
            {
                ReturnToShooterServer();
                return true;
            }

            return false;
        }

        private void ReturnToShooterServer(IrrigadorCombat combat = null)
        {
            if (!IsServer || !IsSpawned || _isReturningToShooter)
                return;

            _isReturningToShooter = true;
            if (combat == null && NetworkManager.ConnectedClients.TryGetValue(_shooterClientId, out var client)
                && client.PlayerObject != null)
            {
                combat = client.PlayerObject.GetComponent<IrrigadorCombat>();
            }

            // Coleta, expiração e saída do mapa devolvem exatamente um prego.
            if (combat != null && combat.IsSpawned)
                combat.AddNailServer();

            NetworkObject.Despawn();
        }
    }
}

