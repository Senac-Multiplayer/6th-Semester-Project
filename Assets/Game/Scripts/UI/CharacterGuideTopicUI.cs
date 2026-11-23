using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    public sealed class CharacterGuideTopicUI : Selectable, ISubmitHandler
    {
        [SerializeField] private Text _heading, _description;
        [SerializeField] private Image _icon, _highlight;
        private MenuCharactersPanelUI _owner;
        private int _index;
        private Color _accent;
        internal void Bind(MenuCharactersPanelUI owner, int index) { _owner = owner; _index = index; }
        internal void SetContent(string heading, string description, Sprite icon, Color accent)
        {
            _accent = accent;
            _heading.text = heading;
            _description.text = description;
            _icon.sprite = icon;
            _icon.gameObject.SetActive(icon);
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
            SetHighlighted(false);
        }
        internal void SetHighlighted(bool highlighted)
        {
            _highlight.color = new Color(_accent.r, _accent.g, _accent.b, highlighted ? 0.12f : 0f);
            _heading.color = highlighted ? _accent : new Color(0.906f, 0.729f, 0.443f);
        }
        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
            if (IsInteractable()) _owner?.SelectTopic(_index);
        }
        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            _owner?.SelectTopic(_index);
        }
        public override void OnMove(AxisEventData eventData) => _owner?.NavigateTopic(_index, eventData);
        public void OnSubmit(BaseEventData eventData) => _owner?.SelectTopic(_index);
    }
}
