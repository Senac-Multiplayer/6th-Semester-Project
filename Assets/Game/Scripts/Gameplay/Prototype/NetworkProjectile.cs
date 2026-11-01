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
        protected Vector2 _direction;
        protected ulong _shooterClientId;
        public ulong ShooterClientId => _shooterClientId;
        protected float _despawnAt;
        protected float _speed;
        protected int _damage;

        protected bool _useLifetime = true;
        protected bool _canDoSelfDamage = false;

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
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

            _shooterClientId = shooterClientId;
            _speed = definition.Speed;
            _damage = definition.Damage;
            _direction = direction.sqrMagnitude > 0f
                ? direction.normalized
                : Vector2.right;
            _color.Value = color;
            _despawnAt = Time.time + definition.LifetimeSeconds;

            _canDoSelfDamage = false;
        }

        protected virtual void Update()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            float step = _speed * Time.deltaTime;

            // Fazemos um Raycast na direção do movimento para garantir que a bala 
            var filter = new ContactFilter2D();
            filter.SetLayerMask(LayerMask.GetMask("Ground", "Wall"));
            filter.useTriggers = false; // Queremos detetar os colliders sólidos do mapa

            RaycastHit2D[] hits = new RaycastHit2D[1];
            int count = Physics2D.Raycast(transform.position, _direction, filter, hits, step);

            if (count > 0 && hits[0].collider != null)
            {
                // Se o raio bateu em algo no caminho deste frame, movemos a bala exatamente até o ponto de impacto
                transform.position = hits[0].point;
                NetworkObject.Despawn();
                return;
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
                if (playerState.OwnerClientId == _shooterClientId && !_canDoSelfDamage)
                {
                    return;
                }

                if (playerState.IsInvulnerable)
                {
                    return;
                }

                playerState.ApplyDamageServer(_damage, _shooterClientId);
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

        public virtual void ReflectServer(float minimumSpeed)
        {
            if (!IsServer || !IsSpawned)
                return;
            
            _direction = -_direction;

            _speed = Mathf.Max(_speed, minimumSpeed);
            _canDoSelfDamage = true;

            float angle = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg;

            transform.rotation = Quaternion.Euler(0f,0f,angle);
        }
    }
}
