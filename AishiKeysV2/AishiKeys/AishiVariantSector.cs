using UnityEngine;
using UnityEngine.UI;

namespace AishiKeys
{
    public sealed class AishiVariantSector : MaskableGraphic
    {
        private Sprite _sprite;
        private int _index, _count;
        public override Texture mainTexture => _sprite != null ? _sprite.texture : Texture2D.whiteTexture;

        internal void Set(Sprite sprite, int index, int count)
        {
            if (_sprite == sprite && _index == index && _count == count)
                return;
            _sprite = sprite;
            _index = index;
            _count = count;
            raycastTarget = false;
            SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_sprite == null || _count < 2)
                return;
            Rect r = GetPixelAdjustedRect();
            Vector4 uv = UnityEngine.Sprites.DataUtility.GetOuterUV(_sprite);
            if (r.width <= 0 || r.height <= 0)
                return;
            float start = 2 * Mathf.PI * _index / _count;
            float end = 2 * Mathf.PI * (_index + 1) / _count;
            float corner = Mathf.Atan2(r.width, r.height);
            vh.AddVert(r.center, color, new Vector2((uv.x + uv.z) / 2, (uv.y + uv.w) / 2));
            Edge(vh, r, uv, start);
            for (int i = 0; i < 4; i++)
            {
                float phase = i == 0 ? corner : i == 1 ? Mathf.PI - corner : i == 2 ? Mathf.PI + corner : 2 * Mathf.PI - corner;
                if (phase > start && phase < end)
                    Edge(vh, r, uv, phase);
            }

            Edge(vh, r, uv, end);
            for (int i = 1; i < vh.currentVertCount - 1; i++)
                vh.AddTriangle(0, i, i + 1);
        }

        private void Edge(VertexHelper vh, Rect r, Vector4 uv, float phase)
        {
            float x = Mathf.Sin(phase), y = Mathf.Cos(phase);
            float radius = Mathf.Min(r.width * .5f / Mathf.Max(.0001f, Mathf.Abs(x)), r.height * .5f / Mathf.Max(.0001f, Mathf.Abs(y)));
            vh.AddVert(new Vector3(r.center.x + x * radius, r.center.y + y * radius), color, new Vector2(Mathf.Lerp(uv.x, uv.z, .5f + x * radius / r.width), Mathf.Lerp(uv.y, uv.w, .5f + y * radius / r.height)));
        }
    }
}
