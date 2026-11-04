using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    // Explicit migration; runtime continues to use the existing CardWidget and server DTOs.
    public static class ApplyBattleRenewalStage3
    {
        private const string Art = "Assets/Art/UI/BattleRenewalStage3/";

        public static void Apply()
        {
            foreach (string name in new[] { "HandCardFrame", "HandTitle", "FieldCardFrame", "HandCardCostBadge", "FieldCardAttackBadge", "FieldCardHealthBadge", "FieldStatusFrame", "HandCardGlowMask", "FieldCardGlowMask", "TurnGlowMask" })
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
            const string materialPath = Art + "BattleFrameGlow.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("ProjectC/UI/BattleFrameGlow"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            Edit("Assets/Prefabs/Battle/BattleCard.prefab", true);
            Edit("Assets/Prefabs/Battle/BattleFieldCard.prefab", false);
            EditTurn();
            AssetDatabase.SaveAssets();
        }

        private static void Edit(string assetPath, bool hand)
        {
            var root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                var frame = Find(root.transform, "FrameImage");
                var sprite = Sprite(hand ? "HandCardFrame" : "FieldCardFrame");
                var image = frame.GetComponent<Image>();
                image.sprite = sprite;
                image.color = Color.white;
                var overlay = Find(frame, "RenewalFrame").GetComponent<Image>();
                overlay.enabled = true;
                overlay.sprite = sprite;
                overlay.raycastTarget = false;
                overlay.transform.SetAsLastSibling();
                var glow = EnsureImage(frame, "FrameGlow", Sprite(hand ? "HandCardGlowMask" : "FieldCardGlowMask"));
                Stretch((RectTransform)glow.transform);
                glow.material = AssetDatabase.LoadAssetAtPath<Material>(Art + "BattleFrameGlow.mat");
                glow.color = Color.clear;
                glow.transform.SetAsLastSibling();

                var mask = (RectTransform)Find(frame, "ArtworkMask");
                mask.anchorMin = Vector2.zero;
                mask.anchorMax = Vector2.one;
                mask.offsetMin = new Vector2(hand ? 4 : 6, hand ? 7 : 8);
                mask.offsetMax = new Vector2(hand ? -4 : -6, hand ? -32 : -8);

                var title = Find(frame, "Name").GetComponent<TMP_Text>();
                var titleRect = title.rectTransform;
                Fixed(titleRect, new Vector2(.5f, 1), new Vector2(.5f, 1),
                    hand ? new Vector2(13, -7) : new Vector2(0, -10),
                    hand ? new Vector2(132, 27) : new Vector2(136, 18));
                title.fontSizeMin = hand ? 10 : 8;
                title.fontSizeMax = hand ? 18 : 12;
                title.enableAutoSizing = true;
                title.alignment = TextAlignmentOptions.Center;
                title.raycastTarget = false;
                var titleFrame = EnsureImage(title.transform.parent, "RenewalTitle", Sprite("HandTitle"));
                Fixed((RectTransform)titleFrame.transform, titleRect.anchorMin, titleRect.pivot,
                    titleRect.anchoredPosition, titleRect.sizeDelta + new Vector2(8, 3));
                titleFrame.color = hand ? Color.white : new Color(.65f, .72f, .69f, .8f);
                titleFrame.transform.SetAsLastSibling();
                title.transform.SetAsLastSibling();

                Badge(frame, "AttackBadgeFrame", "FieldCardAttackBadge", false, hand);
                Badge(frame, "HealthBadgeFrame", "FieldCardHealthBadge", true, hand);
                var cost = (RectTransform)Find(frame, "CostBadgeFrame");
                cost.GetComponent<Image>().sprite = Sprite("HandCardCostBadge");
                cost.GetComponent<Image>().color = Color.white;
                Fixed(cost, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(45, 45));
                cost.SetAsLastSibling();
                var status = frame.Find("FieldStatusFrame");
                if (hand)
                {
                    if (status != null)
                    {
                        status.gameObject.SetActive(false);
                    }
                }
                else
                {
                    var statusImage = EnsureImage(frame, "FieldStatusFrame", Sprite("FieldStatusFrame"));
                    Fixed((RectTransform)statusImage.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 3), new Vector2(50, 23));
                    statusImage.transform.SetAsLastSibling();
                }
                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EditTurn()
        {
            const string screenPath = "Assets/Prefabs/Battle/BattleScreen.prefab";
            var root = PrefabUtility.LoadPrefabContents(screenPath);
            try
            {
                var button = root.GetComponent<BattlePresenter>().EndTurn;
                var glow = EnsureImage(button.transform, "TurnGlow", Sprite("TurnGlowMask"));
                Stretch((RectTransform)glow.transform);
                glow.material = AssetDatabase.LoadAssetAtPath<Material>(Art + "BattleFrameGlow.mat");
                glow.color = Color.clear;
                if (button.GetComponent<TurnFrameGlow>() == null)
                {
                    button.gameObject.AddComponent<TurnFrameGlow>();
                }
                PrefabUtility.SaveAsPrefabAsset(root, screenPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Badge(Transform frame, string name, string sprite, bool right, bool hand)
        {
            var badge = (RectTransform)Find(frame, name);
            badge.SetParent(frame, false);
            var image = badge.GetComponent<Image>();
            image.sprite = Sprite(sprite);
            image.color = Color.white;
            var outline = badge.GetComponent<Outline>();
            if (outline != null)
            {
                outline.enabled = false;
            }
            var anchor = new Vector2(right ? 1 : 0, 0);
            Fixed(badge, anchor, anchor, new Vector2(right ? -2 : 2, 1), hand ? new Vector2(40, 44) : new Vector2(49, 53));
            badge.SetAsLastSibling();
        }

        private static Image EnsureImage(Transform parent, string name, Sprite sprite)
        {
            var node = parent.Find(name);
            var go = node == null ? new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)) : node.gameObject;
            if (node == null)
            {
                go.transform.SetParent(parent, false);
            }
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            go.SetActive(true);
            return image;
        }

        private static void Fixed(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static Transform Find(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).First(item => item.name == name);
        }

        private static Sprite Sprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
        }
    }
}
