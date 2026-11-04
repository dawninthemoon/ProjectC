#if UNITY_EDITOR
using System;
using ProjectC.Client;
using UnityEditor;
using UnityEngine;

public static class VerifyAccountProfileMenus
{
    public static void Run()
    {
        string previous = AccountProfiles.EditorProfile;
        try
        {
            foreach (string profile in AccountProfiles.Options)
            {
                if (!EditorApplication.ExecuteMenuItem("Project C/Development/Account profile/" + profile)
                    || AccountProfiles.EditorProfile != profile)
                {
                    throw new InvalidOperationException("Account profile menu is missing or did not select " + profile);
                }
            }
        }
        finally
        {
            AccountProfiles.EditorProfile = previous;
        }
        Debug.Log("Account profile editor menus: all four registered and selectable before Play Mode on " + Application.platform);
    }
}
#endif
