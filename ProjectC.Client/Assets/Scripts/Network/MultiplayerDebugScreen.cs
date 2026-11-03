using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("ProjectC.Client.PlayModeTests")]

namespace ProjectC.Client
{
    public sealed class MultiplayerDebugScreen : MonoBehaviour
    {
        string address = "ws://127.0.0.1:7777",
            boundAddress,
            selectedDeck = "Strikers";
        WebSocketTransport transport;
        string token,
            matchId = "";
        int playerId,
            gameNumber,
            cardId,
            targetId;
        long sequence,
            lastEvents;
        bool connecting,
            pending,
            resyncing;
        public readonly HashSet<int> MulliganSelection = new HashSet<int>();
        public ActionChoiceView PendingChoice { get; private set; }
        StateView state;
        LobbyView lobby;
        DeckView[] decks = Array.Empty<DeckView>();
        CardView selectedCard;
        readonly Queue<string> log = new Queue<string>();

        // Transport/controller only. The scene-authored BattleScreen prefab owns all rendering.
        public event Action<EventView> EventReceived;
        public event Action<StateView> SnapshotReceived;
        public event Action<string> NoticeReceived;
        public event Action<bool, string> CommandFinished;
        public event Action ViewInvalidated;
        string commandRequestId;
        long acceptedSequence = -1;
        public int PlayerId => playerId;
        public LobbyView Lobby => lobby;
        public DeckView[] Decks => decks;
        public string SelectedDeck
        {
            get => selectedDeck;
            set => selectedDeck = value;
        }
        string deckRequestId;
        string serverCatalogVersion = "";
        bool personalDeckSupported;
        public bool UsePersonalDeck { get; set; } = true;
        public bool PersonalMode =>
            UsePersonalDeck && ClientApp.Instance != null && ClientApp.Instance.Network == this;
        public string ConfirmedDeckName { get; private set; } = "";
        public string ServerCatalogVersion => serverCatalogVersion;
        public string CatalogProblem
        {
            get
            {
                string localVersion = CardPresentation.Library.CatalogVersion;
                return !string.IsNullOrEmpty(serverCatalogVersion) && serverCatalogVersion != localVersion
                    ? "서버와 카드 카탈로그 버전이 다릅니다. 서버/클라이언트 데이터를 함께 갱신하세요."
                    : "";
            }
        }
        public string DeckProblem
        {
            get
            {
                if (!string.IsNullOrEmpty(CatalogProblem))
                {
                    return CatalogProblem;
                }
                if (!PersonalMode)
                {
                    return "";
                }
                var app = ClientApp.Instance;
                var validation = DeckValidator.Validate(app.Decks.Selected, app.Library);
                if (!validation.CanPlay)
                {
                    return validation.Summary;
                }
                if (!personalDeckSupported)
                {
                    return "서버가 개인 덱 규칙을 지원하지 않습니다. 백귀야행 서버에 접속하세요.";
                }
                if (app.Library.CatalogVersion != serverCatalogVersion)
                {
                    return "서버와 카드 카탈로그 버전이 다릅니다.";
                }
                return "";
            }
        }
        public string DeckSummary =>
            PersonalMode
                ? (ClientApp.Instance.Decks.Selected?.Name ?? "개인 덱 없음")
                    + " · "
                    + (ClientApp.Instance.Decks.Selected?.Cards.Count ?? 0)
                    + "장"
                : "서버 덱 / " + selectedDeck;

        public void CycleDeck()
        {
            if (Busy || !string.IsNullOrEmpty(matchId))
            {
                return;
            }
            var personal = ClientApp.Instance?.Decks.Decks;
            if (PersonalMode && personal != null)
            {
                int index = -1;
                for (int i = 0; i < personal.Count; ++i)
                {
                    if (personal[i].DeckId == ClientApp.Instance.Decks.SelectedId)
                    {
                        index = i;
                    }
                }
                if (index + 1 < personal.Count)
                {
                    SelectPersonalDeck(personal[index + 1].DeckId);
                    return;
                }
                if (decks.Length > 0)
                {
                    SelectServerDeck(decks[0].Id);
                }
            }
            else
            {
                int index = _serverDeckSelected ? Array.FindIndex(decks, deck => deck.Id == selectedDeck) : -1;
                if (index + 1 < decks.Length)
                {
                    SelectServerDeck(decks[index + 1].Id);
                }
                else if (personal != null && personal.Count > 0)
                {
                    SelectPersonalDeck(personal[0].DeckId);
                }
                else if (decks.Length > 0)
                {
                    SelectServerDeck(decks[0].Id);
                }
            }
        }

