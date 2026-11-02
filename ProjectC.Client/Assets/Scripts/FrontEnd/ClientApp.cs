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
        public string ServerAddress { get; set; } = "ws://127.0.0.1:7777";
        public bool Navigating { get; private set; }
        public PersonalDeck Starter { get; private set; }

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
            Decks = new DeckRepository(StorageOverride ?? Application.persistentDataPath);
            Decks.Load();
            try
            {
                Library = CardLibrary.LoadResources();
                Starter = CardLibrary.Starter(
                    Resources.Load<TextAsset>("CardData/HyakkiDecks").text
                );
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
