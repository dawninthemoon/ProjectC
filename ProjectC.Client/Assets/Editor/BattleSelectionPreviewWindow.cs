using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectC.Client.Editor
{
    public sealed class BattleSelectionPreviewWindow : EditorWindow
    {
        private const string ScenePath = "Assets/Scenes/Previews/BattleSelectionPreview.unity";

        [MenuItem("ProjectC/UI/Selection and Defense Preview")]
        public static void Open() => GetWindow<BattleSelectionPreviewWindow>("Selection Preview");

        public static void AuthorScene()
        {
            Directory.CreateDirectory("Assets/Scenes/Previews");
            bool replaceEmptyBatchScene = Application.isBatchMode && SceneManager.sceneCount == 1
                && string.IsNullOrEmpty(SceneManager.GetActiveScene().path);
            if (!Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                Debug.LogWarning("Save the untitled scene before authoring the preview scene. Existing scenes were preserved.");
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                replaceEmptyBatchScene ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                var root = new GameObject("UI Preview - synthetic data, no server", typeof(BattleSelectionPreviewDriver));
                SceneManager.MoveGameObjectToScene(root, scene);
                root.GetComponent<BattleSelectionPreviewDriver>().Configure(
                    AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleScreen.prefab"));
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (replaceEmptyBatchScene)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
                else
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            AssetDatabase.SaveAssets();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("UI-only fixture. No server connection or game commands.\nPlay the preview scene. Keys 1–5 also change modes.", MessageType.Info);
            if (!EditorApplication.isPlaying && GUILayout.Button("Open preview scene"))
            {
                EditorSceneManager.OpenScene(ScenePath);
            }
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                foreach (BattleSelectionPreviewDriver.PreviewMode mode in System.Enum.GetValues(typeof(BattleSelectionPreviewDriver.PreviewMode)))
                {
                    if (GUILayout.Button(mode.ToString()))
                    {
                        var driver = Object.FindFirstObjectByType<BattleSelectionPreviewDriver>();
                        if (driver != null)
                        {
                            driver.ShowMode(mode);
                        }
                    }
                }
            }
        }
    }
}
