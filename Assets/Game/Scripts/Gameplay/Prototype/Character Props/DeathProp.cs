using UnityEngine;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Animator))]
    public class DeathProp : MonoBehaviour
    {
        private Rigidbody2D _rb;
        private Animator _animator;
        private bool _hasHitGround;
        [SerializeField] private LayerMask _groundlayer;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _animator = GetComponent<Animator>();
        }

        private void OnTriggerEnter2D(Collider2D collider2D)
        {
            if (_hasHitGround) return;

            if(((1 << collider2D.gameObject.layer) & _groundlayer) != 0)           
            {
                _hasHitGround = true;
                _rb.linearVelocity = Vector2.zero;
                _rb.bodyType = RigidbodyType2D.Static;
                _animator.SetBool("IsGrounded", true);
            }
        }
    }
}