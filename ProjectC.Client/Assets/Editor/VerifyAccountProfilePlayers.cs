#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

// Script-only player compilation verifies actual preprocessor output without building assets.
public static class VerifyAccountProfilePlayers
{
    public static void Release() => Verify(false, BuildTarget.StandaloneWindows64);
    public static void Development() => Verify(true, BuildTarget.StandaloneWindows64);
    public static void MacRelease() => Verify(false, BuildTarget.StandaloneOSX);
    public static void MacDevelopment() => Verify(true, BuildTarget.StandaloneOSX);

    private static void Verify(bool development, BuildTarget target)
    {
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/AccountProfilePlayerScripts",
            target + "/" + (development ? "Development" : "Release")));
        Directory.CreateDirectory(directory);
        PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
        {
            target = target,
            group = BuildTargetGroup.Standalone,
            options = development ? ScriptCompilationOptions.DevelopmentBuild : ScriptCompilationOptions.None,
        }, directory);
        string path = Path.Combine(directory, "ProjectC.Client.Runtime.dll");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Player script compilation did not produce the client assembly.");
        }
        ResolveEventHandler resolver = (sender, arguments) =>
        {
            string name = new AssemblyName(arguments.Name).Name;
            string dependency = Path.Combine(directory, name + ".dll");
            if (File.Exists(dependency))
            {
                return Assembly.ReflectionOnlyLoadFrom(dependency);
            }
            var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(value => value.GetName().Name == name);
            return loaded != null ? Assembly.ReflectionOnlyLoadFrom(loaded.Location) : Assembly.ReflectionOnlyLoad(arguments.Name);
        };
        AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += resolver;
        try
        {
            var assembly = Assembly.ReflectionOnlyLoad(File.ReadAllBytes(path));
            var profiles = assembly.GetType("ProjectC.Client.AccountProfiles", true);
            bool features = (bool)profiles.GetField("DevelopmentFeaturesEnabled").GetRawConstantValue();
            bool panel = assembly.GetType("ProjectC.Client.DeveloperAccountPanel", false) != null;
            bool editorSetting = profiles.GetProperty("EditorProfile") != null;
            bool windowsStore = assembly.GetType("ProjectC.Client.WindowsCredentialSessionStore", false) != null;
            bool macStore = assembly.GetType("ProjectC.Client.MacKeychainSessionStore", false) != null;
            if (features != development || panel != development || editorSetting
                || windowsStore != (target == BuildTarget.StandaloneWindows64)
                || macStore != (target == BuildTarget.StandaloneOSX))
            {
                throw new InvalidOperationException("Player compilation exposed incorrect account profile features.");
            }
        }
        finally
        {
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= resolver;
        }
        Debug.Log((development ? "Development" : "Release")
            + " " + target + " player scripts: account profile feature gate, developer UI inclusion and editor-only settings verified.");
    }
}
#endif
