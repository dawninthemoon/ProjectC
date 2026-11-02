using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace ProjectC.Client
{
    // The RectTransform defines the crop window; only the sampled UVs change.
    [AddComponentMenu("UI/Aspect Fill Image")]
    public sealed class AspectFillImage : Image
    {
        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            var artwork = overrideSprite;
            var rect = GetPixelAdjustedRect();
            if (artwork == null || rect.width <= 0 || rect.height <= 0)
            {
                base.OnPopulateMesh(vertexHelper);
                return;
            }

            var uv = DataUtility.GetOuterUV(artwork);
            float sourceAspect = artwork.rect.width / artwork.rect.height;
            float targetAspect = rect.width / rect.height;
            if (sourceAspect > targetAspect)
            {
                float center = (uv.x + uv.z) * .5f;
                float halfWidth = (uv.z - uv.x) * .5f * targetAspect / sourceAspect;
                uv.x = center - halfWidth;
                uv.z = center + halfWidth;
            }
            else
            {
                float center = (uv.y + uv.w) * .5f;
                float halfHeight = (uv.w - uv.y) * .5f * sourceAspect / targetAspect;
                uv.y = center - halfHeight;
                uv.w = center + halfHeight;
            }

            vertexHelper.Clear();
            vertexHelper.AddVert(new Vector3(rect.xMin, rect.yMin), color, new Vector2(uv.x, uv.y));
            vertexHelper.AddVert(new Vector3(rect.xMin, rect.yMax), color, new Vector2(uv.x, uv.w));
            vertexHelper.AddVert(new Vector3(rect.xMax, rect.yMax), color, new Vector2(uv.z, uv.w));
            vertexHelper.AddVert(new Vector3(rect.xMax, rect.yMin), color, new Vector2(uv.z, uv.y));
            vertexHelper.AddTriangle(0, 1, 2);
            vertexHelper.AddTriangle(2, 3, 0);
        }
    }
}
