using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    // Explicit authoring operation: does not run when the game starts.
    public static class ApplyBattleRenewalArt
    {
        private const string Art = "Assets/Art/UI/BattleRenewal/";
        private const string CardPath = "Assets/Prefabs/Battle/BattleCard.prefab";
        private const string FieldPath = "Assets/Prefabs/Battle/BattleFieldCard.prefab";
        private const string ScreenPath = "Assets/Prefabs/Battle/BattleScreen.prefab";

        public static void Apply()
        {
            foreach (string name in new[] { "FieldFrame", "TurnFrame", "HeroFrame", "HpBadge", "RpBadge", "ZoneFrame" })
            {
                string path = Art + name + ".png";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            EditPrefab(CardPath, card =>
            {
                var face = Find(card.transform, "FrameImage");
                Overlay(face, "RenewalFrame", "FieldFrame", Vector2.zero, Vector2.zero, false);
            });
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FieldPath) == null)
            {
                AssetDatabase.CopyAsset(CardPath, FieldPath);
            }
            EditPrefab(FieldPath, card =>
            {
                ((RectTransform)card.transform).sizeDelta = new Vector2(180, 228);
            });
            EditPrefab(ScreenPath, root =>
            {
                var presenter = root.GetComponent<BattlePresenter>();
                var fieldCard = AssetDatabase.LoadAssetAtPath<GameObject>(FieldPath).GetComponent<CardWidget>();
                presenter.OwnField.CardPrefab = fieldCard;
                presenter.EnemyField.CardPrefab = fieldCard;
                ((RectTransform)presenter.OwnPlayer.transform).anchoredPosition = new Vector2(30, -790);
                ((RectTransform)Find(root.transform, "OwnResources")).anchoredPosition = new Vector2(1550, -880);
                ((RectTransform)presenter.LogToggle.transform).anchoredPosition = new Vector2(24, -150);
                ((RectTransform)presenter.LobbyToggle.transform).anchoredPosition = new Vector2(24, -210);
                var turn = (RectTransform)presenter.EndTurn.transform;
                turn.sizeDelta = new Vector2(210, 310);
                var image = presenter.EndTurn.GetComponent<Image>();
                image.sprite = Sprite("TurnFrame");
                image.type = Image.Type.Simple;
                image.color = Color.white;
                var inside = Find(turn, "PhaseInside").GetComponent<Image>();
                inside.enabled = false;
                foreach (var name in new[] { "Label", "Phase", "TurnTimer" })
                {
                    var item = Find(turn, name) as RectTransform;
                    item.sizeDelta = new Vector2(180, item.sizeDelta.y);
                    item.anchoredPosition = new Vector2(15, item.anchoredPosition.y);
                }
                foreach (var player in new[] { presenter.OwnPlayer, presenter.EnemyPlayer })
                {
                    var portrait = Find(player.transform, "PortraitMask");
                    Overlay(portrait, "RenewalPortraitFrame", "HeroFrame", new Vector2(-7, -7), new Vector2(14, 14), false);
                    var badge = Find(player.transform, "LifeBadge").GetComponent<Image>();
                    badge.sprite = Sprite("HpBadge");
                    badge.type = Image.Type.Simple;
                    badge.color = Color.white;
                }
                foreach (string side in new[] { "Own", "Enemy" })
                {
                    var row = Find(root.transform, side + "Resources");
                    for (int i = 0; i < 3; i++)
                    {
                        var existing = row.Find("RenewalZone" + i);
                        if (existing != null)
                        {
                            UnityEngine.Object.DestroyImmediate(existing.gameObject);
                        }
                        var go = new GameObject("RenewalZone" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                        var rect = (RectTransform)go.transform;
                        rect.SetParent(row, false);
                        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                        rect.pivot = new Vector2(0, 1);
                        rect.anchoredPosition = new Vector2(48 + i * 94, 0);
                        rect.sizeDelta = new Vector2(92, 50);
                        var frame = go.GetComponent<Image>();
                        frame.sprite = Sprite("ZoneFrame");
                        frame.raycastTarget = false;
                        rect.SetAsFirstSibling();
                    }
                }
            });
            AssetDatabase.SaveAssets();
        }

        private static void EditPrefab(string path, Action<GameObject> edit)
        {
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

        private static Transform Find(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
        }

        private static Sprite Sprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
        }

        private static void Overlay(Transform parent, string name, string sprite, Vector2 position, Vector2 size, bool raycast)
        {
            var old = parent.Find(name);
            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.sprite = Sprite(sprite);
            image.raycastTarget = raycast;
        }
    }
}
