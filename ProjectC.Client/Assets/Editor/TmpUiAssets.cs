using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    public static class TmpUiAssets
    {
        const string FontPath = "Assets/Fonts/Harmonic SDF.asset";

        [MenuItem("Project C/Validate TMP scene fonts")]
        public static void Validate()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null || TMP_Settings.defaultFontAsset != font)
            {
                throw new InvalidOperationException("Harmonic SDF must be the default TMP font.");
            }
            int labels = 0;
            foreach (string path in Directory.GetFiles("Assets/Prefabs", "*.prefab", SearchOption.AllDirectories))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    labels += ValidateRoot(root, font);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            var previousScenes = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string path in Directory.GetFiles("Assets/Scenes", "*.unity"))
                {
                    var scene = EditorSceneManager.OpenScene(path);
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        ValidateRoot(root, font);
                    }
                    Debug.Log("TMP scene verified: " + path);
                }
            }
            finally
            {
                if (previousScenes.Any(setup => setup.isLoaded && !string.IsNullOrEmpty(setup.path)))
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
                }
                else
                {
                    EditorSceneManager.OpenScene("Assets/Scenes/LobbyScene.unity");
                }
            }
            Debug.Log("TMP font validation succeeded: " + labels + " prefab labels use Harmonic SDF.");
        }

        static int ValidateRoot(GameObject root, TMP_FontAsset font)
        {
            if (root.GetComponentsInChildren<Text>(true).Length != 0
                || root.GetComponentsInChildren<InputField>(true).Length != 0
                || root.GetComponentsInChildren<Dropdown>(true).Length != 0)
            {
                throw new InvalidOperationException("Legacy UI text remains in " + root.name);
            }
            var labels = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (var label in labels)
            {
                if (label.font != font)
                {
                    throw new InvalidOperationException("Incorrect TMP font: " + root.name + "/" + label.name);
                }
            }
            foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                if (input.textComponent == null || input.textViewport == null)
                {
                    throw new InvalidOperationException("Incomplete TMP input: " + input.name);
                }
            }
            foreach (var dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
            {
                if (dropdown.captionText == null || dropdown.itemText == null || dropdown.template == null)
                {
                    throw new InvalidOperationException("Incomplete TMP dropdown: " + dropdown.name);
                }
            }
            return labels.Length;
        }
    }
}
