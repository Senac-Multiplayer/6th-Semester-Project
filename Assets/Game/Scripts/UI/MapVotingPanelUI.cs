using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using PiGame.Input;
using PiGame.Lobby;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;
using PiGame.Sound;

namespace PiGame.UI
{
    public class MapVotingPanelUI : MonoBehaviour,
        IMoveHandler,
        ISubmitHandler,
        ICancelHandler,
        IPointerEnterHandler,
        IPointerMoveHandler
    {
        [Header("Maps")]
        [SerializeField] private LobbyMapDefinition[] _maps;
        [SerializeField] private Button[] _optionButtons;
        [SerializeField] private Image[] _previewImages;
        [SerializeField] private Text[] _optionLabels;
        [SerializeField] private Text[] _voterLabels;

        [SerializeField, Min(1)] private int _columns = 5;

        [Header("Status")]
        [SerializeField] private Text _statusText;

        [Header("Input icons")]
        [SerializeField] private Image _confirmLegendIcon;
        [SerializeField] private Image _backLegendIcon;
        [SerializeField] private InputPromptCatalog _promptCatalog;
        [FormerlySerializedAs("_instructionText")]
        [SerializeField] private Text _confirmLegendText;
        [SerializeField] private Text _backLegendText;

        [Header("Selection visual")]
        [SerializeField] private Color _normalColor = new(0.32f, 0.27f, 0.38f, 1f);
        [SerializeField] private Color _selectedColor = new(0.98f, 0.72f, 0.18f, 1f);
        [SerializeField] private Color _winnerColor = new(0.3f, 0.9f, 0.5f, 1f);
        [SerializeField, Min(1f)] private float _selectedScale = 1.12f;
        [SerializeField, Range(0f, 0.05f)] private float _selectedPulseAmount = 0.015f;
        [SerializeField, Min(0.1f)] private float _selectedPulseSpeed = 2f;

        public event Action<LobbyMapId, LobbyInputDeviceKind> VoteRequested;
        public event Action<LobbyInputDeviceKind> ConfirmRequested;
        public event Action BackRequested;

        private LobbyInputDeviceKind _lastInputDevice = LobbyInputDeviceKind.Keyboard;
        private Gamepad _lastGamepad;
        private LobbyMapId _selectedMapId = LobbyMapId.Random;
        private LobbyStage _stage;
        private UiAudioManager _uiAudioManager;
        private LobbyMapDrawData _draw = LobbyMapDrawData.Empty;
        private readonly List<LobbyMapId> _concreteOptions = new();
        private readonly List<LobbyMapId> _tiedOptions = new();
        private int _drawPhase = -1;
        private int _soundPhase = -1;
        private bool _isDrawingMap;
        private bool _interactionEnabled = true;
        private bool _localVoteConfirmed;
        private UnityAction[] _optionButtonActions;

        private bool HasFinalMap => _stage == LobbyStage.MatchStarting
            && !_isDrawingMap && _selectedMapId == _draw.MapId;

        private void Awake()
        {
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                DisableAutomaticNavigation(_optionButtons[i]);
            }

            for (int i = 1; i < _optionButtons.Length; i++)
            {
                LobbyMapId map = GetOptionMapId(i);
                if (map != LobbyMapId.None && _optionButtons[i].gameObject.activeSelf)
                    _concreteOptions.Add(map);
            }

            ConfigureMapCards();
            RefreshLegend();
            RefreshSelectionVisuals();

            _uiAudioManager = GetComponent<UiAudioManager>();
        }

        private void OnEnable()
        {
            _optionButtonActions = new UnityAction[_optionButtons.Length];
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                int optionIndex = i;
                _optionButtonActions[i] = () => HandleOptionClicked(optionIndex);
                _optionButtons[i].onClick.AddListener(_optionButtonActions[i]);
            }

