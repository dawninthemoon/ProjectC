using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
[assembly: InternalsVisibleTo("ProjectC.Client.PlayModeTests")]

namespace ProjectC.Client
{
    public sealed class MultiplayerDebugScreen : MonoBehaviour
    {
        string address = "ws://127.0.0.1:7777", boundAddress, selectedDeck = "Strikers";
        WebSocketTransport transport;
        string token, matchId = "";
        int playerId, gameNumber, cardId, targetId;
        long sequence, lastEvents;
        bool connecting, pending, resyncing;
        StateView state;
        LobbyView lobby;
        DeckView[] decks = Array.Empty<DeckView>();
        CardView selectedCard;
        readonly Queue<string> log = new Queue<string>();
        // Transport/controller only. The scene-authored BattleScreen prefab owns all rendering.
        public event Action<EventView> EventReceived;
        public event Action<string> NoticeReceived;
        public event Action<bool,string> CommandFinished;
        public event Action ViewInvalidated;
        string commandRequestId;
        long acceptedSequence = -1;
        public int PlayerId => playerId;
        public LobbyView Lobby => lobby;
        public DeckView[] Decks => decks;
        public string SelectedDeck { get => selectedDeck; set => selectedDeck = value; }
        public bool Busy => pending || resyncing || connecting;
        public void LeaveMatch() => LobbyAction("LeaveMatch");
        public void CloseMatch() => LobbyAction("CloseMatch");
        public void RequestSnapshot() => Resync();
        public StateView State => state;
        public string CurrentMatch => matchId;
        public int GameNumber => gameNumber;
        public long Sequence => sequence;

