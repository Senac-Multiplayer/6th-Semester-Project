using UnityEngine;

namespace PiGame.Gameplay
{
    public class AmmoIndicatorsView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer[] _indicators;

        private void Awake()
        {
            for (int index = 0; index < _indicators.Length; index++)
            {
                _indicators[index].enabled = false;
                _indicators[index].gameObject.SetActive(false);
            }
        }

        public void SetCount(int count, bool visible, Color color)
        {
            SetIndicators(_indicators, transform.position.x, count, visible, color);
        }

        public static void SetIndicators(SpriteRenderer[] indicators, float centerX, int count, bool visible, Color color)
        {
            count = Mathf.Clamp(count, 0, indicators.Length);
            for (int index = 0; index < indicators.Length; index++)
            {
                bool showIndicator = visible && index < count;
                indicators[index].color = color;
                indicators[index].enabled = showIndicator;
                // O contorno serializado é filho do indicador e acompanha sua visibilidade.
                indicators[index].gameObject.SetActive(showIndicator);
            }

            if (!visible || count == 0) return;

            Bounds bounds = indicators[0].bounds;
            for (int index = 1; index < count; index++) bounds.Encapsulate(indicators[index].bounds);
            Vector3 offset = Vector3.right * (centerX - bounds.center.x);
            foreach (SpriteRenderer indicator in indicators) indicator.transform.position += offset;
        }
    }
}
