using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectC.Client
{
    public static class AccountProfiles
    {
        public const string Default = "default";
        public const string Editor = "dev-editor";
        public const string ClientOne = "dev-client-1";
        public const string ClientTwo = "dev-client-2";
        private const string ArgumentPrefix = "--account-profile=";
        private static readonly string[] ProfileIds = { Default, Editor, ClientOne, ClientTwo };
        public static IReadOnlyList<string> Options { get; } = Array.AsReadOnly(ProfileIds);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public const bool DevelopmentFeaturesEnabled = true;
#else
        public const bool DevelopmentFeaturesEnabled = false;
#endif

        public static string Validate(string profileId)
        {
            if (Array.IndexOf(ProfileIds, profileId) < 0)
            {
                throw new ArgumentException("Unknown account profile. Use default, dev-editor, dev-client-1 or dev-client-2.", nameof(profileId));
            }
            return profileId;
        }

        internal static string ResolveLaunchProfile(string[] arguments, string editorProfile,
            bool isEditor, bool isDevelopmentBuild)
        {
            // Release ignores even malformed development arguments and editor preferences.
            if (!isEditor && !isDevelopmentBuild)
            {
                return Default;
            }
            if (isEditor)
            {
                return Validate(editorProfile);
            }
            string profile = Default;
            bool found = false;
            foreach (string argument in arguments)
            {
                if (!argument.StartsWith(ArgumentPrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                if (found)
                {
                    throw new ArgumentException("Specify --account-profile only once.");
                }
                profile = Validate(argument.Substring(ArgumentPrefix.Length));
                found = true;
            }
            return profile;
        }

        public static string LaunchProfile()
        {
#if UNITY_EDITOR
            return ResolveLaunchProfile(Array.Empty<string>(), EditorProfile, true, false);
#elif DEVELOPMENT_BUILD
            return ResolveLaunchProfile(System.Environment.GetCommandLineArgs(), Default, false, true);
#else
            return Default;
#endif
        }

#if UNITY_EDITOR
        private static string EditorPreferenceKey => "ProjectC.AccountProfile/" + Application.dataPath;
        public static string EditorProfile
        {
            get => UnityEditor.EditorPrefs.GetString(EditorPreferenceKey, Default);
            set => UnityEditor.EditorPrefs.SetString(EditorPreferenceKey, Validate(value));
        }
#endif
    }
}
