using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    // Explicit migration for the 1920x1080 battle layout; does not change game data.
    public static class ApplyBattleLayoutReadability
    {
        private const string ArtPath = "Assets/Art/UI/BattleCardBack/FoxCardBack.png";
        private const string MaterialPath = "Assets/Art/UI/BattleCardBack/BattleTextOutline.mat";

        public static void Apply()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(ArtPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansKR-Regular.asset");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(font.material) { name = "BattleTextOutline" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.015f, .04f, .045f, 1));
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, .12f);
            material.SetFloat(ShaderUtilities.ID_FaceDilate, .11f);
            material.SetFloat(ShaderUtilities.ID_WeightBold, .75f);
            material.EnableKeyword("OUTLINE_ON");
            EditorUtility.SetDirty(material);

            var settings = AssetDatabase.LoadAssetAtPath<BattleVisualSettings>("Assets/Prefabs/Battle/BattleVisualSettings.asset");
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("_compactHandScale").floatValue = 1.055f;
            serialized.FindProperty("_expandedHandScale").floatValue = 1.055f;
            serialized.FindProperty("_compactHandSpacing").floatValue = 200;
            serialized.FindProperty("_expandedHandSpacing").floatValue = 200;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Edit("Assets/Prefabs/Battle/BattleCard.prefab", root => ConfigureCard(root, true, material));
            Edit("Assets/Prefabs/Battle/BattleFieldCard.prefab", root => ConfigureCard(root, false, material));
            Edit("Assets/Prefabs/Battle/CardBack.prefab", root =>
            {
                ((RectTransform)root.transform).sizeDelta = new Vector2(82, 130);
                var frame = Find(root.transform, "FrameImage");
                var image = frame.GetComponent<Image>();
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath);
                image.color = Color.white;
                Find(frame, "Inset").GetComponent<Image>().enabled = false;
                Find(frame, "Accent").GetComponent<Image>().enabled = false;
            });
            Edit("Assets/Prefabs/Battle/BattleScreen.prefab", root =>
            {
                var presenter = root.GetComponent<BattlePresenter>();
                Place((RectTransform)presenter.EnemyField.transform, 390, 255, 1120, 215);
                Place((RectTransform)presenter.OwnField.transform, 390, 508, 1120, 215);
                Place((RectTransform)Find(root.transform, "OwnFieldDropZone"), 360, 493, 1170, 245);
                Place((RectTransform)presenter.EnemyHand.transform, 355, 0, 435, 140);
                presenter.EnemyHand.Layout.OpponentHand = true;
                Place(presenter.TurnLabel.rectTransform, 24, 44, 260, 42);
                foreach (var player in new[] { presenter.OwnPlayer, presenter.EnemyPlayer })
                {
                    Style(Find(player.transform, "Life").GetComponent<TMP_Text>(), material, 48, 30, 48);
                }
                var turnLabel = presenter.EndTurn.GetComponentsInChildren<TMP_Text>(true).First(item => item != presenter.PhaseLabel);
                Style(turnLabel, material, 34, 24, 34);
                Style(presenter.PhaseLabel, material, 22, 18, 22);
            });
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureCard(GameObject root, bool hand, Material material)
        {
            var frame = Find(root.transform, "FrameImage");
            var title = Find(frame, "Name").GetComponent<TMP_Text>();
            var titleRect = title.rectTransform;
            titleRect.sizeDelta = new Vector2(hand ? 132 : 146, hand ? 27 : 23);
            Style(title, material, hand ? 20 : 17, hand ? 13 : 12, hand ? 20 : 17);
            title.enableWordWrapping = false;
            title.overflowMode = TextOverflowModes.Ellipsis;
            var titleFrame = Find(frame, "RenewalTitle").GetComponent<Image>();
            titleFrame.color = Color.white;
            ((RectTransform)titleFrame.transform).sizeDelta = titleRect.sizeDelta + new Vector2(8, 3);
            foreach (string name in new[] { "AttackBadge", "HealthBadge" })
            {
                Style(Find(frame, name).GetComponent<TMP_Text>(), material, hand ? 31 : 35, 20, hand ? 31 : 35);
            }
            if (hand)
            {
                Style(Find(frame, "CostBadge").GetComponent<TMP_Text>(), material, 32, 18, 32);
            }
        }

        private static void Style(TMP_Text text, Material material, float size, float min, float max)
        {
            text.fontSharedMaterial = material;
            text.fontStyle = FontStyles.Normal;
            text.color = new Color(1, .96f, .84f, 1);
            text.fontSize = size;
            text.fontSizeMin = min;
            text.fontSizeMax = max;
            text.enableAutoSizing = true;
            text.alignment = TextAlignmentOptions.Center;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static Transform Find(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).First(item => item.name == name);
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
