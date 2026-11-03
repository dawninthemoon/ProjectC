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
        bool wasStarted;

        void Start()
        {
            app = ClientApp.Instance;
            this.Bind("MatchPanel", out matchmaking);
            matchPanel = matchmaking.gameObject;
            matchmaking.Bind(app.Network);
            matchmaking.Address.text = app.ServerAddress;
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
            this.BindButton("DeckManager", out Button _, () => app.Navigate("DeckEditorScene"));
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
            app.Network.NoticeReceived += Notice;
            wasStarted = app.Network.State?.Started == true && !app.Network.State.Finished;
            feedback.text = app.Error + app.Decks.Warning;
        }

        void Notice(string text)
        {
            feedback.text = text;
        }

        void OnDestroy()
        {
            if (app != null)
            {
                app.Network.NoticeReceived -= Notice;
                app.ServerAddress = matchmaking.Address.text;
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
                        + "\n준비 시 선택한 개인 덱을 서버에 제출합니다.";
            serverDeck.text =
                "온라인 대전 덱: "
                + app.Network.DeckSummary
                + "\n덱 버튼으로 개인 덱 / 서버 테스트 덱을 전환합니다.";
            resume.gameObject.SetActive(
                app.Network.State?.Started == true && !app.Network.State.Finished
            );
            bool started = app.Network.State?.Started == true && !app.Network.State.Finished;
            if (started && !wasStarted)
            {
                app.Navigate("BattleScene");
            }
            wasStarted = started;
        }
    }
}
