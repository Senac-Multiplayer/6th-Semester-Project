using Unity.Netcode;
using UnityEngine;

namespace PiGame.Gameplay
{
    public class PlayerAnimatorController : NetworkBehaviour
    {
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private bool _useAnimations = true;
        [SerializeField] private Animator _animator;

        [Header("Arma visual (personagens)")]
        [SerializeField] private bool _useWeapon;
        [SerializeField] private SpriteRenderer _weapon;
        [SerializeField] private Vector2 _weaponOffset = new(0.08f, -0.1f);
        [SerializeField] private Vector2 _crouchWeaponOffset = new(0.1f, -0.4f);

        private static readonly int XVelocity = Animator.StringToHash("xVelocity");
        private static readonly int YVelocity = Animator.StringToHash("yVelocity");
        private static readonly int IsJumping = Animator.StringToHash("isJumping");
        private static readonly int IsCrouching = Animator.StringToHash("isCrouching");
        private static readonly int IsWallSliding = Animator.StringToHash("isWallSliding");
        private static readonly int IsSkill = Animator.StringToHash("isSkill");

        private readonly NetworkVariable<bool> _facingLeft = new(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<Vector2> _weaponDirection = new(Vector2.zero,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private float _movementFacingInput;
        private float _aimFacingInput;
        private bool _isAiming;

        private void Awake()
        {
            if (_spriteRenderer == null
                || (_useAnimations && (_animator == null || _animator.runtimeAnimatorController == null))
                || (_useWeapon && !_weapon))
            {
                Debug.LogError("Configure SpriteRenderer, Animator e Weapon (quando Use Weapon estiver ativo) no PlayerAnimatorController.", this);
                enabled = false;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!enabled) return;
            _facingLeft.OnValueChanged += OnFacingChanged;
            OnFacingChanged(false, _facingLeft.Value);
        }

        public override void OnNetworkDespawn()
        {
            _facingLeft.OnValueChanged -= OnFacingChanged;
            _isAiming = false;
            _movementFacingInput = 0f;
            _aimFacingInput = 0f;
        }

        public void SetAimFacing(Vector2 direction, bool isAiming)
        {
            if (!enabled || !IsSpawned || !IsOwner) return;
            _isAiming = isAiming;
            _aimFacingInput = direction.x;
            if (_useWeapon) _weaponDirection.Value = isAiming ? direction : Vector2.zero;
            UpdateFacing();
        }

        private void LateUpdate()
        {
            if (!_useWeapon || !IsSpawned) return;
            // Só apresentação: a arma não possui collider nem muda a origem física do disparo.
            _weapon.enabled = _spriteRenderer.enabled && (!_useAnimations || !_animator.GetBool(IsSkill));
            _weapon.color = new Color(1f, 1f, 1f, _spriteRenderer.color.a);
            Vector2 offset = _useAnimations && _animator.GetBool(IsCrouching)
                ? _crouchWeaponOffset : _weaponOffset;
            if (_facingLeft.Value) offset.x = -offset.x;
            Vector2 direction = _weaponDirection.Value;
            if (direction.sqrMagnitude < 0.01f) direction = _facingLeft.Value ? Vector2.left : Vector2.right;
            _weapon.transform.localPosition = offset;
            _weapon.transform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            _weapon.flipY = direction.x < -0.01f || (Mathf.Abs(direction.x) < 0.01f && _facingLeft.Value);
        }

        public void UpdateMovement(Vector2 velocity, float facingInput,
            bool isJumping, bool isCrouching, bool isWallSliding)
        {
            if (!enabled || !IsSpawned || !IsOwner) return;
            _movementFacingInput = facingInput;
            UpdateFacing();
            if (!_useAnimations) return;
            _animator.SetFloat(XVelocity, Mathf.Abs(velocity.x));
            _animator.SetFloat(YVelocity, velocity.y);
            _animator.SetBool(IsJumping, isJumping);
            _animator.SetBool(IsCrouching, isCrouching);
            _animator.SetBool(IsWallSliding, isWallSliding);
        }

        public void SetCrouching(bool isCrouching)
        {
            if (enabled && _useAnimations)
                _animator.SetBool(IsCrouching, isCrouching);
        }

        public void SetSkillActive(bool isActive)
        {
            if (enabled && _useAnimations)
                _animator.SetBool(IsSkill, isActive);
        }

        private void UpdateFacing()
        {
            float direction = _isAiming ? _aimFacingInput : _movementFacingInput;
            // Mira vertical e personagem parado mantêm o último lado visível.
            if (Mathf.Abs(direction) <= 0.01f) return;
            bool facingLeft = direction < 0f;
            if (_facingLeft.Value != facingLeft) _facingLeft.Value = facingLeft;
        }

        private void OnFacingChanged(bool previousValue, bool facingLeft)
        {
            _spriteRenderer.flipX = facingLeft;
        }
    }
}