        public MatchmakingState Matchmaking { get; private set; }
        public string MatchmakingError { get; private set; } = "";
        public int QueuePosition { get; private set; }
        public bool CancellingQueue => Matchmaking == MatchmakingState.Queuing && _cancelQueueRequestId != null;
        public float QueueSeconds => Matchmaking == MatchmakingState.Queuing
            ? Mathf.Max(0, Time.realtimeSinceStartup - _queueStartedAt)
            : 0;
        string _queueRequestId;
        string _cancelQueueRequestId;
        float _queueStartedAt;

        public bool ConnectionReady => Connected && !connecting && _deckCatalogReceived;
        public string ConnectionError { get; private set; } = "";
        private bool _deckCatalogReceived;
        private bool _serverDeckSelected;
        private PersonalDeck _queuedPersonalDeck;
        private string _queuedCatalogVersion;
        private string _queuedServerDeck;
        private bool _autoReadyPending;
        public bool AutomaticMatchInProgress { get; private set; }
        public bool HasDeckSelection => PersonalMode ? ClientApp.Instance.Decks.Selected != null
            : _serverDeckSelected && Array.Exists(decks, deck => deck.Id == selectedDeck);
        public bool CanQueueSelectedDeck => HasDeckSelection && string.IsNullOrEmpty(CatalogProblem)
            && (!PersonalMode || string.IsNullOrEmpty(DeckProblem));

        public bool Busy => pending || resyncing || connecting || deckRequestId != null
            || Matchmaking == MatchmakingState.Queuing;

        void ResetMatchmaking(MatchmakingState value, string error = "")
        {
            Matchmaking = value;
            MatchmakingError = error;
            QueuePosition = 0;
            _queueRequestId = null;
            _cancelQueueRequestId = null;
            if (value == MatchmakingState.Idle)
            {
                _queuedPersonalDeck = null;
                _autoReadyPending = false;
                AutomaticMatchInProgress = false;
            }
        }

        private void TryAutomaticReady()
        {
            if (!_autoReadyPending || !AutomaticMatchInProgress || Matchmaking != MatchmakingState.Matched
                || !ConnectionReady || Busy || string.IsNullOrEmpty(matchId) || lobby?.Players == null)
            {
                return;
            }
            var own = Array.Find(lobby.Players, player => player.PlayerId == playerId);
            if (own == null || !own.Occupied)
            {
                return;
            }
            _autoReadyPending = false;
            if (own.Ready || state?.Started == true)
            {
                return;
            }
            if (_queuedPersonalDeck != null)
            {
                SubmitPersonalDeck(_queuedPersonalDeck, _queuedCatalogVersion);
            }
            else
            {
                LobbyAction("SetReady", ",\"Ready\":true,\"DeckId\":" + Quote(_queuedServerDeck));
            }
        }

        public void SelectPersonalDeck(string id)
        {
            if (Busy || !string.IsNullOrEmpty(matchId) || ClientApp.Instance == null)
            {
                return;
            }
            if (!ClientApp.Instance.Decks.Select(id, out var error))
            {
                Note(error);
                return;
            }
            UsePersonalDeck = true;
        }

        public void SelectServerDeck(string id)
        {
            if (Busy || !string.IsNullOrEmpty(matchId) || !Array.Exists(decks, deck => deck.Id == id))
            {
                return;
            }
            UsePersonalDeck = false;
            selectedDeck = id;
            _serverDeckSelected = true;
        }

        internal void QueueMatch()
        {
            if (!Connected || Busy || !string.IsNullOrEmpty(matchId))
            {
                return;
            }
            if (!HasDeckSelection)
            {
                ResetMatchmaking(MatchmakingState.Error, "대전에 사용할 덱을 선택하세요.");
                return;
            }
            if (!string.IsNullOrEmpty(CatalogProblem))
            {
                ResetMatchmaking(MatchmakingState.Error, CatalogProblem);
                Note(CatalogProblem);
                return;
            }
            if (PersonalMode && !string.IsNullOrEmpty(DeckProblem))
            {
                ResetMatchmaking(MatchmakingState.Error, DeckProblem);
                Note(MatchmakingError);
                return;
            }
            ResetMatchmaking(MatchmakingState.Queuing);
            _queuedPersonalDeck = PersonalMode ? ClientApp.Instance.Decks.Selected.Copy() : null;
            _queuedCatalogVersion = PersonalMode ? ClientApp.Instance.Library.CatalogVersion : null;
            _queuedServerDeck = PersonalMode ? null : selectedDeck;
            _autoReadyPending = true;
            AutomaticMatchInProgress = true;
            _queueStartedAt = Time.realtimeSinceStartup;
            _queueRequestId = Guid.NewGuid().ToString("N");
            // QueueMatch accepts server catalog IDs only. Local personal decks are
            // submitted by the existing SubmitDeck/SetReady flow after pairing.
            Send(Request("QueueMatch", _queueRequestId)
                + (PersonalMode ? "" : ",\"DeckId\":" + Quote(selectedDeck)));
        }

