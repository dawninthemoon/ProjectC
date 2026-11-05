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
    // Explicit prefab migration. Never runs automatically on scene load or changes match rules.
    public static class ApplySunsetCelBattleUi
    {
        private const string Art = "Assets/Art/UI/SunsetCel/";
        private const string Prefabs = "Assets/Prefabs/Battle/";

        [MenuItem("Project C/Apply Sunset Cel battle UI")]
        public static void Apply()
        {
            CreateArtworkMasks();
            AssetDatabase.Refresh();
            foreach (string path in Directory.GetFiles(Art, "*.png", SearchOption.AllDirectories))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            Edit("BattleCard", root => Card(root, true));
            Edit("BattleFieldCard", root => Card(root, false));
            Edit("CardBack", root =>
            {
                var frame = Find(root.transform, "FrameImage").GetComponent<Image>();
                SetSprite(frame, "FloralCardBack");
                frame.preserveAspect = true;
                Find(frame.transform, "Inset").GetComponent<Image>().enabled = false;
            });
            Edit("BattleScreen", Screen);
            AssetDatabase.SaveAssets();
            Debug.Log("Sunset Cel UI applied to battle screen, hand, field and card back prefabs.");
        }

        private static void Card(GameObject root, bool hand)
        {
            var widget = root.GetComponent<CardWidget>();
            var frame = Find(root.transform, "FrameImage");
            // Fit the new aspect ratio inside the existing interaction/layout slot.
            var slot = ((RectTransform)root.transform).rect.size;
            float ratio = hand ? 180f / 244f : 180f / 228f;
            float height = Mathf.Min(slot.y, slot.x / ratio);
            Fixed(frame, new Vector2(.5f, .5f), Vector2.zero, new Vector2(height * ratio, height));
            frame.localRotation = Quaternion.identity;
            SetSprite(frame.GetComponent<Image>(), hand ? "HandCardFrame" : "FieldMonsterFrame");
            frame.GetComponent<Image>().raycastTarget = true;
            var inset = Find(frame, "Inset");
            Stretch(inset);
            // Keep the component/hierarchy but remove the old rectangular CardInset artwork.
            inset.GetComponent<Image>().enabled = false;
            var mask = Find(inset, "ArtworkMask");
            Stretch(mask);
            SetSprite(mask.GetComponent<Image>(), hand ? "HandArtworkMask" : "FieldArtworkMask");
            var stencil = mask.GetComponent<Mask>() ?? mask.gameObject.AddComponent<Mask>();
            stencil.showMaskGraphic = false;
            var rectMask = mask.GetComponent<RectMask2D>();
            if (rectMask != null)
            {
                Object.DestroyImmediate(rectMask);
            }
            Stretch(Find(mask, "ArtworkImage"));
            foreach (var label in Find(mask, "ArtworkImage").GetComponentsInChildren<TMP_Text>(true))
            {
                label.enabled = false;
            }
            var overlay = Find(frame, "RenewalFrame").GetComponent<Image>();
            Stretch(overlay.rectTransform);
            SetSprite(overlay, hand ? "HandCardFrame" : "FieldMonsterFrame");
            overlay.transform.SetAsLastSibling();
            var glow = Find(frame, "FrameGlow").GetComponent<Image>();
            SetSprite(glow, hand ? "HandCardFrame" : "FieldMonsterFrame");
            Stretch(glow.rectTransform);
            glow.rectTransform.sizeDelta = new Vector2(12, 12);
            glow.color = Color.clear;
            glow.transform.SetAsFirstSibling();

            var plate = Find(frame, "RenewalTitle");
            plate.gameObject.SetActive(hand);
            if (hand)
            {
                SetSprite(plate.GetComponent<Image>(), "HandTitlePlate");
                Fixed(plate, new Vector2(.5f, 1), new Vector2(0, -5), new Vector2(frame.rect.width * 150f / 180f, 32));
                plate.SetAsLastSibling();
                var title = Find(frame, "Name").GetComponent<TMP_Text>();
                title.transform.SetParent(plate, false);
                Stretch(title.rectTransform);
                title.rectTransform.offsetMin = new Vector2(10, 2);
                title.rectTransform.offsetMax = new Vector2(-10, -2);
                title.alignment = TextAlignmentOptions.Center;
                Ink(title);
                title.fontSizeMin = 10;
                title.fontSizeMax = 18;
                title.enableAutoSizing = true;
                title.raycastTarget = false;
            }
            else
            {
                Find(frame, "Name").gameObject.SetActive(false);
                Find(frame, "FieldStatusFrame").gameObject.SetActive(false);
            }
            Badge(frame, "HealthBadge", false, hand);
            Badge(frame, "AttackBadge", true, hand);
            // These prefabs have no top-left cost space. Preserve the optional activation-cost
            // binding for compatibility, but keep its graphics hidden even when Bind activates it.
            foreach (var graphic in Find(frame, "CostBadgeFrame").GetComponentsInChildren<Graphic>(true))
            {
                graphic.enabled = false;
                graphic.raycastTarget = false;
            }
            foreach (var graphic in inset.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }
            widget.FrameImage = frame.GetComponent<Image>();
        }

        private static void Badge(RectTransform frame, string name, bool right, bool hand)
        {
            var badge = Find(frame, name + "Frame");
            var size = new Vector2(hand ? 40 : 44, hand ? 40f * 52f / 48f : 44f * 52f / 48f);
            var anchor = new Vector2(right ? 1 : 0, 0);
            Fixed(badge, anchor, new Vector2(right ? -3 : 3, 1), size, anchor);
            SetSprite(badge.GetComponent<Image>(), name);
            var outline = badge.GetComponent<Outline>();
            if (outline != null)
            {
                outline.enabled = false;
            }
            badge.SetAsLastSibling();
            var value = Find(badge, name).GetComponent<TMP_Text>();
            Stretch(value.rectTransform);
            value.rectTransform.offsetMin = new Vector2(4, 6);
            value.rectTransform.offsetMax = new Vector2(-4, -4);
            value.alignment = TextAlignmentOptions.Center;
            value.enableAutoSizing = true;
            value.fontSizeMin = 14;
            value.fontSizeMax = hand ? 29 : 32;
            value.raycastTarget = false;
            value.color = new Color(1, .96f, .87f);
        }

        private static void Screen(GameObject root)
        {
            var ui = root.GetComponent<BattlePresenter>();
            var background = Find(root.transform, "Background");
            background.gameObject.SetActive(true);
            var backdrop = Find(background, "MoonlitArenaBackdrop");
            backdrop.gameObject.SetActive(true);
            SetSprite(backdrop.GetComponent<Image>(), "BattleBackground_SunsetCel_1920x1080");
            Stretch(backdrop);
            backdrop.GetComponent<Image>().preserveAspect = false;
            // The supplied background covers the existing 3D stage without touching its scene.
            var backgroundGroup = background.GetComponent<CanvasGroup>();
            if (backgroundGroup != null)
            {
                backgroundGroup.alpha = 1;
            }
            foreach (var image in background.GetComponentsInChildren<Image>(true))
            {
                image.raycastTarget = false;
            }
            foreach (string name in new[] { "YourDeck", "OpponentDeck" })
            {
                SetSprite(Find(root.transform, name).GetComponent<Image>(), "FloralCardBack");
            }
            Zones(Find(root.transform, "OwnResources"), "Own");
            Zones(Find(root.transform, "EnemyResources"), "Enemy");
            TurnHud(ui);
            PhaseButton(ui);
        }

        private static void Zones(RectTransform row, string prefix)
        {
            string[] names = { "Deck", "Grave", "Exile" };
            for (int i = 0; i < names.Length; ++i)
            {
                var frame = Find(row, "RenewalZone" + i).GetComponent<Image>();
                SetSprite(frame, "ZoneOmamoriBase");
                frame.preserveAspect = true;
                var icon = Find(row, prefix + names[i] + "Icon").GetComponent<Image>();
                SetSprite(icon, "ZoneIcon" + names[i]);
                icon.preserveAspect = true;
                // Preserve row/slot positions and existing browse buttons, adjust only overlays.
                float x = frame.rectTransform.anchoredPosition.x;
                float width = frame.rectTransform.rect.width;
                float height = frame.rectTransform.rect.height;
                Place(icon.rectTransform, x + (width - 38) / 2, height * .25f, 38, 38);
                var count = Find(row, prefix + names[i] + "Count").GetComponent<TMP_Text>();
                Place(count.rectTransform, x + 10, height * .64f, width - 20, 30);
                count.alignment = TextAlignmentOptions.Center;
                Ink(count);
                count.raycastTarget = false;
            }
        }

        private static void TurnHud(BattlePresenter ui)
        {
            var hud = Find(ui.transform, "TurnHud");
            SetSprite(hud.GetComponent<Image>(), "TurnStatusBase");
            hud.GetComponent<Image>().preserveAspect = true;
            // Keep the HUD root at its authored upper-left position and size.
            Place(ui.TurnLabel.rectTransform, 25, 38, 40, 33);
            Ink(ui.TurnLabel);
            Place(Find(hud, "TurnUnit"), 30, 70, 32, 14);
            Ink(Find(hud, "TurnUnit").GetComponent<TMP_Text>());
            Place(Find(hud, "TurnOwner"), 83, 39, 150, 34);
            Place(Find(hud, "TurnOwnerAccent"), 83, 39, 150, 34);
            Find(hud, "TurnOwnerAccent").GetComponent<Image>().enabled = false;
            var track = Find(hud, "TimeTrack").GetComponent<Image>();
            Place(track.rectTransform, 250, 14, 78, 78);
            var ring = Find(hud, "TimeRing").GetComponent<Image>();
            Place(ring.rectTransform, 250, 14, 78, 78);
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.raycastTarget = false;
            var ornament = Decor(hud, "TimerRingFrame", "TimerRingFrame");
            Place(ornament.rectTransform, 244, 8, 90, 90);
            // The ornament is a separate Simple image; only TimeRing consumes the live fill.
            ornament.transform.SetAsLastSibling();
            Place(Find(hud, "TurnTimer"), 263, 33, 52, 34);
            Place(Find(hud, "TimeUnit"), 273, 64, 32, 16);
            Find(hud, "TurnTimer").SetAsLastSibling();
            Find(hud, "TimeUnit").SetAsLastSibling();
        }

        private static void PhaseButton(BattlePresenter ui)
        {
            var button = ui.EndTurn;
            SetSprite(button.GetComponent<Image>(), "PhaseButtonBase", true);
            button.GetComponent<Image>().preserveAspect = true;
            button.targetGraphic = button.GetComponent<Image>();
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1, .91f, .8f);
            colors.pressedColor = new Color(.78f, .64f, .62f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = .1f;
            button.colors = colors;
            var rect = (RectTransform)button.transform;
            float width = rect.rect.width;
            float height = rect.rect.height;
            Find(rect, "PhaseInside").GetComponent<Image>().enabled = false;
            Find(rect, "TurnPhasePlate").gameObject.SetActive(false);
            var label = Find(rect, "Label").GetComponent<TMP_Text>();
            Place(ui.PhaseLabel.rectTransform, width * .2f, height * .167f, width * .6f, height * .1f);
            Ink(ui.PhaseLabel);
            Place(label.rectTransform, width * .17f, height * .38f, width * .66f, height * .18f);
            label.fontSizeMax = 38;
            label.fontSizeMin = 20;
            label.enableAutoSizing = true;
            label.alignment = TextAlignmentOptions.Center;
            var arrow = Find(rect, "TurnArrow").GetComponent<Image>();
            SetSprite(arrow, "PhaseArrow");
            Place(arrow.rectTransform, width * .35f, height * .59f, width * .3f, width * .3f * 48f / 88f);
            var glow = Find(rect, "TurnGlow").GetComponent<Image>();
            SetSprite(glow, "PhaseArrow");
            glow.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/UI/BattleRenewalStage3/BattleFrameGlow.mat");
            Place(glow.rectTransform, width * .35f - 4, height * .59f - 2, width * .3f + 8, width * .3f * 48f / 88f + 4);
        }

        private static void CreateArtworkMasks()
        {
            // Same outer paths as the supplied SVGs, inset beneath the opaque border. This
            // clips illustration corners without rectangular CardInset protrusions.
            var hand = new System.Collections.Generic.List<Vector2>();
            Add(hand, new Vector2(13, 19), new Vector2(13, 9), new Vector2(25, 9));
            hand.Add(new Vector2(81, 6));
            Add(hand, hand.Last(), new Vector2(90, 3), new Vector2(99, 6));
            hand.Add(new Vector2(155, 9));
            Add(hand, hand.Last(), new Vector2(167, 9), new Vector2(167, 19));
            hand.Add(new Vector2(173, 216));
            Add(hand, hand.Last(), new Vector2(172, 230), new Vector2(159, 232));
            hand.Add(new Vector2(98, 239));
            Add(hand, hand.Last(), new Vector2(90, 241), new Vector2(82, 239));
            hand.Add(new Vector2(21, 232));
            Add(hand, hand.Last(), new Vector2(8, 230), new Vector2(7, 216));
            var field = new System.Collections.Generic.List<Vector2>();
            Cubic(field, new Vector2(8, 46), new Vector2(8, 20), new Vector2(34, 9), new Vector2(90, 9));
            Cubic(field, field.Last(), new Vector2(146, 9), new Vector2(172, 20), new Vector2(172, 46));
            field.Add(new Vector2(172, 201));
            Add(field, field.Last(), new Vector2(172, 219), new Vector2(156, 219));
            field.Add(new Vector2(24, 219));
            Add(field, field.Last(), new Vector2(8, 219), new Vector2(8, 201));
            WriteMask("HandArtworkMask", hand.ToArray(), 180, 244);
            WriteMask("FieldArtworkMask", field.ToArray(), 180, 228);
        }

        private static void Ink(TMP_Text label)
        {
            var font = label.GetComponent<LocalizedBattleFont>() ?? label.gameObject.AddComponent<LocalizedBattleFont>();
            var data = new SerializedObject(font);
            data.FindProperty("_useAuthoredColor").boolValue = true;
            data.FindProperty("_authoredColor").colorValue = new Color(.14f, .1f, .14f);
            data.ApplyModifiedPropertiesWithoutUndo();
            label.color = new Color(.14f, .1f, .14f);
        }

        private static void Add(System.Collections.Generic.List<Vector2> points, Vector2 a, Vector2 b, Vector2 c)
        {
            const int curveSteps = 24;
            for (int i = 0; i <= curveSteps; ++i)
            {
                float t = (float)i / curveSteps;
                points.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c);
            }
        }

        private static void Cubic(System.Collections.Generic.List<Vector2> points, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            const int curveSteps = 32;
            for (int i = 0; i <= curveSteps; ++i)
            {
                float t = (float)i / curveSteps;
                points.Add(Mathf.Pow(1 - t, 3) * a + 3 * Mathf.Pow(1 - t, 2) * t * b + 3 * (1 - t) * t * t * c + t * t * t * d);
            }
        }

        private static void WriteMask(string name, Vector2[] polygon, int width, int height)
        {
            const int rasterScale = 2;
            var texture = new Texture2D(width * rasterScale, height * rasterScale, TextureFormat.RGBA32, false);
            var pixels = new Color32[texture.width * texture.height];
            for (int y = 0; y < texture.height; ++y)
            {
                float py = height - (y + .5f) / rasterScale;
                for (int x = 0; x < texture.width; ++x)
                {
                    float px = (x + .5f) / rasterScale;
                    bool inside = false;
                    for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                    {
                        var a = polygon[i];
                        var b = polygon[j];
                        if ((a.y > py) != (b.y > py) && px < (b.x - a.x) * (py - a.y) / (b.y - a.y) + a.x)
                        {
                            inside = !inside;
                        }
                    }
                    pixels[y * texture.width + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Art + "png/" + name + ".png", texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
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

        private static void SetSprite(Image image, string name, bool raycast = false)
        {
            string path = Art + (name.StartsWith("BattleBackground", StringComparison.Ordinal) ? "" : "png/") + name + ".png";
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path) ?? throw new InvalidOperationException("Missing Sunset Cel sprite: " + path);
            image.enabled = true;
            image.color = Color.white;
            image.type = Image.Type.Simple;
            image.raycastTarget = raycast;
        }

        private static Image Decor(Transform parent, string name, string sprite)
        {
            var child = parent.Find(name);
            if (child == null)
            {
                child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).transform;
                child.SetParent(parent, false);
            }
            var image = child.GetComponent<Image>();
            SetSprite(image, sprite);
            return image;
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

        private static void Fixed(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot ?? anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            Fixed(rect, new Vector2(0, 1), new Vector2(x, -y), new Vector2(width, height));
        }
    }
}
