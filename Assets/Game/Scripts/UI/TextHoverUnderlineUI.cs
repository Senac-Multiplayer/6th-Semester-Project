using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class TextHoverUnderlineUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Text _text;
        [SerializeField] private Image _underline;

        private void Awake()
        {
            if (!_text || !_underline)
            {
                Debug.LogError("Configure o texto e o sublinhado no Inspector.", this);
                enabled = false;
                return;
            }
            _underline.gameObject.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _underline.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Min(_text.preferredWidth, _text.rectTransform.rect.width));
            _underline.color = _text.color;
            _underline.gameObject.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _underline.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            if (_underline) _underline.gameObject.SetActive(false);
        }
    }
}
