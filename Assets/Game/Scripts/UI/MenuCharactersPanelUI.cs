using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class MenuCharactersPanelUI : MonoBehaviour, IMoveHandler, IPointerClickHandler
    {
        [Serializable]
        private class CharacterPreview
        {
            public string Name;
            public Color AccentColor = Color.white;
            [TextArea(5, 12)] public string Description;
            public GameObject Visual;
        }

        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _description;
        [SerializeField] private TMP_Text _counter;
        [SerializeField] private Button _previous;
        [SerializeField] private Button _next;
        [SerializeField] private Button _back;
        [SerializeField] private CharacterPreview[] _characters;
        private int _index;

        public bool IsVisible => _root.activeInHierarchy;
        public event Action Closed;

        private void Awake()
        {
            if (!_root || !_name || !_description || !_counter || !_previous || !_next || !_back
                || _characters == null || _characters.Length == 0
                || Array.Exists(_characters, item => item == null || !item.Visual))
            {
                Debug.LogError("Configure os personagens e referências do MenuCharactersPanelUI.", this);
                enabled = false;
                return;
            }
            _previous.onClick.AddListener(() => Browse(-1));
            _next.onClick.AddListener(() => Browse(1));
            _back.onClick.AddListener(() => Closed?.Invoke());
        }

        public void Show()
        {
            _root.SetActive(true);
            _index = 0;
            Refresh();
        }

        public void Hide() => _root.SetActive(false);

        // O InputSystemUIInputModule já fornece repetição e suporte a teclado/controle.
        public void OnMove(AxisEventData eventData)
        {
            if (!IsVisible) return;
            if (eventData.moveDir == MoveDirection.Up) Browse(-1);
            else if (eventData.moveDir == MoveDirection.Down) Browse(1);
            eventData.Use();
        }

        // EventTrigger dos botões encaminha Move mesmo após uma seleção com o mouse.
        public void HandleNavigation(BaseEventData eventData) => OnMove((AxisEventData)eventData);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (IsVisible) EventSystem.current.SetSelectedGameObject(gameObject);
        }

        private void Browse(int step)
        {
            PiGame.Sound.UiAudioManager.PlayCharacterBrowse();
            _index = (_index + step + _characters.Length) % _characters.Length;
            Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < _characters.Length; i++)
                _characters[i].Visual.SetActive(i == _index);
            _name.text = _characters[_index].Name;
            _name.color = _characters[_index].AccentColor;
            _description.text = _characters[_index].Description;
            _counter.text = $"{_index + 1:00} / {_characters.Length:00}";
            EventSystem.current.SetSelectedGameObject(gameObject);
        }
    }
}
