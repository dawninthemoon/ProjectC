using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectC.Client
{
    // Development-only interface for the opt-in server protocol. The ordinary
    // lobby and battle presenters remain unchanged and never interpret Yakou state.
    public sealed class YakouPrototypeScreen : MonoBehaviour
    {
        private const float Margin = 12f;
        private const float LineHeight = 30f;
        private readonly List<Canvas> _hiddenCanvases = new List<Canvas>();
        private readonly HashSet<int> _costs = new HashSet<int>();
        private readonly Dictionary<int, int> _blockers = new Dictionary<int, int>();
        private bool _open;
        private string _room = "";
        private string _card = "0";
        private string _target = "0";
        private string _replacement = "0";
        private string _definition = "0";
        private string _slot = "0";
        private string _attacker = "0";
        private string _blocker = "0";
        private string _error = "";
        private Vector2 _scroll;
        private CardLibrary _library;
        private YakouSessionClient _session;
        private long _renderedSequence = -1;
        private string _renderedMatch;

        private void OnGUI()
        {
            var app = ClientApp.Instance;
            if (app == null || app.Environment != ServerEnvironment.Local || app.UseWebsite
                || SceneManager.GetActiveScene().name != "LobbyScene")
            {
                RestoreCanvases();
                return;
            }
            if (!_open)
            {
                if (GUI.Button(new Rect(Margin, Margin, 220, LineHeight), "Yakou prototype"))
                {
                    if (!string.IsNullOrEmpty(app.Network.CurrentMatch) || app.Network.Busy)
                    {
                        app.ShowPopup("기존 대전 또는 매칭을 먼저 종료하세요.");
                        return;
                    }
                    _session = app.Network.Yakou;
                    try
                    {
                        _library = CardLibrary.LoadYakouResources();
                        _open = true;
                        HideCanvases();
                    }
                    catch (Exception error)
                    {
                        app.ShowPopup(error.Message);
                    }
                }
                return;
            }
            if (!ReferenceEquals(_session, app.Network.Yakou))
            {
                _session = app.Network.Yakou;
            }
            if (_hiddenCanvases.Count == 0)
            {
                HideCanvases();
            }
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUILayout.BeginArea(new Rect(Margin, Margin, Screen.width - Margin * 2, Screen.height - Margin * 2));
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("YAKOU PROTOTYPE  |  Developer server only  |  Server-authoritative");
            GUILayout.Label("Fixed prototype decks. Share the room ID privately. Resume tokens stay in memory.");
            if (string.IsNullOrEmpty(_session.MatchId) && !_session.Busy
                && GUILayout.Button("Back to lobby", GUILayout.Height(LineHeight)))
            {
                _open = false;
                RestoreCanvases();
            }
            if (!app.Network.ConnectionReady)
            {
                GUILayout.Label("Disconnected: reconnecting through ConnectionScene.");
            }
            else if (string.IsNullOrEmpty(_session.MatchId))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Create room", GUILayout.Height(LineHeight)))
                {
                    Try(() => _session.Create());
                }
                _room = GUILayout.TextField(_room, 64, GUILayout.Height(LineHeight));
                if (GUILayout.Button("Join room", GUILayout.Height(LineHeight)))
                {
                    Try(() => _session.Join(_room.Trim()));
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                DrawMatch();
            }
            if (!string.IsNullOrEmpty(_error))
            {
                GUILayout.Label("Client: " + _error);
            }
            if (!string.IsNullOrEmpty(_session.Error))
            {
                GUILayout.Label("Server: " + _session.Error);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawMatch()
        {
            if (_renderedMatch != _session.MatchId)
            {
                _renderedMatch = _session.MatchId;
                _renderedSequence = -1;
                _card = _target = _replacement = _definition = _slot = _attacker = _blocker = "0";
                _costs.Clear();
                _blockers.Clear();
            }
            GUILayout.Label("Room: " + _session.MatchId + "  |  Seat: " + _session.Seat
                + "  |  Sequence: " + _session.Sequence + "  |  " + (_session.Bound ? "connected" : "disconnected"));
            GUILayout.BeginHorizontal();
            if (!_session.Bound && !_session.Busy && GUILayout.Button("Forget disconnected room"))
            {
                Try(_session.ForgetDisconnectedRoom);
            }
            if (!_session.Bound && !_session.Busy && GUILayout.Button("Resume room"))
            {
                Try(_session.Resume);
            }
            if (GUILayout.Button("Copy room ID"))
            {
                GUIUtility.systemCopyBuffer = _session.MatchId;
            }
            if (_session.Bound && GUILayout.Button("Refresh snapshot"))
            {
                Try(_session.RequestSnapshot);
            }
            if (_session.Bound && GUILayout.Button("Leave and close room"))
            {
                Try(_session.Leave);
            }
            GUILayout.EndHorizontal();
            var state = _session.State;
            if (state == null)
            {
                GUILayout.Label("Waiting for server snapshot.");
                return;
            }
            if (_renderedSequence != _session.Sequence)
            {
                _renderedSequence = _session.Sequence;
                _costs.Clear();
                _blockers.Clear();
            }
            GUILayout.Label("Turn " + (string)state["TurnNumber"] + "  |  Phase " + (string)state["Phase"]
                + "  |  Active " + (string)state["ActivePlayerId"] + "  |  Priority " + (string)state["PriorityPlayerId"]
                + "  |  Winner " + (string)state["WinnerPlayerId"]);
            foreach (var player in (JArray)state["Players"])
            {
                DrawPlayer(player);
            }
            foreach (var attack in state["Attacks"] as JArray ?? new JArray())
            {
                int attacker = (int)attack["AttackerId"];
                GUILayout.BeginHorizontal();
                GUILayout.Label("Raid: attacker " + attacker + " / slot " + (string)attack["Slot"]
                    + " / blocker " + (string)attack["BlockerId"]);
                if (GUILayout.Button("Select attacker"))
                {
                    _attacker = attacker.ToString();
                    _target = attacker.ToString();
                }
                GUILayout.EndHorizontal();
            }
            DrawSelection(state["PendingSelection"] as JObject);
            DrawControls(state);
        }

        private void DrawPlayer(JToken player)
        {
            int id = (int)player["PlayerId"];
            GUILayout.Label("PLAYER " + id + "  Life " + (string)player["Life"] + "  Ki " + (string)player["Ki"]
                + "  Deck " + (string)player["DeckCount"] + "  Hand " + (string)player["HandCount"]);
            DrawCards("Hand", player["Hand"] as JArray);
            DrawCards("Field slots", player["FieldSlots"] as JArray);
            DrawCards("Graveyard", player["Graveyard"] as JArray);
            DrawCards("Exile", player["Exile"] as JArray);
        }

        private void DrawCards(string title, JArray cards)
        {
            if (cards == null)
            {
                return;
            }
            GUILayout.Label(title + " (choose source, target or cost):");
            for (int index = 0; index < cards.Count; ++index)
            {
                var card = cards[index];
                if (card.Type == JTokenType.Null)
                {
                    if (title == "Field slots")
                    {
                        GUILayout.Label("Slot " + index + ": empty");
                    }
                    continue;
                }
                int id = (int)card["Id"];
                int definition = (int)card["DefinitionId"];
                var info = _library.Find(definition);
                string prefix = title == "Field slots" ? "Slot " + index + " " : "";
                GUILayout.BeginHorizontal();
                GUILayout.Label(prefix + (info?.Name ?? ("#" + definition)) + " [" + id + "] ATK "
                    + (string)card["Attack"] + " HP " + (string)card["Hp"]
                    + (card["AttackCounter"] != null ? " countdown " + (string)card["AttackCounter"] : ""), GUILayout.MinWidth(240));
                if (GUILayout.Button("Source"))
                {
                    _card = id.ToString();
                }
                if (GUILayout.Button("Target"))
                {
                    _target = id.ToString();
                    _blocker = id.ToString();
                    _replacement = id.ToString();
                }
                if (GUILayout.Button(_costs.Contains(id) ? "Cost ✓" : "Cost"))
                {
                    if (!_costs.Add(id))
                    {
                        _costs.Remove(id);
                    }
                }
                GUILayout.EndHorizontal();
                if (info != null && id.ToString() == _card)
                {
                    GUILayout.Label(info.Description);
                    foreach (int form in info.AllowedManifestations)
                    {
                        if (GUILayout.Button("Select manifestation " + (_library.Find(form)?.Name ?? form.ToString()) + " (" + form + ")"))
                        {
                            _definition = form.ToString();
                        }
                    }
                }
            }
        }

        private void DrawSelection(JObject selection)
        {
            if (selection == null)
            {
                return;
            }
            GUILayout.Label("Effect selection: " + (string)selection["Status"] + " / " + (string)selection["Operation"]);
            if ((string)selection["Status"] != "Ready")
            {
                GUILayout.Label("This server effect cannot currently be selected or resolved.");
                return;
            }
            if ((int?)selection["MinCount"] == 0 && GUILayout.Button("Skip optional effect"))
            {
                Try(() => Send("ChooseEffect", new JObject { ["TargetId"] = 0 }));
            }
            foreach (var candidate in (JArray)selection["Candidates"])
            {
                int id = (int)candidate;
                if (GUILayout.Button("Choose effect target " + id))
                {
                    Try(() => Send("ChooseEffect", new JObject { ["TargetId"] = id }));
                }
            }
        }

        private void DrawControls(JObject state)
        {
            if (_session.Busy)
            {
                GUILayout.Label("Waiting for command result and server snapshot.");
                if (_session.HasPendingCommand && GUILayout.Button("Retry same request"))
                {
                    Try(_session.RetryPending);
                }
                return;
            }
            GUILayout.Label("Select cards above, or enter instance IDs below. The server validates every action.");
            _card = Field("Source card", _card);
            _target = Field("Target / attacker", _target);
            _replacement = Field("Replacement blocker (0 if none)", _replacement);
            _definition = Field("Manifest definition ID", _definition);
            _slot = Field("Summon slot 0–4", _slot);
            GUILayout.Label("Selected costs: " + string.Join(", ", _costs.OrderBy(value => value)));
            var own = ((JArray)state["Players"]).FirstOrDefault(player => (int)player["PlayerId"] == _session.Seat);
            foreach (var choice in own?["SummonChoices"] as JArray ?? new JArray())
            {
                int card = (int)choice["CardId"];
                string method = (string)choice["Method"];
                string label = "Summon " + card + " / " + method + " / choose " + (string)choice["RequiredCount"]
                    + " from " + choice["SourceZone"] + " " + choice["Candidates"].ToString(Newtonsoft.Json.Formatting.None)
                    + " / slots " + choice["AvailableSlots"].ToString(Newtonsoft.Json.Formatting.None);
                if (GUILayout.Button(label))
                {
                    Try(() => Send("Summon", new JObject { ["CardId"] = card, ["Method"] = method,
                        ["Materials"] = CostArray(), ["Slot"] = Number(_slot) }));
                }
            }
            ActionButton("Start turn", "StartTurn");
            ActionButton("Begin raid", "BeginRaid");
            _attacker = Field("Attacker ID to assign", _attacker);
            _blocker = Field("Blocker ID to assign", _blocker);
            if (GUILayout.Button("Add / replace blocker assignment"))
            {
                Try(() => _blockers[Number(_attacker)] = Number(_blocker));
            }
            GUILayout.Label("Assignments: " + string.Join(", ", _blockers.Select(pair => pair.Key + " → " + pair.Value)));
            if (GUILayout.Button("Assign blockers (empty allows unblocked raid)"))
            {
                var assignments = new JArray(_blockers.Select(pair => new JObject { ["Attacker"] = pair.Key, ["Blocker"] = pair.Value }));
                Try(() => Send("AssignBlockers", new JObject { ["Blockers"] = assignments }));
            }
            ActionButton("Pass raid response", "PassRaidResponse");
            ActionButton("Resolve raid", "ResolveRaid");
            ActionButton("Pass spell response", "PassSpellResponse");
            Button("Activate main spell", () => Send("ActivateMainSpell", new JObject { ["CardId"] = Number(_card) }));
            Button("Open spell response", () => Send("BeginSpellResponse", new JObject { ["CardId"] = Number(_card), ["TargetId"] = Number(_target) }));
            Button("Respond with negation", () => Send("RespondWithNegation", new JObject { ["CardId"] = Number(_card), ["Costs"] = CostArray() }));
            Button("Raid shield", () => Send("RespondWithRaidShield", new JObject { ["CardId"] = Number(_card), ["TargetId"] = Number(_target) }));
            Button("Raid tactic", () => Send("RespondWithRaidTactic", new JObject { ["CardId"] = Number(_card), ["TargetId"] = Number(_target), ["ReplacementBlocker"] = Number(_replacement), ["Costs"] = CostArray() }));
            Button("Manifest", () => Send("Manifest", new JObject { ["CardId"] = Number(_card), ["DefinitionId"] = Number(_definition) }));
            Button("Postpone", () => Send("Postpone", new JObject { ["CardId"] = Number(_card) }));
            Button("Redraw selected costs", () => Send("Redraw", new JObject { ["Cards"] = CostArray() }));
        }

        private static string Field(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(230));
            value = GUILayout.TextField(value, 10);
            GUILayout.EndHorizontal();
            return value;
        }

        private static int Number(string value)
        {
            if (!int.TryParse(value, out int result) || result < 0)
            {
                throw new ArgumentException("Enter a non-negative numeric ID.");
            }
            return result;
        }

        private JArray CostArray() => new JArray(_costs.OrderBy(value => value));
        private void ActionButton(string label, string type) => Button(label, () => Send(type, new JObject()));
        private void Button(string label, Action action)
        {
            if (GUILayout.Button(label, GUILayout.Height(LineHeight)))
            {
                Try(action);
            }
        }
        private void Send(string type, JObject fields)
        {
            fields["Type"] = type;
            _session.Command(fields);
        }
        private void Try(Action action)
        {
            try
            {
                action();
                _error = "";
            }
            catch (Exception error)
            {
                _error = error.Message;
            }
        }
        private void HideCanvases()
        {
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.enabled && canvas.gameObject.scene.name == "LobbyScene")
                {
                    canvas.enabled = false;
                    _hiddenCanvases.Add(canvas);
                }
            }
        }
        private void RestoreCanvases()
        {
            foreach (var canvas in _hiddenCanvases)
            {
                if (canvas != null)
                {
                    canvas.enabled = true;
                }
            }
            _hiddenCanvases.Clear();
        }
        private void OnDestroy() => RestoreCanvases();
    }
}
