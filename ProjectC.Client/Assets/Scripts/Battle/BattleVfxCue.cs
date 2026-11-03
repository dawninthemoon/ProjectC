using Game.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    // A normalized, stateless visual cue. The existing presentation queue owns time.
    public sealed class BattleVfxCue : MonoBehaviour
    {
        private const int SparkCount = 8;
        private RawImage _core;
        private RawImage _ring;
        private RawImage _flash;
        private readonly RawImage[] _sparks = new RawImage[SparkCount];
        private static readonly Color Spirit = new Color(.28f, 1f, .84f);
        private static readonly Color Gold = new Color(1f, .79f, .39f);

        public void Initialize()
        {
            if (_core != null)
            {
                return;
            }
            if (!this.Bind("Core", out _core) || !this.Bind("Ring", out _ring) || !this.Bind("Flash", out _flash))
            {
                throw new System.InvalidOperationException("Summon VFX requires Core, Ring and Flash RawImages.");
            }
            for (int i = 0; i < SparkCount; ++i)
            {
                if (!this.Bind("Spark" + i, out _sparks[i]))
                {
                    throw new System.InvalidOperationException("Summon VFX is missing Spark" + i);
                }
            }
        }

        public void Sample(Vector3 center, float progress)
        {
            Initialize();
            transform.position = center;
            progress = Mathf.Clamp01(progress);
            float gather = Mathf.Clamp01(progress / .4f);
            float burst = Mathf.InverseLerp(.4f, .9f, progress);
            float ring = Mathf.InverseLerp(.18f, .9f, progress);
            Paint(_ring, Spirit, Mathf.Sin(ring * Mathf.PI) * .65f, Mathf.Lerp(35, 320, 1 - (1 - ring) * (1 - ring)));
            _ring.rectTransform.localScale = new Vector3(1, .65f, 1);
            _ring.rectTransform.anchoredPosition = new Vector2(0, -45);
            Paint(_core, Color.Lerp(Spirit, Gold, gather), progress < .4f ? gather * .75f : (1 - burst) * .9f,
                progress < .4f ? Mathf.Lerp(15, 100, gather) : Mathf.Lerp(100, 270, burst));
            float flash = Mathf.InverseLerp(.38f, .76f, progress);
            Paint(_flash, Gold, Mathf.Sin(flash * Mathf.PI) * .85f, Mathf.Lerp(100, 340, flash));
            _flash.rectTransform.localRotation = Quaternion.Euler(0, 0, progress * 35);
            for (int i = 0; i < SparkCount; ++i)
            {
                float angle = (i * Mathf.PI * 2 / SparkCount) + progress * .35f;
                float radius = progress < .4f ? Mathf.Lerp(150, 12, gather * gather) : Mathf.Lerp(12, 165, burst);
                var spark = _sparks[i];
                spark.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * .75f) * radius;
                Paint(spark, Color.Lerp(Spirit, Gold, gather), progress < .4f ? Mathf.Sin(gather * Mathf.PI * .5f) : (1 - burst) * .7f,
                    progress < .4f ? Mathf.Lerp(6, 14, gather) : Mathf.Lerp(14, 3, burst));
            }
        }

        private static void Paint(RawImage image, Color color, float alpha, float size)
        {
            color.a = Mathf.Clamp01(alpha);
            image.color = color;
            image.rectTransform.sizeDelta = Vector2.one * size;
        }
    }
}
