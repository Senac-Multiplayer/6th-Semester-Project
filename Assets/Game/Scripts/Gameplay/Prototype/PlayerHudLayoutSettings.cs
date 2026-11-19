using System.Threading;
using UnityEngine;

namespace PiGame.Gameplay
{
    [CreateAssetMenu(fileName = "data_player_hud_layout", menuName = "Pi Game/UI/Player HUD Layout")]
    public sealed class PlayerHudLayoutSettings : ScriptableObject
    {
        [Header("Grade visual — pixels na resolução de referência")]
        [SerializeField, Min(1)] private int _pixelsPerUnit = 16;
        [SerializeField, Min(0)] private int _horizontalGapPixels = 1;
        [SerializeField, Min(0)] private int _verticalGapPixels = 2;

        [Header("Tamanhos incluindo os contornos")]
        [SerializeField] private Vector2Int _healthSizePixels = new(5, 4);
        [SerializeField, Min(0)] private int _healthBorderPixels = 1;
        [SerializeField, Min(1)] private int _ammoDiameterPixels = 5;
        [SerializeField, Min(1)] private int _markerSizePixels = 10;

        [Header("Altura a partir da origem do personagem")]
        [SerializeField, Min(0)] private int _ammoHeightPixels = 18;
        [SerializeField] private float _nameHeight = 2.9f;

        private int _revision;
        public int Revision => _revision;
        private float Unit => 1f / Mathf.Max(1, _pixelsPerUnit);
        public float HorizontalGap => Mathf.Max(0, _horizontalGapPixels) * Unit;
        public float VerticalGap => Mathf.Max(0, _verticalGapPixels) * Unit;
        public Vector2 HealthSize => new Vector2(Mathf.Max(1, _healthSizePixels.x), Mathf.Max(1, _healthSizePixels.y)) * Unit;
        public float HealthBorder => Mathf.Max(0, _healthBorderPixels) * Unit;
        public float AmmoDiameter => Mathf.Max(1, _ammoDiameterPixels) * Unit;
        public float MarkerSize => Mathf.Max(1, _markerSizePixels) * Unit;
        public float AmmoHeight => Mathf.Max(0, _ammoHeightPixels) * Unit;
        public float NameHeight => _nameHeight;

        // OnValidate pode ocorrer fora da thread principal; a aplicação fica com o componente.
        private void OnValidate() => Interlocked.Increment(ref _revision);
    }
}
