#if UNITY_EDITOR
using ProjectC.Client;
using UnityEditor;

public static class DeveloperAccountProfileSettings
{
    private const string Menu = "Project C/Development/Account profile/";

    [MenuItem(Menu + AccountProfiles.Default)]
    private static void Default() => AccountProfiles.EditorProfile = AccountProfiles.Default;
    [MenuItem(Menu + AccountProfiles.Editor)]
    private static void Editor() => AccountProfiles.EditorProfile = AccountProfiles.Editor;
    [MenuItem(Menu + AccountProfiles.ClientOne)]
    private static void ClientOne() => AccountProfiles.EditorProfile = AccountProfiles.ClientOne;
    [MenuItem(Menu + AccountProfiles.ClientTwo)]
    private static void ClientTwo() => AccountProfiles.EditorProfile = AccountProfiles.ClientTwo;

    private static bool Validate(string profile)
    {
        UnityEditor.Menu.SetChecked(Menu + profile, AccountProfiles.EditorProfile == profile);
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    [MenuItem(Menu + AccountProfiles.Default, true)]
    private static bool ValidateDefault() => Validate(AccountProfiles.Default);
    [MenuItem(Menu + AccountProfiles.Editor, true)]
    private static bool ValidateEditor() => Validate(AccountProfiles.Editor);
    [MenuItem(Menu + AccountProfiles.ClientOne, true)]
    private static bool ValidateClientOne() => Validate(AccountProfiles.ClientOne);
    [MenuItem(Menu + AccountProfiles.ClientTwo, true)]
    private static bool ValidateClientTwo() => Validate(AccountProfiles.ClientTwo);
}
#endif
