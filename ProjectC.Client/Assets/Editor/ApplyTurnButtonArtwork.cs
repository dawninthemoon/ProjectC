using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    public static class ApplyTurnButtonArtwork
    {
        private const string ArtFolder = "Assets/Art/UI/TurnButtonGameReady";
        private const string PrefabPath = "Assets/Prefabs/Battle/BattleScreen.prefab";

        [MenuItem("Project C/Apply supplied turn button artwork")]
        public static void Apply()
        {
            string source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads/turn_button_game_ready");
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++)
            {
                if (arguments[i] == "-turnArtPath")
                {
                    source = arguments[i + 1];
                }
            }
            string[] names = { "TurnFrame_Background_HiRes", "TurnSparkles_Overlay_HiRes" };
            foreach (string name in names)
            {
                string path = Path.Combine(source, name + ".png");
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Turn button artwork is missing.", path);
                }
            }
            Directory.CreateDirectory(ArtFolder);
            foreach (string name in names)
            {
                string target = ArtFolder + "/" + name + ".png";
                File.Copy(Path.Combine(source, name + ".png"), target, true);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(target);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var ui = root.GetComponent<BattlePresenter>();
                var button = ui.EndTurn;
                var frame = button.GetComponent<Image>();
                frame.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + "/" + names[0] + ".png");
                frame.preserveAspect = true;
                frame.type = Image.Type.Simple;
                frame.color = Color.white;
                var sparkles = Find(button.transform, "TurnGlow").GetComponent<Image>();
                sparkles.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + "/" + names[1] + ".png");
                sparkles.material = null;
                sparkles.preserveAspect = true;
                sparkles.raycastTarget = false;
                foreach (string name in new[] { "PhaseInside", "TurnPhasePlate", "TurnAvailability" })
                {
                    var decoration = button.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name);
                    if (decoration != null)
                    {
                        decoration.gameObject.SetActive(false);
                    }
                }
                var bounds = ((RectTransform)button.transform).rect.size;
                Vector2 size = frame.sprite.rect.size;
                size *= Mathf.Min(bounds.x / size.x, bounds.y / size.y);
                Vector2 inset = (bounds - size) * .5f;
                var label = button.GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == "Label");
                Fit(ui.PhaseLabel.rectTransform, inset, size, .34f, .24f, .48f, .08f);
                Fit(label.rectTransform, inset, size, .22f, .405f, .66f, .14f);
                Fit(Find(button.transform, "TurnArrow"), inset, size, .48f, .555f, .14f, .075f);
                label.fontSizeMin = 18;
                label.fontSizeMax = 38;
                label.enableAutoSizing = true;
                ui.PhaseLabel.fontSizeMin = 12;
                ui.PhaseLabel.fontSizeMax = 22;
                ui.PhaseLabel.enableAutoSizing = true;
                ui.PhaseLabel.textWrappingMode = TextWrappingModes.NoWrap;
                label.text = BattleText.Get("ui_battle_end_turn");
                ui.PhaseLabel.text = BattleText.Get("ui_battle_main_phase_1");
                Place(Find(button.transform, "TurnTimer"), 38, -32, 158, 28);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
        }

        private static RectTransform Find(Transform root, string name)
        {
            return (RectTransform)root.GetComponentsInChildren<Transform>(true).First(item => item.name == name);
        }

        private static void Fit(RectTransform rect, Vector2 inset, Vector2 size, float x, float y, float width, float height)
        {
            Place(rect, inset.x + size.x * x, inset.y + size.y * y, size.x * width, size.y * height);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
