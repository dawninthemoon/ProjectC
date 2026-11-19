using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    public static class ApplyYakouCardInspector
    {
        private const string ScreenPath = "Assets/Prefabs/Battle/BattleScreen.prefab";
        private const string CardPath = "Assets/Prefabs/Battle/BattleCard.prefab";
        private const string ArtPath = "Assets/Art/UI/YakouInspector/";

        public static void Apply()
        {
            Sprite background = Import("CardInspector_Background.png", new Vector4(24, 24, 24, 24));
            Sprite thumbnail = Import("CardInspector_ThumbnailFrame.png", new Vector4(18, 18, 18, 18));
            Sprite divider = Import("CardInspector_Divider.png", Vector4.zero);
            Sprite inset = Import("CardInspector_DescriptionInset.png", new Vector4(12, 12, 12, 12));
            Sprite attack = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/SunsetCel/png/AttackBadge.png");
            Sprite health = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/SunsetCel/png/HealthBadge.png");
            GameObject cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardPath);
            if (attack == null || health == null || cardPrefab == null)
            {
                throw new InvalidOperationException("Card inspector requires the existing BattleCard badges.");
            }

            GameObject root = PrefabUtility.LoadPrefabContents(ScreenPath);
            try
            {
                RectTransform panel = (RectTransform)Find(root.transform, "CardInspectorImage");
                RectTransform space = (RectTransform)panel.parent;
                Place(space, 174, -260, 468, 239);
                Place(panel, 0, 0, 720, 368);
                panel.localScale = Vector3.one * 0.65f;
                foreach (string name in new[]
                {
                    "InspectorThumbnailFrame", "InspectorStats", "InspectorDivider",
                    "InspectorDescriptionInset", "InspectorScrollbar",
                })
                {
                    Transform existing = panel.Find(name);
                    if (existing != null)
                    {
                        UnityEngine.Object.DestroyImmediate(existing.gameObject);
                    }
                }
                Image panelImage = panel.GetComponent<Image>();
                panelImage.sprite = background;
                panelImage.type = Image.Type.Sliced;
                panelImage.color = Color.white;

                RectTransform artwork = (RectTransform)Find(panel, "ArtworkMask");
                Place(artwork, 34, -33, 104, 104);
                RectTransform placeholder = (RectTransform)Find(artwork, "ArtPlaceholderText");
                Place(placeholder, 0, 0, 104, 104);
                Image thumbnailImage = Decor(panel, "InspectorThumbnailFrame", thumbnail, 25, -24, 122, 122,
                    Image.Type.Sliced);
                thumbnailImage.transform.SetSiblingIndex(artwork.GetSiblingIndex() + 1);

                TMP_Text title = Find(panel, "NameText").GetComponent<TMP_Text>();
                Place(title.rectTransform, 175, -38, 290, 49);
                title.fontSize = 30;
                title.fontSizeMin = 23;
                title.fontSizeMax = 30;
                title.enableAutoSizing = true;
                title.textWrappingMode = TextWrappingModes.Normal;
                title.alignment = TextAlignmentOptions.MidlineLeft;
                title.GetComponent<LocalizedBattleFont>().UseKoreanGlyphFallback = true;
                TMP_Text type = Find(panel, "CardTypeText").GetComponent<TMP_Text>();
                Place(type.rectTransform, 175, -95, 290, 42);
                type.fontSize = 24;
                type.fontSizeMin = 19;
                type.fontSizeMax = 24;
                type.enableAutoSizing = true;
                type.textWrappingMode = TextWrappingModes.Normal;
                type.alignment = TextAlignmentOptions.MidlineLeft;
                type.GetComponent<LocalizedBattleFont>().UseKoreanGlyphFallback = true;
                Find(panel, "StatsText").gameObject.SetActive(false);

                RectTransform stats = NewRect(panel, "InspectorStats", 480, -60, 190, 73);
                Badge(stats, cardPrefab, "AttackValueContainer", attack, 0);
                Badge(stats, cardPrefab, "HealthValueContainer", health, 88);

                Image dividerImage = Decor(panel, "InspectorDivider", divider, 26, -177, 668, 16, Image.Type.Simple);
                dividerImage.transform.SetSiblingIndex(stats.GetSiblingIndex() + 1);
                Image insetImage = Decor(panel, "InspectorDescriptionInset", inset, 26, -205, 668, 138,
                    Image.Type.Sliced);
                RectTransform viewport = (RectTransform)Find(panel, "DetailViewportImage");
                insetImage.transform.SetSiblingIndex(viewport.GetSiblingIndex());
                Place(viewport, 45, -219, 630, 109);
                Image viewportImage = viewport.GetComponent<Image>();
                viewportImage.color = Color.clear;
                ScrollRect scroll = viewport.GetComponent<ScrollRect>();
                scroll.scrollSensitivity = 40;
                RectTransform barRect = NewRect(panel, "InspectorScrollbar", 679, -222, 7, 104);
                Image barImage = barRect.gameObject.AddComponent<Image>();
                barImage.color = new Color(0.6f, 0.5f, 0.34f, 0.22f);
                barImage.raycastTarget = false;
                Scrollbar bar = barRect.gameObject.AddComponent<Scrollbar>();
                RectTransform handle = NewRect(barRect, "Handle", 0, 0, 7, 35);
                handle.anchorMin = new Vector2(0, 0);
                handle.anchorMax = new Vector2(1, 1);
                handle.sizeDelta = Vector2.zero;
                Image handleImage = handle.gameObject.AddComponent<Image>();
                handleImage.color = new Color(0.85f, 0.7f, 0.44f, 0.8f);
                bar.targetGraphic = handleImage;
                bar.handleRect = handle;
                bar.direction = Scrollbar.Direction.BottomToTop;
                scroll.verticalScrollbar = bar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                RectTransform content = (RectTransform)Find(viewport, "DetailContentImage");
                Place(content, 0, 0, 630, 18);
                VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(2, 2, 1, 1);
                layout.spacing = 8;
                foreach (string name in new[] { "RulesText", "FormsText" })
                {
                    TMP_Text text = Find(content, name).GetComponent<TMP_Text>();
                    text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 626);
                    text.fontSize = 25;
                    text.textWrappingMode = TextWrappingModes.Normal;
                    text.overflowMode = TextOverflowModes.Overflow;
                    text.alignment = TextAlignmentOptions.TopLeft;
                    text.GetComponent<LocalizedBattleFont>().UseKoreanGlyphFallback = true;
                }
                PrefabUtility.SaveAsPrefabAsset(root, ScreenPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
        }

        private static Sprite Import(string file, Vector4 border)
        {
            string path = ArtPath + file;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                throw new InvalidOperationException("Missing card inspector asset: " + path);
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                throw new InvalidOperationException("Card inspector sprite import failed: " + path);
            }
            return sprite;
        }

        private static void Badge(RectTransform parent, GameObject cardPrefab, string name, Sprite sprite, float x)
        {
            GameObject badge = UnityEngine.Object.Instantiate(Find(cardPrefab.transform, name).gameObject, parent);
            badge.name = name;
            RectTransform rect = (RectTransform)badge.transform;
            Place(rect, x, 0, 74, 73);
            Image image = badge.GetComponent<Image>() ?? badge.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            TMP_Text value = badge.GetComponentInChildren<TMP_Text>();
            value.rectTransform.anchorMin = Vector2.zero;
            value.rectTransform.anchorMax = Vector2.one;
            value.rectTransform.anchoredPosition = Vector2.zero;
            value.rectTransform.sizeDelta = Vector2.zero;
            value.fontSize = 33;
            value.fontSizeMin = 24;
            value.fontSizeMax = 33;
            value.enableAutoSizing = true;
            value.alignment = TextAlignmentOptions.Center;
        }

        private static Image Decor(RectTransform parent, string name, Sprite sprite, float x, float y,
            float width, float height, Image.Type type)
        {
            RectTransform rect = NewRect(parent, name, x, y, width, height);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = type;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform NewRect(RectTransform parent, string name, float x, float y,
            float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Place(rect, x, y, width, height);
            return rect;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }
            throw new InvalidOperationException("Missing BattleScreen UI element: " + name);
        }
    }
}
