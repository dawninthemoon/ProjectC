using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    public static class ApplyBattleDetail6
    {
        private const string ScreenPath = "Assets/Prefabs/Battle/BattleScreen.prefab";
        private const string GemPath = "Assets/Art/UI/BattleDetail6/HeroHpGem.png";
        private const string HpMaterialPath = "Assets/Art/UI/BattleDetail6/HeroHp.mat";

        public static void Apply()
        {
            AssetDatabase.ImportAsset(GemPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(GemPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            var gem = AssetDatabase.LoadAssetAtPath<Sprite>(GemPath);
            var frame = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/BattleRenewal/ZoneFrame.png");
            var flourish = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/BattleHud4/HeroOrnament.png");
            var body = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/UI/BattleHud5/HudBody.mat");
            var hp = AssetDatabase.LoadAssetAtPath<Material>(HpMaterialPath);
            if (hp == null)
            {
                hp = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/UI/BattleHud5/HudValue.mat"));
                AssetDatabase.CreateAsset(hp, HpMaterialPath);
            }
            hp.SetFloat("_FaceDilate", .04f);
            hp.SetFloat("_OutlineWidth", .035f);
            hp.SetFloat("_WeightBold", .25f);
            EditorUtility.SetDirty(hp);
            var root = PrefabUtility.LoadPrefabContents(ScreenPath);
            try
            {
                var presenter = root.GetComponent<BattlePresenter>();
                var space = Find(root.transform, "CardInfoSpace");
                var panel = (RectTransform)presenter.Detail.transform;
                Place((RectTransform)space, 24, -270, 360, 780);
                space.SetSiblingIndex(presenter.OwnPlayer.transform.GetSiblingIndex() + 1);
                var spaceImage = space.GetComponent<Image>();
                spaceImage.color = Color.clear;
                spaceImage.raycastTarget = false;
                Place(panel, 0, 0, 360, 340);
                var panelImage = panel.GetComponent<Image>();
                panelImage.sprite = frame;
                panelImage.type = Image.Type.Sliced;
                panelImage.pixelsPerUnitMultiplier = 1.2f;
                panelImage.color = Color.white;
                var title = Find(panel, "Name").GetComponent<TMP_Text>();
                Place(title.rectTransform, 25, -18, 310, 46);
                title.fontSharedMaterial = body;
                title.fontSize = 25;
                title.fontSizeMin = 17;
                title.fontSizeMax = 25;
                title.enableAutoSizing = true;
                title.alignment = TextAlignmentOptions.Center;
                title.textWrappingMode = TextWrappingModes.NoWrap;
                var artwork = (RectTransform)Find(panel, "ArtworkMask");
                Place(artwork, 20, -72, 145, 166);
                var type = Find(panel, "CardType").GetComponent<TMP_Text>();
                Place(type.rectTransform, 180, -88, 155, 45);
                type.fontSharedMaterial = body;
                type.fontSize = 18;
                type.textWrappingMode = TextWrappingModes.Normal;
                var stats = Find(panel, "Stats").GetComponent<TMP_Text>();
                Place(stats.rectTransform, 180, -140, 155, 62);
                stats.fontSharedMaterial = body;
                stats.fontSize = 20;
                stats.textWrappingMode = TextWrappingModes.Normal;
                var viewport = (RectTransform)Find(panel, "DetailViewport");
                Place(viewport, 18, -260, 324, 60);
                var viewImage = viewport.GetComponent<Image>();
                viewImage.color = new Color(.015f, .09f, .095f, .01f);
                var content = (RectTransform)Find(viewport, "DetailContent");
                Place(content, 0, 0, 324, 60);
                var layout = content.GetComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(4, 4, 4, 4);
                layout.spacing = 8;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                var fitter = content.GetComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                foreach (string item in new[] { "Rules", "Forms" })
                {
                    var label = Find(content, item).GetComponent<TMP_Text>();
                    label.fontSharedMaterial = body;
                    label.fontSize = 19;
                    label.enableAutoSizing = false;
                    label.textWrappingMode = TextWrappingModes.Normal;
                    label.alignment = TextAlignmentOptions.TopLeft;
                    label.rectTransform.sizeDelta = new Vector2(316, 0);
                }
                Decor(panel, "DetailFlourishTop", flourish, false);
                Decor(panel, "DetailFlourishBottom", flourish, true);
                foreach (var player in new[] { presenter.OwnPlayer, presenter.EnemyPlayer })
                {
                    var badge = (RectTransform)Find(player.transform, "LifeBadge");
                    Place(badge, 92, -112, 132, 54);
                    var image = badge.GetComponent<Image>();
                    image.sprite = gem;
                    image.color = Color.white;
                    var life = Find(badge, "Life").GetComponent<TMP_Text>();
                    Place(life.rectTransform, 29, -2, 91, 50);
                    life.fontSharedMaterial = hp;
                    life.fontStyle = FontStyles.Bold;
                    life.color = new Color(1f, .96f, .83f);
                    life.alignment = TextAlignmentOptions.Center;
                    life.enableAutoSizing = true;
                    life.fontSizeMin = 22;
                    life.fontSizeMax = 42;
                    life.textWrappingMode = TextWrappingModes.NoWrap;
                }
                PrefabUtility.SaveAsPrefabAsset(root, ScreenPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
        }

        private static void Decor(RectTransform parent, string name, Sprite sprite, bool bottom)
        {
            var old = parent.Find(name);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = bottom ? new Vector2(1, 0) : new Vector2(0, 1);
            rect.anchoredPosition = bottom ? new Vector2(-5, 5) : new Vector2(5, -5);
            rect.sizeDelta = new Vector2(65, 47);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = new Color(1, 1, 1, .8f);
            image.raycastTarget = false;
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
