using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace ProjectC.Client
{
    // The RectTransform defines the crop window; only the sampled UVs change.
    [AddComponentMenu("UI/Aspect Fill Image")]
    public sealed class AspectFillImage : Image
    {
        [SerializeField, Min(1f)] private float _cropZoom = 1f;
        [SerializeField] private Vector2 _cropOffset;

        public void SetCrop(float zoom, Vector2 offset)
        {
            zoom = Mathf.Max(1f, zoom);
            if (Mathf.Approximately(_cropZoom, zoom) && _cropOffset == offset)
            {
                return;
            }
            _cropZoom = zoom;
            _cropOffset = offset;
            SetVerticesDirty();
        }

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

            float halfU = (uv.z - uv.x) * .5f / _cropZoom;
            float halfV = (uv.w - uv.y) * .5f / _cropZoom;
            var outer = DataUtility.GetOuterUV(artwork);
            float centerU = Mathf.Clamp((uv.x + uv.z) * .5f + _cropOffset.x * (outer.z - outer.x), outer.x + halfU, outer.z - halfU);
            float centerV = Mathf.Clamp((uv.y + uv.w) * .5f + _cropOffset.y * (outer.w - outer.y), outer.y + halfV, outer.w - halfV);
            uv.x = centerU - halfU;
            uv.z = centerU + halfU;
            uv.y = centerV - halfV;
            uv.w = centerV + halfV;

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
