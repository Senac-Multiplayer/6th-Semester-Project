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
        [SerializeField] private Button _previousPageButton;
        [SerializeField] private Button _nextPageButton;
        [SerializeField] private TMP_Text _pageText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private RectTransform _content;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private RoomSlotUI _slotPrefab;
        [SerializeField] private CanvasGroup _interactionGroup;
        private IList<ISessionInfo> _rooms = Array.Empty<ISessionInfo>();
        private readonly List<RoomSlotUI> _slots = new();
        private GameObject _lastSelection;
        private int _pageNumber = 1;
        private bool _hasNextPage;
        public event Action BackRequested;
        public event Action RefreshRequested;
        public event Action<string> JoinRequested;
        public event Action<int> PageRequested;

        public bool Initialize()
        {
            if (_refreshButton && _backButton && _previousPageButton && _nextPageButton
                && _pageText && _statusText && _content && _scroll && _slotPrefab && _interactionGroup) return true;
            Debug.LogError("Configure botões, status, conteúdo, ScrollRect e prefab de sala no RoomBrowserPanelUI.", this);
            enabled = false;
            return false;
        }

        private void OnEnable()
        {
            if (Initialize()) StartCoroutine(SelectRefreshButton());
        }

        private void OnDisable() => StopAllCoroutines();
        public void Show()
        {
            SetInteractionEnabled(true);
            gameObject.SetActive(true);
        }
        public void Hide() => gameObject.SetActive(false);
        public void HandleBack() => BackRequested?.Invoke();
        public void RefreshRooms() => RefreshRequested?.Invoke();
        public void PreviousPage() => PageRequested?.Invoke(-1);
        public void NextPage() => PageRequested?.Invoke(1);
        private void Join(string id) => JoinRequested?.Invoke(id);

        public void SetInteractionEnabled(bool value)
        {
            _interactionGroup.interactable = value;
            _interactionGroup.blocksRaycasts = value;
        }

        public ISessionInfo FindRoom(string id)
        {
            foreach (ISessionInfo room in _rooms)
                if (room.Id == id) return room;
            return null;
        }

        public void FocusRoom(string id)
        {
            foreach (RoomSlotUI slot in _slots)
                if (slot.gameObject.activeSelf && slot.SessionId == id) { slot.Focus(); return; }
            EventSystem.current.SetSelectedGameObject(_refreshButton.gameObject);
        }

        public void SetStatus(string message, bool busy = false)
        {
            _statusText.text = message;
            _refreshButton.interactable = !busy;
            _previousPageButton.interactable = !busy && _pageNumber > 1;
            _nextPageButton.interactable = !busy && _hasNextPage;
            _previousPageButton.GetComponentInChildren<TMP_Text>().alpha = _previousPageButton.interactable ? 1f : 0.35f;
            _nextPageButton.GetComponentInChildren<TMP_Text>().alpha = _nextPageButton.interactable ? 1f : 0.35f;
            _content.gameObject.SetActive(!busy);
            if (busy) Navigate(_backButton, null, null, null, null);
            else ConfigureNavigation();
            EventSystem.current.SetSelectedGameObject(_backButton.gameObject);
        }

        public void Render(IList<ISessionInfo> rooms, int pageNumber = 1, bool hasNextPage = false)
        {
            EventSystem.current.SetSelectedGameObject(null);
            _rooms = rooms;
            _pageNumber = pageNumber;
            _hasNextPage = hasNextPage;
            _pageText.text = pageNumber.ToString();
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
            SetStatus(rooms.Count == 0 ? "NENHUMA SALA DISPONÍVEL. VOLTE E CRIE UMA SALA." : $"{rooms.Count} SALA(S) NESTA PÁGINA");
            if (rooms.Count > 0) _slots[0].Focus();
            else EventSystem.current.SetSelectedGameObject(_refreshButton.gameObject);
        }

        private void ConfigureNavigation()
        {
            Button[] rooms = _content.GetComponentsInChildren<Button>();
            Selectable pageButton = _previousPageButton.interactable ? _previousPageButton
                : _nextPageButton.interactable ? _nextPageButton : _refreshButton;
            Selectable lastRoom = rooms.Length > 0 ? rooms[^1] : _refreshButton;
            for (int i = 0; i < rooms.Length; i++)
                Navigate(rooms[i], i > 0 ? rooms[i - 1] : pageButton,
                    i + 1 < rooms.Length ? rooms[i + 1] : pageButton, null, null);
            Navigate(_previousPageButton, lastRoom, _refreshButton, null,
                _nextPageButton.interactable ? _nextPageButton : null);
            Navigate(_nextPageButton, lastRoom, _backButton,
                _previousPageButton.interactable ? _previousPageButton : null, null);
            Navigate(_refreshButton, pageButton == _refreshButton ? lastRoom : pageButton,
                null, null, _backButton);
            Navigate(_backButton, _nextPageButton.interactable ? _nextPageButton : lastRoom,
                null, _refreshButton, null);
        }

        private static void Navigate(Selectable item, Selectable up, Selectable down,
            Selectable left, Selectable right)
        {
            item.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up, selectOnDown = down, selectOnLeft = left, selectOnRight = right
            };
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