        internal void CancelQueue()
        {
            if (!Connected || Matchmaking != MatchmakingState.Queuing || CancellingQueue)
            {
                return;
            }
            _cancelQueueRequestId = Guid.NewGuid().ToString("N");
            Send(Request("CancelQueue", _cancelQueueRequestId));
        }

        bool ApplyMatchmakingMessage(MessageView message)
        {
            if (message.Type == "MatchmakingQueued")
            {
                if (Matchmaking == MatchmakingState.Queuing && message.RequestId == _queueRequestId)
                {
                    QueuePosition = message.QueuePosition;
                }
                return true;
            }
            if (message.Type == "MatchmakingCancelled")
            {
                if (Matchmaking == MatchmakingState.Queuing && message.RequestId == _cancelQueueRequestId)
                {
                    ResetMatchmaking(MatchmakingState.Idle);
                }
                return true;
            }
            if (message.Type == "MatchmakingFound")
            {
                // Found has its own request ID and no game number. Never reset the
                // epoch here: snapshots, ready submission or mulligan may already exist.
                if (!string.IsNullOrEmpty(matchId) && message.MatchId == matchId)
                {
                    Matchmaking = MatchmakingState.Matched;
                    MatchmakingError = "";
                    QueuePosition = 0;
                }
                return true;
            }
            if (message.Type != "ServerError"
                || string.IsNullOrEmpty(message.RequestId)
                || (message.RequestId != _queueRequestId && message.RequestId != _cancelQueueRequestId))
            {
                return false;
            }
            string error = message.Reason + (string.IsNullOrEmpty(message.Message) ? "" : ": " + message.Message);
            bool cancelling = message.RequestId == _cancelQueueRequestId;
            if (!string.IsNullOrEmpty(matchId))
            {
                Matchmaking = MatchmakingState.Matched;
            }
            else if (message.Reason == "AlreadyQueued")
            {
                MatchmakingError = error;
            }
            else if (cancelling && message.Reason != "NotQueued")
            {
                _cancelQueueRequestId = null;
                MatchmakingError = error;
            }
            else
            {
                ResetMatchmaking(message.Reason == "NotQueued" ? MatchmakingState.Idle : MatchmakingState.Error, error);
            }
            Note(error);
            return true;
        }

        public void LeaveMatch() => LobbyAction("LeaveMatch");

        public void CloseMatch() => LobbyAction("CloseMatch");

        public void RequestSnapshot() => Resync();

        public StateView State => state;
        public long SnapshotServerTimeUnixMs { get; private set; }
        public string CurrentMatch => matchId;
        public int GameNumber => gameNumber;
        public long Sequence => sequence;

        void Awake()
        {
            Application.runInBackground = true;
        }

        void Note(string text)
        {
            log.Enqueue(text);
            while (log.Count > 100)
            {
                log.Dequeue();
            }
            NoticeReceived?.Invoke(text);
        }

        void ResetEpoch(int game)
        {
            PendingChoice = null;
            commandRequestId = null;
            acceptedSequence = -1;
            ViewInvalidated?.Invoke();
            MulliganSelection.Clear();
            gameNumber = game;
            sequence = lastEvents = 0;
            state = null;
            cardId = targetId = 0;
            selectedCard = null;
            pending = resyncing = false;
        }

        void ClearMatch()
        {
            ResetMatchmaking(MatchmakingState.Idle);
            deckRequestId = null;
            ConfirmedDeckName = "";
            token = null;
            matchId = "";
            playerId = 0;
            lobby = null;
            ResetEpoch(0);
        }

        internal bool Connected => transport != null && transport.Connected;

        internal void ConnectTo(string serverAddress)
        {
            address = serverAddress;
            Connect();
        }

        internal void DisconnectNetwork()
        {
            if (Matchmaking == MatchmakingState.Queuing)
            {
                ResetMatchmaking(MatchmakingState.Error, "매칭 대기 중 연결이 종료되었습니다. 다시 접속하세요.");
            }
            deckRequestId = null;
            PendingChoice = null;
            commandRequestId = null;
            acceptedSequence = -1;
            ViewInvalidated?.Invoke();
            transport?.Dispose();
            connecting = pending = resyncing = false;
        }

