using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DefenseBezierGraphic : MaskableGraphic
    {
        private const int Segments = 64;
        private Vector2 _from, _to;
        public float Width = 5;

        public void SetEndpoints(Vector3 fromWorld, Vector3 toWorld)
        {
            _from = rectTransform.InverseTransformPoint(fromWorld);
            _to = rectTransform.InverseTransformPoint(toWorld);
            SetVerticesDirty();
        }

        public static Vector2 Evaluate(Vector2 from, Vector2 to, float t)
        {
            float bend = Mathf.Clamp(Mathf.Abs(to.y - from.y) * .65f, 40, 170);
            // Flatten continuously while the pointer crosses the source row instead of flipping the curve.
            float direction = Mathf.Clamp((to.y - from.y) / 40, -1, 1);
            Vector2 a = from + Vector2.up * bend * direction;
            Vector2 b = to - Vector2.up * bend * direction;
            float u = 1 - t;
            return u * u * u * from + 3 * u * u * t * a + 3 * u * t * t * b + t * t * t * to;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            DrawCurve(vh, Width + 7, new Color(1, .36f, .08f, .22f));
            DrawCurve(vh, Width, color);
            Vector2 tangent = (_to - Evaluate(_from, _to, .96f)).normalized;
            Vector2 side = new Vector2(-tangent.y, tangent.x);
            int start = vh.currentVertCount;
            vh.AddVert(_to, color, Vector2.zero);
            vh.AddVert(_to - tangent * 18 + side * 10, color, Vector2.zero);
            vh.AddVert(_to - tangent * 18 - side * 10, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
        }

        private void DrawCurve(VertexHelper vh, float width, Color tint)
        {
            int start = vh.currentVertCount;
            for (int i = 0; i <= Segments; ++i)
            {
                float t = (float)i / Segments;
                Vector2 point = Evaluate(_from, _to, t);
                Vector2 tangent = Evaluate(_from, _to, Mathf.Min(1, t + .005f))
                    - Evaluate(_from, _to, Mathf.Max(0, t - .005f));
                Vector2 normal = new Vector2(-tangent.y, tangent.x).normalized * width * .5f;
                vh.AddVert(point - normal, tint, Vector2.zero);
                vh.AddVert(point + normal, tint, Vector2.zero);
                if (i > 0)
                {
                    int offset = start + i * 2;
                    vh.AddTriangle(offset - 2, offset - 1, offset);
                    vh.AddTriangle(offset, offset - 1, offset + 1);
                }
            }
        }
    }
}
