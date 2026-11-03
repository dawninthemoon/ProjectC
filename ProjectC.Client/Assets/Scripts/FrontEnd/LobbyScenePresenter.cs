using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class LobbyScenePresenter : MonoBehaviour
    {
        ClientApp app;
        LobbyPanelView matchmaking;
        GameObject matchPanel,
            settings;
        TMP_Text selected,
            feedback,
            serverDeck;
        Button resume;
        private Button _environment;
        private Button _deckManager;

        void Start()
        {
            app = ClientApp.Instance;
            this.Bind("MatchPanel", out matchmaking);
            matchPanel = matchmaking.gameObject;
            matchmaking.Bind(app.Network);
            if (!app.Network.ConnectionReady)
            {
                app.Navigate("ConnectionScene");
            }
            this.Bind("SettingsPanel", out settings);
            this.Bind("SelectedPersonalDeck", out selected);
            this.Bind("Notice", out feedback);
            this.Bind("ServerDeckSummary", out serverDeck);
            this.BindButton(
                "BattleMenu",
                out Button _,
                () =>
                {
                    settings.SetActive(false);
                    matchPanel.SetActive(!matchPanel.activeSelf);
                }
            );
            this.BindButton("DeckManager", out _deckManager, () =>
            {
                if (app.UseWebsite)
                {
                    app.ShowPopup("개인 덱 관리", new PopupAction("덱 편집", () => app.Navigate("DeckEditorScene")),
                        new PopupAction("선택한 로컬 덱 업로드", app.UploadSelectedDeck),
                        new PopupAction("온라인 목록 새로고침", app.RefreshWebsiteData));
                }
                else
                {
                    app.Navigate("DeckEditorScene");
                }
            });
            this.BindButton(
                "SettingsMenu",
                out Button _,
                () =>
                {
                    matchPanel.SetActive(false);
                    settings.SetActive(!settings.activeSelf);
                }
            );
            this.BindButton("CloseSettings", out Button _, () => settings.SetActive(false));
            this.BindButton("ResumeBattle", out resume, () => app.Navigate("BattleScene"));
            this.Bind("Volume", out Slider volume);
            volume.value = AudioListener.volume;
            volume.onValueChanged.AddListener(v => AudioListener.volume = v);
            if (!this.Bind("UiSfxVolume", out Slider uiVolume)
                || !this.Bind("BattleSfxVolume", out Slider battleVolume))
            {
                throw new System.InvalidOperationException("SettingsPanel requires UI and battle sound volume sliders.");
            }
            uiVolume.SetValueWithoutNotify(ClientAudio.Shared.UiVolume);
            battleVolume.SetValueWithoutNotify(ClientAudio.Shared.BattleVolume);
            uiVolume.onValueChanged.AddListener(value => ClientAudio.Shared.UiVolume = value);
            battleVolume.onValueChanged.AddListener(value => ClientAudio.Shared.BattleVolume = value);
            this.Bind("Fullscreen", out Toggle fullscreen);
            fullscreen.SetIsOnWithoutNotify(Screen.fullScreen);
            fullscreen.onValueChanged.AddListener(value => Screen.fullScreen = value);
            if (!this.Bind("ConnectionEnvironment", out _environment))
            {
                throw new System.InvalidOperationException("SettingsPanel requires ConnectionEnvironment button.");
            }
            _environment.onClick.AddListener(() => app.SetEnvironment(app.Environment == ServerEnvironment.Local
                ? ServerEnvironment.Production : ServerEnvironment.Local));
            app.Network.NoticeReceived += Notice;
            feedback.text = app.Error + app.Decks.Warning;
        }

        void Notice(string text)
        {
            feedback.text = app.Network.MatchmakingError;
        }

        void OnDestroy()
        {
            if (app != null)
            {
                app.Network.NoticeReceived -= Notice;
                if (app.Network.Matchmaking == MatchmakingState.Queuing)
                {
                    app.Network.CancelQueue();
                }
            }
        }

        void Update()
        {
            if (app == null)
            {
                return;
            }
            matchmaking.Render();
            var deck = app.Decks.Selected;
            selected.text =
                deck == null
                    ? "개인 덱 없음\n덱 관리에서 백귀야행 기본 덱을 복사하세요."
                    : deck.Name
                        + "\n"
                        + deck.Cards.Count
                        + "장 · "
                        + (
                            DeckValidator.Validate(deck, app.Library).CanPlay
                                ? "구성 완료"
                                : "미완성 덱"
                        )
                        + "\n선택한 덱으로 대전합니다.";
            if (app.UseWebsite && app.Website != null)
            {
                var website = app.Website;
                selected.text = (website.User?.displayName ?? "로그인 중") + "\n" + website.Rank
                    + "\n선택 덱: " + (website.SelectedDeck?.name ?? "없음")
                    + "\n" + string.Join(" / ", System.Linq.Enumerable.Select(website.Decks, deck => deck.name));
            }
            serverDeck.text = "덱 선택 버튼으로 사용할 덱을 바꿀 수 있습니다.";
            _environment.GetComponentInChildren<TMP_Text>().text = "서버: " + app.Environment + " · 변경 시 재연결";
            bool joined = !string.IsNullOrEmpty(app.Network.CurrentMatch);
            _environment.interactable = !app.WebsiteBusy && !app.Network.Busy && !joined;
            _deckManager.interactable = !app.WebsiteBusy && !app.Network.Busy && !joined;
            resume.gameObject.SetActive(false);
            bool started = app.Network.State?.Started == true && !app.Network.State.Finished;
            if (app.Network.ConnectionReady && (started || (joined && app.Network.Matchmaking == MatchmakingState.Matched)))
            {
                app.Navigate("BattleScene");
            }
        }
    }
}
