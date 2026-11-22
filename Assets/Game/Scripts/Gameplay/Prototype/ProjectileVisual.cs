using System.Collections.Generic;
using UnityEngine;

namespace PiGame.Gameplay
{
    // Orienta somente a arte. O movimento e os colliders continuam nos scripts originais.
    public sealed class ProjectileVisual : MonoBehaviour
    {
        [SerializeField] private Transform _art;
        [SerializeField] private Sprite[] _sprites;
        private static readonly Dictionary<Sprite, int> NextSpriteByProjectile = new();
        private SpriteRenderer _spriteRenderer;
        private Vector3 _previousPosition;
        private bool _hasMoved;

        private void Awake()
        {
            if (_art && (_spriteRenderer = _art.GetComponent<SpriteRenderer>())) return;
            Debug.LogError("Configure Art com SpriteRenderer no ProjectileVisual do prefab.", this);
            enabled = false;
        }

        private void OnEnable()
        {
            _previousPosition = transform.position;
            _hasMoved = false;
            if (!enabled || _sprites == null || _sprites.Length == 0 || !_sprites[0]) return;

            Sprite projectileType = _sprites[0];
            NextSpriteByProjectile.TryGetValue(projectileType, out int nextSprite);
            _spriteRenderer.sprite = _sprites[nextSprite];
            NextSpriteByProjectile[projectileType] = (nextSprite + 1) % _sprites.Length;
        }

        private void LateUpdate()
        {
            Vector2 movement = transform.position - _previousPosition;
            _previousPosition = transform.position;
            if (movement.sqrMagnitude > 0.000001f)
            {
                _art.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(movement.y, movement.x) * Mathf.Rad2Deg);
                _hasMoved = true;
            }
            else if (!_hasMoved) _art.rotation = transform.rotation;
        }
    }
}
