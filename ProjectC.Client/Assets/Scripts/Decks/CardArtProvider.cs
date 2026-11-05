using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public static class CardArtProvider
    {
        private static readonly Dictionary<int, Sprite> Artworks = new Dictionary<int, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Artworks.Clear();
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
    }
}