            StartCoroutine(FocusNextFrame());
        }

        private void OnDisable()
        {
            _drawPhase = -1;
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                if (_optionButtonActions != null && i < _optionButtonActions.Length)
                {
                    _optionButtons[i].onClick.RemoveListener(_optionButtonActions[i]);
                }
            }

            _optionButtonActions = null;
        }

        private void Update()
        {
            if (_lastGamepad != Gamepad.current)
            {
                _lastGamepad = Gamepad.current;
                if (_lastGamepad == null) _lastInputDevice = LobbyInputDeviceKind.Keyboard;
                RefreshLegend();
            }

            if (_stage == LobbyStage.MatchStarting)
            {
                AnimateSelectedCard();
                return;
            }

            LobbyInputDeviceKind detectedDevice = DetectRecentlyUsedDevice();
            if (detectedDevice != LobbyInputDeviceKind.Unknown)
            {
                SetLastInputDevice(detectedDevice);
            }

            AnimateSelectedCard();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (!_interactionEnabled || _stage != LobbyStage.MapVoting || _localVoteConfirmed)
            {
                return;
            }

            LobbyInputDeviceKind inputDevice = ResolveInputDevice();
            SetLastInputDevice(inputDevice);
            ConfirmRequested?.Invoke(inputDevice);
            eventData.Use();
        }

        public void OnCancel(BaseEventData eventData)
        {
            if (!_interactionEnabled || _stage != LobbyStage.MapVoting)
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
            HandlePointerSelection(eventData);
            Focus();
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            HandlePointerSelection(eventData);
            Focus();
        }

        public void Render(
            IReadOnlyList<LobbyPlayerData> players,
            IReadOnlyList<LobbyMapVoteData> votes,
            ulong localClientId,
            LobbyStage stage,
            LobbyMapDrawData draw,
            double serverTime)
        {
            if (!_draw.Equals(draw))
            {
                _drawPhase = -1;
                _soundPhase = -1;
                _tiedOptions.Clear();
                for (int i = 0; i < draw.Options.Length; i++) _tiedOptions.Add(draw.Options[i]);
            }
            _stage = stage;
            _draw = draw;
            _localVoteConfirmed = false;
            if (stage == LobbyStage.MapVoting && TryFindVote(votes, localClientId, out LobbyMapVoteData localVote))
            {
                _selectedMapId = localVote.MapId;
                _localVoteConfirmed = localVote.IsConfirmed;
            }
            if (stage == LobbyStage.MatchStarting) RenderDraw(serverTime);
            else _isDrawingMap = false;
            RefreshLegend();

            RenderVoters(players, votes);
            RefreshStatus();
            RefreshSelectionVisuals();
            Focus();
        }

        public void SetInteractionEnabled(bool isEnabled)
        {
            _interactionEnabled = isEnabled;
            RefreshSelectionVisuals();
            if (isEnabled)
            {
                Focus();
            }
        }

        private void ConfigureMapCards()
        {
            if (_optionLabels.Length > 0)
            {
                _optionLabels[0].text = "ALEATÓRIO";
            }

            for (int i = 0; i < _maps.Length && i + 1 < _optionLabels.Length; i++)
            {
                LobbyMapDefinition map = _maps[i];
                if (map == null)
                {
                    continue;
                }

                _optionLabels[i + 1].text = map.DisplayName.ToUpperInvariant();
                if (i + 1 < _previewImages.Length && map.Preview != null)
                {
                    _previewImages[i + 1].sprite = map.Preview;
                    _previewImages[i + 1].color = Color.white;
                }
            }
        }

        private void MoveSelection(int direction, LobbyInputDeviceKind inputDevice)
        {
            int currentIndex = GetOptionIndex(_selectedMapId);
            int nextIndex = (currentIndex + direction + _optionButtons.Length) % _optionButtons.Length;
            SelectOption(nextIndex, inputDevice);
        }

        private void HandleOptionClicked(int optionIndex)
        {
            if (!_interactionEnabled || _stage != LobbyStage.MapVoting || _localVoteConfirmed)
            {
                return;
            }

            SetLastInputDevice(LobbyInputDeviceKind.Keyboard);
            LobbyMapId mapId = GetOptionMapId(optionIndex);
            if (mapId == _selectedMapId)
            {
                ConfirmRequested?.Invoke(LobbyInputDeviceKind.Keyboard);
                return;
            }

            SelectOption(optionIndex, LobbyInputDeviceKind.Keyboard);
        }

        private void HandlePointerSelection(PointerEventData eventData)
        {
            if (!_interactionEnabled || _stage != LobbyStage.MapVoting || _localVoteConfirmed)
            {
                return;
            }

            GameObject hoveredObject = eventData.pointerCurrentRaycast.gameObject;
            if (hoveredObject == null)
            {
                return;
            }

            for (int i = 0; i < _optionButtons.Length; i++)
            {
                if (hoveredObject.transform.IsChildOf(_optionButtons[i].transform)
                    || hoveredObject == _optionButtons[i].gameObject)
                {
                    SelectOption(i, LobbyInputDeviceKind.Keyboard);
                    return;
                }
            }
        }

        private void SelectOption(int optionIndex, LobbyInputDeviceKind inputDevice)
        {
            LobbyMapId mapId = GetOptionMapId(optionIndex);
            if (mapId == LobbyMapId.None || mapId == _selectedMapId)
            {
                return;
            }

            _selectedMapId = mapId;
            RefreshSelectionVisuals();
            VoteRequested?.Invoke(mapId, inputDevice);
        }

        private void RenderVoters(
            IReadOnlyList<LobbyPlayerData> players,
            IReadOnlyList<LobbyMapVoteData> votes)
        {
            StringBuilder[] labels = new StringBuilder[_voterLabels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = new StringBuilder();
            }

            for (int playerIndex = 0; playerIndex < players.Count; playerIndex++)
            {
                LobbyPlayerData player = players[playerIndex];
                if (!TryFindVote(votes, player.ClientId, out LobbyMapVoteData vote))
                {
                    continue;
                }

                int optionIndex = GetOptionIndex(vote.MapId);
                if (optionIndex < 0 || optionIndex >= labels.Length)
                {
                    continue;
                }

                if (labels[optionIndex].Length > 0)
                {
                    labels[optionIndex].Append("  ");
                }

                string playerLabel = $"P{player.PlayerSlot + 1}";
                labels[optionIndex].Append(vote.IsConfirmed
                    ? $"<color=#68FF94>{playerLabel}</color>"
                    : playerLabel);
            }

            for (int i = 0; i < _voterLabels.Length; i++)
            {
                _voterLabels[i].text = labels[i].Length > 0
                    ? labels[i].ToString()
                    : "—";
            }
        }

        private void RefreshStatus()
        {
            if (_stage == LobbyStage.MatchStarting)
            {
                _statusText.text = _isDrawingMap
                    ? (_drawPhase == 0 ? "DESEMPATANDO VOTOS..." : "SORTEANDO MAPA...")
                    : $"MAPA ESCOLHIDO: {GetMapDisplayName(_selectedMapId)}\nINICIANDO PARTIDA...";
                if (!_isDrawingMap && _selectedMapId == LobbyMapId.Random)
                    _statusText.text = "ALEATÓRIO VENCEU O DESEMPATE...";
                _statusText.color = HasFinalMap ? _winnerColor : _selectedColor;
                return;
            }

            if (_localVoteConfirmed)
            {
                _statusText.text = "VOTO CONFIRMADO\nCANCELE PARA ALTERAR";
                _statusText.color = new Color(0.42f, 1f, 0.58f, 1f);
                return;
            }

            _statusText.text = "ESCOLHA E CONFIRME SEU VOTO";
            _statusText.color = Color.white;
        }

        private void RefreshSelectionVisuals()
        {
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                LobbyMapId optionMapId = GetOptionMapId(i);
                bool isWinner = _stage == LobbyStage.MatchStarting
                    && optionMapId == _selectedMapId;
                bool isSelected = _stage == LobbyStage.MapVoting && optionMapId == _selectedMapId;
                Button button = _optionButtons[i];

                button.interactable = _interactionEnabled
                    && _stage == LobbyStage.MapVoting
                    && !_localVoteConfirmed;
                button.transform.localScale = isSelected || isWinner
                    ? Vector3.one * _selectedScale
                    : Vector3.one;
                button.targetGraphic.color = isWinner || isSelected
                    ? (HasFinalMap ? _winnerColor : _selectedColor)
                    : _normalColor;
            }
        }

        private void AnimateSelectedCard()
        {
            float pulse = HasFinalMap ? 1f : 1f
                + Mathf.Sin(Time.unscaledTime * Mathf.PI * _selectedPulseSpeed)
                    * _selectedPulseAmount;

            for (int i = 0; i < _optionButtons.Length; i++)
            {
                LobbyMapId optionMapId = GetOptionMapId(i);
                bool isWinner = _stage == LobbyStage.MatchStarting
                    && optionMapId == _selectedMapId;
                bool isSelected = _stage == LobbyStage.MapVoting
                    && optionMapId == _selectedMapId;
                _optionButtons[i].transform.localScale = isSelected || isWinner
                    ? Vector3.one * (_selectedScale * pulse)
                    : Vector3.one;
            }
        }

        public void RenderDraw(double serverTime)
        {
            if (_stage != LobbyStage.MatchStarting || _draw.MapId == LobbyMapId.None) return;
            double elapsed = serverTime - _draw.StartsAt;
            bool tiePhase = _draw.HasTie && elapsed < LobbyMapDrawData.DrawSeconds;
            bool randomPhase = _draw.IsRandom && (!_draw.HasTie || elapsed >= _draw.RandomStartsAfter)
                && elapsed < _draw.Duration;
            int phase = tiePhase ? 0 : randomPhase ? 1 : 2;
            bool spinning = tiePhase || randomPhase;
            LobbyMapId displayed = _draw.HasTie && _draw.IsRandom
                && elapsed < _draw.RandomStartsAfter ? LobbyMapId.Random : _draw.MapId;
            if (spinning)
            {
                List<LobbyMapId> options = tiePhase ? _tiedOptions : _concreteOptions;
                LobbyMapId winner = tiePhase ? _draw.WinningOption : _draw.MapId;
                int winnerIndex = options.IndexOf(winner);
                if (winnerIndex < 0)
                {
                    Debug.LogError($"O mapa sorteado {winner} não está nos cards da votação.", this);
                    enabled = false;
                    return;
                }
                double phaseTime = tiePhase ? elapsed : elapsed - _draw.RandomStartsAfter;
                displayed = options[GetDrawIndex(options.Count, winnerIndex, phaseTime)];
                if (_soundPhase != phase && phaseTime >= 0d)
                {
                    _soundPhase = phase;
                    if (_uiAudioManager != null) _uiAudioManager.PlayDrawRoullete();
                }
            }
            if (_drawPhase == phase && _selectedMapId == displayed && _isDrawingMap == spinning)
                return;
            _drawPhase = phase;
            _selectedMapId = displayed;
            _isDrawingMap = spinning;
            RefreshStatus();
            RefreshSelectionVisuals();
        }

        // O mesmo relógio e a mesma sequência em cada peer; o último passo leva cerca de 1–2 s.
        private static int GetDrawIndex(int count, int winnerIndex, double elapsed)
        {
            if (count == 1 || elapsed >= LobbyMapDrawData.DrawSeconds) return winnerIndex;
            int steps = count * Mathf.Max(2, Mathf.CeilToInt(16f / count)) + winnerIndex;
            float totalWeight = 0f;
            for (int step = 0; step < steps; step++)
                totalWeight += 0.02f + Mathf.Pow((float)step / (steps - 1), 5f);
            double nextStepTime = 0d;
            for (int step = 0; step < steps; step++)
            {
                float weight = 0.02f + Mathf.Pow((float)step / (steps - 1), 5f);
                nextStepTime += LobbyMapDrawData.DrawSeconds * weight / totalWeight;
                if (elapsed < nextStepTime) return step % count;
            }
            return winnerIndex;
        }

        private void SetLastInputDevice(LobbyInputDeviceKind inputDevice)
        {
            if (inputDevice == LobbyInputDeviceKind.Unknown || inputDevice == _lastInputDevice)
            {
                return;
            }

            _lastInputDevice = inputDevice;
            RefreshLegend();
        }

        private void RefreshLegend()
        {
            bool voting = _stage != LobbyStage.MatchStarting;
            _confirmLegendIcon.enabled = voting;
            _backLegendIcon.enabled = voting;
            _confirmLegendText.enabled = voting;
            _backLegendText.enabled = voting;
            bool usesGamepad = _lastInputDevice == LobbyInputDeviceKind.Gamepad;
            _confirmLegendIcon.sprite = _promptCatalog.GetSprite(
                usesGamepad ? "<Gamepad>/buttonSouth" : "<Keyboard>/enter");
            _backLegendIcon.sprite = _promptCatalog.GetSprite(
                usesGamepad ? "<Gamepad>/buttonEast" : "<Keyboard>/escape");
            _confirmLegendText.text = "CONFIRMAR VOTO";
            _backLegendText.text = "VOLTAR";
        }

        private void Focus()
        {
            if (_interactionEnabled && _stage == LobbyStage.MapVoting && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(gameObject);
            }
        }

        public void OnMove(AxisEventData eventData)
        {
            if (!_interactionEnabled || _stage != LobbyStage.MapVoting || _localVoteConfirmed)
            {
                return;
            }

            // The existing Input System UI module handles keyboard/gamepad navigation and repeat.
            int offset = eventData.moveDir switch
            {
                MoveDirection.Left => -1,
                MoveDirection.Right => 1,
                MoveDirection.Up => -_columns,
                MoveDirection.Down => _columns,
                _ => 0
            };
            if (offset == 0) return;

            LobbyInputDeviceKind inputDevice = ResolveInputDevice();
            SetLastInputDevice(inputDevice);
            MoveSelection(offset, inputDevice);
            eventData.Use();
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

        private LobbyMapId GetOptionMapId(int optionIndex)
        {
            if (optionIndex == 0)
            {
                return LobbyMapId.Random;
            }

            int mapIndex = optionIndex - 1;
            return mapIndex >= 0 && mapIndex < _maps.Length && _maps[mapIndex] != null
                ? _maps[mapIndex].Id
                : LobbyMapId.None;
        }

        private int GetOptionIndex(LobbyMapId mapId)
        {
            if (mapId == LobbyMapId.Random)
            {
                return 0;
            }

            for (int i = 0; i < _maps.Length; i++)
            {
                if (_maps[i] != null && _maps[i].Id == mapId)
                {
                    return i + 1;
                }
            }

            return 0;
        }

        private string GetMapDisplayName(LobbyMapId mapId)
        {
            if (mapId == LobbyMapId.Random) return "ALEATÓRIO";
            for (int i = 0; i < _maps.Length; i++)
            {
                if (_maps[i] != null && _maps[i].Id == mapId)
                {
                    return _maps[i].DisplayName.ToUpperInvariant();
                }
            }

            return mapId.ToString().ToUpperInvariant();
        }

        private static bool TryFindVote(
            IReadOnlyList<LobbyMapVoteData> votes,
            ulong clientId,
            out LobbyMapVoteData vote)
        {
            for (int i = 0; i < votes.Count; i++)
            {
                if (votes[i].ClientId == clientId)
                {
                    vote = votes[i];
                    return true;
                }
            }

            vote = default;
            return false;
        }

        private static LobbyInputDeviceKind ResolveInputDevice()
        {
            if (Gamepad.current != null
                && (Gamepad.current.buttonSouth.wasPressedThisFrame
                    || Gamepad.current.buttonEast.wasPressedThisFrame
                    || Gamepad.current.dpad.IsPressed()
                    || Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.25f
                    || Gamepad.current.rightStick.ReadValue().sqrMagnitude > 0.25f))
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
                    || Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.36f
                    || Gamepad.current.rightStick.ReadValue().sqrMagnitude > 0.36f))
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
