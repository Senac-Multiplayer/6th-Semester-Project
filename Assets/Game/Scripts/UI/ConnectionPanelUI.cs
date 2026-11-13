using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class ConnectionPanelUI : MonoBehaviour, ICancelHandler
    {
        [Header("Grupos")]
        [SerializeField] private GameObject _mainActions;
        [SerializeField] private GameObject _createRoomContent;
        [SerializeField] private GameObject _joinCodeContent;
        [SerializeField] private CanvasGroup _interactionGroup;

        [Header("Seleção e campos")]
        [SerializeField] private Button _createRoomButton;
        [SerializeField] private InputField _roomNameInput;
        [SerializeField] private InputField _joinCodeInput;
        [SerializeField] private TMP_Text _statusText;

        [SerializeField] private Toggle _privateRoomToggle;

        private Coroutine _selectionRoutine;
        private bool _isConnecting;
        private bool _initialized;

        public event Action CancelConnectionRequested;
        public event Action BackRequested;
        public event Action<string, bool> CreateRoomRequested;
        public event Action<string> JoinCodeRequested;
        public event Action QuickJoinRequested;

        public void CreateRoom() => CreateRoomRequested?.Invoke(_roomNameInput.text, _privateRoomToggle.isOn);
        public void JoinByCode() => JoinCodeRequested?.Invoke(_joinCodeInput.text);
        public void QuickJoin() => QuickJoinRequested?.Invoke();

        private void Awake() => Initialize();

        private void OnEnable()
        {
            if (Initialize()) ShowIdle();
        }

        private void OnDisable() => StopSelectionRoutine();

        public bool Initialize()
        {
            if (_initialized) return true;

            if (!_mainActions || !_createRoomContent || !_joinCodeContent
                || !_interactionGroup || !_createRoomButton || !_roomNameInput
                || !_joinCodeInput || !_statusText || !_privateRoomToggle)
            {
                Debug.LogError("Configure grupos, CanvasGroup, seleção, campos e status no ConnectionPanelUI.", this);
                enabled = false;
                return false;
            }

            _initialized = true;
            return true;
        }

        public void ShowIdle()
        {
            _isConnecting = false;
            SetInteractionEnabled(true);
            SetStatus(string.Empty);
            _joinCodeInput.SetTextWithoutNotify(string.Empty);
            ShowGroup(_mainActions, _createRoomButton.gameObject);
        }

        public void ShowCreateRoom()
        {
            SetStatus(string.Empty);
            ShowGroup(_createRoomContent, _roomNameInput.gameObject);
        }

        public void ShowJoinCode()
        {
            SetStatus(string.Empty);
            ShowGroup(_joinCodeContent, _joinCodeInput.gameObject);
        }

        private void ShowGroup(GameObject group, GameObject firstSelection)
        {
            StopSelectionRoutine();
            EventSystem.current.SetSelectedGameObject(null);
            _mainActions.SetActive(group == _mainActions);
            _createRoomContent.SetActive(group == _createRoomContent);
            _joinCodeContent.SetActive(group == _joinCodeContent);
            SelectNextFrame(firstSelection);
        }

        public void HandleBack()
        {
            if (_isConnecting)
            {
                CancelConnectionRequested?.Invoke();
                return;
            }

            if (_mainActions.activeSelf) BackRequested?.Invoke();
            else ShowIdle();
        }

        public void OnCancel(BaseEventData eventData) => HandleBack();

        public void ShowConnecting(string message)
        {
            _isConnecting = true;
            StopSelectionRoutine();
            SetInteractionEnabled(false);
            SetStatus(message);
            EventSystem.current.SetSelectedGameObject(null);
        }

        public void ShowError(string message)
        {
            _isConnecting = false;
            SetInteractionEnabled(true);
            SetStatus(message);
            FocusDefaultButton();
        }

        public void SetInteractionEnabled(bool value)
        {
            _interactionGroup.interactable = value && !_isConnecting;
        }

        public void FocusDefaultButton()
        {
            GameObject target = _createRoomContent.activeSelf ? _roomNameInput.gameObject
                : _joinCodeContent.activeSelf ? _joinCodeInput.gameObject
                : _createRoomButton.gameObject;
            SelectNextFrame(target);
        }

        private void SetStatus(string message)
        {
            _statusText.text = message;
            _statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        private void SelectNextFrame(GameObject target)
        {
            StopSelectionRoutine();
            _selectionRoutine = StartCoroutine(Select(target));
        }

        private IEnumerator Select(GameObject target)
        {
            yield return null;
            if (target.activeInHierarchy && _interactionGroup.interactable)
            {
                EventSystem.current.SetSelectedGameObject(target);
                if (target.TryGetComponent(out InputField input)) input.ActivateInputField();
            }
            _selectionRoutine = null;
        }

        private void StopSelectionRoutine()
        {
            if (_selectionRoutine == null) return;
            StopCoroutine(_selectionRoutine);
            _selectionRoutine = null;
        }
    }
}