using Unity.Netcode;
using UnityEngine;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Collider2D))]
    public class NetworkProjectile : NetworkBehaviour
    {
        private readonly NetworkVariable<Color> _color =
            new NetworkVariable<Color>(Color.white);

        private SpriteRenderer _spriteRenderer;
        private Collider2D _projectileBodyCollider;
        private readonly RaycastHit2D[] _surfaceHits = new RaycastHit2D[8];
        private ContactFilter2D _surfaceFilter;
        private readonly Collider2D[] _terrainOverlap = new Collider2D[1];
        protected Vector2 _direction;
        protected ulong _shooterClientId;
        public ulong ShooterClientId => _shooterClientId;
        public ulong DamageSourceClientId { get; protected set; }
        protected float _despawnAt;
        protected float _speed;
        protected int _damage;

        protected bool _useLifetime = true;

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _projectileBodyCollider = GetComponent<Collider2D>();
            _surfaceFilter = new ContactFilter2D();
            _surfaceFilter.SetLayerMask(LayerMask.GetMask("Ground", "Wall"));
            _surfaceFilter.useTriggers = false;
        }

        public override void OnNetworkSpawn()
        {
            _color.OnValueChanged += HandleColorChanged;
            ApplyColor(_color.Value);
        }

        public override void OnNetworkDespawn()
        {
            _color.OnValueChanged -= HandleColorChanged;
        }

        // Chamar antes do Spawn: a distância da boca do tiro não pode pular uma superfície.
        public void PlaceAtMuzzle(Vector2 direction, float distance)
        {
            var filter = new ContactFilter2D();
            filter.SetLayerMask(LayerMask.GetMask("Ground", "Wall"));
            // Os colliders de voo dos dois projéteis são triggers.
            filter.useTriggers = true;
            var hits = new RaycastHit2D[8];
            Physics2D.SyncTransforms();
            int count = GetComponent<Rigidbody2D>().Cast(direction, filter, hits, distance);
            for (int i = 0; i < count; i++)
            {
                float approach = Mathf.Max(0.01f, -Vector2.Dot(direction, hits[i].normal));
                distance = Mathf.Min(distance,
                    Mathf.Max(0f, hits[i].distance - Physics2D.defaultContactOffset * 2f / approach));
            }

            transform.position += (Vector3)(direction * distance);
        }

        public virtual void InitializeServer(
            ulong shooterClientId,
            Vector2 direction,
            Color color,
            ProjectileDefinition definition)
        {
            if (!IsServer)
            {
                return;
            }

            InitializeShooter(shooterClientId);
            _speed = definition.Speed;
            _damage = definition.Damage;
            _direction = direction.sqrMagnitude > 0f
                ? direction.normalized
                : Vector2.right;
            _color.Value = color;
            _despawnAt = Time.time + definition.LifetimeSeconds;
        }

        protected void InitializeShooter(ulong shooterClientId)
        {
            _shooterClientId = shooterClientId;
            DamageSourceClientId = shooterClientId;
        }

        protected virtual void Update()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            float step = _speed * Time.deltaTime;

            if (step > 0f)
            {
                Physics2D.SyncTransforms();
                int count = _projectileBodyCollider.Cast(_direction, _surfaceFilter, _surfaceHits, step);
                float closestDistance = step;
                bool hitSurface = false;
                for (int i = 0; i < count; i++)
                {
                    if (_surfaceHits[i].collider == null || _surfaceHits[i].distance > closestDistance)
                    {
                        continue;
                    }

                    closestDistance = _surfaceHits[i].distance;
                    hitSurface = true;
                }

                if (hitSurface)
                {
                    transform.position += (Vector3)(_direction * Mathf.Max(0f,
                        closestDistance - Physics2D.defaultContactOffset));
                    NetworkObject.Despawn();
                    return;
                }
            }

            transform.position += (Vector3)(_direction * step);
            if (_useLifetime && Time.time >= _despawnAt)
            {
                NetworkObject.Despawn();
            }
        }

        protected virtual void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            if (other.GetComponentInParent<NetworkProjectile>() != null)
            {
                return;
            }

            NetworkPlayerState playerState = other.GetComponentInParent<NetworkPlayerState>();
            if (playerState != null)
            {
                if (!IsEnemyOfDamageSource(playerState) || playerState.IsInvulnerable)
                {
                    return;
                }

                playerState.ApplyDamageServer(_damage, DamageSourceClientId);
            }

            NetworkObject.Despawn();
        }

        private void HandleColorChanged(Color previousValue, Color currentValue)
        {
            ApplyColor(currentValue);
        }

        private void ApplyColor(Color color)
        {
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = color;
            }
        }

        protected static Vector2 GetWorldBoxSize(BoxCollider2D box)
        {
            Vector3 scale = box.transform.lossyScale;
            return Vector2.Scale(box.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
        }

        protected bool IsTerrainClear(Vector2 center, Vector2 size, float angle, LayerMask terrain)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(terrain);
            return Physics2D.OverlapBox(center, size, angle, filter, _terrainOverlap) == 0;
        }

        protected bool IsEnemyOfDamageSource(NetworkPlayerState player) =>
            player.OwnerClientId != DamageSourceClientId && player.CanReceiveHitFrom(DamageSourceClientId);

        public virtual void ReflectServer(ulong reflectorClientId, float minimumSpeed)
        {
            if (!IsServer || !IsSpawned)
                return;
            
            _direction = -_direction;

            _speed = Mathf.Max(_speed, minimumSpeed);
            // Quem disparou continua sendo o dono da munição. Quem rebateu assume o dano.
            DamageSourceClientId = reflectorClientId;

            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;

            transform.rotation = Quaternion.Euler(0f,0f,angle);
        }
    }
}
