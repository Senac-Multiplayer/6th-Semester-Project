using System;
using System.Collections;
using PiGame.Lobby;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class PlayerNamePanelUI : MonoBehaviour, ICancelHandler
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private InputField _nameInput;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;

        private Coroutine _focusRoutine;
        private bool _buttonsBound;

        public event Action Closed;
        public bool IsVisible => enabled && _root.activeSelf;

        private void Awake()
        {
            if (_root == null || _nameInput == null || _confirmButton == null || _cancelButton == null)
            {
                Debug.LogError("PlayerNamePanelUI precisa do painel, campo de nome e botões no Inspector.", this);
                enabled = false;
                return;
            }

            _nameInput.characterLimit = LocalPlayerName.MaximumLength;
            _nameInput.onSubmit.AddListener(HandleInputSubmitted);
            _confirmButton.onClick.AddListener(Save);
            _cancelButton.onClick.AddListener(HandleBack);
            _buttonsBound = true;
        }

        private void OnDestroy()
        {
            if (!_buttonsBound) return;
            _nameInput.onSubmit.RemoveListener(HandleInputSubmitted);
            _confirmButton.onClick.RemoveListener(Save);
            _cancelButton.onClick.RemoveListener(HandleBack);
        }

        private void OnDisable() => StopFocusRoutine();

        public void Show()
        {
            bool canCancel = LocalPlayerName.HasSavedName;
            _nameInput.SetTextWithoutNotify(canCancel ? LocalPlayerName.Load() : string.Empty);
            _cancelButton.gameObject.SetActive(canCancel);
            _nameInput.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnDown = _confirmButton,
                selectOnUp = canCancel ? _cancelButton : _confirmButton
            };
            _confirmButton.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = _nameInput,
                selectOnRight = canCancel ? _cancelButton : null
            };
            _cancelButton.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = _nameInput,
                selectOnLeft = _confirmButton
            };
            _root.SetActive(true);
            StopFocusRoutine();
            _focusRoutine = StartCoroutine(FocusInputNextFrame());
        }

        public void Hide()
        {
            StopFocusRoutine();
            _root.SetActive(false);
        }

        public void HandleBack()
        {
            if (!IsVisible || !LocalPlayerName.HasSavedName) return;
            PiGame.Sound.UiAudioManager.PlayButton();
            Hide();
            Closed?.Invoke();
        }

        public void OnCancel(BaseEventData eventData)
        {
            HandleBack();
            eventData.Use();
        }

        private void HandleInputSubmitted(string _) => Save();

        private void Save()
        {
            if (!IsVisible) return;
            PiGame.Sound.UiAudioManager.PlayButton();
            LocalPlayerName.Save(_nameInput.text);
            Hide();
            Closed?.Invoke();
        }

        private IEnumerator FocusInputNextFrame()
        {
            yield return null;
            _focusRoutine = null;
            EventSystem.current.SetSelectedGameObject(_nameInput.gameObject);
            _nameInput.ActivateInputField();
        }

        private void StopFocusRoutine()
        {
            if (_focusRoutine == null) return;
            StopCoroutine(_focusRoutine);
            _focusRoutine = null;
        }
    }
}
