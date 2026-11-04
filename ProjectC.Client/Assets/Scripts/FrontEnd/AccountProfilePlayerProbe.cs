#if PROJECTC_ACCOUNT_PROFILE_PROBE
using System;
using System.IO;
using UnityEngine;

namespace ProjectC.Client
{
    // Verification builds only; no Website API requests or real account credentials are used.
    internal static class AccountProfilePlayerProbe
    {
        private const string OutputPrefix = "--profile-probe-output=";
        [Serializable]
        private sealed class Result
        {
            public string profile;
            public bool developmentFeatures, developerPanelIncluded, editorSettingIncluded, macStoreIncluded, windowsStoreIncluded, keychainRoundTrip;
            public string storeError;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            string output = null;
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (argument.StartsWith(OutputPrefix, StringComparison.Ordinal))
                {
                    output = argument.Substring(OutputPrefix.Length);
                }
            }
            if (string.IsNullOrEmpty(output))
            {
                Application.Quit(2);
                return;
            }
            var assembly = typeof(AccountProfiles).Assembly;
            var result = new Result
            {
                profile = AccountProfiles.LaunchProfile(),
                developmentFeatures = AccountProfiles.DevelopmentFeaturesEnabled,
                developerPanelIncluded = assembly.GetType("ProjectC.Client.DeveloperAccountPanel") != null,
                editorSettingIncluded = typeof(AccountProfiles).GetProperty("EditorProfile") != null,
                macStoreIncluded = assembly.GetType("ProjectC.Client.MacKeychainSessionStore") != null,
                windowsStoreIncluded = assembly.GetType("ProjectC.Client.WindowsCredentialSessionStore") != null
            };
            try
            {
                var store = new SecureSessionStore("https://profile-probe.invalid/" + Guid.NewGuid().ToString("N"), result.profile);
                try
                {
                    store.Write("probe token pair");
                    bool first = store.Read() == "probe token pair";
                    store.Write("rotated probe pair");
                    bool updated = store.Read() == "rotated probe pair";
                    store.Delete();
                    result.keychainRoundTrip = first && updated && store.Read() == null;
                }
                finally
                {
                    store.Delete();
                }
            }
            catch (Exception error)
            {
                result.storeError = error.GetType().Name;
            }
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            Application.Quit(result.keychainRoundTrip ? 0 : 3);
        }
    }
}
#endif
