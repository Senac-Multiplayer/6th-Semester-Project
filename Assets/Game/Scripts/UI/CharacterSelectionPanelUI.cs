using System;
using System.Collections;
using System.Collections.Generic;
using PiGame.Gameplay;
using PiGame.Input;
using PiGame.Lobby;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class CharacterSelectionPanelUI : MonoBehaviour,
        ISubmitHandler,
        ICancelHandler,
        IPointerEnterHandler,
        IPointerClickHandler
    {
        private const float NavigationRepeatDelay = 0.4f;
        private const float NavigationRepeatRate = 0.12f;

        [Header("Characters")]
        [SerializeField] private LobbyCharacterDefinition[] _characters;

        [Header("Player slots")]
        [SerializeField] private RectTransform[] _slotRoots;
        [SerializeField] private Image[] _slotBackgrounds;
        [SerializeField] private Image[] _characterPortraits;
        [SerializeField] private Image[] _deviceIcons;
        [SerializeField] private Text[] _slotLabels;
        [SerializeField] private Text[] _playerNameTexts;
        [SerializeField] private Text[] _statusTexts;

        [Header("Character navigation")]
        [SerializeField] private RectTransform _browseControlsRoot;
        [SerializeField] private Button _previousCharacterButton;
        [SerializeField] private Button _nextCharacterButton;

        [Header("Input icons")]
        [SerializeField] private Sprite _keyboardDeviceSprite;
        [SerializeField] private Sprite _gamepadDeviceSprite;
        [SerializeField] private Image _confirmLegendIcon;
        [SerializeField] private Image _backLegendIcon;
        [SerializeField] private InputPromptCatalog _promptCatalog;
        [SerializeField] private Text _instructionText;

        public event Action<int, LobbyInputDeviceKind> BrowseRequested;
        public event Action<LobbyInputDeviceKind> SubmitRequested;
        public event Action BackRequested;

        private LobbyInputDeviceKind _lastInputDevice = LobbyInputDeviceKind.Keyboard;
        private Gamepad _lastGamepad;
        private bool _interactionEnabled = true;
        private int _localPlayerSlot = -1;
        private bool _localPlayerIsReady = true;
        private int _heldNavigationDirection;
        private float _nextNavigationTime;
        private int _blockedFeedbackSlot = -1;
        private int _lockingPlayerSlot = -1;
        private Vector2 _blockedSlotOriginalPosition;
        private Coroutine _blockedFeedbackRoutine;

        private void Awake()
        {
            DisableAutomaticNavigation(_previousCharacterButton);
            DisableAutomaticNavigation(_nextCharacterButton);

            RefreshLegend();
        }

        private void OnEnable()
        {
            _previousCharacterButton.onClick.AddListener(HandlePreviousCharacterClicked);
            _nextCharacterButton.onClick.AddListener(HandleNextCharacterClicked);
            StartCoroutine(FocusNextFrame());
        }

        private void OnDisable()
        {
            _previousCharacterButton.onClick.RemoveListener(HandlePreviousCharacterClicked);
            _nextCharacterButton.onClick.RemoveListener(HandleNextCharacterClicked);
            ResetNavigationInput();
            StopBlockedFeedback(false);
        }

        private void Update()
        {
            if (_lastGamepad != Gamepad.current)
            {
                _lastGamepad = Gamepad.current;
                RefreshLegend();
            }

            LobbyInputDeviceKind detectedDevice = DetectRecentlyUsedDevice();
            if (detectedDevice != LobbyInputDeviceKind.Unknown)
            {
                SetLastInputDevice(detectedDevice);
            }

            ProcessNavigationInput();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (!_interactionEnabled)
            {
                return;
            }

            LobbyInputDeviceKind inputDevice = ResolveInputDevice();
            SetLastInputDevice(inputDevice);
            SubmitRequested?.Invoke(inputDevice);
            eventData.Use();
        }

        public void OnCancel(BaseEventData eventData)
        {
            if (!_interactionEnabled)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            SetLastInputDevice(ResolveInputDevice());
            BackRequested?.Invoke();
            eventData.Use();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Focus();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_interactionEnabled
                || eventData.button != PointerEventData.InputButton.Left
                || _localPlayerSlot < 0
                || _localPlayerSlot >= _slotRoots.Length
                || !RectTransformUtility.RectangleContainsScreenPoint(
                    _slotRoots[_localPlayerSlot],
                    eventData.position,
                    eventData.pressEventCamera))
            {
                return;
            }

            SetLastInputDevice(LobbyInputDeviceKind.Keyboard);
            SubmitRequested?.Invoke(LobbyInputDeviceKind.Keyboard);
            Focus();
            eventData.Use();
        }

        public void Render(
            IReadOnlyList<LobbyPlayerData> players,
            ulong localClientId,
            bool requireUniqueCharacters)
        {
            _localPlayerSlot = -1;
            _localPlayerIsReady = true;

            for (int slot = 0; slot < _slotRoots.Length; slot++)
            {
                if (TryFindPlayerInSlot(players, slot, out LobbyPlayerData player))
                {
                    bool isLocalPlayer = player.ClientId == localClientId;
                    if (isLocalPlayer && player.InputDevice != LobbyInputDeviceKind.Unknown)
                    {
                        SetLastInputDevice(player.InputDevice);
                    }

                    if (isLocalPlayer)
                    {
                        _localPlayerSlot = slot;
                        _localPlayerIsReady = player.IsReady;
                    }

                    bool isCharacterLocked = requireUniqueCharacters
                        && TryFindCharacterLock(players, player, out _);
                    RenderConnectedSlot(slot, player, isLocalPlayer, isCharacterLocked);
                }
                else
                {
                    RenderEmptySlot(slot);
                }
            }

            RefreshBrowseControls();
            Focus();
        }

        public void SetInteractionEnabled(bool isEnabled)
        {
            _interactionEnabled = isEnabled;
            ResetNavigationInput();
            RefreshBrowseControls();
            if (isEnabled)
            {
                Focus();
            }
        }

        public void ShowCharacterBlocked(int lockingPlayerSlot)
        {
            if (_localPlayerSlot < 0 || _localPlayerIsReady)
            {
                return;
            }

            StopBlockedFeedback(false);
            _blockedFeedbackSlot = _localPlayerSlot;
            _lockingPlayerSlot = lockingPlayerSlot;
            _blockedSlotOriginalPosition = _slotRoots[_blockedFeedbackSlot].anchoredPosition;
            RenderBlockedStatus(_blockedFeedbackSlot, _lockingPlayerSlot);
            _blockedFeedbackRoutine = StartCoroutine(PlayBlockedFeedback());
        }

        public void ClearCharacterBlockedFeedback()
        {
            StopBlockedFeedback(true);
        }

        private void HandlePreviousCharacterClicked()
        {
            HandleBrowseButtonClicked(-1);
        }

        private void HandleNextCharacterClicked()
        {
            HandleBrowseButtonClicked(1);
        }

        private void HandleBrowseButtonClicked(int direction)
        {
            if (!_interactionEnabled)
            {
                return;
            }

            SetLastInputDevice(LobbyInputDeviceKind.Keyboard);
            ResetNavigationInput();
            RequestBrowse(direction, LobbyInputDeviceKind.Keyboard);
            Focus();
        }

        private void RequestBrowse(int direction, LobbyInputDeviceKind inputDevice)
        {
            ClearCharacterBlockedFeedback();
            BrowseRequested?.Invoke(direction, inputDevice);
        }

        private void RefreshBrowseControls()
        {
            bool shouldShow = _interactionEnabled
                && _localPlayerSlot >= 0
                && !_localPlayerIsReady;

            _browseControlsRoot.gameObject.SetActive(shouldShow);
            if (shouldShow)
            {
                _browseControlsRoot.anchoredPosition = _slotRoots[_localPlayerSlot].anchoredPosition;
            }
        }

        public void Focus()
        {
            if (_interactionEnabled && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(gameObject);
            }
        }

        private void RenderConnectedSlot(
            int slot,
            LobbyPlayerData player,
            bool isLocalPlayer,
            bool isCharacterLocked)
        {
            LobbyCharacterDefinition character = FindCharacter(player.CharacterId);
            _playerNameTexts[slot].text = player.PlayerName.ToString();
            _slotRoots[slot].localScale = isLocalPlayer && !player.IsReady
                ? Vector3.one * 1.03f
                : Vector3.one;

            Color slotColor = PlayerSlotColors.Get(slot);
            slotColor.a = isCharacterLocked ? 0.38f : isLocalPlayer ? 1f : 0.88f;
            _slotBackgrounds[slot].color = slotColor;

            Image portrait = _characterPortraits[slot];
            portrait.gameObject.SetActive(true);
            portrait.sprite = character != null ? character.Portrait : null;
            Color portraitColor = character != null && character.Portrait == null
                ? Color.Lerp(character.Color, Color.black, 0.38f)
                : Color.white;
            portraitColor.a = isCharacterLocked ? 0.42f : 1f;
            portrait.color = portraitColor;

            string characterName = character != null ? character.DisplayName : "ESCOLHENDO";
            _slotLabels[slot].text = characterName.ToUpperInvariant();
            _slotLabels[slot].color = isCharacterLocked
                ? new Color(1f, 1f, 1f, 0.52f)
                : Color.white;

            Image deviceIcon = _deviceIcons[slot];
            Sprite deviceSprite = GetDeviceSprite(player.InputDevice);
            deviceIcon.gameObject.SetActive(deviceSprite != null);
            deviceIcon.sprite = deviceSprite;

            Text statusText = _statusTexts[slot];
            if (player.IsReady)
            {
                statusText.text = "PRONTO";
                statusText.fontSize = 28;
                statusText.color = new Color(0.42f, 1f, 0.58f, 1f);
            }
            else
            {
                RenderChoosingStatus(slot, isLocalPlayer);
            }

            if (isLocalPlayer && _blockedFeedbackSlot == slot)
            {
                RenderBlockedStatus(slot, _lockingPlayerSlot);
            }
        }

        private void RenderEmptySlot(int slot)
        {
            _playerNameTexts[slot].text = string.Empty;
            _slotRoots[slot].localScale = Vector3.one;
            Color emptyColor = PlayerSlotColors.Get(slot);
            emptyColor.a = 0.28f;
            _slotBackgrounds[slot].color = emptyColor;
            _characterPortraits[slot].gameObject.SetActive(false);
            _deviceIcons[slot].gameObject.SetActive(false);
            _slotLabels[slot].text = "AGUARDANDO";
            _slotLabels[slot].color = new Color(1f, 1f, 1f, 0.65f);
            _statusTexts[slot].text = "AGUARDANDO";
            _statusTexts[slot].fontSize = 16;
            _statusTexts[slot].color = new Color(0.58f, 0.58f, 0.64f, 1f);
        }

        private void RenderChoosingStatus(int slot, bool isLocalPlayer)
        {
            Text statusText = _statusTexts[slot];
            statusText.text = "ESCOLHENDO...";
            statusText.fontSize = isLocalPlayer ? 18 : 16;
            statusText.color = isLocalPlayer
                ? Color.white
                : new Color(0.78f, 0.78f, 0.84f, 1f);
        }

        private void RenderBlockedStatus(int slot, int lockingPlayerSlot)
        {
            Text statusText = _statusTexts[slot];
            statusText.text = $"BLOQUEADO - {GetColoredPlayerLabel(lockingPlayerSlot)}";
            statusText.fontSize = 18;
            statusText.color = new Color(1f, 0.42f, 0.38f, 1f);
        }

        private static string GetColoredPlayerLabel(int playerSlot)
        {
            return $"<color=#{PlayerSlotColors.GetHtml(playerSlot)}>P{playerSlot + 1}</color>";
        }

        private IEnumerator PlayBlockedFeedback()
        {
            const float shakeDuration = 0.24f;
            const float messageDuration = 1.5f;
            const float shakeAmplitude = 9f;
            float elapsed = 0f;

            while (elapsed < shakeDuration && _blockedFeedbackSlot >= 0)
            {
                elapsed += Time.unscaledDeltaTime;
                float offset = Mathf.Sin(elapsed * 95f) * shakeAmplitude;
                _slotRoots[_blockedFeedbackSlot].anchoredPosition =
                    _blockedSlotOriginalPosition + Vector2.right * offset;
                yield return null;
            }

            if (_blockedFeedbackSlot >= 0)
            {
                _slotRoots[_blockedFeedbackSlot].anchoredPosition = _blockedSlotOriginalPosition;
                RefreshBrowseControls();
            }

            yield return new WaitForSecondsRealtime(messageDuration - shakeDuration);

            int feedbackSlot = _blockedFeedbackSlot;
            _blockedFeedbackSlot = -1;
            _lockingPlayerSlot = -1;
            _blockedFeedbackRoutine = null;
            if (feedbackSlot >= 0 && feedbackSlot < _statusTexts.Length)
            {
                RenderChoosingStatus(feedbackSlot, feedbackSlot == _localPlayerSlot);
            }
        }

        private void StopBlockedFeedback(bool restoreStatus)
        {
            if (_blockedFeedbackRoutine != null)
            {
                StopCoroutine(_blockedFeedbackRoutine);
                _blockedFeedbackRoutine = null;
            }

            int feedbackSlot = _blockedFeedbackSlot;
            if (feedbackSlot >= 0 && feedbackSlot < _slotRoots.Length)
            {
                _slotRoots[feedbackSlot].anchoredPosition = _blockedSlotOriginalPosition;
            }

            _blockedFeedbackSlot = -1;
            _lockingPlayerSlot = -1;
            RefreshBrowseControls();

            if (restoreStatus && feedbackSlot >= 0 && feedbackSlot < _statusTexts.Length)
            {
                RenderChoosingStatus(feedbackSlot, feedbackSlot == _localPlayerSlot);
            }
        }

        private void ProcessNavigationInput()
        {
            if (!_interactionEnabled || _localPlayerIsReady)
            {
                ResetNavigationInput();
                return;
            }

            int direction = ReadVerticalNavigationDirection(out LobbyInputDeviceKind inputDevice);
            if (direction == 0)
            {
                ResetNavigationInput();
                return;
            }

            bool directionChanged = direction != _heldNavigationDirection;
            if (!directionChanged && Time.unscaledTime < _nextNavigationTime)
            {
                return;
            }

            _heldNavigationDirection = direction;
            _nextNavigationTime = Time.unscaledTime
                + (directionChanged ? NavigationRepeatDelay : NavigationRepeatRate);
            SetLastInputDevice(inputDevice);
            Focus();
            RequestBrowse(direction, inputDevice);
        }

        private static int ReadVerticalNavigationDirection(out LobbyInputDeviceKind inputDevice)
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.upArrowKey.wasPressedThisFrame
                    || Keyboard.current.wKey.wasPressedThisFrame)
                {
                    inputDevice = LobbyInputDeviceKind.Keyboard;
                    return -1;
                }

                if (Keyboard.current.downArrowKey.wasPressedThisFrame
                    || Keyboard.current.sKey.wasPressedThisFrame)
                {
                    inputDevice = LobbyInputDeviceKind.Keyboard;
                    return 1;
                }
            }

            if (Gamepad.current != null)
            {
                if (Gamepad.current.dpad.up.wasPressedThisFrame)
                {
                    inputDevice = LobbyInputDeviceKind.Gamepad;
                    return -1;
                }

                if (Gamepad.current.dpad.down.wasPressedThisFrame)
                {
                    inputDevice = LobbyInputDeviceKind.Gamepad;
                    return 1;
                }
            }

            if (Keyboard.current != null
                && (Keyboard.current.upArrowKey.isPressed || Keyboard.current.wKey.isPressed))
            {
                inputDevice = LobbyInputDeviceKind.Keyboard;
                return -1;
            }

            if (Keyboard.current != null
                && (Keyboard.current.downArrowKey.isPressed || Keyboard.current.sKey.isPressed))
            {
                inputDevice = LobbyInputDeviceKind.Keyboard;
                return 1;
            }

            if (Gamepad.current != null)
            {
                float verticalInput = Gamepad.current.leftStick.y.ReadValue();
                if (Gamepad.current.dpad.up.isPressed || verticalInput > 0.65f)
                {
                    inputDevice = LobbyInputDeviceKind.Gamepad;
                    return -1;
                }

                if (Gamepad.current.dpad.down.isPressed || verticalInput < -0.65f)
                {
                    inputDevice = LobbyInputDeviceKind.Gamepad;
                    return 1;
                }
            }

            inputDevice = LobbyInputDeviceKind.Unknown;
            return 0;
        }

        private void ResetNavigationInput()
        {
            _heldNavigationDirection = 0;
            _nextNavigationTime = 0f;
        }

        private static void DisableAutomaticNavigation(Selectable selectable)
        {
            if (selectable == null)
            {
                return;
            }

            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.None;
            selectable.navigation = navigation;
        }

        private void SetLastInputDevice(LobbyInputDeviceKind inputDevice)
        {
            if (inputDevice == LobbyInputDeviceKind.Unknown || _lastInputDevice == inputDevice)
            {
                return;
            }

            _lastInputDevice = inputDevice;
            RefreshLegend();
        }

        private void RefreshLegend()
        {
            bool usesGamepad = _lastInputDevice == LobbyInputDeviceKind.Gamepad;
            if (_confirmLegendIcon != null)
            {
                _confirmLegendIcon.sprite = _promptCatalog.GetSprite(
                    usesGamepad ? "<Gamepad>/buttonSouth" : "<Keyboard>/enter");
            }

            if (_backLegendIcon != null)
            {
                _backLegendIcon.sprite = _promptCatalog.GetSprite(
                    usesGamepad ? "<Gamepad>/buttonEast" : "<Keyboard>/backspace");
            }

            if (_instructionText != null)
            {
                _instructionText.text = "CONFIRMAR / PRONTO";
            }
        }

        private Sprite GetDeviceSprite(LobbyInputDeviceKind inputDevice)
        {
            return inputDevice switch
            {
                LobbyInputDeviceKind.Keyboard => _keyboardDeviceSprite,
                LobbyInputDeviceKind.Gamepad => _gamepadDeviceSprite,
                _ => null
            };
        }

        private LobbyCharacterDefinition FindCharacter(LobbyCharacterId characterId)
        {
            for (int i = 0; i < _characters.Length; i++)
            {
                if (_characters[i] != null && _characters[i].Id == characterId)
                {
                    return _characters[i];
                }
            }

            return null;
        }

        private static bool TryFindPlayerInSlot(
            IReadOnlyList<LobbyPlayerData> players,
            int slot,
            out LobbyPlayerData player)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].PlayerSlot == slot)
                {
                    player = players[i];
                    return true;
                }
            }

            player = default;
            return false;
        }

        private static bool TryFindCharacterLock(
            IReadOnlyList<LobbyPlayerData> players,
            LobbyPlayerData requestingPlayer,
            out int lockingPlayerSlot)
        {
            for (int i = 0; i < players.Count; i++)
            {
                LobbyPlayerData player = players[i];
                if (player.ClientId != requestingPlayer.ClientId
                    && player.IsReady
                    && player.CharacterId == requestingPlayer.CharacterId)
                {
                    lockingPlayerSlot = player.PlayerSlot;
                    return true;
                }
            }

            lockingPlayerSlot = -1;
            return false;
        }

        private static LobbyInputDeviceKind ResolveInputDevice()
        {
            if (Gamepad.current != null
                && (Gamepad.current.buttonSouth.wasPressedThisFrame
                    || Gamepad.current.buttonEast.wasPressedThisFrame
                    || Gamepad.current.dpad.IsPressed()
                    || Mathf.Abs(Gamepad.current.leftStick.y.ReadValue()) > 0.5f))
            {
                return LobbyInputDeviceKind.Gamepad;
            }

            return LobbyInputDeviceKind.Keyboard;
        }

        private static LobbyInputDeviceKind DetectRecentlyUsedDevice()
        {
            if (Gamepad.current != null
                && (Gamepad.current.buttonSouth.wasPressedThisFrame
                    || Gamepad.current.buttonEast.wasPressedThisFrame
                    || Gamepad.current.dpad.IsPressed()
                    || Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.36f))
            {
                return LobbyInputDeviceKind.Gamepad;
            }

            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            {
                return LobbyInputDeviceKind.Keyboard;
            }

            return LobbyInputDeviceKind.Unknown;
        }

        private IEnumerator FocusNextFrame()
        {
            yield return null;
            Focus();
        }
    }
}
