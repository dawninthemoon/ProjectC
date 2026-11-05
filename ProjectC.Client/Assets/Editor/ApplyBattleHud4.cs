using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    public static class ApplyBattleHud4
    {
        private const string ScreenPath = "Assets/Prefabs/Battle/BattleScreen.prefab";
        private const string OrnamentPath = "Assets/Art/UI/BattleHud4/HeroOrnament.png";
        private const string PanelPath = "Assets/Art/UI/BattleRenewal/ZoneFrame.png";

        public static void Apply()
        {
            AssetDatabase.ImportAsset(OrnamentPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(OrnamentPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            var root = PrefabUtility.LoadPrefabContents(ScreenPath);
            try
            {
                var presenter = root.GetComponent<BattlePresenter>();
                var ornament = AssetDatabase.LoadAssetAtPath<Sprite>(OrnamentPath);
                var panel = AssetDatabase.LoadAssetAtPath<Sprite>(PanelPath);
                foreach (var player in new[] { presenter.OwnPlayer, presenter.EnemyPlayer })
                {
                    var portrait = Find(player.transform, "PortraitMask");
                    var badge = Find(player.transform, "LifeBadge");
                    var old = player.transform.Find("HeroOrnament");
                    if (old != null)
                    {
                        UnityEngine.Object.DestroyImmediate(old.gameObject);
                    }
                    var go = new GameObject("HeroOrnament", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    var rect = (RectTransform)go.transform;
                    rect.SetParent(player.transform, false);
                    Place(rect, -13, -1, 215, 174);
                    rect.SetSiblingIndex(badge.GetSiblingIndex());
                    var image = go.GetComponent<Image>();
                    image.sprite = ornament;
                    image.raycastTarget = false;
                    var badgeRect = (RectTransform)badge;
                    badgeRect.anchoredPosition = new Vector2(101, -112);
                    var hp = badge.GetComponentInChildren<TMP_Text>();
                    hp.enableAutoSizing = true;
                    hp.fontSizeMin = 20;
                    hp.fontSizeMax = 44;
                    Place(hp.rectTransform, 25, 0, 60, 52);
                    hp.textWrappingMode = TextWrappingModes.NoWrap;
                    hp.alignment = TextAlignmentOptions.Center;
                }
                foreach (var button in new[] { presenter.LogToggle, presenter.LobbyToggle })
                {
                    var image = button.GetComponent<Image>();
                    image.sprite = panel;
                    image.type = Image.Type.Simple;
                    image.color = Color.white;
                    var rect = (RectTransform)button.transform;
                    rect.sizeDelta = new Vector2(230, 44);
                    var colors = button.colors;
                    colors.normalColor = Color.white;
                    colors.highlightedColor = new Color(1f, 1f, .84f);
                    colors.pressedColor = new Color(.65f, .85f, .76f);
                    colors.disabledColor = new Color(.65f, .7f, .67f, 1f);
                    button.colors = colors;
                    var text = button.GetComponentInChildren<TMP_Text>();
                    text.alignment = TextAlignmentOptions.Center;
                    text.enableAutoSizing = true;
                    text.fontSizeMin = 18;
                    text.fontSizeMax = 25;
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.raycastTarget = false;
                }
                var action = presenter.EndTurn.GetComponentInChildren<TMP_Text>();
                Place(action.rectTransform, 40, -102, 130, 54);
                action.enableAutoSizing = true;
                action.fontSizeMin = 20;
                action.fontSizeMax = 33;
                action.alignment = TextAlignmentOptions.Center;
                action.textWrappingMode = TextWrappingModes.NoWrap;
                Place(presenter.PhaseLabel.rectTransform, 25, -48, 160, 30);
                presenter.PhaseLabel.alignment = TextAlignmentOptions.Center;
                presenter.PhaseLabel.enableAutoSizing = true;
                presenter.PhaseLabel.fontSizeMin = 16;
                presenter.PhaseLabel.fontSizeMax = 22;
                presenter.PhaseLabel.textWrappingMode = TextWrappingModes.NoWrap;
                var timer = Find(presenter.EndTurn.transform, "TurnTimer").GetComponent<TMP_Text>();
                Place(timer.rectTransform, 45, -78, 120, 20);
                timer.alignment = TextAlignmentOptions.Center;
                timer.enableAutoSizing = true;
                timer.fontSizeMin = 12;
                timer.fontSizeMax = 15;
                PrefabUtility.SaveAsPrefabAsset(root, ScreenPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
        }

        private static Transform Find(Transform parent, string name)
        {
            return parent.GetComponentsInChildren<Transform>(true).First(item => item.name == name);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
