using UnityEngine;
using UnityEngine.UI;

namespace AishiKeys
{
    public sealed class AishiVariantDividers : MaskableGraphic
    {
        private int _count;
        internal void Set(int count)
        {
            _count = count;
            color = new Color(1, 1, 1, .9f);
            raycastTarget = false;
            SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_count < 2)
                return;
            Rect r = GetPixelAdjustedRect();
            Vector2 c = r.center;
            for (int i = 0; i < _count; i++)
            {
                float a = Mathf.PI * .5f - 2 * Mathf.PI * i / _count;
                Vector2 direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float radius = Mathf.Min(r.width * .5f / Mathf.Max(.0001f, Mathf.Abs(direction.x)), r.height * .5f / Mathf.Max(.0001f, Mathf.Abs(direction.y)));
                Vector2 end = c + direction * (radius - .5f);
                Vector2 n = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * .65f;
                Quad(vh, c - n, c + n, end + n, end - n);
            }

            Quad(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMin + 1, r.yMax), new Vector2(r.xMin + 1, r.yMin));
            Quad(vh, new Vector2(r.xMax - 1, r.yMin), new Vector2(r.xMax - 1, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin));
            Quad(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMin + 1), new Vector2(r.xMin, r.yMin + 1));
            Quad(vh, new Vector2(r.xMin, r.yMax - 1), new Vector2(r.xMax, r.yMax - 1), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));
        }

        private void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(c, color, Vector2.zero);
            vh.AddVert(d, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
