using UnityEngine;
using Unity.Netcode;
using PiGame.Sound;

namespace PiGame.Gameplay 
{

    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Collider2D))]
    public class RebeldeProjectile : NetworkProjectile
    {
        [Header("Projectile Settings")]
        [SerializeField] private float _minimumSpeed = 3f;
        [SerializeField] private float _wallBounceSpeedLoss = 0.25f;
        [SerializeField] private float _playerBounceSpeedLoss = 0.5f;
        [SerializeField] private float _fallSpeedThreshold = 3f;

        [Header("Collision")]
        [SerializeField] private LayerMask _collisionLayers;
        [SerializeField] private float _collisionOffset = 0.01f;
        

        [Header("Referencer")]
        [SerializeField] private Collider2D _solidCollider;
        private Rigidbody2D _rigidbody;
        private BoxCollider2D _collisionBox;
        private RebeldeSoundManager _ownerSound;
        private readonly RaycastHit2D[] _flightHits = new RaycastHit2D[16];

        private bool _isStopped;
        private bool _falling;



        public float MinimumSpeed => _minimumSpeed;

        private void Awake()
        {

            _rigidbody = GetComponent<Rigidbody2D>();
            _collisionBox = (BoxCollider2D)_solidCollider;

            _useLifetime = false;
            _solidCollider.enabled = false;

            _rigidbody.bodyType = RigidbodyType2D.Kinematic;
            _rigidbody.gravityScale = 0f;
        }

        public void InitializeServer(ulong shooterClientId, Vector2 direction, ProjectileDefinition definition, float speed, RebeldeSoundManager ownerSound)
        {
            if (!IsServer)
                return;

            _shooterClientId = shooterClientId;

            _damage = definition.Damage;

            _direction = direction.sqrMagnitude > 0f
                ? direction.normalized
                : Vector2.right;

            _speed = speed;

            _isStopped = false;
            _falling = false; 
            _ownerSound = ownerSound;     

        }

        protected override void Update()
        {
            //Deixando Update vazio para sobrescrever a movimentação no Update da classe pai
        }

        protected override void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsServer || !IsSpawned)
                return;


            NetworkPlayerState playerState =
                other.GetComponentInParent<NetworkPlayerState>();

            if (playerState == null)
                return;


            if (playerState.OwnerClientId != _shooterClientId)
                return;

            RebeldeCombat combat =
                playerState.GetComponent<RebeldeCombat>();

            if (combat == null)
                return;

            combat.AddShotServer();
            NetworkObject.Despawn();
        }

        private void FixedUpdate()
        {
            if(!IsServer || !IsSpawned)
                return;
            
            if(_isStopped)
                return;
            
            if(_falling)
            {
                return;
            }

            UpdateFlying();
        }

        private void UpdateFlying()
        {
            if(_speed <= _fallSpeedThreshold)
            {
                StartFalling();
                return;
            }

            float deltaTime = Time.fixedDeltaTime;

            Vector2 movement =
                _direction * _speed * deltaTime;

            float distance = movement.magnitude;

            if (distance <= 0f)
                return;

            // O centro pode estar fora do tile enquanto a borda da bola já entrou nele.
            Vector2 scale = _collisionBox.transform.lossyScale;
            Vector2 size = Vector2.Scale(_collisionBox.size,
                new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            // Reservar a margem de contato da física antes de ativar o collider na queda.
            size += Vector2.one * (Physics2D.defaultContactOffset * 2f);
            var filter = new ContactFilter2D();
            filter.SetLayerMask(_collisionLayers);
            filter.useTriggers = true;
            int hitCount = Physics2D.BoxCast(_collisionBox.transform.TransformPoint(_collisionBox.offset),
                size, _collisionBox.transform.eulerAngles.z, movement.normalized,
                filter, _flightHits, distance);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = _flightHits[i];
                NetworkPlayerState player = hit.collider.GetComponentInParent<NetworkPlayerState>();
                if (hit.collider.GetComponentInParent<NetworkProjectile>() != null
                    || (player != null && player.OwnerClientId == _shooterClientId)) continue;

                transform.position += (Vector3)(movement.normalized * hit.distance
                    + hit.normal * _collisionOffset);
                HandleCollision(hit);
                return;
            }

            transform.position += (Vector3)movement;
        }

        private void StartFalling()
        {
            if (_falling)
                return;

            _falling = true;
            _speed = 0f;
            _solidCollider.enabled = true;

            _rigidbody.bodyType = RigidbodyType2D.Dynamic;
            _rigidbody.gravityScale = 1f;
            _rigidbody.linearVelocity = Vector2.down * 2f;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!IsServer || !_falling || _isStopped)
                return;

            foreach (ContactPoint2D contact in collision.contacts)
            {
                if (contact.normal.y > 0.5f)
                {
                    StopOnGround();
                    return;
                }
            }
        }

        private void StopOnGround()
        {
            _isStopped = true;

            _rigidbody.linearVelocity = Vector2.zero;
            _rigidbody.angularVelocity = 0f;

            _rigidbody.bodyType = RigidbodyType2D.Kinematic;
            _rigidbody.gravityScale = 0f;
        }

        private void HandleCollision(RaycastHit2D hit)
        {
            NetworkPlayerState player =
                hit.collider.GetComponentInParent<NetworkPlayerState>();

            if (player != null)
            {
                HandlePlayerCollision(player, hit);
                return;
            }

            HandleEnvironmentCollision(hit);
        }

        private void HandleEnvironmentCollision(RaycastHit2D hit)
        {
            Bounce(
                hit.normal,
                _wallBounceSpeedLoss);
        }

        private void HandlePlayerCollision(NetworkPlayerState player, RaycastHit2D hit)
        {
            
            if (player.OwnerClientId == _shooterClientId)
            {

                transform.position +=
                    (Vector3)(_direction * _speed * Time.fixedDeltaTime);

                return;
            }

            if (!player.IsInvulnerable)
            {
                player.ApplyDamageServer(
                    _damage,
                    _shooterClientId);
            }

            Bounce(
                hit.normal,
                _playerBounceSpeedLoss);  
        }

        private void Bounce(Vector2 normal, float speedLoss)
        {
            float currentSpeed =
                _speed - speedLoss;

            if (currentSpeed <= _minimumSpeed)
            {
                StartFalling();
                return;
            }

            if(_ownerSound != null)
            {
                _ownerSound.PlayRebeldeBounce();
            }

            _direction =
                Vector2.Reflect(
                    _direction,
                    normal).normalized;

            _speed = currentSpeed;
        }

        public override void ReflectServer(float minimumSpeed)
        {
            if (!IsServer || !IsSpawned)
                return;

            base.ReflectServer(minimumSpeed);

            _falling = false;
            _isStopped = false;

            _rigidbody.bodyType = RigidbodyType2D.Kinematic;
            _rigidbody.gravityScale = 0f;
            _rigidbody.linearVelocity = Vector2.zero;
            _rigidbody.angularVelocity = 0f;

            _solidCollider.enabled = false;
        }

    }
    
}