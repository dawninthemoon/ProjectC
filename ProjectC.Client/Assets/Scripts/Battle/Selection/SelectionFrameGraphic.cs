using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    // Resolution-independent flat UI shapes. No imported texture, bloom or bevel.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SelectionFrameGraphic : MaskableGraphic
    {
        public enum FrameShape { Panel, Ribbon, Check, Eye, Shield, Flower }
        public FrameShape Shape;
        public Color Border = new Color(.78f, .52f, .34f, 1);
        public float BorderWidth = 2;
        public bool EyeClosed;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0)
            {
                return;
            }
            Vector2 center = rect.center;
            if (Shape == FrameShape.Flower)
            {
                for (int petal = 0; petal < 5; ++petal)
                {
                    float angle = petal * Mathf.PI * 2 / 5;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Disc(vh, center + Vector2.Scale(rect.size, direction) * .24f, rect.size * .23f, color);
                }
                Disc(vh, center, rect.size * .12f, Border);
                return;
            }
            Vector2[] points;
            if (Shape == FrameShape.Check)
            {
                const int segments = 32;
                points = new Vector2[segments];
                for (int i = 0; i < segments; ++i)
                {
                    float angle = i * Mathf.PI * 2 / segments;
                    points[i] = center + Vector2.Scale(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), rect.size * .5f);
                }
            }
            else if (Shape == FrameShape.Eye)
            {
                points = new Vector2[32];
                for (int i = 0; i < points.Length; ++i)
                {
                    float angle = i * Mathf.PI * 2 / points.Length;
                    float sine = Mathf.Sin(angle);
                    points[i] = center + Vector2.Scale(rect.size, new Vector2(Mathf.Cos(angle) * .5f, sine * Mathf.Abs(sine) * .5f));
                }
            }
            else if (Shape == FrameShape.Shield)
            {
                points = new[] { new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMax, rect.yMax),
                    new Vector2(rect.xMax, center.y), new Vector2(center.x, rect.yMin), new Vector2(rect.xMin, center.y) };
            }
            else
            {
                float cut = Shape == FrameShape.Ribbon ? rect.height * .42f : Mathf.Min(14, rect.height * .2f);
                points = new[] { new Vector2(rect.xMin + cut, rect.yMax), new Vector2(rect.xMax - cut, rect.yMax),
                    new Vector2(rect.xMax, rect.yMax - cut), new Vector2(rect.xMax, rect.yMin + cut),
                    new Vector2(rect.xMax - cut, rect.yMin), new Vector2(rect.xMin + cut, rect.yMin),
                    new Vector2(rect.xMin, rect.yMin + cut), new Vector2(rect.xMin, rect.yMax - cut) };
            }
            Polygon(vh, points, color);
            Vector2 inset = new Vector2(Mathf.Max(0, 1 - BorderWidth * 2 / rect.width),
                Mathf.Max(0, 1 - BorderWidth * 2 / rect.height));
            for (int i = 0; i < points.Length; ++i)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Length];
                Quad(vh, a, b, center + Vector2.Scale(b - center, inset), center + Vector2.Scale(a - center, inset), Border);
            }
            if (Shape == FrameShape.Check)
            {
                Stroke(vh, center + Vector2.Scale(rect.size, new Vector2(-.23f, 0)),
                    center + Vector2.Scale(rect.size, new Vector2(-.05f, -.18f)), rect.width * .09f, Color.white);
                Stroke(vh, center + Vector2.Scale(rect.size, new Vector2(-.05f, -.18f)),
                    center + Vector2.Scale(rect.size, new Vector2(.26f, .2f)), rect.width * .09f, Color.white);
            }
            if (Shape == FrameShape.Eye)
            {
                Disc(vh, center, Vector2.one * rect.height * .16f, Border);
                if (EyeClosed)
                {
                    Stroke(vh, center - rect.size * .3f, center + rect.size * .3f, 3, Border);
                }
            }
        }

        private static void Disc(VertexHelper vh, Vector2 center, Vector2 radius, Color tint)
        {
            var points = new Vector2[20];
            for (int i = 0; i < points.Length; ++i)
            {
                float angle = i * Mathf.PI * 2 / points.Length;
                points[i] = center + Vector2.Scale(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), radius);
            }
            Polygon(vh, points, tint);
        }

        private static void Polygon(VertexHelper vh, Vector2[] points, Color tint)
        {
            int start = vh.currentVertCount;
            foreach (Vector2 point in points)
            {
                vh.AddVert(point, tint, Vector2.zero);
            }
            for (int i = 1; i < points.Length - 1; ++i)
            {
                vh.AddTriangle(start, start + i, start + i + 1);
            }
        }

        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero);
            vh.AddVert(b, tint, Vector2.zero);
            vh.AddVert(c, tint, Vector2.zero);
            vh.AddVert(d, tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private static void Stroke(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            Quad(vh, a - normal, b - normal, b + normal, a + normal, tint);
        }
    }
}
