using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    public static class ApplyCompactBattleHud
    {
        private const string Art = "Assets/Art/UI/BattleCompact/";
        private static BattleFontPalette _fonts;

        [MenuItem("Project C/Apply compact battle HUD")]
        public static void Apply()
        {
            _fonts = AssetDatabase.LoadAssetAtPath<BattleFontPalette>("Assets/Resources/UI/BattleFontPalette.asset");
            foreach (string file in Directory.GetFiles(Art, "*.png"))
            {
                AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(file);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                if (Path.GetFileNameWithoutExtension(file) == "TooltipFrame")
                {
                    importer.spritePixelsPerUnit = 200;
                    importer.spriteBorder = new Vector4(64, 64, 64, 64);
                }
                importer.SaveAndReimport();
            }
            var definition = AssetDatabase.LoadAssetAtPath<BattleCostDefinition>(Art + "YokaiCost.asset");
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<BattleCostDefinition>();
                AssetDatabase.CreateAsset(definition, Art + "YokaiCost.asset");
            }
            var costData = new SerializedObject(definition);
            costData.FindProperty("_id").stringValue = "yokai";
            costData.FindProperty("_nameKey").stringValue = "ui_battle_yokai_power";
            costData.FindProperty("_descriptionKey").stringValue = "ui_battle_yokai_description";
            costData.FindProperty("_icon").objectReferenceValue = Sprite("YokaiIcon");
            costData.ApplyModifiedPropertiesWithoutUndo();
            var item = BuildCostItem();
            Edit("Assets/Prefabs/Battle/BattleScreen.prefab", root =>
            {
                var ui = root.GetComponent<BattlePresenter>();
                var table = Find(root.transform, "Table");
                TurnPanel(ui, table);
                MenuButton(ui.LogToggle, 44, 127, "LogIcon");
                MenuButton(ui.LobbyToggle, 44, 219, "MenuIcon");
                Costs(table, item);
                var hud = root.GetComponentInChildren<BattleHudView>(true);
                var data = new SerializedObject(hud);
                data.FindProperty("_yokaiCost").objectReferenceValue = definition;
                data.ApplyModifiedPropertiesWithoutUndo();
                Find(root.transform, "YokaiPanel").gameObject.SetActive(false);
                Place(Find(root.transform, "CardInfoSpace"), 174, 260, 300, 480);
                foreach (string side in new[] { "Own", "Enemy" })
                {
                    var resources = Find(root.transform, side + "Resources");
                    for (int i = 0; i < 3; i++)
                    {
                        var frame = Find(resources, "RenewalZone" + i).GetComponent<Image>();
                        frame.sprite = Sprite("CounterFrame");
                        frame.preserveAspect = true;
                        string zone = new[] { "Deck", "Grave", "Exile" }[i];
                        var count = Find(resources, side + zone + "Count").GetComponent<TMP_Text>();
                        float x = count.rectTransform.anchoredPosition.x;
                        float height = ((RectTransform)resources).rect.height;
                        Place(count.rectTransform, x, height - 38, count.rectTransform.sizeDelta.x, 32);
                        count.enableAutoSizing = true;
                        count.fontSizeMin = 18;
                        count.fontSizeMax = 34;
                        if (i == 2)
                        {
                            Find(resources, side + "ExileIcon").GetComponent<Image>().sprite = Sprite("ExileIcon");
                        }
                    }
                }
                Place(Find(ui.EndTurn.transform, "TurnArrow"), 0, 0, 55, 44);
                Place((RectTransform)ui.EndTurn.transform, 1620, 335, 260, 400, new Vector2(1, .5f), new Vector2(1920, 1080));
                Vector2 size = ui.EndTurn.GetComponent<Image>().sprite.rect.size;
                size *= Mathf.Min(260 / size.x, 400 / size.y);
                Vector2 inset = (new Vector2(260, 400) - size) * .5f;
                Fit(ui.PhaseLabel.rectTransform, inset, size, .32f, .24f, .44f, .08f);
                var main = ui.EndTurn.GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == "Label");
                Fit(main.rectTransform, inset, size, .23f, .405f, .54f, .14f);
                main.enableAutoSizing = true;
                main.fontSizeMin = 16;
                main.fontSizeMax = 42;
                main.textWrappingMode = TextWrappingModes.NoWrap;
                Fit(Find(ui.EndTurn.transform, "TurnArrow"), inset, size, .41f, .545f, .22f, .11f);
            });
            Edit("Assets/Prefabs/Battle/BattleCard.prefab", root =>
            {
                var frame = Find(root.transform, "FrameImage");
                var title = Find(frame, "Name").GetComponent<TMP_Text>();
                var plate = Find(frame, "RenewalTitle").GetComponent<Image>();
                plate.transform.SetParent(frame, false);
                plate.gameObject.SetActive(true);
                plate.sprite = Sprite("HandNamePlate");
                plate.color = Color.white;
                Place(plate.rectTransform, 36, 8, 148, 30);
                plate.transform.SetAsLastSibling();
                title.transform.SetParent(frame, false);
                Place(title.rectTransform, 46, 10, 128, 26);
                title.alignment = TextAlignmentOptions.Center;
                title.margin = new Vector4(2, 1, 2, 1);
                title.enableAutoSizing = true;
                title.fontSizeMin = 6;
                title.fontSizeMax = 18;
                title.textWrappingMode = TextWrappingModes.NoWrap;
                title.overflowMode = TextOverflowModes.Overflow;
                title.transform.SetAsLastSibling();
                Find(frame, "CostBadgeFrame").SetAsLastSibling();
            });
            AssetDatabase.SaveAssets();
        }

        private static BattleCostItemView BuildCostItem()
        {
            var root = Image(null, "BattleCostItem", Sprite("CostRow"), true);
            Place(root.rectTransform, 0, 0, 126, 46);
            root.gameObject.AddComponent<BattleCostItemView>();
            var element = root.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 46;
            element.minHeight = 46;
            var icon = Image(root.transform, "CostIcon", Sprite("YokaiIcon"), false);
            Place(icon.rectTransform, 7, 5, 35, 36);
            icon.preserveAspect = true;
            Label(root.transform, "CostValue", 43, 2, 75, 42, 27, BattleFontRole.Number);
            var asset = PrefabUtility.SaveAsPrefabAsset(root.gameObject, "Assets/Prefabs/Battle/BattleCostItem.prefab");
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            return asset.GetComponent<BattleCostItemView>();
        }

        private static void TurnPanel(BattlePresenter ui, Transform table)
        {
            var panel = Image(table, "TurnHud", Sprite("TurnPanel"), false);
            Place(panel.rectTransform, 20, 8, 320, 118);
            if (panel.GetComponent<BattleTurnHudView>() == null)
            {
                panel.gameObject.AddComponent<BattleTurnHudView>();
            }
            var number = Image(panel.transform, "TurnSocket", Sprite("CircleFrame"), false);
            Place(number.rectTransform, 0, 3, 112, 112);
            var timer = Image(panel.transform, "TimeSocket", Sprite("CircleFrame"), false);
            Place(timer.rectTransform, 202, 3, 112, 112);
            var ring = Image(panel.transform, "TimeRing", Sprite("TimerRing"), false);
            Place(ring.rectTransform, 202, 3, 112, 112);
            ring.type = UnityEngine.UI.Image.Type.Filled;
            ring.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            ring.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.fillAmount = 0;
            ui.TurnLabel.transform.SetParent(panel.transform, false);
            Place(ui.TurnLabel.rectTransform, 14, 15, 82, 82);
            ui.TurnLabel.alignment = TextAlignmentOptions.Center;
            ui.TurnLabel.enableAutoSizing = true;
            ui.TurnLabel.fontSizeMin = 28;
            ui.TurnLabel.fontSizeMax = 62;
            ui.TurnLabel.text = "--";
            LocalizedBattleFont.SetRole(ui.TurnLabel, BattleFontRole.Number);
            Label(panel.transform, "TurnUnit", 112, 36, 44, 46, 28, BattleFontRole.Hud);
            var time = Find(table, "TurnTimer").GetComponent<TMP_Text>();
            time.transform.SetParent(panel.transform, false);
            Place(time.rectTransform, 215, 30, 78, 56);
            time.enableAutoSizing = true;
            time.fontSizeMin = 20;
            time.fontSizeMax = 36;
            time.alignment = TextAlignmentOptions.Center;
            time.text = "--";
            LocalizedBattleFont.SetRole(time, BattleFontRole.Number);
            Label(panel.transform, "TimeUnit", 283, 51, 23, 23, 15, BattleFontRole.Hud);
        }

        private static void MenuButton(Button button, float x, float y, string iconName)
        {
            Place((RectTransform)button.transform, x, y, 78, 78);
            button.GetComponent<Image>().sprite = Sprite("MenuFrame");
            button.GetComponent<Image>().type = UnityEngine.UI.Image.Type.Simple;
            button.GetComponent<Image>().color = Color.white;
            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                text.gameObject.SetActive(false);
            }
            var icon = Image(button.transform, "CompactIcon", Sprite(iconName), false);
            Place(icon.rectTransform, 15, 15, 48, 48);
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1, .9f, .6f);
            colors.pressedColor = new Color(.65f, .85f, .8f);
            colors.selectedColor = Color.white;
            button.colors = colors;
        }

        private static void Costs(Transform table, BattleCostItemView prefab)
        {
            var root = Rect(table, "SpecialCosts");
            Place(root, 25, 313, 126, 86);
            var view = root.GetComponent<BattleSpecialCostsView>() ?? root.gameObject.AddComponent<BattleSpecialCostsView>();
            var data = new SerializedObject(view);
            data.FindProperty("_itemPrefab").objectReferenceValue = prefab;
            data.ApplyModifiedPropertiesWithoutUndo();
            var toggle = Image(root, "CostToggle", Sprite("CostToggle"), true);
            Place(toggle.rectTransform, 0, 0, 126, 32);
            if (toggle.GetComponent<Button>() == null)
            {
                toggle.gameObject.AddComponent<Button>();
            }
            var arrow = Image(toggle.transform, "CostChevron", Sprite("Chevron"), false);
            Place(arrow.rectTransform, 49, 7, 28, 18);
            var viewport = Image(root, "CostViewport", null, true);
            viewport.color = Color.clear;
            Place(viewport.rectTransform, 0, 40, 126, 46);
            if (viewport.GetComponent<RectMask2D>() == null)
            {
                viewport.gameObject.AddComponent<RectMask2D>();
            }
            var content = Rect(viewport.transform, "CostContent");
            Place(content, 0, 0, 126, 46);
            var layout = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.GetComponent<ScrollRect>() ?? viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport.rectTransform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;
            var tooltip = Image(root, "CostTooltip", Sprite("TooltipFrame"), false);
            Place(tooltip.rectTransform, 144, 40, 300, 150);
            tooltip.type = UnityEngine.UI.Image.Type.Sliced;
            Label(tooltip.transform, "CostTooltipTitle", 20, 12, 260, 32, 26, BattleFontRole.Heading);
            var line = Image(tooltip.transform, "CostTooltipDivider", null, false);
            line.color = new Color(.8f, .61f, .28f);
            Place(line.rectTransform, 28, 50, 244, 1);
            Label(tooltip.transform, "CostTooltipAmount", 20, 56, 260, 28, 25, BattleFontRole.Number);
            var description = Label(tooltip.transform, "CostTooltipDescription", 20, 92, 260, 40, 19, BattleFontRole.Body);
            description.textWrappingMode = TextWrappingModes.Normal;
            description.enableAutoSizing = false;
            description.overflowMode = TextOverflowModes.Ellipsis;
            tooltip.gameObject.SetActive(false);
        }

        private static TMP_Text Label(Transform parent, string name, float x, float y, float width, float height, int size, BattleFontRole role)
        {
            var rect = Rect(parent, name);
            var label = rect.GetComponent<TextMeshProUGUI>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
            Place(rect, x, y, width, height);
            label.fontSize = size;
            label.fontSizeMin = Math.Min(size, 12);
            label.fontSizeMax = size;
            label.enableAutoSizing = true;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.color = new Color(1, .94f, .8f);
            label.margin = Vector4.zero;
            var font = label.GetComponent<LocalizedBattleFont>() ?? label.gameObject.AddComponent<LocalizedBattleFont>();
            font.Configure(role, _fonts);
            return label;
        }

        private static Image Image(Transform parent, string name, Sprite sprite, bool raycast)
        {
            var rect = Rect(parent, name);
            var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = raycast;
            image.color = Color.white;
            return image;
        }
        private static RectTransform Rect(Transform parent, string name)
        {
            var existing = parent != null ? parent.Find(name) : null;
            var rect = existing != null ? (RectTransform)existing : new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            if (existing == null && parent != null)
            {
                rect.SetParent(parent, false);
            }
            return rect;
        }
        private static RectTransform Find(Transform root, string name) => (RectTransform)root.GetComponentsInChildren<Transform>(true).First(item => item.name == name);
        private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            Place(rect, x, y, width, height, new Vector2(0, 1), rect.parent is RectTransform parent ? parent.rect.size : Vector2.zero);
        }
        private static void Place(RectTransform rect, float x, float y, float width, float height, Vector2 anchor, Vector2 parentSize)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x - parentSize.x * anchor.x, parentSize.y * (1 - anchor.y) - y);
            rect.sizeDelta = new Vector2(width, height);
        }
        private static void Fit(RectTransform rect, Vector2 inset, Vector2 size, float x, float y, float width, float height)
        {
            Place(rect, inset.x + size.x * x, inset.y + size.y * y, size.x * width, size.y * height);
        }
        private static void Edit(string path, Action<GameObject> apply)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                apply(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
