using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectC.Client
{
    [DefaultExecutionOrder(-200)]
    public sealed class ClientApp : MonoBehaviour
    {
        public static ClientApp Instance { get; private set; }
        internal static string StorageOverride;
        public MultiplayerDebugScreen Network { get; private set; }
        public WebsiteSession Website { get; private set; }
        public bool UseWebsite => Environment == ServerEnvironment.Production || _connectionSettings?.LocalWebsiteEnabled == true;
        public bool WebsiteBusy { get; private set; }
        public async System.Threading.Tasks.Task PrepareWebsiteAsync()
        {
            if (!UseWebsite)
            {
                return;
            }
            if (Website == null)
            {
                string url = _connectionSettings != null ? _connectionSettings.ApiAddressFor(Environment)
                    : Environment == ServerEnvironment.Local ? ServerConnectionSettings.LocalApiAddress : ServerConnectionSettings.ProductionApiAddress;
                Website = new WebsiteSession(url, Mathf.CeilToInt(ConnectionTimeoutSeconds));
            }
            await Website.EnsureSessionAsync();
            await Website.LoadUserDataAsync();
        }
        public async void RefreshWebsiteData()
        {
            if (WebsiteBusy || Network.Busy)
            {
                return;
            }
            WebsiteBusy = true;
            try
            {
                await PrepareWebsiteAsync();
            }
            catch (Exception)
            {
                ShowPopup("사용자 정보를 불러올 수 없습니다.", new PopupAction("재시도", RefreshWebsiteData));
            }
            finally
            {
                WebsiteBusy = false;
            }
        }
        public async void UploadSelectedDeck()
        {
            if (WebsiteBusy || Network.Busy || !string.IsNullOrEmpty(Network.CurrentMatch))
            {
                return;
            }
            var deck = Decks.Selected?.Copy();
            var validation = DeckValidator.Validate(deck, Library);
            if (!validation.CanPlay)
            {
                ShowPopup(validation.Summary);
                return;
            }
            WebsiteBusy = true;
            try
            {
                await PrepareWebsiteAsync();
                await Website.UploadDeckAsync(deck);
                ShowPopup("Website에 덱을 저장했습니다.");
            }
            catch (Exception)
            {
                // POST is not retried: a lost response may already have created the deck.
                ShowPopup("덱 저장을 확인할 수 없습니다. 목록을 새로고침해 확인하세요.", new PopupAction("목록 새로고침", RefreshWebsiteData));
            }
            finally
            {
                WebsiteBusy = false;
            }
        }
        public async void ReceiveRankChange(WebsiteRankChange change)
        {
            try
            {
                if (Website != null)
                {
                    await Website.ApplyRankChangeAsync(change);
                }
            }
            catch (Exception)
            {
                ShowPopup("경기 결과는 저장되었으나 최신 랭크를 불러오지 못했습니다.", new PopupAction("새로고침", RefreshWebsiteData));
            }
        }
        public CardLibrary Library { get; private set; }
        public DeckRepository Decks { get; private set; }
        public string Error { get; private set; } = "";
        public string ServerAddress { get; set; } = ServerConnectionSettings.ProductionAddress;
        private const string EnvironmentPreference = "ProjectC.Connection.Environment";
        [SerializeField] private ServerConnectionSettings _connectionSettings;
        private CommonPopupView _popup;
        private bool _hadLiveConnection;
        private string _reportedMatchError = "";
        public ServerEnvironment Environment { get; private set; } = ServerEnvironment.Production;
        public float ConnectionTimeoutSeconds => _connectionSettings != null
            ? _connectionSettings.TimeoutSeconds : ServerConnectionSettings.DefaultTimeoutSeconds;
        public bool Navigating { get; private set; }
        public PersonalDeck Starter { get; private set; }
        public System.Collections.Generic.IReadOnlyList<PersonalDeck> Samples { get; private set; } = Array.Empty<PersonalDeck>();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Network = GetComponent<MultiplayerDebugScreen>();
            _connectionSettings = _connectionSettings != null ? _connectionSettings
                : Resources.Load<ServerConnectionSettings>("Network/ServerConnectionSettings");
            int environment = PlayerPrefs.GetInt(EnvironmentPreference,
                (int)(_connectionSettings != null ? _connectionSettings.DefaultEnvironment : ServerEnvironment.Production));
            Environment = environment == (int)ServerEnvironment.Local ? ServerEnvironment.Local : ServerEnvironment.Production;
            ServerAddress = _connectionSettings != null ? _connectionSettings.BattleAddressFor(Environment)
                : ServerConnectionSettings.AddressFor(Environment);
            Decks = new DeckRepository(StorageOverride ?? Application.persistentDataPath);
            Decks.Load();
            try
            {
                Library = CardLibrary.LoadResources();
                Samples = CardLibrary.Samples(Resources.Load<TextAsset>("CardData/HyakkiDecks").text, Library);
                Starter = Samples[0];
            }
            catch (Exception exception)
            {
                Library = new CardLibrary();
                Error = "카드 데이터를 읽지 못했습니다: " + exception.Message;
            }
            SceneManager.sceneLoaded += Loaded;
        }

        void Loaded(Scene scene, LoadSceneMode mode)
        {
            Navigating = false;
            _popup?.Hide();
        }

        public void ShowPopup(string message, params PopupAction[] actions)
        {
            if (_popup == null)
            {
                var prefab = Resources.Load<GameObject>("UI/CommonPopup");
                if (prefab == null)
                {
                    throw new InvalidOperationException("Resources/UI/CommonPopup.prefab is required.");
                }
                _popup = Instantiate(prefab, transform).GetComponent<CommonPopupView>();
                if (_popup == null)
                {
                    throw new InvalidOperationException("CommonPopup prefab requires CommonPopupView.");
                }
            }
            _popup.Show(message, actions == null || actions.Length == 0
                ? new[] { new PopupAction("확인", null) } : actions);
        }

        public void SetEnvironment(ServerEnvironment environment)
        {
            if (environment == Environment)
            {
                return;
            }
            Environment = environment;
            Website = null;
            ServerAddress = _connectionSettings != null ? _connectionSettings.BattleAddressFor(environment)
                : ServerConnectionSettings.AddressFor(environment);
            PlayerPrefs.SetInt(EnvironmentPreference, (int)environment);
            PlayerPrefs.Save();
            _hadLiveConnection = false;
            Network.DisconnectNetwork();
            Navigate("ConnectionScene");
        }

        private void Update()
        {
            if (Network.ConnectionReady)
            {
                _hadLiveConnection = true;
            }
            else if (_hadLiveConnection && !Network.Connected && !Navigating
                && SceneManager.GetActiveScene().name != "ConnectionScene")
            {
                _hadLiveConnection = false;
                Navigate("ConnectionScene");
            }
            if (Network.AutomaticMatchInProgress && Network.Matchmaking == MatchmakingState.Error
                && Network.Connected && _reportedMatchError != Network.MatchmakingError)
            {
                _reportedMatchError = Network.MatchmakingError;
                ShowPopup(Network.MatchmakingError, new PopupAction("로비로", () =>
                {
                    Network.LeaveMatch();
                    Navigate("LobbyScene");
                }));
            }
            if (Network.Matchmaking != MatchmakingState.Error)
            {
                _reportedMatchError = "";
            }
        }

        public void Navigate(string scene)
        {
            if (Navigating || SceneManager.GetActiveScene().name == scene)
            {
                return;
            }
            Navigating = true;
            SceneManager.LoadSceneAsync(scene);
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= Loaded;
                Instance = null;
            }
        }
    }
}