        void Awake() { Application.runInBackground = true; }
        void Note(string text) { log.Enqueue(text); while (log.Count > 100) log.Dequeue(); NoticeReceived?.Invoke(text); }
        void ResetEpoch(int game)
        {
            commandRequestId=null; acceptedSequence=-1; ViewInvalidated?.Invoke();
            gameNumber = game; sequence = lastEvents = 0; state = null;
            cardId = targetId = 0; selectedCard = null; pending = resyncing = false;
        }
        void ClearMatch()
        {
            token = null; matchId = ""; playerId = 0; lobby = null; ResetEpoch(0);
        }
        internal bool Connected => transport != null && transport.Connected;
        internal void ConnectTo(string serverAddress) { address = serverAddress; Connect(); }
        internal void DisconnectNetwork() { commandRequestId=null; acceptedSequence=-1; ViewInvalidated?.Invoke(); transport?.Dispose(); connecting = pending = resyncing = false; }
        internal void CreateMatch() { pending = true; Send(Request("CreateMatch") + ",\"DeckId\":" + Quote(selectedDeck)); }
        internal void JoinMatch(string id) { pending = true; Send(Request("JoinMatch") + ",\"MatchId\":" + Quote(id) + ",\"DeckId\":" + Quote(selectedDeck)); }
        internal void SetReady(string deck, bool ready = true) { selectedDeck = deck; LobbyAction("SetReady", ",\"Ready\":" + (ready ? "true" : "false") + ",\"DeckId\":" + Quote(deck)); }
        internal void RequestRematch() { LobbyAction("Rematch", ",\"DeckId\":" + Quote(selectedDeck)); }
        internal bool SendGameCommand(string type, int card = 0, int target = 0) { cardId = card; targetId = target; return Submit(type); }
        void Connect()
        {
            transport?.Dispose();
            if (boundAddress != address || string.IsNullOrEmpty(token)) ClearMatch();
            boundAddress = address;
            transport = new WebSocketTransport();
            connecting = true; pending = resyncing = false;
            _ = transport.ConnectAsync(address);
        }
        static string Quote(string text) => "\"" + (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string Request(string type, string requestId = null) => "{\"Type\":" + Quote(type) + ",\"ProtocolVersion\":2,\"RequestId\":" + Quote(requestId ?? Guid.NewGuid().ToString("N"));
        void Send(string json) { if (transport != null && transport.Connected) _ = transport.SendAsync(json + "}"); }
        void LobbyAction(string type, string extra = "")
        {
            pending = true;
            Send(Request(type) + ",\"MatchId\":" + Quote(matchId) + ",\"GameNumber\":" + gameNumber + extra);
        }
        void Resync()
        {
            if (string.IsNullOrEmpty(matchId) || transport == null || !transport.Connected) return;
            commandRequestId=null; acceptedSequence=-1; ViewInvalidated?.Invoke();
            resyncing = true;
            Send(Request("StateResyncRequest") + ",\"MatchId\":" + Quote(matchId));
        }
        bool Submit(string type)
        {
            string command = "{\"Type\":" + Quote(type);
            if (type == "PlayCard" || type == "Attack" || type == "DirectAttack") command += ",\"CardId\":" + cardId;
            if (type == "Attack") command += ",\"TargetCardId\":" + targetId;
            return SubmitJson(command + "}");
        }
        internal bool SendEffect(int source, int effect, List<List<EffectTargetView>> targets)
        {
            var groups = new List<string>();
            foreach (var group in targets)
            {
                var entries = new List<string>(); foreach (var target in group) entries.Add(JsonUtility.ToJson(target));
                groups.Add("[" + string.Join(",",entries) + "]");
            }
            return SubmitJson("{\"Type\":\"ActivateEffect\",\"CardId\":" + source + ",\"EffectId\":" + effect + ",\"Targets\":[" + string.Join(",",groups) + "]}");
        }
        bool SubmitJson(string command)
        {
            if (!Connected || Busy || string.IsNullOrEmpty(matchId)) return false;
            commandRequestId = Guid.NewGuid().ToString("N"); acceptedSequence = -1; pending = true;
            Send(Request("SubmitCommand",commandRequestId) + ",\"MatchId\":" + Quote(matchId) + ",\"GameNumber\":" + gameNumber +
                ",\"SequenceNumber\":" + sequence + ",\"Command\":" + command);
            return true;
        }
        void Update()
        {
            if (transport == null) return;
            while (transport.Notices.TryDequeue(out var notice))
            {
                Note(notice);
                if (notice == "Connected")
                {
                    connecting = false;
                    Send(Request("ListDecks"));
                    if (!string.IsNullOrEmpty(token))
                        Send(Request("JoinMatch") + ",\"MatchId\":" + Quote(matchId) + ",\"SessionToken\":" + Quote(token));
                }
                if (notice == "Disconnected")
                {
                    connecting = pending = resyncing = false; commandRequestId=null; acceptedSequence=-1; ViewInvalidated?.Invoke();
                    if (string.IsNullOrEmpty(token)) ClearMatch();
                }
                if (notice.StartsWith("Send failed")) { pending=false; commandRequestId=null; CommandFinished?.Invoke(false,notice); }
            }
            while (transport.Messages.TryDequeue(out var json))
            {
                try { ApplyMessage(json); }
                catch (Exception error) { pending = false; Note("Protocol error: " + error.Message); Resync(); }
            }
        }
        internal void ApplyMessage(string json)
        {
            var message = JsonUtility.FromJson<MessageView>(json);
            if (message.ProtocolVersion != 2) throw new InvalidOperationException("Server/client protocol mismatch; rebuild both");
            if (message.Type == "DeckCatalog") { decks = JsonUtility.FromJson<DeckCatalogMessage>(json).Payload; return; }
            if (message.Type == "MatchCreated" || message.Type == "MatchJoined")
            {
                matchId = message.MatchId; playerId = message.PlayerId; token = message.SessionToken;
                if (!string.IsNullOrEmpty(message.DeckId)) selectedDeck = message.DeckId;
                ResetEpoch(message.GameNumber); Note("Joined " + matchId + " as Player " + playerId); return;
            }
            if (message.Type == "ServerError" || message.Type == "CommandRejected")
            {
                if (!string.IsNullOrEmpty(message.MatchId) && !string.IsNullOrEmpty(matchId) && message.MatchId != matchId) return;
                if (message.GameNumber > 0 && message.GameNumber < gameNumber) return;
                if(commandRequestId==null || message.RequestId==commandRequestId || message.Type=="ServerError") pending = resyncing = false; Note("Rejected: " + Reason(message.Reason));
                if(commandRequestId!=null && (message.RequestId==commandRequestId || message.Type=="ServerError"))
                { commandRequestId=null; acceptedSequence=-1; CommandFinished?.Invoke(false,Reason(message.Reason)); }
                if (message.Reason == "InvalidSession" || message.Reason == "MatchNotFound") { ClearMatch(); return; }
                if (message.Reason == "StaleSequence" || message.Reason == "StaleGame") Resync();
                return;
            }
            // A late message from a departed match must never populate the current screen.
            if (message.MatchId != matchId || string.IsNullOrEmpty(matchId)) return;
            if (message.GameNumber < gameNumber) return;
            if (message.GameNumber > gameNumber) ResetEpoch(message.GameNumber);
            switch (message.Type)
            {
                case "SessionUpdated": token = message.SessionToken; break;
                case "LobbyState": lobby = JsonUtility.FromJson<LobbyMessage>(json).Payload; if(commandRequestId==null) pending = false; break;
                case "LobbyAccepted": if(commandRequestId==null) pending = false; break;
                case "MatchStarted": Note("Game " + gameNumber + " started"); break;
                case "GameEvents":
                    if (message.SequenceNumber <= lastEvents) return;
                    if (lastEvents != 0 && message.SequenceNumber != lastEvents + 1) { Resync(); return; }
                    lastEvents = message.SequenceNumber;
                    foreach (var e in JsonUtility.FromJson<EventsMessage>(json).Payload) { Note(EventText(e)); EventReceived?.Invoke(e); }
                    break;
                case "CommandAccepted":
                    if(message.RequestId==commandRequestId) acceptedSequence=message.SequenceNumber;
                    break;
                case "StateSnapshot":
                    if (message.SequenceNumber < sequence) return;
                    state = JsonUtility.FromJson<SnapshotMessage>(json).Payload;
                    sequence = lastEvents = message.SequenceNumber; resyncing=false;
                    if(commandRequestId!=null && acceptedSequence>=0 && sequence>=acceptedSequence)
                    { commandRequestId=null; acceptedSequence=-1; pending=false; CommandFinished?.Invoke(true,""); }
                    else if(commandRequestId==null) pending=false;
                    // Selection is a view hint; discard it if its old card no longer exists in our visible zones.
                    RefreshSelection();
                    break;
                case "MatchEnded":
                    token = null; pending = false;
                    Note(message.WinnerPlayerId == playerId ? "VICTORY - choose Rematch or Return to lobby" : "DEFEAT - choose Rematch or Return to lobby");
                    break;
                case "MatchLeft": ClearMatch(); Note("Returned to lobby"); break;
                case "MatchClosed": ClearMatch(); Note("Match closed: " + message.Reason); break;
            }
        }
        void RefreshSelection()
        {
            selectedCard = null;
            if (state?.Players == null) return;
            bool targetExists = false;
            foreach (var player in state.Players)
            {
                foreach (var zone in new[] { player.Hand, player.Field, player.Graveyard })
                    if (zone != null) foreach (var card in zone) if (card.Id == cardId) selectedCard = card;
                if (player.PlayerId != playerId && player.Field != null)
                    foreach (var card in player.Field) if (card.Id == targetId) targetExists = true;
            }
            if (selectedCard == null) cardId = 0;
            if (!targetExists) targetId = 0;
        }
        static string Reason(string reason)
        {
            if (reason == null) return "Unknown reason";
            if (reason.Contains("WrongPhase")) return "This action is unavailable in the current phase.";
            if (reason.Contains("NotActivePlayer")) return "Wait for your turn.";
            if (reason.Contains("NormalSummonLimit")) return "One normal summon per turn.";
            if (reason.Contains("BattleRejected"))
            {
                if (reason.EndsWith(":9")) return "Summoning sickness: attack on your next turn.";
                if (reason.EndsWith(":7")) return "This monster already attacked this turn.";
                return "Attack unavailable. Select your ready monster and a valid target.";
            }
            return reason;
        }
        static string Phase(int phase) => phase == 0 ? "Main" : phase == 1 ? "Battle" : "End";
        static string EventText(EventView e)
        {
            switch (e.Type)
            {
                case 0: return "Battle started: #" + e.SourceCardId + " vs #" + e.TargetCardId;
                case 1: return "Card #" + e.TargetCardId + " took " + e.Damage + " damage (HP " + e.HpAfter + ")";
                case 2: return "Card #" + e.TargetCardId + " destroyed";
                case 3: return "Battle completed";
                case 4: return e.TargetCardId == 0 ? "Opponent moved a hidden card" : "Card #" + e.TargetCardId + " changed zone";
                case 5: return "Player " + e.TargetPlayerId + " turn " + e.TurnNumber + " started";
                case 6: return "Player " + e.TargetPlayerId + " turn ended";
                case 7: return "Phase: " + Phase(e.Phase);
                case 8: return "Player " + e.TargetPlayerId + " Life: " + e.LifeAfter;
                case 9: return "Game finished";
                case 10: return "Player " + e.TargetPlayerId + " took " + e.Damage + " damage";
                case 11: return "Player " + e.TargetPlayerId + " summoned #" + e.TargetCardId;
                case 13: return "Card effect activated";
                case 14: return "Card effect resolved";
                case 15: return "Card manifested";
                case 16: return "Card healed";
                case 17: return "Card #" + e.TargetCardId + " stats changed (ATK " + e.AttackAfter + ")";
                default: return "Game event " + e.Type;
            }
        }
        void OnDestroy() { transport?.Dispose(); }
    }
}
