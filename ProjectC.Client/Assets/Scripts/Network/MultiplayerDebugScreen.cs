using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectC.Client
{
    public sealed class MultiplayerDebugScreen : MonoBehaviour
    {
        string address = "ws://127.0.0.1:7777";
        WebSocketTransport transport;
        string token, matchId;
        int playerId, cardId, targetId;
        long sequence, lastEvents;
        bool connecting, pending, resyncing;
        StateView state;
        Vector2 scroll;
        readonly Queue<string> log = new Queue<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<MultiplayerDebugScreen>() != null) return;
            var screen = new GameObject("Project C Multiplayer Debug");
            DontDestroyOnLoad(screen);
            screen.AddComponent<MultiplayerDebugScreen>();
        }
        void Note(string text)
        {
            log.Enqueue(text);
            while (log.Count > 12) log.Dequeue();
        }
        void Connect()
        {
            transport?.Dispose();
            transport = new WebSocketTransport();
            connecting = true; pending = false; resyncing = false;
            sequence = lastEvents = 0; state = null;
            _ = transport.ConnectAsync(address);
        }
        static string Quote(string text) { return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
        string Request(string type)
        {
            return "{\"Type\":" + Quote(type) + ",\"ProtocolVersion\":1,\"RequestId\":" + Quote(Guid.NewGuid().ToString("N"));
        }
        void Join()
        {
            string json = Request("JoinMatch");
            if (!string.IsNullOrEmpty(token)) json += ",\"MatchId\":" + Quote(matchId) + ",\"SessionToken\":" + Quote(token);
            _ = transport.SendAsync(json + "}");
        }
        void Resync()
        {
            if (playerId == 0 || string.IsNullOrEmpty(matchId) || transport == null || !transport.Connected) return;
            resyncing = true;
            _ = transport.SendAsync(Request("StateResyncRequest") + ",\"MatchId\":" + Quote(matchId) + "}");
        }
        void Submit(string type)
        {
            if (pending || resyncing) return;
            string command = "{\"Type\":" + Quote(type);
            if (type == "PlayCard" || type == "Attack" || type == "DirectAttack") command += ",\"CardId\":" + cardId;
            if (type == "Attack") command += ",\"TargetCardId\":" + targetId;
            pending = true;
            _ = transport.SendAsync(Request("SubmitCommand") + ",\"MatchId\":" + Quote(matchId) +
                ",\"SequenceNumber\":" + sequence + ",\"Command\":" + command + "}}");
        }
        void Update()
        {
            if (transport == null) return;
            while (transport.Notices.TryDequeue(out var notice))
            {
                Note(notice);
                if (notice == "Connected") { connecting = false; Join(); }
                if (notice == "Disconnected") { connecting = false; pending = false; }
            }
            while (transport.Messages.TryDequeue(out var json))
            {
                try
                {
                    var message = JsonUtility.FromJson<MessageView>(json);
                    if (message.ProtocolVersion != 1) throw new InvalidOperationException("Unsupported protocol");
                    if (message.Type == "MatchJoined")
                    {
                        playerId = message.PlayerId; matchId = message.MatchId; token = message.SessionToken;
                        Note("Joined " + matchId + " as Player " + playerId);
                    }
                    else if (message.Type == "GameEvents")
                    {
                        if (message.SequenceNumber <= lastEvents) continue;
                        if (lastEvents != 0 && message.SequenceNumber != lastEvents + 1) { Resync(); continue; }
                        lastEvents = message.SequenceNumber;
                        var events = JsonUtility.FromJson<EventsMessage>(json).Payload;
                        foreach (var e in events) Note("Event " + e.Type + " P" + e.SourcePlayerId + ":" + e.SourceCardId + " -> P" + e.TargetPlayerId + ":" + e.TargetCardId);
                        // Snapshots follow every accepted command and atomically apply all event results.
                    }
                    else if (message.Type == "StateSnapshot")
                    {
                        if (message.SequenceNumber < sequence) continue;
                        state = JsonUtility.FromJson<SnapshotMessage>(json).Payload;
                        sequence = lastEvents = message.SequenceNumber; pending = false; resyncing = false;
                    }
                    else if (message.Type == "CommandRejected" || message.Type == "ServerError")
                    {
                        pending = false; Note(message.Type + ": " + message.Reason);
                        if (message.Reason == "StaleSequence") Resync();
                    }
                    else if (message.Type == "MatchEnded") Note(message.WinnerPlayerId == playerId ? "VICTORY" : "DEFEAT");
                }
                catch (Exception error) { pending = false; Note("Protocol error: " + error.Message); Resync(); }
            }
        }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, Mathf.Min(Screen.width - 20, 1000), Screen.height - 20), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Project C — Local WebSocket Multiplayer (fixed test rules)");
            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address);
            GUI.enabled = !connecting && (transport == null || !transport.Connected);
            if (GUILayout.Button(string.IsNullOrEmpty(token) ? "Connect + Join" : "Reconnect")) Connect();
            if (GUILayout.Button("Forget session")) { token = matchId = null; playerId = 0; state = null; }
            GUI.enabled = true;
            if (GUILayout.Button("Disconnect")) { transport?.Dispose(); connecting = false; pending = false; }
            GUILayout.EndHorizontal();
            GUILayout.Label("Player " + playerId + " | Match " + matchId + " | Sequence " + sequence + " | " + (transport != null && transport.Connected ? "Connected" : "Offline"));
            if (state != null && state.Started)
            {
                GUILayout.Label("Turn " + state.TurnNumber + " | Active P" + state.ActivePlayerId + " | " + new[] { "Main", "Battle", "End" }[state.Phase] + " | Started: " + state.TurnStarted);
                if (state.Finished) GUILayout.Label(state.WinnerPlayerId == playerId ? "VICTORY" : "DEFEAT");
                foreach (var p in state.Players)
                {
                    GUILayout.Label("P" + p.PlayerId + " Life=" + p.Life + " Hand=" + p.HandCount + " Deck=" + p.DeckCount);
                    if (p.PlayerId == playerId) DrawCards("Your hand (click to select)", p.Hand, false);
                    DrawCards("Field (click yours as attacker; opponent as target)", p.Field, p.PlayerId != playerId);
                    DrawCards("Graveyard", p.Graveyard, false);
                }
                GUILayout.Label("Selected card=" + cardId + " | Target=" + targetId);
                GUI.enabled = transport != null && transport.Connected && !pending && !resyncing && !state.Finished && state.ActivePlayerId == playerId;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Summon")) Submit("PlayCard");
                if (GUILayout.Button("Start turn")) Submit("StartTurn");
                if (GUILayout.Button("Next phase")) Submit("AdvancePhase");
                if (GUILayout.Button("End turn (End phase)")) Submit("EndTurn");
                if (GUILayout.Button("Attack target")) Submit("Attack");
                if (GUILayout.Button("Direct attack")) Submit("DirectAttack");
                GUILayout.EndHorizontal();
                GUI.enabled = true;
            }
            if (GUILayout.Button("Resync snapshot")) Resync();
            GUILayout.Label("Summoning sickness: attack on your next turn. Advance Main -> Battle -> End before End turn; then the opponent presses Start turn.");
            foreach (var line in log) GUILayout.Label(line);
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
        void DrawCards(string title, CardView[] cards, bool target)
        {
            GUILayout.Label(title);
            if (cards == null) return;
            GUILayout.BeginHorizontal();
            foreach (var card in cards)
                if (GUILayout.Button("#" + card.Id + " " + card.Name + " ATK " + card.Attack + " HP " + card.Hp + "/" + card.MaxHp + (card.HasAttacked ? " [used]" : "")))
                { if (target) targetId = card.Id; else cardId = card.Id; }
            GUILayout.EndHorizontal();
        }
        void OnDestroy() { transport?.Dispose(); }
    }
}
