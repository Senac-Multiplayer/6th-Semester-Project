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
        private static readonly int ReadyParameter = Animator.StringToHash("Ready");

        [SerializeField] private LobbySelectionBackgroundUI _background;

        [Header("Characters")]
        [SerializeField] private LobbyCharacterDefinition[] _characters;

        [Header("Player slots")]
        [SerializeField] private RectTransform _slotsLayout;
        [SerializeField] private RectTransform _headerBand;
        [SerializeField] private RectTransform _footerBand;
        [SerializeField] private RectTransform[] _slotRoots;
        [SerializeField] private Image[] _slotBackgrounds;
        [SerializeField] private Image[] _clanBackgrounds = Array.Empty<Image>();
        [SerializeField] private Image[] _clanDetails = Array.Empty<Image>();
        [SerializeField] private Image[] _playerNumbers;
        [SerializeField] private Image[] _characterPortraits;
        [SerializeField] private Image[] _deviceIcons;
        [SerializeField] private Text[] _slotLabels;
        [SerializeField] private Text[] _playerNameTexts;
        [SerializeField] private Text[] _statusTexts;
        [SerializeField] private LobbyConnectionView _connectionView;
        [SerializeField] private Image[] _characterNameplates = Array.Empty<Image>();
        [SerializeField] private Image[] _readyChecks = Array.Empty<Image>();

        private enum CardBackground { Banner, BannerWithDetails, GradientWithDetails }

        [Header("Card appearance")]
        [SerializeField] private CardBackground _cardBackground = CardBackground.GradientWithDetails;
        [SerializeField] private bool _useCharacterBorderColors;
        [SerializeField] private Sprite _bronzeBorder;
        [SerializeField] private Image[] _cardBorders;
        [SerializeField] private Image[] _selectionBanners;
        [SerializeField] private Image[] _clanEmblems;

        [Header("Ready banners")]
        [SerializeField] private Animator[] _readyBanners = Array.Empty<Animator>();
        [SerializeField] private Image[] _readyBannerCloths = Array.Empty<Image>();
        [SerializeField] private Image[] _readyBannerMotifs;

        [Header("Character navigation")]
        [SerializeField] private RectTransform _browseControlsRoot;
        [SerializeField] private Button _previousCharacterButton;
        [SerializeField] private Button _nextCharacterButton;

        [Header("Input icons")]
        [SerializeField] private Sprite _keyboardDeviceSprite;
        [SerializeField] private Sprite _gamepadDeviceSprite;
        [SerializeField] private float _gamepadDeviceOffsetY = -3f;
        [SerializeField] private Image _confirmLegendIcon;
        [SerializeField] private Image _backLegendIcon;
        [SerializeField] private Image _upLegendIcon;
        [SerializeField] private Image _downLegendIcon;
        [SerializeField] private Image _pauseLegendIcon;
        [SerializeField] private InputPromptCatalog _promptCatalog;
        [SerializeField] private Text _instructionText;

        public event Action<int, LobbyInputDeviceKind> BrowseRequested;
        public event Action<LobbyInputDeviceKind> SubmitRequested;
        public event Action BackRequested;
        public bool InteractionEnabled => _interactionEnabled && gameObject.activeInHierarchy;

        private LobbyInputDeviceKind _lastInputDevice = LobbyInputDeviceKind.Keyboard;
        private Gamepad _lastGamepad;
        private Vector2[] _deviceIconPositions;
        private bool _interactionEnabled = true;
        private int _localPlayerSlot = -1;
        private bool _localPlayerIsReady = true;
        private int _heldNavigationDirection;
        private float _nextNavigationTime;
        private int _blockedFeedbackSlot = -1;
        private int _lockingPlayerSlot = -1;
        private Vector2 _blockedSlotOriginalPosition;
        private Coroutine _blockedFeedbackRoutine;
        private Coroutine _browsePulseRoutine;

        private void Awake()
        {
            if (!_connectionView || !_background || !_slotsLayout
                || !_upLegendIcon || !_downLegendIcon || !_pauseLegendIcon)
            {
                Debug.LogError("Configure o fundo, a área dos slots e os indicadores de conexão no seletor de personagens.", this);
                enabled = false;
                return;
            }
            if (_readyBanners.Length != _slotRoots.Length || _readyBannerCloths.Length != _slotRoots.Length
                || _characterNameplates.Length != _slotRoots.Length || _readyChecks.Length != _slotRoots.Length
                || Array.Exists(_readyBanners, banner => !banner) || Array.Exists(_readyBannerCloths, cloth => !cloth)
                || Array.Exists(_characterNameplates, plate => !plate) || Array.Exists(_readyChecks, check => !check))
            {
                Debug.LogError("Configure bandeira, placa do personagem e ícone de pronto para cada slot no CharacterSelectionPanelUI.", this);
                enabled = false;
                return;
            }
            if (_clanBackgrounds.Length != _slotRoots.Length || _clanDetails.Length != _slotRoots.Length
                || Array.Exists(_clanBackgrounds, image => !image) || Array.Exists(_clanDetails, image => !image)
                || Array.Exists(_characters, character => !character || !character.LobbyBackground || !character.LobbyBackgroundDetails))
            {
                Debug.LogError("Configure fundos e detalhes dos clãs no prefab e nos assets de personagem.", this);
                enabled = false;
                return;
            }
            if (_playerNumbers.Length != _slotRoots.Length
                || _readyBannerMotifs.Length != _slotRoots.Length
                || Array.Exists(_playerNumbers, image => !image)
                || Array.Exists(_readyBannerMotifs, image => !image)
                || Array.Exists(_characters, character => !character.LobbyBanner
                    || !character.LobbyUp || !character.LobbyDown || !character.LobbyUpPressed || !character.LobbyDownPressed))
            {
                Debug.LogError("Configure bandeiras, números e setas dos quatro clãs no seletor.", this);
                enabled = false;
                return;
            }
            if (_clanEmblems.Length != _slotRoots.Length || Array.Exists(_clanEmblems, image => !image)
                || !_bronzeBorder || _cardBorders.Length != _slotRoots.Length
                || _selectionBanners.Length != _slotRoots.Length
                || Array.Exists(_cardBorders, image => !image) || Array.Exists(_selectionBanners, image => !image)
                || Array.Exists(_characters, character => !character.LobbyFrame))
            {
                Debug.LogError("Configure molduras e bandeiras de fundo para os quatro cards do lobby.", this);
                enabled = false;
                return;
            }
            CacheDeviceIconPositions();

            DisableAutomaticNavigation(_previousCharacterButton);
            DisableAutomaticNavigation(_nextCharacterButton);

            RefreshLegend();
        }

        private void CacheDeviceIconPositions()
        {
            // O controlador pode renderizar um painel ainda inativo, antes do Awake.
            if (_deviceIconPositions != null) return;
            _deviceIconPositions = new Vector2[_deviceIcons.Length];
            for (int i = 0; i < _deviceIcons.Length; i++)
                _deviceIconPositions[i] = _deviceIcons[i].rectTransform.anchoredPosition;
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
            ResetBrowsePulse();
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

        private void LateUpdate()
        {
            Vector2 available = ((RectTransform)transform).rect.size;
            float headerHeight = _headerBand ? _headerBand.rect.height : 130f;
            float footerHeight = _footerBand ? _footerBand.rect.height : 130f;
            // O espaço da apresentação acompanha as barras, sem esticar as artes dos cards.
            float scale = Mathf.Clamp(Mathf.Min((available.y - headerHeight - footerHeight) / 820f,
                (available.x - 80f) / _slotsLayout.rect.width), 0.1f, 1f);
            _slotsLayout.localScale = Vector3.one * scale;
            _slotsLayout.anchoredPosition = new Vector2(_slotsLayout.anchoredPosition.x,
                (footerHeight - headerHeight) * 0.5f - 20f * scale);
            _browseControlsRoot.localScale = _slotsLayout.localScale;

            // Os botões acompanham o slot local ao redimensionar a janela.
            if (_browseControlsRoot.gameObject.activeSelf)
                PositionBrowseControls();
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
            if (!enabled) return;
            CacheDeviceIconPositions();
            _connectionView.SetPlayers(players);
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
            ResetBrowsePulse();
            _browsePulseRoutine = StartCoroutine(PulseBrowseFeedback(
                direction < 0 ? _previousCharacterButton.transform : _nextCharacterButton.transform));
            BrowseRequested?.Invoke(direction, inputDevice);
        }

        private IEnumerator PulseBrowseFeedback(Transform arrow)
        {
            const float duration = 0.18f;
            float elapsed = 0f;
            Button arrowButton = arrow.GetComponent<Button>();
            while (elapsed < duration)
            {
                float pulse = Mathf.Sin(elapsed / duration * Mathf.PI);
                arrowButton.image.overrideSprite = arrowButton.spriteState.pressedSprite;
                arrow.localScale = Vector3.one * (1f + 0.25f * pulse);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            arrowButton.image.overrideSprite = null;
            arrow.localScale = Vector3.one;
            _browsePulseRoutine = null;
        }

        private void ResetBrowsePulse()
        {
            if (_browsePulseRoutine != null)
                StopCoroutine(_browsePulseRoutine);
            _browsePulseRoutine = null;
            _previousCharacterButton.image.overrideSprite = null;
            _nextCharacterButton.image.overrideSprite = null;
            _previousCharacterButton.transform.localScale = Vector3.one;
            _nextCharacterButton.transform.localScale = Vector3.one;
        }

        private void RefreshBrowseControls()
        {
            bool shouldShow = _interactionEnabled
                && _localPlayerSlot >= 0
                && !_localPlayerIsReady;

            _browseControlsRoot.gameObject.SetActive(shouldShow);
            if (shouldShow)
            {
                PositionBrowseControls();
            }
        }

        private void PositionBrowseControls()
        {
            _browseControlsRoot.position = _slotRoots[_localPlayerSlot].position;
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
            if (isLocalPlayer) _background.ShowCharacter(character);
            _playerNameTexts[slot].text = player.PlayerName.ToString();
            _playerNumbers[slot].color = PlayerSlotColors.Get(slot);
            _slotRoots[slot].localScale = Vector3.one;
            _readyBanners[slot].gameObject.SetActive(true);
            Color characterColor = character != null ? character.Color : Color.gray;
            _clanEmblems[slot].color = Color.Lerp(characterColor, Color.white, 0.35f);
            _readyBannerCloths[slot].color = Color.Lerp(characterColor, Color.black, 0.18f);
            _readyBanners[slot].SetBool(ReadyParameter, player.IsReady);
            _readyChecks[slot].gameObject.SetActive(player.IsReady);

            _slotBackgrounds[slot].color = Color.clear;
            _clanBackgrounds[slot].gameObject.SetActive(character != null);
            _clanDetails[slot].gameObject.SetActive(character != null && _cardBackground != CardBackground.Banner);
            _selectionBanners[slot].gameObject.SetActive(character != null && _cardBackground != CardBackground.GradientWithDetails);
            _cardBorders[slot].sprite = _useCharacterBorderColors && character != null ? character.LobbyFrame : _bronzeBorder;
            _cardBorders[slot].type = _useCharacterBorderColors && character != null ? Image.Type.Simple : Image.Type.Sliced;
            if (character != null)
            {
                _clanBackgrounds[slot].sprite = character.LobbyBackground;
                _clanDetails[slot].sprite = character.LobbyBackgroundDetails;
                _clanDetails[slot].rectTransform.anchoredPosition = character.LobbyDetailsOffset;
                _clanDetails[slot].rectTransform.localScale = Vector3.one * character.LobbyDetailsScale;
                _readyBannerMotifs[slot].sprite = character.LobbyBanner;
                _readyBannerMotifs[slot].rectTransform.anchoredPosition = character.LobbyReadyBannerOffset;
                _selectionBanners[slot].sprite = character.LobbyBanner;

                if (isLocalPlayer)
                {
                    ConfigureArrow(_previousCharacterButton, character.LobbyUp, character.LobbyUpPressed);
                    ConfigureArrow(_nextCharacterButton, character.LobbyDown, character.LobbyDownPressed);
                }
            }
            _characterNameplates[slot].gameObject.SetActive(true);
            _characterNameplates[slot].color = Color.white;

            Image portrait = _characterPortraits[slot];
            portrait.gameObject.SetActive(true);
            portrait.sprite = character != null ? character.Portrait : null;
            portrait.rectTransform.localScale = Vector3.one * (character != null ? character.LobbyPortraitScale : 1f);
            portrait.rectTransform.anchoredPosition = new Vector2(0f, character != null ? character.LobbyPortraitOffsetY : 0f);
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
            deviceIcon.rectTransform.anchoredPosition = _deviceIconPositions[slot]
                + Vector2.up * (player.InputDevice == LobbyInputDeviceKind.Gamepad ? _gamepadDeviceOffsetY : 0f);

            Text statusText = _statusTexts[slot];
            if (player.IsReady)
            {
                statusText.text = "PRONTO";
                statusText.fontSize = 20;
                statusText.color = Color.white;
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

        private static void ConfigureArrow(Button button, Sprite normal, Sprite pressed)
        {
            button.image.sprite = normal;
            SpriteState states = button.spriteState;
            states.highlightedSprite = pressed;
            states.selectedSprite = pressed;
            states.pressedSprite = pressed;
            button.spriteState = states;
        }

        private void RenderEmptySlot(int slot)
        {
            _readyBanners[slot].gameObject.SetActive(false);
            _readyChecks[slot].gameObject.SetActive(false);
            _characterNameplates[slot].gameObject.SetActive(false);
            _playerNameTexts[slot].text = string.Empty;
            _slotRoots[slot].localScale = Vector3.one;

            _playerNumbers[slot].color = PlayerSlotColors.Get(slot);
            _clanBackgrounds[slot].gameObject.SetActive(false);
            _clanDetails[slot].gameObject.SetActive(false);
            _selectionBanners[slot].gameObject.SetActive(false);
            _cardBorders[slot].sprite = _bronzeBorder;
            _cardBorders[slot].type = Image.Type.Sliced;
            _slotBackgrounds[slot].color = new Color(0.12f, 0.14f, 0.18f, 1f);
            _characterPortraits[slot].gameObject.SetActive(false);
            _deviceIcons[slot].gameObject.SetActive(false);
            _slotLabels[slot].text = string.Empty;
            _slotLabels[slot].color = new Color(1f, 1f, 1f, 0.65f);
            _statusTexts[slot].text = "AGUARDANDO JOGADOR";
            _statusTexts[slot].fontSize = 12;
            _statusTexts[slot].color = new Color(0.8f, 0.8f, 0.84f, 1f);
        }

        private void RenderChoosingStatus(int slot, bool isLocalPlayer)
        {
            Text statusText = _statusTexts[slot];
            statusText.text = "ESCOLHENDO...";
            statusText.fontSize = 12;
            statusText.color = isLocalPlayer
                ? Color.white
                : new Color(0.78f, 0.78f, 0.84f, 1f);
        }

        private void RenderBlockedStatus(int slot, int lockingPlayerSlot)
        {
            Text statusText = _statusTexts[slot];
            statusText.text = $"BLOQUEADO - {GetColoredPlayerLabel(lockingPlayerSlot)}";
            statusText.fontSize = 12;
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
            _upLegendIcon.sprite = _promptCatalog.GetSprite(
                usesGamepad ? "<Gamepad>/dpad/up" : "<Keyboard>/upArrow");
            _downLegendIcon.sprite = _promptCatalog.GetSprite(
                usesGamepad ? "<Gamepad>/dpad/down" : "<Keyboard>/downArrow");
            _pauseLegendIcon.sprite = _promptCatalog.GetSprite(
                usesGamepad ? "<Gamepad>/start" : "<Keyboard>/escape");
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
