using TMPro;
using UnityEngine;

namespace PiGame.Gameplay
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class PlayerHudLayout : MonoBehaviour
    {
        [SerializeField] private PlayerHudLayoutSettings _settings;
        [SerializeField] private SpriteRenderer _marker;
        [SerializeField] private TextMeshPro _playerLabel;
        [SerializeField] private Transform _healthRoot;
        [SerializeField] private SpriteRenderer[] _healthSegments;
        [SerializeField] private SpriteRenderer[] _ammoIndicators;

        private PlayerHudLayoutSettings _appliedSettings;
        private int _appliedRevision = -1;
        public PlayerHudLayoutSettings Settings => _settings;

        private void OnEnable() => ApplyLayout();
        private void OnValidate() => _appliedRevision = -1;
        private void LateUpdate()
        {
            if (_settings && (_appliedSettings != _settings || _appliedRevision != _settings.Revision))
                ApplyLayout();
        }

        public void ApplyLayout()
        {
            if (!_settings || !_marker || !_playerLabel || !_healthRoot
                || _healthSegments == null || _ammoIndicators == null) return;

            Vector2 healthSize = _settings.HealthSize;
            float healthY = _settings.AmmoHeight + _settings.AmmoDiameter * .5f
                + _settings.VerticalGap + healthSize.y * .5f;
            float markerY = healthY + healthSize.y * .5f
                + _settings.VerticalGap + _settings.MarkerSize * .5f;
            _healthRoot.localPosition = new Vector3(0f, healthY, _healthRoot.localPosition.z);
            _playerLabel.transform.localPosition = new Vector3(0f, _settings.NameHeight, _playerLabel.transform.localPosition.z);
            SetGraphic(_marker, new Vector2(_settings.MarkerSize, _settings.MarkerSize), new Vector2(0f, markerY));

            for (int i = 0; i < _healthSegments.Length; i++)
            {
                SpriteRenderer fill = _healthSegments[i];
                if (!fill || !fill.sprite || !fill.transform.parent.TryGetComponent(out SpriteRenderer border) || !border.sprite) continue;
                float x = (i - (_healthSegments.Length - 1) * .5f) * (healthSize.x + _settings.HorizontalGap);
                Transform slot = border.transform;
                slot.localScale = new Vector3(healthSize.x / border.sprite.bounds.size.x,
                    healthSize.y / border.sprite.bounds.size.y, 1f);
                slot.localPosition = new Vector3(x, 0f, slot.localPosition.z);
                Vector2 inner = Vector2.Max(Vector2.zero, healthSize - Vector2.one * (_settings.HealthBorder * 2f));
                fill.transform.localScale = new Vector3(inner.x / (fill.sprite.bounds.size.x * slot.localScale.x),
                    inner.y / (fill.sprite.bounds.size.y * slot.localScale.y), 1f);
                fill.transform.localPosition = Vector3.zero;
            }

            for (int i = 0; i < _ammoIndicators.Length; i++)
            {
                var ammo = _ammoIndicators[i];
                if (!ammo) continue;
                float x = (i - (_ammoIndicators.Length - 1) * .5f)
                    * (_settings.AmmoDiameter + _settings.HorizontalGap);
                SetGraphic(ammo, Vector2.one * _settings.AmmoDiameter, new Vector2(x, _settings.AmmoHeight));
            }

            // Alterar o asset durante o Play mantém a munição restante centralizada.
            bool any = false;
            Bounds visible = default;
            foreach (var ammo in _ammoIndicators)
            {
                if (!ammo || !ammo.enabled || !ammo.gameObject.activeInHierarchy) continue;
                if (any) visible.Encapsulate(ammo.bounds);
                else { visible = ammo.bounds; any = true; }
            }
            if (Application.isPlaying && any)
            {
                Vector3 offset = Vector3.right * (transform.position.x - visible.center.x);
                foreach (var ammo in _ammoIndicators) if (ammo) ammo.transform.position += offset;
            }

            _appliedSettings = _settings;
            _appliedRevision = _settings.Revision;
        }

        private static void SetGraphic(SpriteRenderer graphic, Vector2 size, Vector2 position)
        {
            // Inclui os filhos de contorno, mesmo quando a munição está oculta.
            Bounds local = default;
            bool found = false;
            foreach (SpriteRenderer part in graphic.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (!part.sprite) continue;
                Bounds sprite = part.sprite.bounds;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++)
                {
                    Vector3 corner = new(x == 0 ? sprite.min.x : sprite.max.x,
                        y == 0 ? sprite.min.y : sprite.max.y, 0f);
                    corner = graphic.transform.InverseTransformPoint(part.transform.TransformPoint(corner));
                    if (found) local.Encapsulate(corner);
                    else { local = new Bounds(corner, Vector3.zero); found = true; }
                }
            }
            if (!found || local.size.x <= 0f || local.size.y <= 0f) return;
            Transform target = graphic.transform;
            target.localScale = new Vector3(size.x / local.size.x, size.y / local.size.y, 1f);
            Vector3 center = Vector3.Scale(local.center, target.localScale);
            target.localPosition = new Vector3(position.x - center.x, position.y - center.y, target.localPosition.z);
        }
    }
}