        internal void CreateMatch()
        {
            if (!Connected || Busy || !string.IsNullOrEmpty(matchId))
            {
                return;
            }
            pending = true;
            Send(Request("CreateMatch") + ",\"DeckId\":" + Quote(selectedDeck));
        }

        internal void JoinMatch(string id)
        {
            if (!Connected || Busy || !string.IsNullOrEmpty(matchId))
            {
                return;
            }
            pending = true;
            Send(
                Request("JoinMatch")
                    + ",\"MatchId\":"
                    + Quote(id)
                    + ",\"DeckId\":"
                    + Quote(selectedDeck)
            );
        }

        internal void SetReady(string deck, bool ready = true)
        {
            if (Busy || !Connected)
            {
                return;
            }
            if (ready && !string.IsNullOrEmpty(CatalogProblem))
            {
                Note(CatalogProblem);
                return;
            }
            selectedDeck = deck;
            if (ready && PersonalMode)
            {
                if (!string.IsNullOrEmpty(DeckProblem))
                {
                    Note(DeckProblem);
                    return;
                }
                var app = ClientApp.Instance;
                SubmitPersonalDeck(app.Decks.Selected, app.Library.CatalogVersion);
                return;
            }
            LobbyAction(
                "SetReady",
                ",\"Ready\":"
                    + (ready ? "true" : "false")
                    + (ready ? ",\"DeckId\":" + Quote(deck) : "")
            );
        }

        internal bool SubmitPersonalDeck(PersonalDeck deck, string version)
        {
            if (Busy || !Connected || string.IsNullOrEmpty(matchId) || deck == null)
            {
                return false;
            }
            var copy = deck.Copy();
            deckRequestId = Guid.NewGuid().ToString("N");
            pending = true;
            var payload = new Newtonsoft.Json.Linq.JObject
            {
                ["Type"] = "SubmitDeck",
                ["ProtocolVersion"] = 2,
                ["RequestId"] = deckRequestId,
                ["MatchId"] = matchId,
                ["GameNumber"] = gameNumber,
                ["CatalogVersion"] = version,
                ["DeckName"] = copy.Name,
                ["DefinitionIds"] = new Newtonsoft.Json.Linq.JArray(copy.Cards),
            };
            _ = transport.SendAsync(payload.ToString(Newtonsoft.Json.Formatting.None));
            return true;
        }

        internal void RequestRematch()
        {
            LobbyAction(
                "Rematch",
                !UsePersonalDeck || string.IsNullOrEmpty(ConfirmedDeckName)
                    ? ",\"DeckId\":" + Quote(selectedDeck)
                    : ""
            );
        }

        internal bool SendGameCommand(string type, int card = 0, int target = 0)
        {
            cardId = card;
            targetId = target;
            return Submit(type);
        }

        void Connect()
        {
            if (connecting || Connected)
            {
                return;
            }
            ConnectionError = "";
            _deckCatalogReceived = false;
            bool restoreAutomaticMatch = AutomaticMatchInProgress && !string.IsNullOrEmpty(token) && boundAddress == address;
            if (restoreAutomaticMatch)
            {
                Matchmaking = MatchmakingState.Matched;
                MatchmakingError = "";
                _autoReadyPending = state?.Started != true;
            }
            else
            {
                ResetMatchmaking(MatchmakingState.Idle);
            }
            deckRequestId = null;
            serverCatalogVersion = "";
            personalDeckSupported = false;
            transport?.Dispose();
            if (boundAddress != address || string.IsNullOrEmpty(token))
            {
                ClearMatch();
            }
            boundAddress = address;
            transport = new WebSocketTransport();
            connecting = true;
            pending = resyncing = false;
            _ = transport.ConnectAsync(address);
        }

