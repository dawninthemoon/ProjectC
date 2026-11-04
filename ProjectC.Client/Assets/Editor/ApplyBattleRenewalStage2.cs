using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    public static class ApplyBattleRenewalStage2
    {
        private const string Art = "Assets/Art/UI/BattleRenewalStage2/";
        private const string Screen = "Assets/Prefabs/Battle/BattleScreen.prefab";

        public static void Apply()
        {
            foreach (string name in new[] { "ArenaGuides", "CardInset", "ZoneVertical" })
            {
                string assetPath = Art + name + ".png";
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            foreach (string cardPath in new[] { "Assets/Prefabs/Battle/BattleCard.prefab", "Assets/Prefabs/Battle/BattleFieldCard.prefab" })
            {
                Edit(cardPath, card =>
                {
                    var face = Find(card.transform, "FrameImage");
                    var image = face.GetComponent<Image>();
                    image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/BattleRenewal/FieldFrame.png");
                    image.color = Color.white;
                    var inset = Find(face, "Inset").GetComponent<Image>();
                    inset.sprite = Sprite("CardInset");
                    inset.color = Color.white;
                    Find(face, "RenewalFrame").GetComponent<Image>().enabled = false;
                });
            }
            var settings = AssetDatabase.LoadAssetAtPath<BattleVisualSettings>("Assets/Prefabs/Battle/BattleVisualSettings.asset");
            var serializedSettings = new SerializedObject(settings);
            serializedSettings.FindProperty("_expandedHandScale").floatValue = .9f;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            Edit(Screen, root =>
            {
                var presenter = root.GetComponent<BattlePresenter>();
                presenter.EnemyPlayer.transform.localScale = Vector3.one * 1.2f;
                ((RectTransform)presenter.EnemyPlayer.transform).anchoredPosition = new Vector2(884, 0);
                var field = Find(root.transform, "Battlefield");
                var old = field.Find("ArenaGuides");
                if (old != null)
                {
                    UnityEngine.Object.DestroyImmediate(old.gameObject);
                }
                var guide = new GameObject("ArenaGuides", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var guideRect = (RectTransform)guide.transform;
                guideRect.SetParent(field, false);
                guideRect.anchorMin = Vector2.zero;
                guideRect.anchorMax = Vector2.one;
                guideRect.offsetMin = new Vector2(-110, -28);
                guideRect.offsetMax = new Vector2(110, 28);
                var guideImage = guide.GetComponent<Image>();
                guideImage.sprite = Sprite("ArenaGuides");
                guideImage.raycastTarget = false;
                Find(root.transform, "Midline").GetComponent<Image>().enabled = false;
                Place((RectTransform)presenter.Hand.transform, 425, 865, 1050, 200);
                Place((RectTransform)presenter.EnemyField.transform, 390, 220, 1120, 210);
                Place((RectTransform)presenter.OwnField.transform, 390, 455, 1120, 210);
                Place((RectTransform)Find(root.transform, "OwnFieldDropZone"), 360, 440, 1170, 240);
                Place((RectTransform)Find(root.transform, "YourDeck"), 1565, 865, 86, 102);
                Place((RectTransform)Find(root.transform, "OpponentDeck"), 1565, 20, 86, 102);
                foreach (string side in new[] { "Own", "Enemy" })
                {
                    var row = Find(root.transform, side + "Resources");
                    Place((RectTransform)row, 1550, side == "Own" ? 865 : 20, 340, 102);
                    row.GetComponent<Image>().color = Color.clear;
                    var sideLabel = Find(row, side + "ResourceLabel");
                    sideLabel.gameObject.SetActive(false);
                    string[] kinds = { "Deck", "Grave", "Exile" };
                    for (int i = 0; i < kinds.Length; i++)
                    {
                        float x = 15 + i * 100;
                        var frame = Find(row, "RenewalZone" + i).GetComponent<Image>();
                        frame.sprite = Sprite("ZoneVertical");
                        Place((RectTransform)frame.transform, x, 0, 86, 102);
                        Place((RectTransform)Find(row, side + kinds[i] + "Icon"), x + 24, 12, 38, 38);
                        var text = Find(row, side + kinds[i] + "Count").GetComponent<TMP_Text>();
                        Place(text.rectTransform, x + 12, 63, 62, 32);
                        text.alignment = TextAlignmentOptions.Center;
                        if (i > 0)
                        {
                            Place((RectTransform)Find(row, side + kinds[i] + "Browse"), x, 0, 86, 102);
                        }
                    }
                }
            });
            AssetDatabase.SaveAssets();
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static Sprite Sprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
        }

        private static Transform Find(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).First(item => item.name == name);
        }

        private static void Edit(string assetPath, Action<GameObject> action)
        {
            var root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                action(root);
                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
