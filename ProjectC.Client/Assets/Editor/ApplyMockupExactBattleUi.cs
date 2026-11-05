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
    // Uses the supplied PNG bytes unchanged. Coordinates are measured in the 1672x941 mockup.
    public static class ApplyMockupExactBattleUi
    {
        private const string Art = "Assets/Art/UI/Battle/MockupExact/";
        private const string Prefabs = "Assets/Prefabs/Battle/";
        private const float Scale = 1920f / 1672f;
        private static readonly Color Ivory = new Color(1, .92f, .79f);
        private static readonly Color Ink = new Color(.13f, .09f, .08f);

        [MenuItem("Project C/Apply original mockup battle UI")]
        public static void Apply()
        {
            AssetDatabase.Refresh();
            foreach (string path in Directory.GetFiles(Art + "png", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            Directory.CreateDirectory(Art + "Slices");
            AssetDatabase.Refresh();
            Edit("BattleCard", root => Card(root, true));
            Edit("BattleFieldCard", root => Card(root, false));
            Edit("CardBack", root =>
            {
                ((RectTransform)root.transform).sizeDelta = new Vector2(88, 134) * Scale;
                var frame = Find(root.transform, "FrameImage");
                Stretch(frame);
                Paint(frame.GetComponent<Image>(), Sprite("CardBack_Exact"), true);
            });
            Edit("BattleCostItem", CostItem);
            // PlayerPanel is a legacy template; BattleScreen owns authored instances with portraits.
            Edit("PlayerPanel", root => Hero(root.GetComponent<PlayerPanelView>(), false));
            Edit("BattleScreen", Screen);
            var settingsAsset = AssetDatabase.LoadAssetAtPath<BattleVisualSettings>(Prefabs + "BattleVisualSettings.asset");
            var data = new SerializedObject(settingsAsset);
            data.FindProperty("_compactHandScale").floatValue = 1;
            data.FindProperty("_expandedHandScale").floatValue = 1;
            data.FindProperty("_compactHandSpacing").floatValue = 172 * Scale;
            data.FindProperty("_expandedHandSpacing").floatValue = 172 * Scale;
            data.FindProperty("_handCurve").floatValue = 10 * Scale;
            data.FindProperty("_handRotation").floatValue = 3;
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("Original mockup UI saved. PNG files were not rewritten.");
        }

        private static void Card(GameObject root, bool hand)
        {
            var size = new Vector2(hand ? 180 : 139, hand ? 262 : 156) * Scale;
            ((RectTransform)root.transform).sizeDelta = size;
            var frame = Find(root.transform, "FrameImage");
            Stretch(frame);
            Paint(frame.GetComponent<Image>(), Sprite(hand ? "HandCardFrame_Exact" : "FieldMonsterFrame_Exact"), true);
            // RenewalFrame draws the border once; the transparent base keeps the existing hit target.
            frame.GetComponent<Image>().color = Color.clear;
            var inset = Find(frame, "Inset");
            Stretch(inset);
            inset.GetComponent<Image>().enabled = false;
            var mask = Find(inset, "ArtworkMask");
            Stretch(mask);
            Paint(mask.GetComponent<Image>(), Sprite(hand ? "Mask_HandArtwork" : "Mask_FieldArtwork"));
            var stencil = mask.GetComponent<Mask>() ?? mask.gameObject.AddComponent<Mask>();
            stencil.showMaskGraphic = false;
            Stretch(Find(mask, "ArtworkImage"));
            foreach (var text in Find(mask, "ArtworkImage").GetComponentsInChildren<TMP_Text>(true))
            {
                text.enabled = false;
            }
            var overlay = Find(frame, "RenewalFrame");
            Stretch(overlay);
            Paint(overlay.GetComponent<Image>(), frame.GetComponent<Image>().sprite);
            overlay.SetAsLastSibling();
            overlay.SetParent(frame, false);
            Stretch(overlay);
            foreach (string oldPart in new[] { "FrameTopViewport", "FrameBottomTrim" })
            {
                var old = Optional(frame, oldPart);
                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                }
            }
            var glow = Find(frame, "FrameGlow");
            Stretch(glow);
            glow.sizeDelta = new Vector2(9, 9);
            Paint(glow.GetComponent<Image>(), frame.GetComponent<Image>().sprite);
            glow.GetComponent<Image>().material = null;
            glow.GetComponent<Image>().color = Color.clear;
            glow.SetAsFirstSibling();
            var title = Find(frame, "Name").GetComponent<TMP_Text>();
            title.transform.SetParent(frame, false);
            title.gameObject.SetActive(hand);
            var titlePlate = Find(frame, "RenewalTitle");
            titlePlate.gameObject.SetActive(false);
            if (hand)
            {
                Local(title.rectTransform, 17, 13, 145, 25);
                Style(title, Ink, 18, 10);
                title.transform.SetAsLastSibling();
            }
            else
            {
                Find(frame, "FieldStatusFrame").gameObject.SetActive(false);
            }
            foreach (var graphic in Find(frame, "CostBadgeFrame").GetComponentsInChildren<Graphic>(true))
            {
                graphic.enabled = false;
                graphic.raycastTarget = false;
            }
            // v3 field badges are embedded; hand badges use the supplied independent parts.
            Stat(frame, "AttackBadge", false, hand);
            Stat(frame, "HealthBadge", true, hand);
            foreach (var graphic in inset.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }
        }

        private static void Stat(RectTransform frame, string name, bool right, bool hand)
        {
            var badge = Find(frame, name + "Frame");
            badge.SetParent(frame, false);
            if (hand)
            {
                // v3 hand extraction has incomplete badge edges; use its supplied independent parts.
                Paint(badge.GetComponent<Image>(), Sprite(right ? "CardDefenseShield_Exact" : "CardAttackFlower_Exact"));
                Local(badge, right ? 120 : 0, 202, 60, 55);
            }
            else
            {
                badge.GetComponent<Image>().enabled = false;
                Local(badge, right ? 94 : 0, 112, 43, 40);
            }
            var outline = badge.GetComponent<Outline>();
            if (outline != null)
            {
                outline.enabled = false;
            }
            var label = Find(badge, name).GetComponent<TMP_Text>();
            Stretch(label.rectTransform);
            Style(label, Ivory, hand ? 35 : 32, 15);
            label.rectTransform.offsetMin = new Vector2(3, 4);
            label.rectTransform.offsetMax = new Vector2(-3, -2);
            badge.SetAsLastSibling();
        }

        private static void Screen(GameObject root)
        {
            var ui = root.GetComponent<BattlePresenter>();
            var presenterData = new SerializedObject(ui);
            presenterData.FindProperty("_useAuthoredHandLayout").boolValue = true;
            presenterData.ApplyModifiedPropertiesWithoutUndo();
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.referenceResolution = new Vector2(1920, 1080);
            var background = Find(root.transform, "Background");
            Stretch(background);
            background.GetComponent<Image>().color = Color.clear;
            foreach (var image in background.GetComponentsInChildren<Image>(true))
            {
                image.raycastTarget = false;
            }
            // This clean source was supplied separately in Downloads; its pixels remain unchanged.
            var backdrop = Find(background, "MoonlitArenaBackdrop");
            Paint(backdrop.GetComponent<Image>(), Sprite("BattleBackground_Original"));
            Stretch(backdrop);
            if (backdrop.GetComponent<AspectFillImage>() is AspectFillImage aspectFill)
            {
                aspectFill.SetCrop(1, Vector2.zero);
            }
            Turn(ui);
            SideButton(ui.LogToggle, "SideButton_Log_Exact", 20, 136, 83, 68);
            SideButton(ui.LobbyToggle, "SideButton_Menu_Exact", 20, 204, 83, 74);
            Hero(ui.OwnPlayer, false);
            Hero(ui.EnemyPlayer, true);
            ScreenRect((RectTransform)ui.OwnPlayer.transform, 0, 641, 256, 300, new Vector2(0, 0));
            ScreenRect((RectTransform)ui.EnemyPlayer.transform, 765, 0, 221, 199, new Vector2(.5f, 1));
            Zones(Find(root.transform, "EnemyResources"), "Enemy", true);
            Zones(Find(root.transform, "OwnResources"), "Own", false);
            foreach (string name in new[] { "YourDeck", "OpponentDeck" })
            {
                var deck = Find(root.transform, name);
                Paint(deck.GetComponent<Image>(), Sprite("CardBack_Exact"));
                // Keep draw origins, suppress the old duplicate deck picture behind the new tags.
                deck.GetComponent<Image>().enabled = false;
                foreach (var text in deck.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.enabled = false;
                }
            }
            ScreenRect((RectTransform)ui.EnemyHand.transform, 354, 12, 438, 134, new Vector2(.5f, 1));
            ScreenRect((RectTransform)ui.Hand.transform, 435, 650, 865, 262, new Vector2(0, 1));
            ScreenRect((RectTransform)ui.EnemyField.transform, 410, 266, 1000, 156, new Vector2(.5f, .5f));
            ScreenRect((RectTransform)ui.OwnField.transform, 335, 480, 1000, 156, new Vector2(.5f, .5f));
            ScreenRect(Find(root.transform, "OwnFieldDropZone"), 325, 470, 1020, 176, new Vector2(.5f, .5f));
            FieldSpacing(ui.EnemyField, 16 * Scale);
            FieldSpacing(ui.OwnField, 35 * Scale);
            Phase(ui);
            Costs(ui.GetComponentsInChildren<BattleSpecialCostsView>(true).Single());
            // The event-backed history stays in its modal; the legacy notification strip stays hidden.
            ui.Log.GetComponent<Image>().enabled = false;
            foreach (Transform child in ui.Log.transform)
            {
                child.gameObject.SetActive(false);
            }
            // Events remain in the modal history; legacy top notifications must not cover the HUD.
            foreach (var graphic in ui.Effects.PopupGroup.GetComponentsInChildren<Graphic>(true))
            {
                graphic.enabled = false;
            }
            var turnBanner = (RectTransform)ui.Effects.TurnGroup.transform;
            ScreenRect(turnBanner, 640, 422, 390, 62, new Vector2(.5f, .5f));
            turnBanner.GetComponent<Image>().enabled = false;
            Local(ui.Effects.TurnTitle.rectTransform, 40, 0, 310, 44);
            Local(ui.Effects.TurnSubtitle.rectTransform, 60, 42, 270, 20);
            Style(ui.Effects.TurnTitle, Ivory, 34, 20);
            Style(ui.Effects.TurnSubtitle, Ivory, 16, 12);
            ui.Effects.TurnTitle.enabled = false;
            ui.Effects.TurnSubtitle.enabled = false;
            Find(root.transform, "TurnDim").GetComponent<Image>().enabled = false;
        }

        private static void FieldSpacing(CardZoneView zone, float spacing)
        {
            var data = new SerializedObject(zone.Layout);
            data.FindProperty("_fieldSpacing").floatValue = spacing;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Hero(PlayerPanelView player, bool enemy)
        {
            var root = (RectTransform)player.transform;
            root.localScale = Vector3.one;
            root.sizeDelta = new Vector2(enemy ? 221 : 256, enemy ? 199 : 300) * Scale;
            var hitImage = root.GetComponent<Image>();
            hitImage.enabled = true;
            hitImage.color = Color.clear;
            hitImage.raycastTarget = true;
            var mask = Optional(root, "PortraitMask") ?? Rect(root, "PortraitMask");
            Stretch(mask);
            var maskImage = mask.GetComponent<Image>() ?? mask.gameObject.AddComponent<Image>();
            Paint(maskImage, Sprite(enemy ? "Mask_OpponentPortrait" : "Mask_PlayerPortrait"));
            var stencil = mask.GetComponent<Mask>() ?? mask.gameObject.AddComponent<Mask>();
            stencil.showMaskGraphic = false;
            var portrait = Optional(root, "PortraitImage");
            if (portrait == null)
            {
                var image = Image(mask, "PortraitImage");
                portrait = image.rectTransform;
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Portraits/Megachiyo.png");
            }
            portrait.SetParent(mask, false);
            Stretch(portrait);
            var oldFrame = Optional(mask, "RenewalPortraitFrame");
            if (oldFrame != null)
            {
                oldFrame.gameObject.SetActive(false);
            }
            var ring = Image(root, "HeroRing");
            Paint(ring, Sprite(enemy ? "HeroRing_Opponent_Exact" : "HeroRing_Player_Exact"));
            Stretch(ring.rectTransform);
            ring.transform.SetAsLastSibling();
            var badge = Optional(root, "LifeBadge");
            if (badge == null)
            {
                badge = Image(root, "LifeBadge").rectTransform;
                Find(root, "Life").SetParent(badge, false);
            }
            Paint(badge.GetComponent<Image>(), Sprite("HeroHealthBadge_Exact"));
            badge.GetComponent<Image>().raycastTarget = true;
            Local(badge, enemy ? 190 : 230, enemy ? 74 : 100, 83, 109);
            var value = Find(badge, "Life").GetComponent<TMP_Text>();
            Stretch(value.rectTransform);
            value.rectTransform.offsetMin = new Vector2(8, 16);
            value.rectTransform.offsetMax = new Vector2(-8, -16);
            Style(value, Ivory, 43, 20);
            badge.SetAsLastSibling();
            foreach (string name in new[] { "LifeIcon", "Name", "Counts", "LifeCaption", "TurnMark" })
            {
                var node = Optional(root, name);
                if (node != null)
                {
                    foreach (var graphic in node.GetComponents<Graphic>())
                    {
                        graphic.enabled = false;
                    }
                }
            }
        }

        private static void Turn(BattlePresenter ui)
        {
            var hud = Find(ui.transform, "TurnHud");
            ScreenRect(hud, 0, 0, 355, 143, new Vector2(0, 1));
            Paint(hud.GetComponent<Image>(), Sprite("TurnHUD_Exact"));
            // v3 combines banner and housing. Sprite regions separate their layers without editing PNGs.
            hud.GetComponent<Image>().color = Color.clear;
            var bannerArt = Image(hud, "TurnBannerArt");
            Paint(bannerArt, Slice("TurnBannerArt", "TurnHUD_Exact", 0, 0, 234, 143));
            Local(bannerArt.rectTransform, 0, 0, 234, 143);
            bannerArt.transform.SetAsFirstSibling();
            var housing = Image(hud, "TimerHousing");
            Paint(housing, Slice("TimerHousing", "TurnHUD_Exact", 234, 0, 121, 143));
            Local(housing.rectTransform, 234, 0, 121, 143);
            housing.transform.SetSiblingIndex(1);
            foreach (string name in new[] { "TurnSocket", "TimeSocket", "TimerRingFrame", "TurnOwnerAccent", "TurnUnit", "TimeUnit" })
            {
                var graphic = Find(hud, name).GetComponent<Graphic>();
                graphic.enabled = false;
            }
            // The housing in TurnHUD is fixed; Off and On sample the identical authored tick rect.
            var off = Find(hud, "TimeTrack").GetComponent<Image>();
            Paint(off, Sprite("TimerTicks_Off_Exact"));
            Local(off.rectTransform, 234, 0, 118, 137);
            var on = Find(hud, "TimeRing").GetComponent<Image>();
            Paint(on, Sprite("TimerTicks_On_Exact"));
            Local(on.rectTransform, 234, 0, 118, 137);
            on.type = UnityEngine.UI.Image.Type.Filled;
            on.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            on.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
            on.fillClockwise = true;
            on.fillAmount = 0;
            Local(ui.TurnLabel.rectTransform, 39, 41, 45, 44);
            Style(ui.TurnLabel, Ink, 43, 20);
            var owner = Find(hud, "TurnOwner").GetComponent<TMP_Text>();
            Local(owner.rectTransform, 110, 48, 126, 45);
            Style(owner, Ivory, 34, 18);
            var shadow = owner.GetComponent<Shadow>();
            if (shadow != null)
            {
                shadow.enabled = false;
            }
            var timer = Find(hud, "TurnTimer").GetComponent<TMP_Text>();
            Local(timer.rectTransform, 258, 44, 68, 52);
            Style(timer, Ivory, 45, 24);
            ui.TurnLabel.transform.SetAsLastSibling();
            owner.transform.SetAsLastSibling();
            timer.transform.SetAsLastSibling();
            var table = Find(ui.transform, "Table");
            var centerOwner = Optional(table, "CenterTurnOwner")?.GetComponent<TMP_Text>();
            if (centerOwner == null)
            {
                centerOwner = Object.Instantiate(owner, table);
                centerOwner.name = "CenterTurnOwner";
            }
            Local(centerOwner.rectTransform, 690, 422, 290, 40);
            Style(centerOwner, Ivory, 34, 20);
            var centerTurn = Optional(table, "CenterTurnNumber")?.GetComponent<TMP_Text>();
            if (centerTurn == null)
            {
                centerTurn = Object.Instantiate(ui.TurnLabel, table);
                centerTurn.name = "CenterTurnNumber";
            }
            Local(centerTurn.rectTransform, 760, 461, 150, 20);
            Style(centerTurn, Ivory, 16, 12);
            LocalizedBattleFont.SetRole(centerTurn, BattleFontRole.Hud);
            var hudData = new SerializedObject(hud.GetComponent<BattleTurnHudView>());
            hudData.FindProperty("_showCenterStatus").boolValue = true;
            hudData.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Zones(RectTransform row, string prefix, bool enemy)
        {
            ScreenRect(row, enemy ? 1355 : 1345, enemy ? 0 : 705, 310, 180, enemy ? new Vector2(1, 1) : new Vector2(1, 0));
            row.GetComponent<Image>().enabled = false;
            string[] names = { "Deck", "Grave", "Exile" };
            float[] offsets = { 0, 96, 190 };
            for (int i = 0; i < names.Length; ++i)
            {
                var tag = Find(row, "RenewalZone" + i);
                Paint(tag.GetComponent<Image>(), Sprite("ZoneTag_" + names[i] + "_Exact"));
                Local(tag, offsets[i], 0, i == 2 ? 110 : 103, 170);
                Find(row, prefix + names[i] + "Icon").GetComponent<Image>().enabled = false;
                var count = Find(row, prefix + names[i] + "Count").GetComponent<TMP_Text>();
                Local(count.rectTransform, offsets[i] + 24, 106, 55, 40);
                Style(count, Ink, 36, 18);
                if (i > 0)
                {
                    var browse = Find(row, prefix + names[i] + "Browse");
                    Local(browse, offsets[i], 0, i == 2 ? 110 : 103, 170);
                    browse.GetComponent<Image>().color = Color.clear;
                }
            }
        }

        private static void Phase(BattlePresenter ui)
        {
            var button = ui.EndTurn;
            ScreenRect((RectTransform)button.transform, 1374, 316, 278, 322, new Vector2(1, .5f));
            Paint(button.GetComponent<Image>(), Sprite("PhaseButton_Exact"), true);
            Find(button.transform, "PhaseInside").GetComponent<Image>().enabled = false;
            Find(button.transform, "TurnGlow").GetComponent<Image>().enabled = false;
            Find(button.transform, "TurnPhasePlate").gameObject.SetActive(false);
            Local(ui.PhaseLabel.rectTransform, 65, 77, 154, 36);
            Style(ui.PhaseLabel, Ink, 28, 16);
            var label = Find(button.transform, "Label").GetComponent<TMP_Text>();
            Local(label.rectTransform, 45, 129, 187, 70);
            Style(label, Ivory, 55, 24);
            var arrow = Find(button.transform, "TurnArrow");
            Paint(arrow.GetComponent<Image>(), Sprite("PhaseArrow_Exact"));
            Local(arrow, 99, 203, 81, 36);
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1, .95f, .87f);
            colors.pressedColor = new Color(.8f, .7f, .65f);
            button.colors = colors;
        }

        private static void SideButton(Button button, string sprite, float x, float y, float width, float height)
        {
            ScreenRect((RectTransform)button.transform, x, y, width, height, new Vector2(0, 1));
            Paint(button.GetComponent<Image>(), Sprite(sprite), true);
            foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.gameObject != button.gameObject)
                {
                    graphic.enabled = false;
                }
            }
        }

        private static void Costs(BattleSpecialCostsView costs)
        {
            var root = (RectTransform)costs.transform;
            ScreenRect(root, 22, 280, 84, 181, new Vector2(0, 1));
            var data = new SerializedObject(costs);
            data.FindProperty("_headerHeight").floatValue = 34 * Scale;
            data.FindProperty("_bottomPadding").floatValue = 24 * Scale;
            data.FindProperty("_expandedChevronAngle").floatValue = 180;
            data.FindProperty("_collapsedChevronAngle").floatValue = 0;
            data.ApplyModifiedPropertiesWithoutUndo();
            var body = Rect(root, "CostBody");
            Stretch(body);
            body.SetAsFirstSibling();
            // Source-region sprites keep original pixels and avoid the three baked icon rows.
            var fill = Image(body, "BodyFill");
            Paint(fill, Slice("CostFill", "SpecialCost_Expanded_Exact", 54, 50, 18, 18));
            Stretch(fill.rectTransform);
            fill.rectTransform.offsetMin = new Vector2(7, 12) * Scale;
            fill.rectTransform.offsetMax = new Vector2(-7, -30) * Scale;
            var left = Image(body, "LeftRail");
            Paint(left, Slice("CostLeftRail", "SpecialCost_Expanded_Exact", 0, 40, 8, 100));
            left.rectTransform.anchorMin = new Vector2(0, 0);
            left.rectTransform.anchorMax = new Vector2(0, 1);
            left.rectTransform.pivot = new Vector2(0, .5f);
            left.rectTransform.offsetMin = new Vector2(0, 20) * Scale;
            left.rectTransform.offsetMax = new Vector2(8, -30) * Scale;
            var right = Image(body, "RightRail");
            Paint(right, Slice("CostRightRail", "SpecialCost_Expanded_Exact", 76, 40, 8, 100));
            right.rectTransform.anchorMin = new Vector2(1, 0);
            right.rectTransform.anchorMax = new Vector2(1, 1);
            right.rectTransform.pivot = new Vector2(1, .5f);
            right.rectTransform.offsetMin = new Vector2(-8, 20) * Scale;
            right.rectTransform.offsetMax = new Vector2(0, -30) * Scale;
            var bottom = Image(body, "Bottom");
            Paint(bottom, Slice("CostBottom", "SpecialCost_Expanded_Exact", 0, 157, 84, 24));
            bottom.rectTransform.anchorMin = bottom.rectTransform.anchorMax = new Vector2(0, 0);
            bottom.rectTransform.pivot = new Vector2(0, 0);
            bottom.rectTransform.anchoredPosition = Vector2.zero;
            bottom.rectTransform.sizeDelta = new Vector2(84, 24) * Scale;
            var toggle = Find(root, "CostToggle");
            Paint(toggle.GetComponent<Image>(), Sprite("SpecialCost_Collapsed_Exact"), true);
            Local(toggle, 0, 0, 84, 34);
            // Cover the baked arrow with a blank source-region and display one rotatable chevron.
            var patch = Image(toggle, "ChevronWell");
            Paint(patch, Slice("CostWell", "SpecialCost_Expanded_Exact", 54, 50, 18, 18));
            Local(patch.rectTransform, 27, 8, 31, 17);
            var chevron = Find(toggle, "CostChevron");
            Paint(chevron.GetComponent<Image>(), Slice("CostChevron", "SpecialCost_Collapsed_Exact", 28, 9, 28, 16));
            Local(chevron, 28, 9, 28, 16);
            chevron.pivot = new Vector2(.5f, .5f);
            chevron.anchoredPosition += new Vector2(14, -8) * Scale;
            chevron.SetAsLastSibling();
            var viewport = Find(root, "CostViewport");
            Local(viewport, 7, 35, 70, 130);
            viewport.GetComponent<Image>().color = Color.clear;
            var content = Find(viewport, "CostContent");
            content.sizeDelta = new Vector2(70 * Scale, 0);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 6;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static void CostItem(GameObject root)
        {
            var frame = root.GetComponent<Image>();
            frame.sprite = null;
            frame.color = Color.clear;
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(70 * Scale, 46);
            var data = new SerializedObject(root.GetComponent<BattleCostItemView>());
            data.FindProperty("_tintOnHover").boolValue = false;
            data.FindProperty("_showMaximum").boolValue = false;
            data.ApplyModifiedPropertiesWithoutUndo();
            var element = root.GetComponent<LayoutElement>();
            element.preferredHeight = 46;
            Place(Find(root.transform, "CostIcon"), 3, 5, 34, 36);
            var value = Find(root.transform, "CostValue").GetComponent<TMP_Text>();
            Place(value.rectTransform, 38, 1, 39, 42);
            Style(value, Ivory, 32, 16);
            var definition = AssetDatabase.LoadAssetAtPath<BattleCostDefinition>("Assets/Art/UI/BattleCompact/YokaiCost.asset");
            var definitionData = new SerializedObject(definition);
            definitionData.FindProperty("_icon").objectReferenceValue = Slice("CostFire", "SpecialCost_Expanded_Exact", 13, 43, 31, 34);
            definitionData.ApplyModifiedPropertiesWithoutUndo();
            Slice("CostMoon", "SpecialCost_Expanded_Exact", 13, 82, 31, 34);
            Slice("CostStar", "SpecialCost_Expanded_Exact", 13, 121, 31, 34);
        }

        private static Sprite Slice(string name, string source, int x, int top, int width, int height)
        {
            string path = Art + "Slices/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null)
            {
                return existing;
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "png/" + source + ".png");
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(x, texture.height - top - height, width, height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            AssetDatabase.CreateAsset(sprite, path);
            return sprite;
        }

        private static void Style(TMP_Text label, Color color, float max, float min)
        {
            var font = label.GetComponent<LocalizedBattleFont>() ?? label.gameObject.AddComponent<LocalizedBattleFont>();
            var data = new SerializedObject(font);
            data.FindProperty("_useAuthoredColor").boolValue = true;
            data.FindProperty("_authoredColor").colorValue = color;
            data.ApplyModifiedPropertiesWithoutUndo();
            label.color = color;
            label.enableAutoSizing = true;
            label.fontSizeMax = max * Scale;
            label.fontSizeMin = min * Scale;
            label.fontSize = max * Scale;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
        }

        private static void Paint(Image image, Sprite sprite, bool raycast = false)
        {
            image.sprite = sprite;
            image.color = Color.white;
            image.enabled = true;
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = raycast;
        }

        private static Sprite Sprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(Art + "png/" + name + ".png") ?? throw new InvalidOperationException("Missing original mockup sprite: " + name);
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

        private static RectTransform Find(Transform root, string name)
        {
            return Optional(root, name) ?? throw new InvalidOperationException(root.name + " requires " + name);
        }

        private static RectTransform Optional(Transform root, string name)
        {
            return (RectTransform)root.GetComponentsInChildren<Transform>(true).FirstOrDefault(value => value.name == name);
        }

        private static RectTransform Rect(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child == null)
            {
                child = new GameObject(name, typeof(RectTransform)).transform;
                child.SetParent(parent, false);
            }
            return (RectTransform)child;
        }

        private static Image Image(Transform parent, string name)
        {
            var rect = Rect(parent, name);
            return rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Local(RectTransform rect, float x, float y, float width, float height)
        {
            Place(rect, x * Scale, y * Scale, width * Scale, height * Scale);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void ScreenRect(RectTransform rect, float x, float y, float width, float height, Vector2 anchor)
        {
            Place(rect, x * Scale, y * Scale, width * Scale, height * Scale);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = new Vector2(x * Scale - 1920 * anchor.x, -y * Scale + 1080 * (1 - anchor.y));
        }
    }
}
