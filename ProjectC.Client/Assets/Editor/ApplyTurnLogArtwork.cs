using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    public static class ApplyTurnLogArtwork
    {
        private const string Art = "Assets/Art/UI/BattleTurnLog/";

        [MenuItem("Project C/Apply turn HUD and popup log artwork")]
        public static void Apply()
        {
            foreach (var file in Directory.GetFiles(Art, "*.png"))
            {
                AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(file);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            var row = BuildRow();
            var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Battle/BattleScreen.prefab");
            try
            {
                var ui = root.GetComponent<BattlePresenter>();
                TurnHud(ui);
                LogPopup(ui);
                var data = new SerializedObject(ui.Log);
                data.FindProperty("_itemPrefab").objectReferenceValue = row;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Battle/BattleScreen.prefab");
                AssetDatabase.SaveAssets();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void TurnHud(BattlePresenter ui)
        {
            var hud = Find(ui.transform, "TurnHud");
            var background = hud.GetComponent<Image>();
            background.sprite = Sprite("ProjectC_TurnHUD_Textless");
            background.type = Image.Type.Simple;
            background.preserveAspect = true;
            background.raycastTarget = false;
            Place(hud, 12, 3, 342, 114);
            foreach (string name in new[] { "TurnSocket", "TimeSocket" })
            {
                Find(hud, name).gameObject.SetActive(false);
            }
            // Coordinates measured in the supplied 2172x724 art, uniformly scaled to 342x114.
            const float scale = 342f / 2172f;
            var track = Graphic(hud, "TimeTrack", Sprite("TimerAnnulus"), false);
            Place(track.rectTransform, 633 * scale, 83 * scale, 534 * scale, 534 * scale);
            track.color = new Color(.022f, .16f, .17f, 1);
            track.transform.SetAsFirstSibling();
            var ring = Find(hud, "TimeRing").GetComponent<Image>();
            ring.sprite = Sprite("TimerAnnulus");
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.fillAmount = 0;
            ring.raycastTarget = false;
            Place(ring.rectTransform, 633 * scale, 83 * scale, 534 * scale, 534 * scale);
            ring.transform.SetSiblingIndex(1);
            // Each number/unit follows the visible circular well rather than the PNG bounds.
            Place(ui.TurnLabel.rectTransform, 185 * scale, 210 * scale, 245 * scale, 210 * scale);
            Style(ui.TurnLabel, BattleFontRole.Number, 34, 18);
            var unit = Find(hud, "TurnUnit").GetComponent<TMP_Text>();
            Place(unit.rectTransform, 190 * scale, 400 * scale, 230 * scale, 80 * scale);
            Style(unit, BattleFontRole.Hud, 12, 10);
            var time = Find(hud, "TurnTimer").GetComponent<TMP_Text>();
            Place(time.rectTransform, 715 * scale, 225 * scale, 370 * scale, 240 * scale);
            Style(time, BattleFontRole.Number, 32, 18);
            var timeUnit = Find(hud, "TimeUnit").GetComponent<TMP_Text>();
            Place(timeUnit.rectTransform, 775 * scale, 440 * scale, 250 * scale, 105 * scale);
            Style(timeUnit, BattleFontRole.Hud, 14, 10);
            var accent = Graphic(hud, "TurnOwnerAccent", Sprite("TurnStatusWash"), false);
            Place(accent.rectTransform, 1210 * scale, 220 * scale, 790 * scale, 280 * scale);
            var owner = Label(hud, "TurnOwner", 1210 * scale, 240 * scale, 790 * scale, 220 * scale, 28, BattleFontRole.Heading);
            owner.transform.SetAsLastSibling();
            var glow = owner.GetComponent<Shadow>() ?? owner.gameObject.AddComponent<Shadow>();
            glow.effectDistance = new Vector2(1.5f, -1.5f);
            glow.effectColor = new Color(.05f, .8f, .65f, .8f);
            owner.text = "--";
        }

        private static void LogPopup(BattlePresenter ui)
        {
            var panel = (RectTransform)ui.Log.History.transform;
            panel.gameObject.SetActive(true);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(640, 800);
            panel.SetAsLastSibling();
            var image = panel.GetComponent<Image>();
            image.sprite = Sprite("ProjectC_BattleLog_PopupFrame");
            image.color = Color.white;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = true;
            var group = panel.GetComponent<CanvasGroup>();
            group.alpha = 0;
            group.blocksRaycasts = group.interactable = false;
            Find(panel, "Header").gameObject.SetActive(false);
            Label(panel, "LogPopupTitle", 86, 58, 426, 50, 32, BattleFontRole.Heading);
            var close = Find(panel, "CloseLog");
            Place(close, 522, 57, 62, 62);
            close.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            close.GetComponent<Image>().sprite = null;
            var closeLabel = close.GetComponentInChildren<TMP_Text>(true);
            closeLabel.gameObject.SetActive(true);
            Place(closeLabel.rectTransform, 5, 5, 52, 52);
            Style(closeLabel, BattleFontRole.Hud, 34, 24);
            closeLabel.text = "×";
            var buttonColors = close.GetComponent<Button>().colors;
            buttonColors.highlightedColor = new Color(1, .84f, .42f);
            close.GetComponent<Button>().colors = buttonColors;
            var viewport = Find(panel, "Viewport");
            Place(viewport, 44, 146, 552, 598);
            var viewportImage = viewport.GetComponent<Image>();
            if (viewportImage != null)
            {
                viewportImage.color = Color.clear;
                viewportImage.raycastTarget = true;
            }
            var mask = viewport.GetComponent<Mask>();
            if (mask != null)
            {
                UnityEngine.Object.DestroyImmediate(mask);
            }
            if (viewport.GetComponent<RectMask2D>() == null)
            {
                viewport.gameObject.AddComponent<RectMask2D>();
            }
            // Retain the legacy text field for tools, outside the dynamic layout and invisible.
            ui.Log.Full.transform.SetParent(panel, false);
            ui.Log.Full.gameObject.SetActive(false);
            var content = Rect(viewport, "LogContent");
            Place(content, 0, 0, 552, 1);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.sizeDelta = new Vector2(0, 1);
            var layout = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 8);
            layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fit = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = panel.GetComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 38;
            var barImage = Graphic(panel, "LogScrollbar", null, true);
            Place(barImage.rectTransform, 600, 152, 8, 586);
            barImage.color = new Color(.02f, .12f, .13f, 1);
            var bar = barImage.GetComponent<Scrollbar>() ?? barImage.gameObject.AddComponent<Scrollbar>();
            var handle = Graphic(barImage.transform, "Handle", null, true);
            handle.rectTransform.anchorMin = Vector2.zero;
            handle.rectTransform.anchorMax = Vector2.one;
            handle.rectTransform.offsetMin = handle.rectTransform.offsetMax = Vector2.zero;
            handle.color = new Color(.76f, .61f, .29f, .9f);
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            var empty = Label(panel, "LogEmpty", 66, 320, 508, 100, 24, BattleFontRole.Body);
            empty.textWrappingMode = TextWrappingModes.Normal;
            ui.Log.GetComponent<Image>().enabled = false;
            foreach (Transform child in ui.Log.transform)
            {
                child.gameObject.SetActive(false);
            }
            panel.gameObject.SetActive(false);
        }

        private static BattleLogItemView BuildRow()
        {
            var background = Graphic(null, "BattleLogItem", null, true);
            var root = background.rectTransform;
            Place(root, 0, 0, 552, 96);
            var element = root.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 96;
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.highlightedColor = new Color(.3f, .66f, .6f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            button.colors = colors;
            var actor = Label(root, "LogActor", 14, 7, 380, 22, 16, BattleFontRole.Hud);
            actor.alignment = TextAlignmentOptions.Left;
            var message = Label(root, "LogMessage", 14, 32, 524, 50, 20, BattleFontRole.Body);
            message.alignment = TextAlignmentOptions.MidlineLeft;
            message.textWrappingMode = TextWrappingModes.Normal;
            message.enableAutoSizing = false;
            message.overflowMode = TextOverflowModes.Ellipsis;
            message.rectTransform.anchorMin = new Vector2(0, 1);
            message.rectTransform.anchorMax = new Vector2(1, 1);
            message.rectTransform.sizeDelta = new Vector2(-28, 50);
            var hint = Label(root, "LogCardHint", 410, 7, 126, 22, 14, BattleFontRole.Hud);
            hint.alignment = TextAlignmentOptions.Right;
            root.gameObject.AddComponent<BattleLogItemView>();
            var asset = PrefabUtility.SaveAsPrefabAsset(root.gameObject, "Assets/Prefabs/Battle/BattleLogItem.prefab");
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            return asset.GetComponent<BattleLogItemView>();
        }

        private static TMP_Text Label(Transform parent, string name, float x, float y, float w, float h, int size, BattleFontRole role)
        {
            var rect = Rect(parent, name);
            var text = rect.GetComponent<TextMeshProUGUI>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
            Place(rect, x, y, w, h);
            Style(text, role, size, Math.Min(size, 12));
            return text;
        }

        private static void Style(TMP_Text text, BattleFontRole role, int size, int minimum)
        {
            text.fontSize = text.fontSizeMax = size;
            text.fontSizeMin = minimum;
            text.enableAutoSizing = true;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.margin = Vector4.zero;
            LocalizedBattleFont.SetRole(text, role);
        }

        private static Image Graphic(Transform parent, string name, Sprite sprite, bool raycast)
        {
            var rect = Rect(parent, name);
            var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = raycast;
            return image;
        }

        private static RectTransform Rect(Transform parent, string name)
        {
            var found = parent != null ? parent.Find(name) : null;
            var rect = found != null ? (RectTransform)found : new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            if (found == null && parent != null)
            {
                rect.SetParent(parent, false);
            }
            return rect;
        }

        private static RectTransform Find(Transform root, string name) => (RectTransform)root.GetComponentsInChildren<Transform>(true).First(value => value.name == name);
        private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
