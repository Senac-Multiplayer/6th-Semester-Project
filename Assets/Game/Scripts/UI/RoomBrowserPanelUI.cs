using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class RoomBrowserPanelUI : MonoBehaviour
    {
        [SerializeField] private Button _refreshButton;
        [SerializeField] private Button _backButton;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private RectTransform _content;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private RoomSlotUI _slotPrefab;
        private readonly List<RoomSlotUI> _slots = new();
        private GameObject _lastSelection;
        public event Action BackRequested;
        public event Action RefreshRequested;
        public event Action<string> JoinRequested;

        public bool Initialize()
        {
            if (_refreshButton && _backButton && _statusText && _content && _scroll && _slotPrefab) return true;
            Debug.LogError("Configure botões, status, conteúdo, ScrollRect e prefab de sala no RoomBrowserPanelUI.", this);
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
        public void RefreshRooms() => RefreshRequested?.Invoke();
        private void Join(string id) => JoinRequested?.Invoke(id);

        public void SetStatus(string message, bool busy = false)
        {
            _statusText.text = message;
            _refreshButton.interactable = !busy;
            _content.gameObject.SetActive(!busy);
            EventSystem.current.SetSelectedGameObject(_backButton.gameObject);
        }

        public void Render(IList<ISessionInfo> rooms)
        {
            EventSystem.current.SetSelectedGameObject(null);
            for (int i = 0; i < rooms.Count; i++)
            {
                if (i == _slots.Count)
                {
                    // Slots dinâmicos são instâncias do prefab configurado, sem construir UI por código.
                    RoomSlotUI slot = Instantiate(_slotPrefab, _content);
                    slot.JoinRequested += Join;
                    _slots.Add(slot);
                }
                _slots[i].gameObject.SetActive(true);
                _slots[i].Render(rooms[i]);
            }
            for (int i = rooms.Count; i < _slots.Count; i++) _slots[i].gameObject.SetActive(false);
            _scroll.verticalNormalizedPosition = 1f;
            SetStatus(rooms.Count == 0 ? "NENHUMA SALA DISPONÍVEL. VOLTE E CRIE UMA SALA." : $"{rooms.Count} SALA(S) DISPONÍVEL(IS)");
            // Navegação automática mantém os botões de entrada e o rodapé no mesmo painel modal.
            foreach (Selectable selectable in GetComponentsInChildren<Selectable>())
                selectable.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            if (rooms.Count > 0) _slots[0].Focus();
            else EventSystem.current.SetSelectedGameObject(_refreshButton.gameObject);
        }

        private void Update()
        {
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected == _lastSelection) return;
            _lastSelection = selected;
            if (!selected || !selected.transform.IsChildOf(_content)) return;
            Canvas.ForceUpdateCanvases();
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_scroll.viewport, selected.transform);
            Rect viewport = _scroll.viewport.rect;
            float offset = bounds.min.y < viewport.yMin ? viewport.yMin - bounds.min.y
                : bounds.max.y > viewport.yMax ? viewport.yMax - bounds.max.y : 0f;
            _content.anchoredPosition += Vector2.up * offset;
        }

        private IEnumerator SelectRefreshButton()
        {
            yield return null;
            EventSystem.current.SetSelectedGameObject(_refreshButton.interactable ? _refreshButton.gameObject : _backButton.gameObject);
        }
    }
}
