using System.Collections.Generic;
using UnityEngine;

namespace ProjectC.Client
{
    public static class ScreenRectOverlap
    {
        private const float MinimumArea = .001f;

        private static List<Vector2> Corners(RectTransform rect)
        {
            var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            var world = new Vector3[4];
            rect.GetWorldCorners(world);
            var result = new List<Vector2>(world.Length);
            foreach (var corner in world)
            {
                result.Add(RectTransformUtility.WorldToScreenPoint(camera, corner));
            }
            return result;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static float Area(List<Vector2> polygon)
        {
            float sum = 0;
            for (int i = 0; i < polygon.Count; ++i)
            {
                sum += Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
            }
            return Mathf.Abs(sum) * .5f;
        }

        public static float Fraction(RectTransform card, RectTransform slot)
        {
            var polygon = Corners(card);
            var clip = Corners(slot);
            float originalArea = Area(polygon);
            if (originalArea < MinimumArea)
            {
                return 0;
            }
            // GetWorldCorners has a consistent clockwise winding. Convex clipping retains
            // the actual rotated/scaled screen quadrilaterals instead of their bounding boxes.
            for (int edge = 0; edge < clip.Count && polygon.Count > 0; ++edge)
            {
                Vector2 a = clip[edge];
                Vector2 direction = clip[(edge + 1) % clip.Count] - a;
                var input = polygon;
                polygon = new List<Vector2>(input.Count + 1);
                Vector2 previous = input[input.Count - 1];
                float previousSide = Cross(direction, previous - a);
                foreach (var current in input)
                {
                    float currentSide = Cross(direction, current - a);
                    bool inside = currentSide <= 0;
                    bool previousInside = previousSide <= 0;
                    if (inside != previousInside)
                    {
                        float t = previousSide / (previousSide - currentSide);
                        polygon.Add(Vector2.LerpUnclamped(previous, current, t));
                    }
                    if (inside)
                    {
                        polygon.Add(current);
                    }
                    previous = current;
                    previousSide = currentSide;
                }
            }
            return Mathf.Clamp01(Area(polygon) / originalArea);
        }
    }
}
