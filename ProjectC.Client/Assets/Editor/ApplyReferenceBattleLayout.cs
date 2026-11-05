using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    // One-time migration from reference screen pixels to authored Canvas coordinates.
    public static class ApplyReferenceBattleLayout
    {
        private const string Art = "Assets/Art/UI/BattleReferenceMatch/";

        [MenuItem("Project C/Apply reference battle layout")]
        public static void Apply()
        {
            foreach (string name in new[] { "FieldFrame", "FieldGlow", "AttackBadge", "HealthBadge", "HandFrame", "HandGlow", "HpBadge", "EnemyHpBadge", "ResourceFrame", "TurnButton", "TurnGlow", "YokaiPanel", "YokaiPill", "DeckIcon", "GraveIcon", "ExileIcon", "DetailFrame" })
            {
                string path = Art + name + ".png";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                if (name == "DetailFrame")
                {
                    importer.spriteBorder = new Vector4(28, 28, 28, 28);
                }
                importer.SaveAndReimport();
            }
            var settings = AssetDatabase.LoadAssetAtPath<BattleVisualSettings>("Assets/Prefabs/Battle/BattleVisualSettings.asset");
            var data = new SerializedObject(settings);
            data.FindProperty("_compactHandScale").floatValue = 205f / 194f;
            data.FindProperty("_expandedHandScale").floatValue = 205f / 194f;
            data.FindProperty("_compactHandSpacing").floatValue = 185f;
            data.FindProperty("_expandedHandSpacing").floatValue = 185f;
            data.ApplyModifiedPropertiesWithoutUndo();
            Edit("Assets/Prefabs/Battle/BattleFieldCard.prefab", field =>
            {
                ((RectTransform)field.transform).sizeDelta = new Vector2(166, 183);
                var frame = Find(field.transform, "FrameImage");
                frame.GetComponent<Image>().sprite = Sprite("FieldFrame");
                Find(frame, "RenewalFrame").GetComponent<Image>().sprite = Sprite("FieldFrame");
                Find(frame, "FrameGlow").GetComponent<Image>().sprite = Sprite("FieldGlow");
                Place((RectTransform)Find(frame, "AttackBadgeFrame"), 1, 150, 58, 58);
                Place((RectTransform)Find(frame, "HealthBadgeFrame"), 106, 147, 62, 64);
                Find(frame, "AttackBadgeFrame").GetComponent<Image>().sprite = Sprite("AttackBadge");
                Find(frame, "HealthBadgeFrame").GetComponent<Image>().sprite = Sprite("HealthBadge");
                Find(frame, "AttackBadgeFrame").GetComponent<Image>().raycastTarget = true;
                Find(frame, "HealthBadgeFrame").GetComponent<Image>().raycastTarget = true;
                Place((RectTransform)Find(frame, "FieldStatusFrame"), 53, 163, 60, 34);
                foreach (string name in new[] { "AttackBadge", "HealthBadge" })
                {
                    var text = Find(frame, name).GetComponent<TMP_Text>();
                    text.enableAutoSizing = true;
                    text.fontSizeMin = 22;
                    text.fontSizeMax = 39;
                    text.raycastTarget = false;
                }
            });
            Edit("Assets/Prefabs/Battle/BattleCard.prefab", hand =>
            {
                ((RectTransform)hand.transform).sizeDelta = new Vector2(194, 248);
                var frame = Find(hand.transform, "FrameImage");
                frame.GetComponent<Image>().sprite = Sprite("HandFrame");
                Find(frame, "RenewalFrame").GetComponent<Image>().sprite = Sprite("HandFrame");
                Find(frame, "FrameGlow").GetComponent<Image>().sprite = Sprite("HandGlow");
            });
            Edit("Assets/Prefabs/Battle/CardBack.prefab", back =>
            {
                ((RectTransform)back.transform).sizeDelta = new Vector2(88, 135);
                Find(back.transform, "FrameImage").GetComponent<Image>().preserveAspect = true;
            });
            Edit("Assets/Prefabs/Battle/BattleScreen.prefab", root =>
            {
                var presenter = root.GetComponent<BattlePresenter>();
                var turn = presenter.EndTurn;
                PlaceScreen((RectTransform)turn.transform, 1632, 352, 234, 360, new Vector2(1, .5f));
                turn.GetComponent<Image>().sprite = Sprite("TurnButton");
                var colors = turn.colors;
                colors.disabledColor = new Color(.55f, .65f, .63f, 1);
                turn.colors = colors;
                Find(turn.transform, "TurnGlow").GetComponent<Image>().sprite = Sprite("TurnGlow");
                Find(turn.transform, "PhaseInside").GetComponent<Image>().enabled = false;
                Place((RectTransform)Find(turn.transform, "TurnPhasePlate"), 71, 73, 142, 33);
                Place(presenter.PhaseLabel.rectTransform, 71, 73, 142, 33);
                var main = turn.GetComponentsInChildren<TMP_Text>(true).First(text => text != presenter.PhaseLabel && text.name == "Label");
                Place(main.rectTransform, 32, 155, 170, 56);
                main.enableAutoSizing = true;
                main.fontSizeMin = 16;
                main.fontSizeMax = 38;
                main.textWrappingMode = TextWrappingModes.NoWrap;
                presenter.PhaseLabel.enableAutoSizing = true;
                presenter.PhaseLabel.fontSizeMin = 16;
                presenter.PhaseLabel.fontSizeMax = 23;
                Place((RectTransform)Find(turn.transform, "TurnArrow"), 92, 207, 52, 38);
                Place((RectTransform)Find(turn.transform, "TurnTimer"), 38, 274, 158, 32);
                PlaceScreen((RectTransform)presenter.EnemyField.transform, 398, 246, 1120, 215, new Vector2(.5f, .5f));
                PlaceScreen((RectTransform)presenter.OwnField.transform, 398, 499, 1120, 215, new Vector2(.5f, .5f));
                PlaceScreen((RectTransform)Find(root.transform, "OwnFieldDropZone"), 368, 493, 1180, 245, new Vector2(.5f, .5f));
                PlaceScreen((RectTransform)presenter.EnemyHand.transform, 349, 0, 464, 135, new Vector2(.5f, 1));
                Place((RectTransform)presenter.Hand.transform, 512, 803, 1050, 200);
                Hero(presenter.OwnPlayer, 10, 745, 325, 335, 278, 120, 77, 85, false);
                Hero(presenter.EnemyPlayer, 805, 0, 285, 215, 273, 100, 82, 115, true);
                ResourceRow(Find(root.transform, "OwnResources"), "Own", 1560, 870, false);
                ResourceRow(Find(root.transform, "EnemyResources"), "Enemy", 1528, 80, true);
                var yokai = Find(root.transform, "YokaiPanel");
                PlaceScreen((RectTransform)yokai, 352, 797, 148, 68, new Vector2(0, 0));
                yokai.GetComponent<Image>().sprite = Sprite("YokaiPanel");
                yokai.GetComponent<Image>().color = Color.white;
                var yokaiRow = yokai.Find("YokaiRow");
                if (yokaiRow == null)
                {
                    yokaiRow = new GameObject("YokaiRow", typeof(RectTransform)).transform;
                    yokaiRow.SetParent(yokai, false);
                }
                Place((RectTransform)yokaiRow, 20, 15, 115, 38);
                var yokaiIcon = Find(yokai, "YokaiIcon");
                yokaiIcon.SetParent(yokaiRow, false);
                Place((RectTransform)yokaiIcon, 0, 0, 38, 38);
                var value = Find(yokai, "YokaiValue").GetComponent<TMP_Text>();
                value.transform.SetParent(yokaiRow, false);
                Place(value.rectTransform, 38, 0, 77, 38);
                value.enableAutoSizing = true;
                value.fontSizeMin = 14;
                value.fontSizeMax = 26;
                Place((RectTransform)Find(yokai, "YokaiTrack"), 15, 51, 118, 5);
                Place((RectTransform)Find(yokai, "YokaiFill"), 15, 51, 118, 5);
                EnemyYokai(Find(root.transform, "Table"), yokaiIcon.GetComponent<Image>(), value);
                PlaceScreen((RectTransform)Find(root.transform, "CardInfoSpace"), 24, 260, 300, 480, new Vector2(0, 1));
                Inspector(presenter.Detail);
            });
            AssetDatabase.SaveAssets();
        }

        private static void EnemyYokai(Transform table, Image ownIcon, TMP_Text ownValue)
        {
            var panel = table.Find("EnemyYokaiPanel");
            if (panel == null)
            {
                panel = new GameObject("EnemyYokaiPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).transform;
                panel.SetParent(table, false);
                var icon = UnityEngine.Object.Instantiate(ownIcon, panel);
                icon.name = "EnemyYokaiIcon";
                var value = UnityEngine.Object.Instantiate(ownValue, panel);
                value.name = "EnemyYokaiValue";
            }
            PlaceScreen((RectTransform)panel, 553, 160, 182, 47, new Vector2(.5f, 1));
            var image = panel.GetComponent<Image>();
            image.sprite = Sprite("YokaiPill");
            image.raycastTarget = false;
            Place((RectTransform)Find(panel, "EnemyYokaiIcon"), 8, 5, 38, 38);
            var label = Find(panel, "EnemyYokaiValue").GetComponent<TMP_Text>();
            Place(label.rectTransform, 46, 4, 126, 39);
            label.fontSizeMax = 26;
        }

        private static void Hero(PlayerPanelView panel, float x, float y, float width, float height,
            float badgeX, float badgeY, float badgeWidth, float badgeHeight, bool enemy)
        {
            var root = (RectTransform)panel.transform;
            root.localScale = Vector3.one;
            PlaceScreen(root, x, y, width, height, enemy ? new Vector2(.5f, 1) : Vector2.zero);
            // Center the circular target graphic inside the measured portrait region.
            root.pivot = new Vector2(.5f, .5f);
            root.anchoredPosition += new Vector2(width * .5f, -height * .5f);
            root.GetComponent<Image>().preserveAspect = true;
            var mask = (RectTransform)Find(root, "PortraitMask");
            float portrait = enemy ? 215 : 325;
            Place(mask, enemy ? 35 : 0, enemy ? 0 : 5, portrait, portrait);
            var frame = (RectTransform)Find(mask, "RenewalPortraitFrame");
            Place(frame, 0, 0, portrait, portrait);
            Place((RectTransform)Find(mask, "PortraitImage"), 0, 0, portrait, portrait);
            var badge = (RectTransform)Find(root, "LifeBadge");
            Place(badge, badgeX, badgeY, badgeWidth, badgeHeight);
            badge.GetComponent<Image>().sprite = Sprite(enemy ? "EnemyHpBadge" : "HpBadge");
            Find(badge, "LifeIcon").gameObject.SetActive(false);
            var life = Find(badge, "Life").GetComponent<TMP_Text>();
            Place(life.rectTransform, 6, enemy ? 26 : 18, badgeWidth - 12, enemy ? 60 : 46);
            life.enableAutoSizing = true;
            life.fontSizeMin = 23;
            life.fontSizeMax = enemy ? 54 : 48;
            life.alignment = TextAlignmentOptions.Center;
            Place((RectTransform)Find(root, "TurnMark"), enemy ? 80 : 90, enemy ? 207 : 327, enemy ? 125 : 145, 4);
        }

        private static void ResourceRow(Transform row, string prefix, float x, float y, bool top)
        {
            float slotWidth = top ? 80 : 83;
            float height = top ? 108 : 115;
            float[] offsets = top ? new[] { 0f, 90f, 182f } : new[] { 0f, 94f, 188f };
            PlaceScreen((RectTransform)row, x, y, offsets[2] + slotWidth, height, top ? Vector2.one : new Vector2(1, 0));
            string[] names = { "Deck", "Grave", "Exile" };
            for (int i = 0; i < names.Length; i++)
            {
                var frame = (RectTransform)Find(row, "RenewalZone" + i);
                Place(frame, offsets[i], 0, slotWidth, height);
                frame.GetComponent<Image>().sprite = Sprite("ResourceFrame");
                frame.GetComponent<Image>().preserveAspect = true;
                var icon = Find(row, prefix + names[i] + "Icon").GetComponent<Image>();
                Place(icon.rectTransform, offsets[i] + (slotWidth - 48) / 2, 13, 48, 48);
                icon.sprite = Sprite(names[i] + "Icon");
                icon.preserveAspect = true;
                Place((RectTransform)Find(row, prefix + names[i] + "Count"), offsets[i] + 8, height - 35, slotWidth - 16, 29);
                if (i > 0)
                {
                    Place((RectTransform)Find(row, prefix + names[i] + "Browse"), offsets[i], 0, slotWidth, height);
                }
            }
        }

        private static void Inspector(CardDetailView inspector)
        {
            var root = (RectTransform)inspector.transform;
            root.sizeDelta = new Vector2(300, 480);
            var background = inspector.GetComponent<Image>() ?? inspector.gameObject.AddComponent<Image>();
            background.sprite = Sprite("DetailFrame");
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;
            Place(inspector.Title.rectTransform, 12, 10, 276, 48);
            Place((RectTransform)Find(root, "ArtworkMask"), 12, 64, 276, 185);
            Place((RectTransform)Find(root, "ArtPlaceholder"), 0, 0, 276, 185);
            Place((RectTransform)Find(root, "CardType"), 12, 255, 276, 26);
            Place(inspector.Stats.rectTransform, 12, 283, 276, 32);
            Place((RectTransform)Find(root, "DetailViewport"), 12, 323, 276, 141);
        }

        private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).First(item => item.name == name);
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
        private static void PlaceScreen(RectTransform rect, float x, float y, float width, float height, Vector2 anchor)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x - 1920 * anchor.x, 1080 * (1 - anchor.y) - y);
            rect.sizeDelta = new Vector2(width, height);
        }
        private static void Edit(string path, Action<GameObject> action)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                action(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
