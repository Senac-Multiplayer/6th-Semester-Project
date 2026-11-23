using Unity.Netcode;
using UnityEngine;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
    public class CowboyRollSpriteSwapper : NetworkBehaviour
    {
        [SerializeField] private Sprite[] _rightRollSprites;
        [SerializeField] private Sprite[] _leftRollSprites;
        [SerializeField, Min(0.01f)] private float _rollDuration = 0.2f;
        [SerializeField, Min(0f)] private float _rollSpeedThreshold = 11f;

        private SpriteRenderer _spriteRenderer;
        private Rigidbody2D _rigidbody;
        private NetworkPlayerState _playerState;

        private bool _isRollingVisual;
        private float _rollElapsed;
        private bool _facingRight;
        private Sprite _cachedSpriteBeforeRoll;

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _rigidbody = GetComponent<Rigidbody2D>();
            _playerState = GetComponent<NetworkPlayerState>();
        }

        private void LateUpdate()
        {
            if (_spriteRenderer == null || _rigidbody == null)
            {
                return;
            }

            if (_playerState != null && !_playerState.CanAct)
            {
                StopRollVisual();
                return;
            }

            float vx = _rigidbody.linearVelocity.x;
            bool rollConditionMet = Mathf.Abs(vx) >= _rollSpeedThreshold;

            if (rollConditionMet)
            {
                if (!_isRollingVisual)
                {
                    _isRollingVisual = true;
                    _rollElapsed = 0f;
                    _facingRight = vx >= 0f;
                    _cachedSpriteBeforeRoll = _spriteRenderer.sprite;
                }

                _rollElapsed += Time.deltaTime;
                Sprite[] activeFrames = _facingRight ? _rightRollSprites : _leftRollSprites;

                if (activeFrames != null && activeFrames.Length > 0)
                {
                    float normalizedTime = Mathf.Clamp01(_rollElapsed / Mathf.Max(0.01f, _rollDuration));
                    int frameIndex = Mathf.Min(
                        activeFrames.Length - 1,
                        Mathf.FloorToInt(normalizedTime * activeFrames.Length));
                    _spriteRenderer.sprite = activeFrames[frameIndex];
                }
            }
            else if (_isRollingVisual)
            {
                StopRollVisual();
            }
        }

        private void StopRollVisual()
        {
            if (!_isRollingVisual)
            {
                return;
            }

            _isRollingVisual = false;
            _rollElapsed = 0f;

            if (_spriteRenderer != null && _cachedSpriteBeforeRoll != null)
            {
                _spriteRenderer.sprite = _cachedSpriteBeforeRoll;
                _cachedSpriteBeforeRoll = null;
            }
        }
    }
}