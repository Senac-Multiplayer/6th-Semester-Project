using Unity.Netcode;
using UnityEngine;

namespace PiGame.Gameplay
{
    public class SmokeVfxController : NetworkBehaviour
    {
        [SerializeField] private GameObject _smokeObject;
        [SerializeField] private Transform _groundCheck;
        [SerializeField] private LayerMask _groundLayer;
         private SpriteRenderer _characterSprite;
         private float _groundCheckRadius = 0.12f;
         private float _speedThreshold = 0.05f;
         private Vector2 _rightOffset = new Vector2(-0.75f, 1f);
         private Vector2 _leftOffset = new Vector2(0.75f, 1f);
         private bool _flipSmokeSpriteWithFacing = true;

        private Rigidbody2D _rigidbody;
        private NetworkPlayerState _playerState;
        private SpriteRenderer _smokeSprite;
        private bool _facingRight = true;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody2D>();
            _playerState = GetComponent<NetworkPlayerState>();

            _characterSprite = GetComponent<SpriteRenderer>();
            _smokeSprite = _smokeObject.GetComponent<SpriteRenderer>();
        }

        public override void OnNetworkSpawn()
        {
            SetSmokeActive(false);
            ApplyFacingOffset(_facingRight);
        }

        private void Update()
        {
            if (_smokeObject == null || _rigidbody == null)
            {
                return;
            }

            UpdateFacingDirection();
            ApplyFacingOffset(_facingRight);

            if (_playerState != null && !_playerState.CanAct)
            {
                SetSmokeActive(false);
                return;
            }

            bool isGrounded = IsGrounded();
            bool isIdleHorizontally = Mathf.Abs(_rigidbody.linearVelocity.x) <= _speedThreshold;
            bool isMovingVertically = Mathf.Abs(_rigidbody.linearVelocity.y) > _speedThreshold;
            bool shouldEmit = isGrounded && isIdleHorizontally && !isMovingVertically;

            SetSmokeActive(shouldEmit);
        }

        private void UpdateFacingDirection()
        {
            if (_characterSprite != null)
            {
                _facingRight = !_characterSprite.flipX;
                return;
            }

            if (Mathf.Abs(_rigidbody.linearVelocity.x) > _speedThreshold)
            {
                _facingRight = _rigidbody.linearVelocity.x > 0f;
            }
        }

        private void ApplyFacingOffset(bool facingRight)
        {
            Vector2 targetOffset = facingRight ? _rightOffset : _leftOffset;
            _smokeObject.transform.localPosition = new Vector3(
                targetOffset.x,
                targetOffset.y,
                _smokeObject.transform.localPosition.z);

            if (_flipSmokeSpriteWithFacing && _smokeSprite != null)
            {
                _smokeSprite.flipX = !facingRight;
            }
        }

        private bool IsGrounded()
        {
            if (_groundCheck == null)
            {
                return Mathf.Abs(_rigidbody.linearVelocity.y) <= _speedThreshold;
            }

            return Physics2D.OverlapCircle(_groundCheck.position, _groundCheckRadius, _groundLayer) != null;
        }

        private void SetSmokeActive(bool active)
        {
            if (_smokeObject.activeSelf != active)
            {
                _smokeObject.SetActive(active);
            }
        }
    }
}