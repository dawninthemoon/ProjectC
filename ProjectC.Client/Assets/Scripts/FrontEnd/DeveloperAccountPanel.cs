#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace ProjectC.Client
{
    // Diagnostic controls only. This component and its UI are excluded from Release players.
    public sealed class DeveloperAccountPanel : MonoBehaviour
    {
        private Rect _window = new Rect(12, 12, 470, 0);
        private bool _expanded;

        private void OnGUI()
        {
            _window = GUILayout.Window(GetInstanceID(), _window, DrawWindow, "개발 계정 프로필");
        }

        private void DrawWindow(int windowId)
        {
            var app = ClientApp.Instance;
            if (app == null)
            {
                return;
            }
            GUILayout.Label("AccountProfileId: " + app.AccountProfileId);
            GUILayout.Label("Website PlayerId: " + (app.Website?.User?.playerId ?? "세션 없음"));
            _expanded = GUILayout.Toggle(_expanded, "계정 프로필 관리");
            if (_expanded)
            {
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && !app.IsChangingAccount && !app.Navigating && !app.WebsiteBusy;
                foreach (string profile in AccountProfiles.Options)
                {
                    if (GUILayout.Button(profile + (profile == app.AccountProfileId ? " (현재)" : "")))
                    {
                        app.ChangeAccountProfile(profile);
                    }
                }
                if (app.AccountLoggedOut)
                {
                    if (GUILayout.Button("현재 프로필로 로그인"))
                    {
                        app.ChangeAccountProfile(app.AccountProfileId);
                    }
                }
                else if (GUILayout.Button("현재 프로필 로그아웃"))
                {
                    app.LogoutCurrentProfile();
                }
                GUI.enabled = previousEnabled;
                GUILayout.Label("프로필 전환은 연결을 종료합니다. 다른 프로필의 저장 정보는 유지됩니다.");
            }
            if (app.IsChangingAccount)
            {
                GUILayout.Label("계정 전환 중...");
            }
            GUI.DragWindow();
        }
    }
}
#endif
