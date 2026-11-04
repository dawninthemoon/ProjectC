#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

public static class BuildAccountProfileProbe
{
    public static void Development() => Build(true);
    public static void Release() => Build(false);

    private static void Build(bool development)
    {
        const string folder = "Assets/__AccountProfileProbe";
        if (Directory.Exists(folder))
        {
            throw new InvalidOperationException("The reserved profile probe folder already exists: " + folder);
        }
        var previous = EditorSceneManager.GetSceneManagerSetup();
        for (int i = 0; i < SceneManager.sceneCount; ++i)
        {
            if (SceneManager.GetSceneAt(i).isDirty)
            {
                throw new InvalidOperationException("Save modified scenes before running a profile probe build.");
            }
        }
        Scene probe = default;
        try
        {
            Directory.CreateDirectory(folder);
            probe = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(probe);
            new GameObject("Account profile probe scene");
            string path = folder + "/Probe.unity";
            EditorSceneManager.SaveScene(probe, path);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { path },
                target = BuildTarget.StandaloneOSX,
                locationPathName = "Build/AccountProfilePlayers/" + (development ? "Development" : "Release") + "/ProjectCProfileProbe.app",
                options = development ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = new[] { "PROJECTC_ACCOUNT_PROFILE_PROBE" }
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("Profile probe player build failed: " + report.summary.result);
            }
            Debug.Log("Account profile " + (development ? "Development" : "Release") + " macOS probe player built.");
        }
        finally
        {
            if (previous.Length > 0 && previous.All(scene => !string.IsNullOrEmpty(scene.path)
                && !scene.path.StartsWith(folder + "/", StringComparison.Ordinal)))
            {
                EditorSceneManager.RestoreSceneManagerSetup(previous);
            }
            else
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
#endif
