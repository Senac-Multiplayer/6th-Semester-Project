using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class RoomBrowserPanelUI : MonoBehaviour
    {
        [SerializeField] private Button _refreshButton;

        public event Action BackRequested;

        public bool Initialize()
        {
            if (_refreshButton) return true;
            Debug.LogError("Configure Refresh Button no RoomBrowserPanelUI.", this);
            enabled = false;
            return false;
        }

        private void OnEnable()
        {
            if (Initialize()) StartCoroutine(SelectRefreshButton());
        }

        private void OnDisable() => StopAllCoroutines();

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
        public void HandleBack() => BackRequested?.Invoke();

        private IEnumerator SelectRefreshButton()
        {
            yield return null;
            EventSystem.current.SetSelectedGameObject(_refreshButton.gameObject);
        }
    }
}