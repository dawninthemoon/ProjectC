using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace ProjectC.Client.Editor
{
    public static class BuildLocalClient
    {
        [MenuItem("Project C/Build Windows Multiplayer Client")]
        public static void Build()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0) throw new System.InvalidOperationException("Enable a scene in Build Settings");
            var report = BuildPipeline.BuildPlayer(scenes, "Build/Multiplayer/ProjectC.Client.exe", BuildTarget.StandaloneWindows64, BuildOptions.Development);
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.InvalidOperationException("Multiplayer client build failed: " + report.summary.result);
        }
    }
}