        static string Quote(string text) =>
            "\"" + (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        string Request(string type, string requestId = null) =>
            "{\"Type\":"
            + Quote(type)
            + ",\"ProtocolVersion\":2,\"RequestId\":"
            + Quote(requestId ?? Guid.NewGuid().ToString("N"));

        void Send(string json)
        {
            if (transport != null && transport.Connected)
            {
                _ = transport.SendAsync(json + "}");
            }
        }

        void LobbyAction(string type, string extra = "")
        {
            pending = true;
            Send(
                Request(type)
                    + ",\"MatchId\":"
                    + Quote(matchId)
                    + ",\"GameNumber\":"
                    + gameNumber
                    + extra
            );
        }

        void Resync()
        {
            if (string.IsNullOrEmpty(matchId) || transport == null || !transport.Connected)
            {
                return;
            }
            PendingChoice = null;
            commandRequestId = null;
            acceptedSequence = -1;
            ViewInvalidated?.Invoke();
            resyncing = true;
            Send(Request("StateResyncRequest") + ",\"MatchId\":" + Quote(matchId));
        }

        bool Submit(string type)
        {
            string command = "{\"Type\":" + Quote(type);
            if (type == "PlayCard" || type == "Attack" || type == "DirectAttack")
            {
                command += ",\"CardId\":" + cardId;
            }
            if (type == "Attack")
            {
                command += ",\"TargetCardId\":" + targetId;
            }
            return SubmitJson(command + "}");
        }

        internal bool SendEffect(int source, int effect, List<List<EffectTargetView>> targets)
        {
            var groups = new List<string>();
            foreach (var group in targets)
            {
                var entries = new List<string>();
                foreach (var target in group)
                {
                    entries.Add(JsonUtility.ToJson(target));
                }
                groups.Add("[" + string.Join(",", entries) + "]");
            }
            return SubmitJson(
                "{\"Type\":\"ActivateEffect\",\"CardId\":"
                    + source
                    + ",\"EffectId\":"
                    + effect
                    + ",\"Targets\":["
                    + string.Join(",", groups)
                    + "]}"
            );
        }

        internal void ReconnectNetwork()
        {
            if (!Connected && !Busy)
            {
                Connect();
            }
        }

        internal bool SubmitMulligan(IEnumerable<int> cards)
        {
            if (state?.MulliganActive != true)
            {
                return false;
            }
            var own = Array.Find(state.Players, p => p.PlayerId == playerId);
            if (own == null || own.MulliganComplete)
            {
                return false;
            }
            var values = new Newtonsoft.Json.Linq.JArray(cards);
            return SubmitJson(
                new Newtonsoft.Json.Linq.JObject
                {
                    ["Type"] = "SubmitMulligan",
                    ["CardIds"] = values,
                }.ToString(Newtonsoft.Json.Formatting.None)
            );
        }

        internal bool SendPlayerAction(int source, int effect) =>
            SubmitJson(
                "{\"Type\":\"PlayerAction\",\"CardId\":" + source + ",\"EffectId\":" + effect + "}"
            );

        internal bool SendCancelChoice() =>
            PendingChoice != null
            && PendingChoice.CanCancel
            && SubmitJson(
                "{\"Type\":\"CancelAction\",\"ChoiceId\":" + Quote(PendingChoice.ChoiceId) + "}"
            );

        internal bool SendChoice(List<string> choices)
        {
            if (PendingChoice == null)
            {
                return false;
            }
            var values = new List<string>();
            foreach (var choice in choices)
            {
                values.Add(Quote(choice));
            }
            return SubmitJson(
                "{\"Type\":\"ResolveChoice\",\"ChoiceId\":"
                    + Quote(PendingChoice.ChoiceId)
                    + ",\"Choices\":["
                    + string.Join(",", values)
                    + "]}"
            );
        }

        bool SubmitJson(string command)
        {
            if (!Connected || Busy || string.IsNullOrEmpty(matchId))
            {
                return false;
            }
            commandRequestId = Guid.NewGuid().ToString("N");
            acceptedSequence = -1;
            pending = true;
            Send(
                Request("SubmitCommand", commandRequestId)
                    + ",\"MatchId\":"
                    + Quote(matchId)
                    + ",\"GameNumber\":"
                    + gameNumber
                    + ",\"SequenceNumber\":"
                    + sequence
                    + ",\"Command\":"
                    + command
            );
            return true;
        }

        void Update()
        {
            TryAutomaticReady();
            if (transport == null)
            {
                return;
            }
            while (transport.Notices.TryDequeue(out var notice))
            {
                Note(notice);
                if (notice != "Connected" && notice != "Disconnected")
                {
                    ConnectionError = notice;
                }
                if (notice == "Connected")
                {
                    connecting = false;
                    Send(Request("ListDecks"));
                    if (!string.IsNullOrEmpty(token))
                    {
                        Send(
                            Request("JoinMatch")
                                + ",\"MatchId\":"
                                + Quote(matchId)
                                + ",\"SessionToken\":"
                                + Quote(token)
                        );
                    }
                }
                if (notice == "Disconnected")
                {
                    _deckCatalogReceived = false;
                    if (string.IsNullOrEmpty(ConnectionError))
                    {
                        ConnectionError = "연결이 종료되었습니다.";
                    }
                    if (AutomaticMatchInProgress && State?.Started != true)
                    {
                        _autoReadyPending = true;
                    }
                    bool wasQueuing = Matchmaking == MatchmakingState.Queuing
                        || Matchmaking == MatchmakingState.Error;
                    deckRequestId = null;
                    connecting = pending = resyncing = false;
                    commandRequestId = null;
                    acceptedSequence = -1;
                    ViewInvalidated?.Invoke();
                    if (string.IsNullOrEmpty(token))
                    {
                        ClearMatch();
                    }
                    if (wasQueuing)
                    {
                        ResetMatchmaking(MatchmakingState.Error, "매칭 대기 중 연결이 종료되었습니다. 다시 접속하세요.");
                    }
                }
                if (notice.StartsWith("Send failed"))
                {
                    if (Matchmaking == MatchmakingState.Queuing)
                    {
                        DisconnectNetwork();
                        MatchmakingError = notice;
                    }
                    deckRequestId = null;
                    pending = false;
                    commandRequestId = null;
                    CommandFinished?.Invoke(false, notice);
                }
            }
            while (transport.Messages.TryDequeue(out var json))
            {
                try
                {
                    ApplyMessage(json);
                }
                catch (Exception error)
                {
                    pending = false;
                    if (Matchmaking == MatchmakingState.Queuing)
                    {
                        DisconnectNetwork();
                        MatchmakingError = "Protocol error: " + error.Message;
                    }
                    Note("Protocol error: " + error.Message);
                    Resync();
                }
            }
        }

        internal void ApplyMessage(string json)
        {
            var message = JsonUtility.FromJson<MessageView>(json);
            if (message.ProtocolVersion != 2)
            {
                throw new InvalidOperationException(
                    "Server/client protocol mismatch; rebuild both"
                );
            }
            if (ApplyMatchmakingMessage(message))
            {
                return;
            }
            if (message.Type == "DeckCatalog")
            {
                serverCatalogVersion = message.CatalogVersion;
                personalDeckSupported = message.PersonalDeckSupported;
                if (!string.IsNullOrEmpty(CatalogProblem))
                {
                    Note(CatalogProblem);
                }
                decks = JsonUtility.FromJson<DeckCatalogMessage>(json).Payload;
                _deckCatalogReceived = true;
                if (decks.Length > 0 && !Array.Exists(decks, d => d.Id == selectedDeck))
                {
                    selectedDeck = decks[0].Id;
                }
                return;
            }
            if (message.Type == "MatchCreated" || message.Type == "MatchJoined")
            {
                var savedMulligan =
                    matchId == message.MatchId && gameNumber == message.GameNumber
                        ? new List<int>(MulliganSelection)
                        : new List<int>();
                if (Matchmaking == MatchmakingState.Queuing)
                {
                    // Keep cancellation correlation until its response; a formed match wins.
                    Matchmaking = MatchmakingState.Matched;
                    MatchmakingError = "";
                    QueuePosition = 0;
                }
                matchId = message.MatchId;
                playerId = message.PlayerId;
                token = message.SessionToken;
                if (!string.IsNullOrEmpty(message.DeckId))
                {
                    selectedDeck = message.DeckId;
                }
                ResetEpoch(message.GameNumber);
                foreach (var id in savedMulligan)
                {
                    MulliganSelection.Add(id);
                }
                Note("Joined " + matchId + " as Player " + playerId);
                return;
            }
            if (message.Type == "DeckAccepted" || message.Type == "DeckRejected")
            {
                if (
                    message.MatchId != matchId
                    || message.GameNumber != gameNumber
                    || message.RequestId != deckRequestId
                )
                {
                    return;
                }
                deckRequestId = null;
                pending = false;
                if (message.Type == "DeckRejected")
                {
                    if (AutomaticMatchInProgress)
                    {
                        Matchmaking = MatchmakingState.Error;
                        MatchmakingError = "선택한 덱을 서버에 제출할 수 없습니다. " + message.Message;
                        _autoReadyPending = false;
                    }
                    Note(message.Reason + ": " + message.Message);
                    return;
                }
                ConfirmedDeckName = message.DeckName;
                LobbyAction("SetReady", ",\"Ready\":true");
                return;
            }
            if (message.Type == "ServerError" || message.Type == "CommandRejected")
            {
                if (
                    !string.IsNullOrEmpty(message.MatchId)
                    && !string.IsNullOrEmpty(matchId)
                    && message.MatchId != matchId
                )
                {
                    return;
                }
                if (message.GameNumber > 0 && message.GameNumber < gameNumber)
                {
                    return;
                }
                if (
                    commandRequestId == null
                    || message.RequestId == commandRequestId
                    || message.Type == "ServerError"
                )
                {
                    pending = resyncing = false;
                }
                if (message.RequestId == deckRequestId)
                {
                    deckRequestId = null;
                }
                if (AutomaticMatchInProgress && state?.Started != true)
                {
                    Matchmaking = MatchmakingState.Error;
                    MatchmakingError = "대전을 준비할 수 없습니다. " + message.Message;
                    _autoReadyPending = false;
                }
                Note(
                    "Rejected: "
                        + (
                            string.IsNullOrEmpty(message.Message)
                                ? Reason(message.Reason)
                                : message.Reason + ": " + message.Message
                        )
                );
                if (
                    commandRequestId != null
                    && (message.RequestId == commandRequestId || message.Type == "ServerError")
                )
                {
                    commandRequestId = null;
                    acceptedSequence = -1;
                    CommandFinished?.Invoke(false, Reason(message.Reason));
                }
                if (message.Reason == "InvalidSession" || message.Reason == "MatchNotFound")
                {
                    ClearMatch();
                    return;
                }
                if (message.Reason == "StaleSequence" || message.Reason == "StaleGame")
                {
                    Resync();
                }
                return;
            }
            // A late message from a departed match must never populate the current screen.
            if (message.MatchId != matchId || string.IsNullOrEmpty(matchId))
            {
                return;
            }
            if (message.GameNumber < gameNumber)
            {
                return;
            }
            if (message.GameNumber > gameNumber)
            {
                ResetEpoch(message.GameNumber);
            }
            switch (message.Type)
            {
                case "SessionUpdated":
                    token = message.SessionToken;
                    break;
                case "LobbyState":
                    ConfirmedDeckName = message.PersonalDeckSelected ? message.DeckName : "";
                    lobby = JsonUtility.FromJson<LobbyMessage>(json).Payload;
                    if (commandRequestId == null)
                    {
                        pending = false;
                    }
                    break;
                case "LobbyAccepted":
                    if (commandRequestId == null)
                    {
                        pending = false;
                    }
                    break;
                case "MatchStarted":
                    Note("Game " + gameNumber + " started");
                    break;
                case "GameEvents":
                    if (message.SequenceNumber <= lastEvents)
                    {
                        return;
                    }
                    if (lastEvents != 0 && message.SequenceNumber != lastEvents + 1)
                    {
                        Resync();
                        return;
                    }
                    lastEvents = message.SequenceNumber;
                    foreach (var e in JsonUtility.FromJson<EventsMessage>(json).Payload)
                    {
                        Note(EventText(e));
                        EventReceived?.Invoke(e);
                    }
                    break;
                case "CommandPending":
                    break;
                case "ActionChoiceRequired":
                    if (message.SequenceNumber < sequence)
                    {
                        break;
                    }
                    PendingChoice = JsonUtility.FromJson<ActionChoiceMessage>(json).Payload;
                    commandRequestId = null;
                    acceptedSequence = -1;
                    pending = resyncing = false;
                    break;
                case "CommandAccepted":
                    if (message.RequestId == commandRequestId)
                    {
                        acceptedSequence = message.SequenceNumber;
                    }
                    break;
                case "StateSnapshot":
                    if (message.SequenceNumber < sequence)
                    {
                        return;
                    }
                    state = JsonUtility.FromJson<SnapshotMessage>(json).Payload;
                    sequence = lastEvents = message.SequenceNumber;
                    resyncing = false;
                    if (state.ResolvingPlayerId == 0)
                    {
                        PendingChoice = null;
                    }
                    if (
                        commandRequestId != null
                        && acceptedSequence >= 0
                        && sequence >= acceptedSequence
                    )
                    {
                        commandRequestId = null;
                        acceptedSequence = -1;
                        pending = false;
                        CommandFinished?.Invoke(true, "");
                    }
                    else if (commandRequestId == null)
                    {
                        pending = false;
                    }
                    // Selection is a view hint; discard it if its old card no longer exists in our visible zones.
                    RefreshSelection();
                    SnapshotServerTimeUnixMs = message.ServerTimeUnixMs;
                    SnapshotReceived?.Invoke(state);
                    break;
                case "MatchEnded":
                    token = null;
                    pending = false;
                    Note(
                        message.WinnerPlayerId == playerId
                            ? "VICTORY - choose Rematch or Return to lobby"
                            : "DEFEAT - choose Rematch or Return to lobby"
                    );
                    break;
                case "MatchLeft":
                    ClearMatch();
                    Note("Returned to lobby");
                    break;
                case "MatchClosed":
                    ClearMatch();
                    Note("Match closed: " + message.Reason);
                    break;
            }
        }

        void RefreshSelection()
        {
            selectedCard = null;
            if (state?.Players == null)
            {
                return;
            }
            bool targetExists = false;
            foreach (var player in state.Players)
            {
                foreach (var zone in new[] { player.Hand, player.Field, player.Graveyard })
                {
                    if (zone != null)
                    {
                        foreach (var card in zone)
                        {
                            if (card.Id == cardId)
                            {
                                selectedCard = card;
                            }
                        }
                    }
                }
                if (player.PlayerId != playerId && player.Field != null)
                {
                    foreach (var card in player.Field)
                    {
                        if (card.Id == targetId)
                        {
                            targetExists = true;
                        }
                    }
                }
            }
            if (selectedCard == null)
            {
                cardId = 0;
            }
            if (!targetExists)
            {
                targetId = 0;
            }
        }

        static string Reason(string reason)
        {
            if (reason == null)
            {
                return "Unknown reason";
            }
            if (reason.Contains("MulliganPending"))
            {
                return "양쪽 플레이어가 멀리건을 완료해야 합니다.";
            }
            if (reason.Contains("MulliganAlreadyComplete"))
            {
                return "이미 멀리건을 완료했습니다.";
            }
            if (reason.Contains("MulliganClosed"))
            {
                return "멀리건 시간이 종료되었거나 이미 전투가 시작되었습니다.";
            }
            if (reason.Contains("InvalidMulliganSelection"))
            {
                return "본인 손패에서 중복 없이 0~5장을 선택하세요.";
            }
            if (reason.Contains("WrongPhase"))
            {
                return "This action is unavailable in the current phase.";
            }
            if (reason.Contains("NotActivePlayer"))
            {
                return "Wait for your turn.";
            }
            if (reason.Contains("NormalSummonLimit"))
            {
                return "One normal summon per turn.";
            }
            if (reason.Contains("BattleRejected"))
            {
                if (reason.EndsWith(":9"))
                {
                    return "Summoning sickness: attack on your next turn.";
                }
                if (reason.EndsWith(":7"))
                {
                    return "This monster already attacked this turn.";
                }
                return "Attack unavailable. Select your ready monster and a valid target.";
            }
            return reason;
        }

        static string Phase(int phase) =>
            phase == 0 ? "Main"
            : phase == 1 ? "Battle"
            : "End";

        static string EventText(EventView e)
        {
            switch (e.Type)
            {
                case 0:
                    return "Battle started: #" + e.SourceCardId + " vs #" + e.TargetCardId;
                case 1:
                    return "Card #"
                        + e.TargetCardId
                        + " took "
                        + e.Damage
                        + " damage (HP "
                        + e.HpAfter
                        + ")";
                case 2:
                    return "Card #" + e.TargetCardId + " destroyed";
                case 3:
                    return "Battle completed";
                case 4:
                    return e.TargetCardId == 0
                        ? "Opponent moved a hidden card"
                        : "Card #" + e.TargetCardId + " changed zone";
                case 5:
                    return "Player " + e.TargetPlayerId + " turn " + e.TurnNumber + " started";
                case 6:
                    return "Player " + e.TargetPlayerId + " turn ended";
                case 7:
                    return "Phase: " + Phase(e.Phase);
                case 8:
                    return "Player " + e.TargetPlayerId + " Life: " + e.LifeAfter;
                case 9:
                    return "Game finished";
                case 10:
                    return "Player " + e.TargetPlayerId + " took " + e.Damage + " damage";
                case 11:
                    return "Player " + e.TargetPlayerId + " summoned #" + e.TargetCardId;
                case 13:
                    return "Card effect activated";
                case 14:
                    return "Card effect resolved";
                case 15:
                    return "Card manifested";
                case 16:
                    return "Card healed";
                case 18:
                    return "Player " + e.TargetPlayerId + " Yokai power: " + e.YokaiPowerAfter;
                case 20:
                    return "Player " + e.TargetPlayerId + " 멀리건 완료";
                case 19:
                    return "Normal spell activated";
                case 17:
                    return "Card #" + e.TargetCardId + " stats changed (ATK " + e.AttackAfter + ")";
                default:
                    return "Game event " + e.Type;
            }
        }

        void OnDestroy()
        {
            transport?.Dispose();
        }
    }
}
