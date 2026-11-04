using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    public static class ApplyBattleFieldClarity
    {
        private const string CropPath = "Assets/Art/UI/BattleRenewalStage3/FieldCardCropProfile.asset";

        public static void Apply()
        {
            const string arrowPath = "Assets/Art/UI/BattleRenewal/TurnArrow.png";
            AssetDatabase.ImportAsset(arrowPath, ImportAssetOptions.ForceUpdate);
            var arrowImporter = (TextureImporter)AssetImporter.GetAtPath(arrowPath);
            arrowImporter.textureType = TextureImporterType.Sprite;
            arrowImporter.spriteImportMode = SpriteImportMode.Single;
            arrowImporter.alphaIsTransparency = true;
            arrowImporter.mipmapEnabled = false;
            arrowImporter.SaveAndReimport();
            var profile = AssetDatabase.LoadAssetAtPath<FieldCardCropProfile>(CropPath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<FieldCardCropProfile>();
                AssetDatabase.CreateAsset(profile, CropPath);
            }
            var data = new SerializedObject(profile);
            data.FindProperty("_defaultZoom").floatValue = 1.38f;
            data.FindProperty("_defaultOffset").vector2Value = new Vector2(0, .12f);
            var overrides = data.FindProperty("_overrides");
            overrides.arraySize = 3;
            Crop(overrides.GetArrayElementAtIndex(0), 1001, 1.4f, new Vector2(0, .11f));
            Crop(overrides.GetArrayElementAtIndex(1), 3001, 1.52f, new Vector2(0, .15f));
            Crop(overrides.GetArrayElementAtIndex(2), 4002, 1.27f, new Vector2(0, .1f));
            data.ApplyModifiedPropertiesWithoutUndo();

            const string fieldPath = "Assets/Prefabs/Battle/BattleFieldCard.prefab";
            var field = PrefabUtility.LoadPrefabContents(fieldPath);
            try
            {
                var widget = field.GetComponent<CardWidget>();
                var serialized = new SerializedObject(widget);
                serialized.FindProperty("_fieldCropProfile").objectReferenceValue = profile;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Find(field.transform, "RenewalTitle").gameObject.SetActive(false);
                Find(field.transform, "FieldStatusFrame").gameObject.SetActive(false);
                Find(field.transform, "Name").gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(field, fieldPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(field);
            }

            const string screenPath = "Assets/Prefabs/Battle/BattleScreen.prefab";
            var screen = PrefabUtility.LoadPrefabContents(screenPath);
            try
            {
                var presenter = screen.GetComponent<BattlePresenter>();
                presenter.OwnField.OwnedByLocalPlayer = true;
                presenter.EnemyField.OwnedByLocalPlayer = false;
                var turn = presenter.EndTurn;
                var frame = turn.GetComponent<Image>();
                frame.color = Color.white;
                var inside = Find(turn.transform, "PhaseInside").GetComponent<Image>();
                inside.color = new Color(.025f, .15f, .155f, 1f);
                var label = turn.GetComponentsInChildren<TMP_Text>(true).First(item => item != presenter.PhaseLabel);
                label.fontSize = 36;
                label.fontSizeMin = 28;
                label.fontSizeMax = 36;
                label.color = new Color(1f, .96f, .83f);
                presenter.PhaseLabel.fontSize = 23;
                presenter.PhaseLabel.fontSizeMin = 19;
                presenter.PhaseLabel.fontSizeMax = 23;
                presenter.PhaseLabel.color = new Color(1f, .94f, .76f);
                var plate = Decor(turn.transform, "TurnPhasePlate", AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/Art/UI/BattleRenewalStage3/HandTitle.png"));
                Place(plate.rectTransform, 20, 44, 170, 42);
                plate.transform.SetSiblingIndex(presenter.PhaseLabel.transform.GetSiblingIndex());
                var arrow = Decor(turn.transform, "TurnArrow", AssetDatabase.LoadAssetAtPath<Sprite>(arrowPath));
                Place(arrow.rectTransform, 80, 224, 50, 41);
                PrefabUtility.SaveAsPrefabAsset(screen, screenPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(screen);
            }
            AssetDatabase.SaveAssets();
        }

        private static void Crop(SerializedProperty row, int id, float zoom, Vector2 offset)
        {
            row.FindPropertyRelative("DefinitionId").intValue = id;
            row.FindPropertyRelative("Zoom").floatValue = zoom;
            row.FindPropertyRelative("Offset").vector2Value = offset;
        }

        private static Image Decor(Transform parent, string name, Sprite sprite)
        {
            var child = parent.Find(name);
            var go = child == null ? new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) : child.gameObject;
            if (child == null)
            {
                go.transform.SetParent(parent, false);
            }
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
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
    }
}
