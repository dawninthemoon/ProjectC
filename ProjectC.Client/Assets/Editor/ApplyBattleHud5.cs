using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    public static class ApplyBattleHud5
    {
        private const string PanelSprite = "Assets/Art/UI/BattleRenewal/ZoneFrame.png";
        private const string ScreenPath = "Assets/Prefabs/Battle/BattleScreen.prefab";
        private const string CardPath = "Assets/Prefabs/Battle/BattleCard.prefab";
        private const string FontPath = "Assets/Fonts/NotoSansKR-Regular.asset";
        private const string MaterialRoot = "Assets/Art/UI/BattleHud5/";

        public static void Apply()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(PanelSprite);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.spriteBorder = new Vector4(18, 18, 18, 18);
            importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelSprite);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var body = Preset(font, "HudBody", .025f);
            var title = Preset(font, "CardName", .015f);
            var number = Preset(font, "HudValue", .05f);
            number.SetFloat("_FaceDilate", .055f);
            EditorUtility.SetDirty(number);
            var root = PrefabUtility.LoadPrefabContents(ScreenPath);
            try
            {
                var presenter = root.GetComponent<BattlePresenter>();
                foreach (var button in new[] { presenter.LogToggle, presenter.LobbyToggle })
                {
                    var image = button.GetComponent<Image>();
                    image.sprite = sprite;
                    image.type = Image.Type.Sliced;
                    image.pixelsPerUnitMultiplier = 2f;
                    button.GetComponentInChildren<TMP_Text>().fontSharedMaterial = body;
                }
                foreach (var player in new[] { presenter.OwnPlayer, presenter.EnemyPlayer })
                {
                    Find(player.transform, "Life").GetComponent<TMP_Text>().fontSharedMaterial = number;
                }
                presenter.TurnLabel.fontSharedMaterial = body;
                presenter.PhaseLabel.fontSharedMaterial = body;
                presenter.EndTurn.GetComponentInChildren<TMP_Text>().fontSharedMaterial = body;
                Find(presenter.EndTurn.transform, "TurnTimer").GetComponent<TMP_Text>().fontSharedMaterial = body;
                var panel = Find(root.transform, "YokaiPanel");
                var panelImage = panel.GetComponent<Image>();
                panelImage.sprite = sprite;
                panelImage.type = Image.Type.Sliced;
                panelImage.color = Color.white;
                panelImage.raycastTarget = false;
                var label = Find(panel, "YokaiValue").GetComponent<TMP_Text>();
                Place(label.rectTransform, 55, -8, 105, 38);
                label.fontSharedMaterial = body;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.enableAutoSizing = true;
                label.fontSizeMin = 14;
                label.fontSizeMax = 22;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                var current = Label(panel, label, "YokaiCurrent");
                var separator = Label(panel, label, "YokaiSeparator");
                var maximum = Label(panel, label, "YokaiMaximum");
                Place(current.rectTransform, 158, -8, 40, 38);
                Place(separator.rectTransform, 200, -8, 18, 38);
                Place(maximum.rectTransform, 220, -8, 48, 38);
                current.fontSharedMaterial = maximum.fontSharedMaterial = number;
                separator.fontSharedMaterial = body;
                current.alignment = TextAlignmentOptions.MidlineRight;
                separator.alignment = TextAlignmentOptions.Center;
                maximum.alignment = TextAlignmentOptions.MidlineLeft;
                current.fontSizeMin = maximum.fontSizeMin = 16;
                current.fontSizeMax = maximum.fontSizeMax = 25;
                Place((RectTransform)Find(panel, "YokaiIcon"), 12, -12, 38, 38);
                Place((RectTransform)Find(panel, "YokaiTrack"), 58, -55, 210, 7);
                Place((RectTransform)Find(panel, "YokaiFill"), 58, -55, 210, 7);
                foreach (var labelName in new[] { "OwnDeckCount", "OwnGraveCount", "OwnExileCount", "EnemyDeckCount", "EnemyGraveCount", "EnemyExileCount" })
                {
                    Find(root.transform, labelName).GetComponent<TMP_Text>().fontSharedMaterial = number;
                }
                PrefabUtility.SaveAsPrefabAsset(root, ScreenPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            var card = PrefabUtility.LoadPrefabContents(CardPath);
            try
            {
                var widget = card.GetComponent<CardWidget>();
                var name = Find(widget.transform, "Name").GetComponent<TMP_Text>();
                name.fontSharedMaterial = title;
                name.rectTransform.sizeDelta = new Vector2(120, 21);
                name.rectTransform.anchoredPosition = new Vector2(13, -10);
                name.alignment = TextAlignmentOptions.Center;
                name.enableAutoSizing = true;
                name.fontSizeMin = 10;
                name.fontSizeMax = 16;
                name.textWrappingMode = TextWrappingModes.NoWrap;
                name.overflowMode = TextOverflowModes.Ellipsis;
                foreach (var badge in new[] { "AttackBadge", "HealthBadge", "CostBadge" })
                {
                    Find(widget.transform, badge).GetComponent<TMP_Text>().fontSharedMaterial = number;
                }
                PrefabUtility.SaveAsPrefabAsset(card, CardPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(card);
            }
            var field = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Battle/BattleFieldCard.prefab");
            try
            {
                foreach (var badge in new[] { "AttackBadge", "HealthBadge", "CostBadge" })
                {
                    Find(field.transform, badge).GetComponent<TMP_Text>().fontSharedMaterial = number;
                }
                PrefabUtility.SaveAsPrefabAsset(field, "Assets/Prefabs/Battle/BattleFieldCard.prefab");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(field);
            }
            AssetDatabase.SaveAssets();
        }

        private static Material Preset(TMP_FontAsset font, string name, float outline)
        {
            string path = MaterialRoot + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(font.material);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetFloat("_FaceDilate", 0f);
            material.SetFloat("_OutlineWidth", outline);
            material.SetColor("_OutlineColor", new Color(.02f, .035f, .035f, .9f));
            material.EnableKeyword("UNDERLAY_ON");
            material.SetFloat("_UnderlayOffsetX", .3f);
            material.SetFloat("_UnderlayOffsetY", -.3f);
            material.SetFloat("_UnderlaySoftness", .2f);
            material.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, .5f));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static TMP_Text Label(Transform parent, TMP_Text template, string name)
        {
            var node = parent.Find(name);
            var label = node != null ? node.GetComponent<TMP_Text>() : Object.Instantiate(template, parent);
            label.name = name;
            label.raycastTarget = false;
            label.gameObject.SetActive(true);
            return label;
        }

        private static Transform Find(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).First(item => item.name == name);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
