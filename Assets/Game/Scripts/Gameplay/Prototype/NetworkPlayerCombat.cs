using PiGame.Lobby;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(NetworkPlayerState), typeof(PlayerMove))]
    public class NetworkPlayerCombat : NetworkBehaviour, IGameplayInputBlocker
    {
        [SerializeField] private InputActionReference _aimDirectionAction;
        [SerializeField] private InputActionReference _aimPointerAction;
        [SerializeField] private InputActionReference _aimFireAction;
        [SerializeField] private InputActionReference _cancelAimAction;
        [SerializeField] private InputActionReference _abilityAction;
        [SerializeField] private SpriteRenderer _aimIndicator;
        [SerializeField] private float _aimIndicatorDistance = 1.15f;
        [SerializeField] private bool _preventAbilityOnWallOrWallJump;

        private NetworkPlayerState _playerState;
        private PlayerMove _playerMove;
        private PlayerAnimatorController _presentation;
        private Animator _animator;
        private ICharacterCombat _characterCombat;
        private Vector2 _aimDirection = Vector2.right;
        private LobbyInputDeviceKind _localAimInputDevice;
        private Vector2 _previousPointerPosition;
        private bool _localAimHeld;
        private bool _localAbilityHeld;
        private bool _waitForAimRelease;
        private bool _waitForAbilityRelease;
        private bool _isGameplayInputBlocked;

        private void Awake()
        {
            _playerState = GetComponent<NetworkPlayerState>();
            _playerMove = GetComponent<PlayerMove>();
            _presentation = GetComponent<PlayerAnimatorController>();
            _animator = GetComponent<Animator>();
            _characterCombat = GetComponent<ICharacterCombat>();
            if (_characterCombat == null || _aimIndicator == null || _presentation == null)
            {
                Debug.LogError("Configure ICharacterCombat, PlayerAnimatorController e o indicador de mira no prefab do jogador.", this);
                enabled = false;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!enabled)
            {
                return;
            }

            _aimIndicator.gameObject.SetActive(false);
            _aimIndicator.enabled = false;
            if (IsOwner)
            {
                if (!ConfigureInput())
                {
                    enabled = false;
                    return;
                }

                _localAimInputDevice = LobbyInputDeviceKind.Unknown;
                _previousPointerPosition = _aimPointerAction.action.ReadValue<Vector2>();
            }

            _playerState.StateChanged += RefreshAimIndicator;
        }

        public override void OnNetworkDespawn()
        {
            _playerState.StateChanged -= RefreshAimIndicator;
            _isGameplayInputBlocked = false;
            _waitForAimRelease = false;
            _waitForAbilityRelease = false;
            _localAimHeld = false;
            _localAbilityHeld = false;
            if (_aimIndicator != null)
            {
                _aimIndicator.enabled = false;
                _aimIndicator.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (!IsOwner)
            {
                return;
            }

            if (_isGameplayInputBlocked)
            {
                _waitForAimRelease = true;
                _waitForAbilityRelease = true;
                CancelLocalAim();
                CancelLocalAbility();
                return;
            }

            if (!_playerState.CanAct)
            {
                _waitForAimRelease = true;
                _waitForAbilityRelease = true;
                CancelLocalAim();
                CancelLocalAbility();
                return;
            }

            Vector2 aim = ReadAimDirection();
            if (aim.sqrMagnitude > 0.0001f)
            {
                _aimDirection = SnapAimToEightDirections(aim);
                RefreshAimIndicator();
            }

            UpdateAbilityInput();

            bool aimPressed = ReadAimPressed();
            if (_waitForAimRelease)
            {
                if (aimPressed)
                {
                    return;
                }

                _waitForAimRelease = false;
            }

            bool cancelPressed = ReadCancelPressed();

            if (cancelPressed && _localAimHeld)
            {
                _waitForAimRelease = true;
                CancelLocalAim();
                return;
            }

            if (aimPressed)
            {
                _localAimHeld = true;
                RefreshAimIndicator();
                UpdateShootAimRpc(_aimDirection);
                return;
            }

            if (!_localAimHeld)
            {
                return;
            }

            _localAimHeld = false;
            RefreshAimIndicator();
            FireRpc(_aimDirection);
            ShootReleasedRpc();
        }

        public void SetGameplayInputBlocked(bool isBlocked)
        {
            if (!IsOwner)
            {
                return;
            }

            _isGameplayInputBlocked = isBlocked;
            if (isBlocked)
            {
                _waitForAimRelease = true;
                _waitForAbilityRelease = true;
                CancelLocalAim();
                CancelLocalAbility();
            }
        }

        private void UpdateAbilityInput()
        {
            bool abilityPressed = _abilityAction.action.IsPressed();
            if (_waitForAbilityRelease)
            {
                if (abilityPressed)
                {
                    return;
                }

                _waitForAbilityRelease = false;
            }

            if (abilityPressed == _localAbilityHeld)
            {
                return;
            }

            _localAbilityHeld = abilityPressed;
            Vector2 moveDirection = _playerMove.ReadMovementDirection();
            if (abilityPressed)
            {
                if (!_preventAbilityOnWallOrWallJump
                    || _playerMove.CanBeginWallRestrictedAbility())
                {
                    SetSkillAnimation(true);
                    BeginAbilityRpc(_aimDirection, moveDirection);
                }
            }
            else
            {
                TriggerSkillReleaseAnimation();
                SetSkillAnimation(false);
                EndAbilityRpc(_aimDirection, moveDirection);
            }
        }

        private void CancelLocalAim()
        {
            if (_localAimHeld)
            {
                ShootReleasedRpc();
            }

            _localAimHeld = false;
            RefreshAimIndicator();
        }

        private void CancelLocalAbility()
        {
            if (_localAbilityHeld)
            {
                EndAbilityRpc(_aimDirection, _playerMove.ReadMovementDirection());
            }

            _localAbilityHeld = false;
            SetSkillAnimation(false);
        }

        private void SetSkillAnimation(bool isActive)
        {
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.SetBool("isSkill", isActive);
            }
        }

        private void TriggerSkillReleaseAnimation()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
            {
                return;
            }

            AnimatorControllerParameter[] parameters = _animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == "skillRelease"
                    && parameters[i].type == AnimatorControllerParameterType.Trigger)
                {
                    _animator.SetTrigger("skillRelease");
                    return;
                }
            }
        }

        [Rpc(SendTo.Server)]
        private void FireRpc(Vector2 direction, RpcParams rpcParams = default)
        {
            if (!_playerState.CanAct || rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            _characterCombat.ShootServer(direction);
        }

        [Rpc(SendTo.Server)]
        private void UpdateShootAimRpc(Vector2 direction, RpcParams rpcParams = default)
        {
            if(!_playerState.CanAct || rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            _characterCombat.UpdateShootAimServer(direction);
        }

        [Rpc(SendTo.Server)]
        private void ShootReleasedRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            _characterCombat.ShootReleaseServer();
        }

        [Rpc(SendTo.Server)]
        private void BeginAbilityRpc(
            Vector2 aimDirection, Vector2 moveDirection, RpcParams rpcParams = default)
        {
            if (_playerState.CanAct && rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                _characterCombat.BeginAbilityServer(aimDirection, moveDirection);
            }
        }

        [Rpc(SendTo.Server)]
        private void EndAbilityRpc(
            Vector2 aimDirection, Vector2 moveDirection, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
            {
                _characterCombat.EndAbilityServer(aimDirection, moveDirection);
            }
        }

        private Vector2 ReadAimDirection()
        {
            Vector2 directionalAim = _aimDirectionAction.action.ReadValue<Vector2>();
            Vector2 pointerPosition = _aimPointerAction.action.ReadValue<Vector2>();
            if (_localAimInputDevice == LobbyInputDeviceKind.Unknown)
            {
                _localAimInputDevice = _playerState.InputDevice;
            }

            // A escolha do lobby é inicial; durante o jogo, vale o último input de mira usado.
            if ((pointerPosition - _previousPointerPosition).sqrMagnitude > 0.01f)
            {
                _localAimInputDevice = LobbyInputDeviceKind.Keyboard;
            }
            _previousPointerPosition = pointerPosition;

            if (_aimDirectionAction.action.WasPerformedThisFrame()
                && directionalAim.sqrMagnitude > 0.0001f)
            {
                _localAimInputDevice = LobbyInputDeviceKind.Gamepad;
            }

            if (_aimFireAction.action.WasPressedThisFrame())
            {
                InputDevice device = _aimFireAction.action.activeControl?.device;
                if (device is Gamepad || device is Joystick)
                {
                    _localAimInputDevice = LobbyInputDeviceKind.Gamepad;
                }
                else if (device is Keyboard || device is Mouse)
                {
                    _localAimInputDevice = LobbyInputDeviceKind.Keyboard;
                }
            }

            if (_localAimInputDevice == LobbyInputDeviceKind.Gamepad)
            {
                return directionalAim;
            }

            if (Camera.main != null)
            {
                Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(
                    pointerPosition);
                return mouseWorld - transform.position;
            }

            return _aimDirection;
        }

        private static Vector2 SnapAimToEightDirections(Vector2 direction)
        {
            float step = Mathf.PI / 4f;
            float angle = Mathf.Round(Mathf.Atan2(direction.y, direction.x) / step) * step;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private bool ReadAimPressed()
        {
            return _aimFireAction.action.IsPressed();
        }

        private bool ReadCancelPressed()
        {
            return _cancelAimAction.action.WasPressedThisFrame();
        }

        private bool ConfigureInput()
        {
            InputAction aimDirection = _aimDirectionAction != null ? _aimDirectionAction.action : null;
            InputAction aimPointer = _aimPointerAction != null ? _aimPointerAction.action : null;
            InputAction aimFire = _aimFireAction != null ? _aimFireAction.action : null;
            InputAction cancelAim = _cancelAimAction != null ? _cancelAimAction.action : null;
            InputAction ability = _abilityAction != null ? _abilityAction.action : null;
            if (aimDirection == null || aimPointer == null || aimFire == null
                || cancelAim == null || ability == null
                || aimDirection.actionMap == null
                || aimDirection.actionMap != aimPointer.actionMap
                || aimDirection.actionMap != aimFire.actionMap
                || aimDirection.actionMap != cancelAim.actionMap
                || aimDirection.actionMap != ability.actionMap)
            {
                Debug.LogError("Configure AimDirection, AimPointer, AimFire, CancelAim e Ability do mesmo Action Map no NetworkPlayerCombat.", this);
                return false;
            }

            return true;
        }

        private void RefreshAimIndicator()
        {
            bool isAiming = IsOwner && _localAimHeld && _playerState.CanAct
                && !_isGameplayInputBlocked;
            _aimIndicator.enabled = isAiming;
            _aimIndicator.gameObject.SetActive(isAiming);
            _aimIndicator.color = Color.white;
            _aimIndicator.transform.localPosition =
                (Vector3)(_aimDirection * _aimIndicatorDistance);
            _presentation.SetAimFacing(_aimDirection, isAiming);
        }
        [Rpc(SendTo.Owner)]
        public void ApplyUpwardImpulseRpc( float impulse, RpcParams rpcParams = default)
        {
            if (!IsOwner)
                return;

            Rigidbody2D rigidbody = GetComponentInParent<Rigidbody2D>();

            if (rigidbody == null)
            {
                Debug.LogWarning(
                    "[Impulse] Rigidbody2D não encontrado.",
                    this);
                return;
            }

            Vector2 velocity = rigidbody.linearVelocity;
            velocity.y = Mathf.Max(velocity.y, impulse);
            rigidbody.linearVelocity = velocity;

        }
    }
}
