using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public static class CardArtProvider
    {
        private static readonly Dictionary<int, Sprite> Artworks = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> YakouArtworks = new Dictionary<int, Sprite>();
        private static Sprite _yakouFallback;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Artworks.Clear();
            YakouArtworks.Clear();
            _yakouFallback = null;
        }

        public static Sprite Load(int definitionId)
        {
            if (definitionId <= 0)
            {
                return null;
            }
            if (!Artworks.TryGetValue(definitionId, out var sprite))
            {
                sprite = Resources.Load<Sprite>($"CardSprite/CardSprite_{definitionId}");
                Artworks.Add(definitionId, sprite);
            }
            return sprite;
        }

        public static void Apply(Image artwork, int definitionId)
        {
            artwork.sprite = Load(definitionId);
            artwork.color = artwork.sprite != null ? Color.white : new Color(.09f, .12f, .14f);
        }

        // IDs overlap GameCore, so Yakou resolves only its own copied artwork.
        public static void ApplyYakou(Image artwork, int definitionId)
        {
            if (!YakouArtworks.TryGetValue(definitionId, out var authored))
            {
                authored = Resources.Load<Sprite>($"YakouCardSprite/CardSprite_{definitionId}");
                YakouArtworks.Add(definitionId, authored);
            }
            artwork.sprite = authored != null ? authored : YakouFallback();
            artwork.color = Color.white;
        }

        private static Sprite YakouFallback()
        {
            if (_yakouFallback != null)
            {
                return _yakouFallback;
            }
            const int width = 192;
            const int height = 288;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    float vertical = (float)y / (height - 1);
                    float horizontal = (float)x / (width - 1);
                    Color ink = Color.Lerp(new Color(.38f, .18f, .23f), new Color(.23f, .43f, .43f), vertical);
                    float moon = new Vector2((horizontal - .5f) * width / (float)height, vertical - .62f).magnitude;
                    if (moon < .16f)
                    {
                        ink = Color.Lerp(ink, new Color(.98f, .78f, .47f), 1 - Mathf.SmoothStep(.13f, .16f, moon));
                    }
                    float ridge = .24f + .08f * Mathf.Sin(horizontal * 8f) + .04f * Mathf.Sin(horizontal * 17f);
                    if (vertical < ridge)
                    {
                        ink = Color.Lerp(new Color(.18f, .22f, .27f), ink, vertical / ridge * .25f);
                    }
                    texture.SetPixel(x, y, ink);
                }
            }
            texture.Apply();
            _yakouFallback = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f));
            return _yakouFallback;
        }
    }
}
