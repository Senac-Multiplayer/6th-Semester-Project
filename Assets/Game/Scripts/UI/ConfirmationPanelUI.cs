using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class ConfirmationPanelUI : MonoBehaviour, ICancelHandler
    {
        [SerializeField] private TMP_Text _messageText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Graphic _confirmText;
        [SerializeField] private Color _normalTextColor = new Color32(246, 242, 218, 255);
        [SerializeField] private Color _dangerTextColor = new Color32(255, 112, 112, 255);

        private Coroutine _selectionRoutine;

        public event Action Confirmed;
        public event Action Canceled;

        public void Show(string message, string confirmLabel = "CONFIRMAR", bool dangerous = true)
        {
            _messageText.text = message;
            if (_confirmText is TMP_Text tmp) tmp.text = confirmLabel;
            else if (_confirmText is Text text) text.text = confirmLabel;
            _confirmText.color = dangerous ? _dangerTextColor : _normalTextColor;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        public void OnCancel(BaseEventData eventData)
        {
            HandleCanceled();
        }

        private void OnEnable()
        {
            _confirmButton.onClick.AddListener(HandleConfirmed);
            _cancelButton.onClick.AddListener(HandleCanceled);
            _selectionRoutine = StartCoroutine(SelectCancelButtonNextFrame());
        }

        private void OnDisable()
        {
            _confirmButton.onClick.RemoveListener(HandleConfirmed);
            _cancelButton.onClick.RemoveListener(HandleCanceled);

            if (_selectionRoutine == null)
            {
                return;
            }

            StopCoroutine(_selectionRoutine);
            _selectionRoutine = null;
        }

        private void HandleConfirmed()
        {
            Confirmed?.Invoke();
        }

        private void HandleCanceled()
        {
            Canceled?.Invoke();
        }

        private IEnumerator SelectCancelButtonNextFrame()
        {
            yield return null;
            EventSystem.current?.SetSelectedGameObject(_cancelButton.gameObject);
            _selectionRoutine = null;
        }
    }
}
