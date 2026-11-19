using System;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace PiGame.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MapCardBorderUI : MaskableGraphic
    {
        [SerializeField] private Sprite _sprite;
        [Tooltip("Faixas da borda em coordenadas normalizadas da arte original.")]
        [SerializeField] private Rect[] _segments = Array.Empty<Rect>();

        public Sprite Sprite
        {
            get => _sprite;
            set
            {
                if (_sprite == value) return;
                _sprite = value;
                SetAllDirty();
            }
        }

        public override Texture mainTexture => _sprite ? _sprite.texture : base.mainTexture;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (!_sprite || _segments == null) return;
            Rect rect = GetPixelAdjustedRect();
            Vector4 uv = DataUtility.GetOuterUV(_sprite);
            var quad = new UIVertex[4];
            for (int i = 0; i < quad.Length; i++)
            {
                quad[i] = UIVertex.simpleVert;
                quad[i].color = color;
            }
            foreach (Rect strip in _segments)
            {
                SetVertex(ref quad[0], strip.xMin, strip.yMin, rect, uv);
                SetVertex(ref quad[1], strip.xMin, strip.yMax, rect, uv);
                SetVertex(ref quad[2], strip.xMax, strip.yMax, rect, uv);
                SetVertex(ref quad[3], strip.xMax, strip.yMin, rect, uv);
                mesh.AddUIVertexQuad(quad);
            }
        }

        private static void SetVertex(ref UIVertex vertex, float x, float y, Rect rect, Vector4 uv)
        {
            vertex.position = new Vector2(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
            vertex.uv0 = new Vector2(Mathf.Lerp(uv.x, uv.z, x), Mathf.Lerp(uv.y, uv.w, y));
        }
    }
}
