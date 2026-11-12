using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Client.Editor
{
    public static class AuthorBattleSelection
    {
        private const string ScreenPath = "Assets/Prefabs/Battle/BattleScreen.prefab";
        private const string BarPath = "Assets/Prefabs/Battle/CardSelectionBar.prefab";
        private const string DefensePath = "Assets/Prefabs/Battle/DefenseAssignment.prefab";
        private static readonly Color Panel = new Color(.153f, .141f, .165f, .97f);
        private static readonly Color Ivory = new Color(1, 1, .97f);

        [MenuItem("ProjectC/UI/Apply Card Selection Framework")]
        public static void Apply()
        {
            CreateBar();
            CreateDefense();
            var root = PrefabUtility.LoadPrefabContents(ScreenPath);
            try
            {
                var gallery = root.GetComponentInChildren<CardGalleryView>(true);
                var old = gallery.transform.Find("CardSelectionBar");
                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                }
                var bar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BarPath), gallery.transform);
                bar.name = "CardSelectionBar";
                Stretch((RectTransform)bar.transform);
                bar.SetActive(false);
                // Keep the actual transient turn animation; remove only the old permanent center duplicate.
                var hud = root.GetComponentInChildren<BattleTurnHudView>(true);
                var hudSettings = new SerializedObject(hud);
                hudSettings.FindProperty("_showCenterStatus").boolValue = false;
                hudSettings.ApplyModifiedPropertiesWithoutUndo();
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.name == "CenterTurnOwnerText" || text.name == "CenterTurnNumberText")
                    {
                        text.gameObject.SetActive(false);
                    }
                }
                var picker = root.GetComponentInChildren<EffectTargetPicker>(true);
                picker.GetComponent<Image>().raycastTarget = false;
                var previous = root.GetComponentInChildren<DefenseAssignmentView>(true);
                if (previous != null)
                {
                    Object.DestroyImmediate(previous.gameObject);
                }
                var defense = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DefensePath), gallery.transform.parent);
                defense.name = "DefenseAssignment";
                Stretch((RectTransform)defense.transform);
                defense.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, ScreenPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            BattleSelectionPreviewWindow.AuthorScene();
            AssetDatabase.SaveAssets();
        }

        private static void CreateBar()
        {
            var root = Rect(null, "CardSelectionBar", 0, 0, 1920, 1080);
            root.gameObject.AddComponent<CardSelectionBar>();
            try
            {
                var panel = Shape(root, "SelectionPanel", 0, 53, 1114, 249, Panel);
                BottomCenter(panel);
                var guide = Shape(root, "SelectionGuide", 0, 306, 760, 40, new Color(.204f, .153f, .157f, .98f));
                BottomCenter(guide);
                guide.GetComponent<SelectionFrameGraphic>().Shape = SelectionFrameGraphic.FrameShape.Ribbon;
                Label(guide, "SelectionTitle", 16, 2, 728, 36, 25);
                // Small flat flower diamonds and fine horizontal rules reuse the UI's restrained palette.
                foreach (int direction in new[] { -1, 1 })
                {
                    var rule = Shape(guide, "GuideRule" + direction, direction < 0 ? -200 : 760, 23, 200, 2, new Color(.74f, .45f, .3f));
                    rule.GetComponent<SelectionFrameGraphic>().BorderWidth = 0;
                    rule.GetComponent<Graphic>().raycastTarget = false;
                    var flower = Shape(guide, "GuideFlower" + direction, direction < 0 ? -28 : 768, 12, 24, 24, new Color(.94f, .68f, .59f));
                    flower.GetComponent<SelectionFrameGraphic>().Shape = SelectionFrameGraphic.FrameShape.Flower;
                    flower.GetComponent<Graphic>().raycastTarget = false;
                }
                var scrollRoot = Rect(panel, "SelectionScroll", 18, 12, 870, 225);
                var scroll = scrollRoot.gameObject.AddComponent<SelectionHorizontalScrollRect>();
                scroll.horizontal = true;
                scroll.vertical = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 38;
                var viewport = Rect(scrollRoot, "SelectionViewport", 0, 0, 870, 225);
                var background = viewport.gameObject.AddComponent<Image>();
                background.color = Color.clear;
                viewport.gameObject.AddComponent<RectMask2D>();
                var content = Rect(viewport, "SelectionContent", 0, 0, 870, 225);
                content.anchorMin = new Vector2(0, 0);
                content.anchorMax = new Vector2(0, 1);
                content.pivot = new Vector2(0, .5f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = new Vector2(870, 0);
                scroll.viewport = viewport;
                scroll.content = content;
                var count = Shape(panel, "SelectionCountChip", 910, 16, 187, 44, new Color(.18f, .15f, .133f));
                Label(count, "SelectionCount", 8, 3, 171, 38, 24);
                var cancel = Shape(panel, "SelectionCancel", 910, 78, 187, 60, new Color(.188f, .173f, .192f));
                Button(cancel, new Color(.188f, .173f, .192f));
                Label(cancel, "SelectionCancelText", 10, 5, 167, 50, 29);
                var confirm = Shape(panel, "SelectionConfirm", 910, 162, 187, 63, new Color(.725f, .176f, .192f));
                Button(confirm, new Color(.725f, .176f, .192f));
                Label(confirm, "SelectionConfirmText", 10, 5, 167, 53, 29);
                var eye = Shape(root, "SelectionEye", 530, 306, 54, 34, new Color(.15f, .13f, .16f));
                BottomCenter(eye);
                eye.GetComponent<SelectionFrameGraphic>().Shape = SelectionFrameGraphic.FrameShape.Eye;
                Button(eye, new Color(.15f, .13f, .16f));
                PrefabUtility.SaveAsPrefabAsset(root.gameObject, BarPath);
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        private static void CreateDefense()
        {
            var root = Rect(null, "DefenseAssignment", 0, 0, 1920, 1080);
            root.gameObject.AddComponent<DefenseAssignmentView>();
            Stretch(Rect(root, "DefenseLines", 0, 0, 1920, 1080));
            Stretch(Rect(root, "DefenseHandles", 0, 0, 1920, 1080));
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, DefensePath);
            Object.DestroyImmediate(root.gameObject);
        }

        private static void Button(RectTransform rect, Color normal)
        {
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Graphic>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1, .86f, .72f);
            colors.pressedColor = new Color(.8f, .7f, .66f);
            // The disabled button retains exactly the same silhouette.
            colors.disabledColor = Color.white;
            button.colors = colors;
        }

        private static RectTransform Shape(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var rect = Rect(parent, name, x, y, width, height);
            var graphic = rect.gameObject.AddComponent<SelectionFrameGraphic>();
            graphic.color = color;
            return rect;
        }

        private static void Label(Transform parent, string name, float x, float y, float width, float height, float size)
        {
            var rect = Rect(parent, name, x, y, width, height);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.color = Ivory;
            label.fontSize = size;
            label.fontSizeMax = size;
            label.fontSizeMin = 16;
            label.enableAutoSizing = true;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            BattleText.StyleHud(label);
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static void BottomCenter(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -rect.anchoredPosition.y);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
