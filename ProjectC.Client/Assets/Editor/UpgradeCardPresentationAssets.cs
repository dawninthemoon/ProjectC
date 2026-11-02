using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Client.Editor
{
    // Explicit, repeatable migration of existing prefabs. Does not rebuild scenes.
    public static class UpgradeCardPresentationAssets
    {
        private const string BattleFolder = "Assets/Prefabs/Battle/";
        private const string FrontEndFolder = "Assets/Prefabs/FrontEnd/";
        private const float CardHeightPerWidth = 244f / 180f;

        [MenuItem("ProjectC/Upgrade Card Presentation")]
        public static void Upgrade()
        {
            Edit(BattleFolder + "BattleCard.prefab", root => ConfigureBattle(root, false));
            Edit(BattleFolder + "CardBack.prefab", root => ConfigureBattle(root, true));
            Edit(FrontEndFolder + "DeckTile.prefab", root => ConfigureTile(root, 150));
            Edit(FrontEndFolder + "DeckEditorScreen.prefab", ConfigureEditor);
            Edit(BattleFolder + "BattleScreen.prefab", ConfigureBattleScreen);
            AssetDatabase.SaveAssets();
            Debug.Log("Card presentation prefabs upgraded.");
        }

        private static void Edit(string path, Action<GameObject> edit)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Transform Find(GameObject root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        }

        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void Fixed(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image Artwork(GameObject root, Transform original, float top, float bottom)
        {
            var mask = Find(root, "ArtworkMask") as RectTransform;
            if (mask == null)
            {
                int sibling = original.GetSiblingIndex();
                mask = NewRect("ArtworkMask", original.parent);
                mask.SetSiblingIndex(sibling);
                mask.gameObject.AddComponent<RectMask2D>();
                original.SetParent(mask, false);
            }
            Stretch(mask, 2, top, 2, bottom);
            original.name = "ArtworkImage";
            var image = original.GetComponent<Image>();
            if (image is not AspectFillImage)
            {
                Object.DestroyImmediate(image);
                image = original.gameObject.AddComponent<AspectFillImage>();
            }
            image.raycastTarget = false;
            image.color = Color.white;
            Stretch((RectTransform)original, 0, 0, 0, 0);
            return image;
        }

        private static void Badge(GameObject root, string name, TMP_Text template, Transform parent, bool right, float scale)
        {
            var textTransform = Find(root, name);
            var text = textTransform != null ? textTransform.GetComponent<TMP_Text>() : Object.Instantiate(template, parent);
            text.name = name;
            var background = Find(root, name + "Frame") as RectTransform;
            if (background == null)
            {
                background = NewRect(name + "Frame", parent);
                var image = background.gameObject.AddComponent<Image>();
                image.color = right ? new Color(.66f, .12f, .16f) : new Color(.06f, .30f, .68f);
                image.raycastTarget = false;
                var outline = background.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(.83f, .71f, .43f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                text.transform.SetParent(background, false);
            }
            var anchor = new Vector2(right ? 1 : 0, 0);
            Fixed(background, anchor, anchor, new Vector2(right ? -3 : 3, 3) * scale, new Vector2(36, 36) * scale);
            background.SetAsLastSibling();
            Stretch(text.rectTransform, 0, 0, 0, 0);
            text.text = right ? "5" : "3";
            text.color = Color.white;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 16 * scale;
            text.fontSizeMax = 26 * scale;
            text.raycastTarget = false;
            text.gameObject.SetActive(true);
        }

        public static void ConfigureBattle(GameObject root, bool back)
        {
            var widget = root.GetComponent<CardWidget>();
            widget.Face.pivot = new Vector2(.5f, .5f);
            widget.Face.anchoredPosition = Vector2.zero;
            ((RectTransform)root.transform).sizeDelta = new Vector2(back ? 68 : 180, (back ? 68 : 180) * CardHeightPerWidth);
            widget.ArtworkImage = Artwork(root, widget.ArtworkImage.transform, back ? 2 : 32, 2);
            widget.Stats.gameObject.SetActive(false);
            widget.Status.gameObject.SetActive(false);
            widget.Stats.text = widget.Status.text = "";
            if (!back)
            {
                var title = widget.Title.rectTransform;
                Fixed(title, new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(164, 32));
                widget.Title.alignment = TextAlignmentOptions.Center;
                widget.Title.fontSizeMin = 12;
                widget.Title.fontSizeMax = 19;
                Badge(root, "AttackBadge", widget.Stats, widget.Title.transform.parent, false, 1.2f);
                Badge(root, "HealthBadge", widget.Stats, widget.Title.transform.parent, true, 1.2f);
            }
        }

        public static void ConfigureTile(GameObject root, float width)
        {
            float scale = width / 150f;
            ((RectTransform)root.transform).sizeDelta = new Vector2(width, width * CardHeightPerWidth);
            var art = Find(root, "ArtworkImage") ?? Find(root, "Placeholder");
            Artwork(root, art, 31 * scale, 2);
            var name = Find(root, "CardName").GetComponent<TMP_Text>();
            Fixed(name.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(width - 10, 30 * scale));
            name.alignment = TextAlignmentOptions.Center;
            name.fontSizeMin = 11 * scale;
            name.fontSizeMax = 17 * scale;
            name.enableAutoSizing = true;
            var stats = Find(root, "CardStats").GetComponent<TMP_Text>();
            stats.gameObject.SetActive(false);
            Find(root, "CardKind").gameObject.SetActive(false);
            Badge(root, "AttackBadge", stats, root.transform, false, scale);
            Badge(root, "HealthBadge", stats, root.transform, true, scale);
            var count = Find(root, "CardCount").GetComponent<TMP_Text>();
            Fixed(count.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 8 * scale), new Vector2(60, 22) * scale);
            count.alignment = TextAlignmentOptions.Center;
            count.fontSize = 13 * scale;
            count.color = Color.white;
            if (count.GetComponent<Shadow>() == null)
            {
                count.gameObject.AddComponent<Shadow>();
            }
            count.transform.SetAsLastSibling();
            var border = Find(root, "Border") as RectTransform;
            border.anchorMin = Vector2.zero;
            border.anchorMax = new Vector2(0, 1);
            border.offsetMin = Vector2.zero;
            border.offsetMax = new Vector2(2, 0);
        }

        public static void ConfigureBattleScreen(GameObject root)
        {
            var hand = (RectTransform)Find(root, "YourHand");
            Fixed(hand, new Vector2(0, 1), new Vector2(0, 1), new Vector2(420, -780), new Vector2(1200, 244));
            var caption = Find(root, "HandCaption").GetComponent<TMP_Text>();
            caption.text = "YOUR HAND";
            Fixed(caption.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(660, -748), new Vector2(700, 28));
        }

        public static void ConfigureEditor(GameObject root)
        {
            foreach (var grid in root.GetComponentsInChildren<GridLayoutGroup>(true))
            {
                grid.cellSize = new Vector2(grid.cellSize.x, grid.cellSize.x * CardHeightPerWidth);
            }
            var preview = Find(root, "DetailCard");
            if (preview == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FrontEndFolder + "DeckTile.prefab");
                preview = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, Find(root, "DetailPanel").parent)).transform;
                preview.name = "DetailCard";
            }
            preview.SetSiblingIndex(Find(root, "DetailPanel").GetSiblingIndex() + 1);
            ConfigureTile(preview.gameObject, 250);
            Fixed((RectTransform)preview, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -240), new Vector2(250, 250 * CardHeightPerWidth));
            Find(root, "DetailCrest").gameObject.SetActive(false);
            Find(root, "DetailName").gameObject.SetActive(false);
            Fixed((RectTransform)Find(root, "DetailStats"), new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -590), new Vector2(250, 50));
            var content = Find(root, "DetailContent");
            // DetailContent is the scroll content; move its viewport root, preserving scrolling.
            var scroll = content.GetComponentInParent<ScrollRect>();
            if (scroll != null)
            {
                Fixed((RectTransform)scroll.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -650), new Vector2(250, 240));
            }
        }
    }
}
