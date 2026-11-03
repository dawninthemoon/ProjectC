using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Client.Editor
{
    // Explicit prefab migration. Existing serialized references and the scene camera are preserved.
    public static class UpgradeIntegratedBattleAssets
    {
        private const string Folder = "Assets/Prefabs/Battle/";
        private static readonly Color Gold = new Color(.82f, .65f, .36f);
        private static readonly Color Panel = new Color(.035f, .085f, .115f, .97f);
        private static Sprite _circle;
        private static TMP_Text _template;

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component == null ? target.AddComponent<T>() : component;
        }

        private static RectTransform Find(GameObject root, string name)
        {
            return root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == name);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var existing = parent.Cast<Transform>().FirstOrDefault(t => t.name == name);
            var rect = existing != null ? (RectTransform)existing : (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            if (existing == null)
            {
                rect.SetParent(parent, false);
            }
            Place(rect, x, y, width, height);
            return rect;
        }

        private static Image Box(Transform parent, string name, float x, float y, float width, float height, Color color, Sprite sprite = null)
        {
            var rect = Rect(parent, name, x, y, width, height);
            var image = GetOrAdd<Image>(rect.gameObject);
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text Label(Transform parent, string name, string text, float x, float y, float width, float height, int size = 22)
        {
            var existing = parent.Cast<Transform>().FirstOrDefault(t => t.name == name);
            var label = existing != null ? existing.GetComponent<TMP_Text>() : Object.Instantiate(_template, parent);
            label.name = name;
            label.gameObject.SetActive(true);
            Place(label.rectTransform, x, y, width, height);
            label.text = text;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        private static void ImportSprite(string path, bool ui = false)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Missing image: " + path);
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = ui ? 256 : 1024;
            importer.SaveAndReimport();
        }

        [MenuItem("ProjectC/Upgrade Integrated Battle UI")]
        public static void Upgrade()
        {
            foreach (string name in new[] { "Deck", "Grave", "Exile", "Life", "Yokai" })
            {
                ImportSprite("Assets/Resources/Icons/Icon_" + name + ".png", true);
            }
            foreach (string name in new[] { "Barbaros", "Megachiyo" })
            {
                ImportSprite("Assets/Resources/Portraits/" + name + ".png");
            }
            // A reusable circle supplies portrait masks and the phase control without a UI package dependency.
            const int circleSize = 128;
            var texture = new Texture2D(circleSize, circleSize, TextureFormat.RGBA32, false);
            for (int y = 0; y < circleSize; ++y)
            {
                for (int x = 0; x < circleSize; ++x)
                {
                    float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * circleSize * .5f);
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(circleSize * .5f - distance)));
                }
            }
            System.IO.File.WriteAllBytes("Assets/Resources/Icons/UI_Circle.png", texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.Refresh();
            ImportSprite("Assets/Resources/Icons/UI_Circle.png", true);
            _circle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Icons/UI_Circle.png");
            Edit(Folder + "BattleCard.prefab", ConfigureCard);
            Edit(Folder + "BattleScreen.prefab", ConfigureScreen);
            AssetDatabase.SaveAssets();
            Debug.Log("Integrated battle UI saved.");
        }

        private static void Edit(string path, Action<GameObject> configure)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                configure(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureCard(GameObject root)
        {
            _template = root.GetComponent<CardWidget>().Title;
            var frame = Find(root, "FrameImage");
            var cost = Box(frame, "CostBadgeFrame", 2, 2, 36, 36, new Color(.21f, .6f, .42f), _circle);
            Label(cost.transform, "CostBadge", "1", 0, 0, 36, 36, 24);
            cost.transform.SetAsLastSibling();
            var outline = GetOrAdd<Outline>(frame.gameObject);
            outline.effectDistance = new Vector2(3, -3);
            outline.enabled = false;
            var mask = Find(root, "ArtworkMask");
            var rectMask = mask.GetComponent<RectMask2D>();
            if (rectMask != null)
            {
                Object.DestroyImmediate(rectMask);
            }
            var image = GetOrAdd<Image>(mask.gameObject);
            image.color = Color.white;
            image.raycastTarget = false;
            var stencil = GetOrAdd<Mask>(mask.gameObject);
            stencil.showMaskGraphic = false;
        }

        private static void Portrait(PlayerPanelView player, float x, float y, string asset)
        {
            if (PrefabUtility.IsAnyPrefabInstanceRoot(player.gameObject))
            {
                PrefabUtility.UnpackPrefabInstance(player.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            var rect = (RectTransform)player.transform;
            Place(rect, x, y, 160, 160);
            var background = player.GetComponent<Image>();
            background.sprite = _circle;
            background.color = Gold;
            foreach (var image in player.GetComponentsInChildren<Image>(true))
            {
                image.raycastTarget = false;
            }
            background.raycastTarget = true;
            var portrait = Box(rect, "PortraitMask", 7, 7, 146, 146, Color.white, _circle);
            var mask = GetOrAdd<Mask>(portrait.gameObject);
            mask.showMaskGraphic = false;
            var art = Box(portrait.transform, "PortraitImage", 0, 0, 146, 146, Color.white);
            Object.DestroyImmediate(art);
            var fill = portrait.transform.Find("PortraitImage").gameObject.AddComponent<AspectFillImage>();
            fill.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Portraits/" + asset + ".png");
            fill.raycastTarget = false;
            portrait.transform.SetAsFirstSibling();
            var life = Find(player.gameObject, "Life");
            var badge = Box(rect, "LifeBadge", 112, 112, 96, 52, new Color(.48f, .1f, .15f));
            life.SetParent(badge.transform, false);
            Place(life, 28, 0, 68, 52);
            var text = life.GetComponent<TMP_Text>();
            text.fontSize = 32;
            text.alignment = TextAlignmentOptions.Center;
            Box(badge.transform, "LifeIcon", 3, 16, 26, 26, Color.white,
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Icons/Icon_Life.png"));
            Find(player.gameObject, "Name").gameObject.SetActive(false);
            Find(player.gameObject, "Counts").gameObject.SetActive(false);
            Find(player.gameObject, "LifeCaption").gameObject.SetActive(false);
            var mark = Find(player.gameObject, "TurnMark");
            Place(mark, 34, 166, 92, 3);
        }

        private static void ResourceRow(Transform parent, string prefix, float y)
        {
            var row = Box(parent, prefix + "Resources", 1550, y, 340, 50, Panel).transform;
            Label(row, prefix + "ResourceLabel", prefix == "Own" ? "나" : "상대", 4, 5, 42, 40, 15);
            var kinds = new[] { "Deck", "Grave", "Exile" };
            for (int i = 0; i < kinds.Length; ++i)
            {
                string kind = kinds[i];
                Box(row, prefix + kind + "Icon", 50 + i * 94, 10, 30, 30, Color.white,
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Icons/Icon_" + kind + ".png"));
                Label(row, prefix + kind + "Count", "0", 82 + i * 94, 6, 48, 38);
                if (kind != "Deck")
                {
                    var hit = Box(row, prefix + kind + "Browse", 48 + i * 94, 0, 92, 50, Color.clear);
                    hit.raycastTarget = true;
                    var button = GetOrAdd<Button>(hit.gameObject);
                    button.targetGraphic = hit;
                    var browser = GetOrAdd<PublicZoneButton>(hit.gameObject);
                    browser.Exile = kind == "Exile";
                }
            }
        }

        private static void ConfigureScreen(GameObject root)
        {
            var p = root.GetComponent<BattlePresenter>();
            _template = p.TurnLabel;
            var table = Find(root, "Table");
            table.GetComponent<Image>().color = new Color(.065f, .16f, .18f);
            foreach (string name in new[] { "Brand", "Edition", "EnemyFieldCaption", "OwnFieldCaption", "HandCaption", "Selection", "Connection", "Debug", "AutoStartHint" })
            {
                var node = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
                if (node != null)
                {
                    node.gameObject.SetActive(false);
                }
            }
            var space = Find(root, "CardInfoSpace");
            space.GetComponent<Image>().color = Color.clear;
            Place(space, 24, 220, 300, 650);
            Place(Find(root, "Battlefield"), 350, 230, 1180, 550);
            Place((RectTransform)p.EnemyField.transform, 390, 245, 1120, 210);
            Place((RectTransform)p.OwnField.transform, 390, 535, 1120, 210);
            Place(Find(root, "OwnFieldDropZone"), 360, 510, 1170, 265);
            Place(Find(root, "Midline"), 330, 495, 1560, 2);
            Portrait(p.EnemyPlayer, 900, 25, "Barbaros");
            Portrait(p.OwnPlayer, 900, 770, "Megachiyo");
            Place((RectTransform)p.EnemyHand.transform, 590, 10, 180, 100);
            Place((RectTransform)p.Hand.transform, 1220, 880, 660, 244);
            p.Hand.Layout.Compact = true;
            p.EnemyHand.Layout.Compact = true;
            // Hands render in front of the heroes while expanded.
            p.Hand.transform.SetAsLastSibling();
            Place(Find(root, "YourDeck"), 1580, 92, 30, 30);
            Place(Find(root, "OpponentDeck"), 1580, 32, 30, 30);
            Find(root, "YourDeck").GetComponent<Image>().color = Color.clear;
            Find(root, "OpponentDeck").GetComponent<Image>().color = Color.clear;
            foreach (string deck in new[] { "YourDeck", "OpponentDeck" })
            {
                foreach (var label in Find(root, deck).GetComponentsInChildren<TMP_Text>(true))
                {
                    label.gameObject.SetActive(false);
                }
            }
            ResourceRow(table, "Enemy", 20);
            ResourceRow(table, "Own", 78);
            if (table.GetComponent<BattleHudView>() == null)
            {
                table.gameObject.AddComponent<BattleHudView>();
            }
            var phase = (RectTransform)p.EndTurn.transform;
            Place(phase, 1600, 420, 270, 270);
            var phaseImage = p.EndTurn.GetComponent<Image>();
            phaseImage.sprite = _circle;
            phaseImage.color = Gold;
            var inside = Box(phase, "PhaseInside", 9, 9, 252, 252, new Color(.1f, .27f, .36f), _circle);
            inside.transform.SetAsFirstSibling();
            ConfigurePhaseAnimation(inside.gameObject);
            var phaseTitle = p.EndTurn.GetComponentInChildren<TMP_Text>();
            Place(phaseTitle.rectTransform, 20, 94, 230, 76);
            phaseTitle.fontSize = 26;
            phaseTitle.alignment = TextAlignmentOptions.Center;
            p.PhaseLabel.transform.SetParent(phase, false);
            Place(p.PhaseLabel.rectTransform, 20, 44, 230, 42);
            p.PhaseLabel.fontSize = 19;
            p.PhaseLabel.alignment = TextAlignmentOptions.Center;
            var timer = Find(root, "TurnTimer");
            timer.SetParent(phase, false);
            Place(timer, 20, 178, 230, 42);
            timer.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;
            Place(p.TurnLabel.rectTransform, 360, 38, 380, 44);
            p.TurnLabel.fontSize = 22;
            var yokai = Box(table, "YokaiPanel", 1590, 720, 290, 78, Panel).transform;
            Box(yokai, "YokaiIcon", 12, 12, 38, 38, Color.white,
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Icons/Icon_Yokai.png"));
            Label(yokai, "YokaiValue", "요력 0 / 0", 50, 5, 226, 46, 24);
            Box(yokai, "YokaiTrack", 20, 59, 250, 7, new Color(.14f, .25f, .29f));
            var gauge = Box(yokai, "YokaiFill", 20, 59, 250, 7, new Color(.25f, .8f, .77f), _circle);
            gauge.type = Image.Type.Filled;
            gauge.fillMethod = Image.FillMethod.Horizontal;
            Place((RectTransform)p.LogToggle.transform, 24, 960, 230, 54);
            p.LogToggle.GetComponentInChildren<TMP_Text>().text = "전투 기록";
            Place((RectTransform)p.LobbyToggle.transform, 24, 1025, 230, 36);
            p.LobbyToggle.GetComponentInChildren<TMP_Text>().text = "메뉴";
            p.GraveyardToggle.gameObject.SetActive(false);
            ConfigureGallery(root, table);
            ConfigurePopup(root);
            var picker = Find(root, "EffectTargetPicker");
            Place(picker, 520, 660, 880, 165);
            picker.GetComponent<Image>().color = Color.clear;
            Place(Find(picker.gameObject, "Title"), 15, 5, 850, 42);
            Place(Find(picker.gameObject, "Confirm"), 480, 105, 180, 44);
            Place(Find(picker.gameObject, "Cancel"), 680, 105, 180, 44);
            var candidates = Find(picker.gameObject, "Candidates");
            var viewport = (RectTransform)candidates.parent;
            Place(viewport, 15, 47, 440, 100);
            viewport.GetComponent<Image>().color = Color.clear;

        }

        private static void ConfigurePhaseAnimation(GameObject inside)
        {
            const string clipPath = Folder + "PhaseChange.anim";
            const string controllerPath = Folder + "PhaseChange.controller";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, clipPath);
            }
            clip.SetCurve("", typeof(Image), "m_Color.r", AnimationCurve.EaseInOut(0, .3f, .15f, .1f));
            clip.SetCurve("", typeof(Image), "m_Color.g", AnimationCurve.EaseInOut(0, .65f, .15f, .27f));
            clip.SetCurve("", typeof(Image), "m_Color.b", AnimationCurve.EaseInOut(0, .7f, .15f, .36f));
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            }
            if (controller.layers.Length == 0)
            {
                controller.AddLayer("Base Layer");
            }
            var stateMachine = controller.layers[0].stateMachine;
            if (stateMachine.states.Length == 0)
            {
                var state = stateMachine.AddState("PhaseChange");
                state.motion = clip;
                stateMachine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            var animator = GetOrAdd<Animator>(inside);
            animator.runtimeAnimatorController = controller;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            if (inside.GetComponent<PhasePulseView>() == null)
            {
                inside.AddComponent<PhasePulseView>();
            }
        }

        private static void ConfigureGallery(GameObject root, Transform table)
        {
            var panel = Box(table, "CardGallery", 350, 830, 1540, 240, Panel);
            panel.raycastTarget = true;
            if (panel.GetComponent<CardGalleryView>() == null)
            {
                panel.gameObject.AddComponent<CardGalleryView>();
            }
            Label(panel.transform, "GalleryTitle", "묘지", 15, 0, 1270, 32, 19);
            var button = Rect(panel.transform, "GalleryClose", 1360, 0, 160, 32);
            var image = GetOrAdd<Image>(button.gameObject);
            image.color = new Color(.15f, .26f, .31f);
            var close = GetOrAdd<Button>(button.gameObject);
            close.targetGraphic = image;
            Label(button, "GalleryCloseLabel", "닫기", 0, 0, 160, 32, 19);
            var viewport = Rect(panel.transform, "GalleryViewport", 15, 36, 1510, 200);
            var viewportImage = GetOrAdd<Image>(viewport.gameObject);
            viewportImage.color = Color.white;
            var mask = GetOrAdd<Mask>(viewport.gameObject);
            mask.showMaskGraphic = false;
            var content = Rect(viewport, "GalleryContent", 0, 0, 1510, 195);
            var layout = GetOrAdd<HorizontalLayoutGroup>(content.gameObject);
            layout.spacing = 14;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            var fitter = GetOrAdd<ContentSizeFitter>(content.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = GetOrAdd<ScrollRect>(panel.gameObject);
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = true;
            scroll.vertical = false;
            panel.gameObject.SetActive(false);
        }

        private static void ConfigurePopup(GameObject root)
        {
            const string controllerPath = Folder + "NumberPopup.controller";
            const string clipPath = Folder + "NumberPopup.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, clipPath);
            }
            clip.SetCurve("", typeof(CanvasGroup), "m_Alpha", new AnimationCurve(new Keyframe(0, 1), new Keyframe(.25f, 1), new Keyframe(.7f, 0)));
            clip.SetCurve("Number", typeof(RectTransform), "m_AnchoredPosition.y", AnimationCurve.EaseInOut(0, 0, .7f, 65));
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                controller.AddMotion(clip);
            }
            var popup = new GameObject("NumberPopup", typeof(RectTransform), typeof(CanvasGroup), typeof(Animator));
            try
            {
                popup.GetComponent<Animator>().runtimeAnimatorController = controller;
                popup.GetComponent<Animator>().updateMode = AnimatorUpdateMode.UnscaledTime;
                popup.GetComponent<CanvasGroup>().blocksRaycasts = false;
                popup.GetComponent<CanvasGroup>().interactable = false;
                Place((RectTransform)popup.transform, 0, 0, 180, 70);
                var label = Label(popup.transform, "Number", "−6", -90, 0, 180, 70, 44);
                label.fontStyle = FontStyles.Bold;
                label.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2, -2);
                var prefab = PrefabUtility.SaveAsPrefabAsset(popup, Folder + "NumberPopup.prefab");
                var data = new SerializedObject(root.GetComponent<BattleEffectsView>());
                data.FindProperty("_numberPopupPrefab").objectReferenceValue = prefab;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            finally
            {
                Object.DestroyImmediate(popup);
            }
        }
    }
}
