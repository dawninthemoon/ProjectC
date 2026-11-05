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
    // Upgrade the existing, functional mockup layout to the supplied v4 PNGs. Never rewrites PNG pixels.
    public static class ApplyMockupRepaintBattleUi
    {
        private const string Art = "Assets/Art/UI/Battle/MockupRepaint/";
        private const string Prefabs = "Assets/Prefabs/Battle/";
        private const float Scale = 1920f / 1672f;
        private static readonly Color Ivory = new Color(1, .92f, .79f);
        private static readonly Color Ink = new Color(.13f, .09f, .08f);

        [MenuItem("Project C/Apply v4 mockup repaint UI")]
        public static void Apply()
        {
            AssetDatabase.Refresh();
            foreach (string path in Directory.GetFiles(Art, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                if (Path.GetFileNameWithoutExtension(path) == "SpecialCostPanel_Expandable")
                {
                    importer.spriteBorder = new Vector4(16, 80, 16, 165);
                }
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            Directory.CreateDirectory(Art + "Regions");
            AssetDatabase.Refresh();
            Edit("BattleCard", root => Card(root, true));
            Edit("BattleFieldCard", root => Card(root, false));
            Edit("CardBack", root =>
            {
                var frame = Find(root.transform, "FrameImage").GetComponent<Image>();
                Paint(frame, Sprite("CardBack"), true);
                frame.preserveAspect = true;
            });
            Edit("PlayerPanel", root => Hero(root.GetComponent<PlayerPanelView>(), false));
            Edit("BattleScreen", Screen);
            AssetDatabase.SaveAssets();
            Debug.Log("V4 mockup repaint applied with unchanged source PNG files.");
        }

        private static void Card(GameObject root, bool hand)
        {
            var frame = Find(root.transform, "FrameImage");
            var sprite = Sprite(hand ? "HandCardFrame" : "FieldMonsterFrame");
            var slot = ((RectTransform)root.transform).rect.size;
            float ratio = sprite.rect.width / sprite.rect.height;
            float height = Mathf.Min(slot.y, slot.x / ratio);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, .5f);
            frame.anchoredPosition = Vector2.zero;
            frame.sizeDelta = new Vector2(height * ratio, height);
            Paint(frame.GetComponent<Image>(), sprite, true);
            frame.GetComponent<Image>().color = Color.clear;
            var overlay = Find(frame, "RenewalFrame");
            Stretch(overlay);
            Paint(overlay.GetComponent<Image>(), sprite);
            var glow = Find(frame, "FrameGlow").GetComponent<Image>();
            Paint(glow, sprite);
            Stretch(glow.rectTransform);
            glow.rectTransform.sizeDelta = new Vector2(9, 9);
            glow.color = Color.clear;
            glow.material = null;
            glow.transform.SetAsFirstSibling();
            var mask = Find(frame, "ArtworkMask");
            Stretch(mask);
            // Refit the existing mask shapes to v4's transparent artwork window, underneath its border.
            mask.offsetMin = new Vector2(hand ? 0 : .071f, hand ? .07f : .019f) * frame.rect.size;
            mask.offsetMax = new Vector2(hand ? .03f : -.055f, hand ? .01f : -.01f) * frame.rect.size;
            Stat(frame, "AttackBadge", hand ? new Rect(28, 515, 115, 105) : new Rect(42, 388, 110, 103), sprite.rect.size);
            Stat(frame, "HealthBadge", hand ? new Rect(312, 515, 115, 105) : new Rect(339, 388, 110, 103), sprite.rect.size);
            var title = Find(frame, "Name").GetComponent<TMP_Text>();
            if (hand)
            {
                Pixels(title.rectTransform, new Rect(55, 24, 344, 80), sprite.rect.size, frame.rect.size);
                Style(title, Ink, 20, 10);
            }
            overlay.SetAsLastSibling();
            Find(frame, "AttackBadgeFrame").SetAsLastSibling();
            Find(frame, "HealthBadgeFrame").SetAsLastSibling();
            title.transform.SetAsLastSibling();
        }

        private static void Stat(RectTransform frame, string name, Rect region, Vector2 sourceSize)
        {
            var badge = Find(frame, name + "Frame");
            badge.GetComponent<Image>().enabled = false;
            badge.GetComponent<Image>().raycastTarget = false;
            Pixels(badge, region, sourceSize, frame.rect.size);
            var label = Find(badge, name).GetComponent<TMP_Text>();
            Stretch(label.rectTransform);
            Style(label, Ivory, 34, 14);
        }

        private static void Screen(GameObject root)
        {
            var ui = root.GetComponent<BattlePresenter>();
            var background = Find(root.transform, "Background");
            Stretch(background);
            var backdrop = Find(background, "MoonlitArenaBackdrop");
            Stretch(backdrop);
            Paint(backdrop.GetComponent<Image>(), Sprite("BattleBackground_1920x1080"));
            if (backdrop.GetComponent<AspectFillImage>() is AspectFillImage fill)
            {
                fill.SetCrop(1, Vector2.zero);
            }
            Turn(ui);
            Hero(ui.OwnPlayer, false);
            Hero(ui.EnemyPlayer, true);
            Zones(Find(root.transform, "OwnResources"), "Own");
            Zones(Find(root.transform, "EnemyResources"), "Enemy");
            foreach (string name in new[] { "YourDeck", "OpponentDeck" })
            {
                var image = Find(root.transform, name).GetComponent<Image>();
                image.sprite = Sprite("CardBack");
            }
            var phase = (RectTransform)ui.EndTurn.transform;
            Paint(phase.GetComponent<Image>(), Sprite("PhaseButton"), true);
            phase.GetComponent<Image>().preserveAspect = true;
            Pixels(ui.PhaseLabel.rectTransform, new Rect(119, 124, 239, 60), new Vector2(480, 539), phase.rect.size);
            Style(ui.PhaseLabel, Ink, 28, 16);
            var action = Find(phase, "Label").GetComponent<TMP_Text>();
            Pixels(action.rectTransform, new Rect(95, 210, 290, 110), new Vector2(480, 539), phase.rect.size);
            Style(action, Ivory, 55, 24);
            var arrow = Find(phase, "TurnArrow");
            Paint(arrow.GetComponent<Image>(), Sprite("PhaseArrow"));
            Pixels(arrow, new Rect(170, 330, 140, 78), new Vector2(480, 539), phase.rect.size);
            var log = ui.LogToggle.GetComponent<Image>();
            Paint(log, Region("SideButton_Log", new Rect(12, 30, 136, 106)), true);
            log.preserveAspect = true;
            var menu = ui.LobbyToggle.GetComponent<Image>();
            Paint(menu, Region("SideButton_Menu", new Rect(19, 15, 147, 123)), true);
            menu.preserveAspect = true;
            Costs(ui.GetComponentsInChildren<BattleSpecialCostsView>(true).Single());
        }

        private static void Turn(BattlePresenter ui)
        {
            var hud = Find(ui.transform, "TurnHud");
            hud.GetComponent<Image>().sprite = Sprite("TurnBanner");
            hud.GetComponent<Image>().color = Color.clear;
            var banner = Find(hud, "TurnBannerArt").GetComponent<Image>();
            Paint(banner, Sprite("TurnBanner"));
            Place(banner.rectTransform, 0, 3 * Scale, 258 * Scale, 102 * Scale);
            var housing = Find(hud, "TimerHousing").GetComponent<Image>();
            Paint(housing, Sprite("TimerHousing"));
            Place(housing.rectTransform, 234 * Scale, 0, 118 * Scale, 118 * Scale * 506 / 512);
            var off = Find(hud, "TimeTrack").GetComponent<Image>();
            Paint(off, Sprite("TimerTicks_On"));
            off.color = new Color(85f / 255f, 37f / 255f, 43f / 255f);
            CopyRect(off.rectTransform, housing.rectTransform);
            var on = Find(hud, "TimeRing").GetComponent<Image>();
            Paint(on, Sprite("TimerTicks_On"));
            CopyRect(on.rectTransform, housing.rectTransform);
            on.type = Image.Type.Filled;
            on.fillMethod = Image.FillMethod.Radial360;
            on.fillOrigin = (int)Image.Origin360.Top;
            on.fillClockwise = true;
            on.fillAmount = 0;
            Place(ui.TurnLabel.rectTransform, 31 * Scale, 41 * Scale, 41 * Scale, 41 * Scale);
            Style(ui.TurnLabel, Ink, 38, 20);
            var owner = Find(hud, "TurnOwner").GetComponent<TMP_Text>();
            Place(owner.rectTransform, 91 * Scale, 40 * Scale, 137 * Scale, 42 * Scale);
            Style(owner, Ivory, 34, 18);
            var timer = Find(hud, "TurnTimer").GetComponent<TMP_Text>();
            Place(timer.rectTransform, 254 * Scale, 34 * Scale, 75 * Scale, 52 * Scale);
            Style(timer, Ivory, 43, 22);
            banner.transform.SetAsFirstSibling();
            housing.transform.SetSiblingIndex(1);
            off.transform.SetSiblingIndex(2);
            on.transform.SetSiblingIndex(3);
            ui.TurnLabel.transform.SetAsLastSibling();
            owner.transform.SetAsLastSibling();
            timer.transform.SetAsLastSibling();
        }

        private static void Hero(PlayerPanelView player, bool enemy)
        {
            var root = (RectTransform)player.transform;
            var ring = Find(root, "HeroRing").GetComponent<Image>();
            var sprite = Sprite(enemy ? "HeroRing_Opponent" : "HeroRing_Player");
            Paint(ring, sprite);
            float height = enemy ? Mathf.Min(root.rect.height, root.rect.width * sprite.rect.height / sprite.rect.width)
                : root.rect.height;
            var size = new Vector2(height * sprite.rect.width / sprite.rect.height, height);
            Place(ring.rectTransform, 0, 0, size.x, size.y);
            var mask = Find(root, "PortraitMask");
            var circle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Icons/UI_Circle.png");
            Paint(mask.GetComponent<Image>(), circle);
            var window = enemy ? new Rect(53, 45, 373, 373) : new Rect(45, 40, 396, 425);
            Pixels(mask, window, sprite.rect.size, size);
            var portrait = Find(mask, "PortraitImage");
            Stretch(portrait);
            if (portrait.GetComponent<AspectFillImage>() is AspectFillImage portraitFill)
            {
                // Existing portraits include their former ring. Crop that rim under the new separate ring.
                portraitFill.SetCrop(1.23f, Vector2.zero);
            }
            var badge = Find(root, "LifeBadge").GetComponent<Image>();
            Paint(badge, Sprite("HeroHealthBadge"), true);
            badge.preserveAspect = true;
        }

        private static void Zones(RectTransform row, string prefix)
        {
            string[] names = { "Deck", "Grave", "Exile" };
            for (int i = 0; i < names.Length; ++i)
            {
                var tag = Find(row, "RenewalZone" + i);
                var sprite = Sprite("ZoneTag_" + names[i]);
                Paint(tag.GetComponent<Image>(), sprite);
                float height = 170 * Scale;
                float width = height * sprite.rect.width / sprite.rect.height;
                tag.sizeDelta = new Vector2(width, height);
                var count = Find(row, prefix + names[i] + "Count").GetComponent<TMP_Text>();
                Place(count.rectTransform, tag.anchoredPosition.x + width * .2f, height * .61f, width * .6f, height * .29f);
                Style(count, Ink, 36, 16);
                Find(row, prefix + names[i] + "Icon").GetComponent<Image>().enabled = false;
                if (i > 0)
                {
                    var browse = Find(row, prefix + names[i] + "Browse");
                    CopyRect(browse, tag);
                }
            }
        }

        private static void Costs(BattleSpecialCostsView costs)
        {
            var root = (RectTransform)costs.transform;
            float ppu = 266 / root.rect.width;
            var body = Find(root, "CostBody");
            foreach (Transform child in body)
            {
                child.gameObject.SetActive(false);
            }
            Stretch(body);
            var background = body.GetComponent<Image>() ?? body.gameObject.AddComponent<Image>();
            Paint(background, Sprite("SpecialCostPanel_Expandable"));
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = ppu;
            var toggle = Find(root, "CostToggle").GetComponent<Image>();
            Paint(toggle, Region("SpecialCostPanel_Expandable", new Rect(0, 0, 266, 110)), true);
            Place(toggle.rectTransform, 0, 0, root.rect.width, 110 / ppu);
            Find(toggle.transform, "ChevronWell").gameObject.SetActive(false);
            var chevron = Find(toggle.transform, "CostChevron").GetComponent<Image>();
            Paint(chevron, Sprite("CostChevron"));
            chevron.rectTransform.anchorMin = chevron.rectTransform.anchorMax = chevron.rectTransform.pivot = new Vector2(.5f, .5f);
            chevron.rectTransform.anchoredPosition = Vector2.zero;
            chevron.rectTransform.sizeDelta = new Vector2(28, 17.46f) * Scale;
            var data = new SerializedObject(costs);
            data.FindProperty("_headerHeight").floatValue = 110 / ppu;
            data.FindProperty("_bottomPadding").floatValue = 80 / ppu;
            data.ApplyModifiedPropertiesWithoutUndo();
            var viewport = Find(root, "CostViewport");
            viewport.anchoredPosition = new Vector2(7 * Scale, -110 / ppu);
        }

        private static Sprite Region(string name, Rect source)
        {
            string path = Art + "Regions/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null)
            {
                return existing;
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + name + ".png");
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(source.x, texture.height - source.y - source.height, source.width, source.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            AssetDatabase.CreateAsset(sprite, path);
            return sprite;
        }

        private static void Pixels(RectTransform rect, Rect source, Vector2 sourceSize, Vector2 displaySize)
        {
            Place(rect, source.x / sourceSize.x * displaySize.x, source.y / sourceSize.y * displaySize.y,
                source.width / sourceSize.x * displaySize.x, source.height / sourceSize.y * displaySize.y);
        }

        private static void CopyRect(RectTransform target, RectTransform source)
        {
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.anchoredPosition = source.anchoredPosition;
            target.sizeDelta = source.sizeDelta;
        }

        private static void Style(TMP_Text label, Color color, float max, float min)
        {
            var data = new SerializedObject(label.GetComponent<LocalizedBattleFont>());
            data.FindProperty("_useAuthoredColor").boolValue = true;
            data.FindProperty("_authoredColor").colorValue = color;
            data.ApplyModifiedPropertiesWithoutUndo();
            label.color = color;
            label.enableAutoSizing = true;
            label.fontSizeMax = max * Scale;
            label.fontSizeMin = min * Scale;
            label.fontSize = max * Scale;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        private static void Paint(Image image, Sprite sprite, bool raycast = false)
        {
            image.sprite = sprite;
            image.color = Color.white;
            image.enabled = true;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = raycast;
        }

        private static Sprite Sprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png") ?? throw new InvalidOperationException("Missing v4 sprite " + name);
        }

        private static RectTransform Find(Transform root, string name)
        {
            return (RectTransform)root.GetComponentsInChildren<Transform>(true).First(value => value.name == name);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Edit(string name, Action<GameObject> edit)
        {
            string path = Prefabs + name + ".prefab";
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
    }
}
