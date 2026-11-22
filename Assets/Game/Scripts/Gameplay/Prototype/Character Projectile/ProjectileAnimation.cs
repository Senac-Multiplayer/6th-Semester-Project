using UnityEngine;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class ProjectileAnimation : MonoBehaviour
    {
        [SerializeField] private Sprite[] _frames;
        [SerializeField, Min(0.01f)] private float _framesPerSecond = 12f;
        [SerializeField] private bool _loop = true;

        private SpriteRenderer _spriteRenderer;
        private int _frameIndex;
        private float _elapsed;
        private bool _isPlaying;

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void OnEnable()
        {
            StartAnimation();
        }

        private void Update()
        {
            if (!_isPlaying || _frames == null || _frames.Length <= 1)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            float frameInterval = 1f / _framesPerSecond;

            while (_elapsed >= frameInterval && _isPlaying)
            {
                _elapsed -= frameInterval;
                StepFrame();
            }
        }

        private void StartAnimation()
        {
            _elapsed = 0f;
            _frameIndex = 0;
            _isPlaying = _frames != null && _frames.Length > 0;

            if (_isPlaying && _spriteRenderer != null)
            {
                _spriteRenderer.sprite = _frames[0];
            }
        }

        private void StepFrame()
        {
            int nextIndex = _frameIndex + 1;

            if (nextIndex >= _frames.Length)
            {
                if (_loop)
                {
                    _frameIndex = 0;
                    _spriteRenderer.sprite = _frames[0];
                }
                else
                {
                    _frameIndex = _frames.Length - 1;
                    _spriteRenderer.sprite = _frames[_frameIndex];
                    _isPlaying = false;
                }

                return;
            }

            _frameIndex = nextIndex;
            _spriteRenderer.sprite = _frames[_frameIndex];
        }
    }
}