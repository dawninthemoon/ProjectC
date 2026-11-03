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
            ServerAddress = ServerConnectionSettings.AddressFor(Environment);
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
            _popup.Show(message, actions);
        }

        public void SetEnvironment(ServerEnvironment environment)
        {
            if (environment == Environment)
            {
                return;
            }
            Environment = environment;
            ServerAddress = ServerConnectionSettings.AddressFor(environment);
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
