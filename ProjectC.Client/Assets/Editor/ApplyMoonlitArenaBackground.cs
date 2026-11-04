using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    // Explicit authoring command. The previous 3D stage remains a disabled prefab instance in the scene.
    public static class ApplyMoonlitArenaBackground
    {
        private const string TexturePath = "Assets/Art/Environment/BattleField/MoonlitArena/MoonlitArena.png";
        private const string ScreenPath = "Assets/Prefabs/Battle/BattleScreen.prefab";
        private const string ScenePath = "Assets/Scenes/BattleScene.unity";

        public static void Apply()
        {
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            if (importer == null)
            {
                throw new InvalidOperationException("Moonlit arena texture was not imported: " + TexturePath);
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
            if (sprite == null)
            {
                throw new InvalidOperationException("Moonlit arena sprite is missing: " + TexturePath);
            }

            var screen = PrefabUtility.LoadPrefabContents(ScreenPath);
            try
            {
                var backdrop = Find(screen.transform, "Background");
                var old = backdrop.Find("MoonlitArenaBackdrop");
                if (old != null)
                {
                    UnityEngine.Object.DestroyImmediate(old.gameObject);
                }
                var go = new GameObject("MoonlitArenaBackdrop", typeof(RectTransform), typeof(CanvasRenderer));
                var rect = (RectTransform)go.transform;
                rect.SetParent(backdrop, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                var image = go.AddComponent<AspectFillImage>();
                image.sprite = sprite;
                image.color = Color.white;
                image.raycastTarget = false;
                var guide = Find(screen.transform, "ArenaGuides");
                guide.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(screen, ScreenPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(screen);
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var stage = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "YokaiShrineStage");
            if (stage == null || PrefabUtility.GetCorrespondingObjectFromSource(stage) == null)
            {
                throw new InvalidOperationException("The restorable YokaiShrineStage prefab instance is missing from BattleScene.");
            }
            stage.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        private static Transform Find(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).First(item => item.name == name);
        }
    }
}
