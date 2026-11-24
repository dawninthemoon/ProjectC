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
        private readonly List<(string Id, string NameKey, string DescriptionKey)> _deckChoices =
            new List<(string Id, string NameKey, string DescriptionKey)>();
        private string _selectedDeckId;
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
                if (GUI.Button(new Rect(Margin, Margin, 220, LineHeight), YakouUiText.Get("ui.yakou.dev.entry")))
                {
                    if (!string.IsNullOrEmpty(app.Network.CurrentMatch) || app.Network.Busy)
                    {
                        app.ShowPopup(YakouUiText.Get("ui.yakou.dev.finish_existing_match"));
                        return;
                    }
                    _session = app.Network.Yakou;
                    try
                    {
                        _library = CardLibrary.LoadYakouResources();
                        LoadDeckChoices();
                        _open = true;
                        HideCanvases();
                    }
                    catch (Exception error)
                    {
                        app.ShowPopup(YakouUiText.Error(error.Message));
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
            _library.Localization.Language = CardPresentation.Library.Localization.Language;
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUILayout.BeginArea(new Rect(Margin, Margin, Screen.width - Margin * 2, Screen.height - Margin * 2));
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label(YakouUiText.Get("ui.yakou.dev.intro"));
            GUILayout.Label(YakouUiText.Get("ui.yakou.dev.fixed_decks"));
            if (string.IsNullOrEmpty(_session.MatchId) && !_session.Busy
                && GUILayout.Button(YakouUiText.Get("ui.yakou.dev.back_lobby"), GUILayout.Height(LineHeight)))
            {
                _open = false;
                RestoreCanvases();
            }
            if (!app.Network.ConnectionReady)
            {
                GUILayout.Label(YakouUiText.Get("ui.yakou.dev.reconnecting_connection_scene"));
            }
            else if (string.IsNullOrEmpty(_session.MatchId))
            {
                DrawDeckChoices();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(YakouUiText.Get("ui.yakou.dev.create_room"), GUILayout.Height(LineHeight)))
                {
                    Try(() =>
                    {
                        RequireDeckChoice();
                        _session.Create(_selectedDeckId);
                    });
                }
                _room = GUILayout.TextField(_room, 64, GUILayout.Height(LineHeight));
                if (GUILayout.Button(YakouUiText.Get("ui.yakou.dev.join_room"), GUILayout.Height(LineHeight)))
                {
                    Try(() =>
                    {
                        RequireDeckChoice();
                        _session.Join(_room.Trim(), _selectedDeckId);
                    });
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                DrawMatch();
            }
            if (!string.IsNullOrEmpty(_error))
            {
                GUILayout.Label(YakouUiText.Get("ui.yakou.dev.client_error", YakouUiText.Error(_error)));
            }
            if (!string.IsNullOrEmpty(_session.Error))
            {
                GUILayout.Label(YakouUiText.Get("ui.yakou.dev.server_error", YakouUiText.Error(_session.Error)));
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawMatch()
        {
            var selected = _deckChoices.FirstOrDefault(deck => deck.Id == _session.SelectedDeckId);
            if (!string.IsNullOrEmpty(selected.NameKey))
            {
                GUILayout.Label(YakouUiText.Get("ui.yakou.deck.selected", YakouUiText.Get(selected.NameKey)));
            }
            if (_renderedMatch != _session.MatchId)
            {
                _renderedMatch = _session.MatchId;
                _renderedSequence = -1;
                _card = _target = _replacement = _definition = _slot = _attacker = _blocker = "0";
                _costs.Clear();
                _blockers.Clear();
            }
            GUILayout.Label(YakouUiText.Get("ui.yakou.dev.room_status", _session.MatchId, _session.Seat,
                _session.Sequence, YakouUiText.Get(_session.Bound ? "ui.yakou.dev.connected" : "ui.yakou.dev.disconnected")));
            GUILayout.BeginHorizontal();
            if (_session.Bound && _session.State != null && GUILayout.Button(YakouUiText.Get("ui.yakou.dev.enter_battle")))
            {
                RestoreCanvases();
                ClientApp.Instance.Navigate("BattleScene");
            }
            if (!_session.Bound && !_session.Busy && GUILayout.Button(YakouUiText.Get("ui.yakou.dev.forget_room")))
            {
                Try(_session.ForgetDisconnectedRoom);
            }
            if (!_session.Bound && !_session.Busy && GUILayout.Button(YakouUiText.Get("ui.yakou.dev.resume_room")))
            {
                Try(_session.Resume);
            }
            if (GUILayout.Button(YakouUiText.Get("ui.yakou.dev.copy_room_id")))
            {
                GUIUtility.systemCopyBuffer = _session.MatchId;
            }
            if (_session.Bound && GUILayout.Button(YakouUiText.Get("ui.yakou.dev.refresh_snapshot")))
            {
                Try(_session.RequestSnapshot);
            }
            if (_session.Bound && GUILayout.Button(YakouUiText.Get("ui.yakou.action.leave_and_close_room")))
            {
                Try(_session.Leave);
            }
            GUILayout.EndHorizontal();
            var state = _session.State;
            if (state == null)
            {
                GUILayout.Label(YakouUiText.Get("ui.yakou.status.wait_snapshot"));
                return;
            }
            if (_renderedSequence != _session.Sequence)
            {
                _renderedSequence = _session.Sequence;
                _costs.Clear();
                _blockers.Clear();
            }
            GUILayout.Label(YakouUiText.Get("ui.yakou.dev.turn_status", (int)state["TurnNumber"],
                YakouUiText.Phase((string)state["Phase"]),
                YakouUiText.Player((int)state["ActivePlayerId"], _session.Seat),
                YakouUiText.Player((int)state["PriorityPlayerId"], _session.Seat),
                YakouUiText.Player((int)state["WinnerPlayerId"], _session.Seat)));
            foreach (var player in (JArray)state["Players"])
            {
                DrawPlayer(player);
            }
            foreach (var attack in state["Attacks"] as JArray ?? new JArray())
            {
                int attacker = (int)attack["AttackerId"];
                GUILayout.BeginHorizontal();
                GUILayout.Label(YakouUiText.Get("ui.yakou.dev.raid_status", attacker, (int)attack["Slot"], (int)attack["BlockerId"]));
                if (GUILayout.Button(YakouUiText.Get("ui.yakou.dev.select_attacker")))
                {
                    _attacker = attacker.ToString();
                    _target = attacker.ToString();
                }
                GUILayout.EndHorizontal();
            }
            DrawSelection(state["PendingSelection"] as JObject);
            DrawControls(state);
        }

        private void LoadDeckChoices()
        {
            var resource = Resources.Load<TextAsset>("CardData/YakouPrototypeDecks");
            if (resource == null)
            {
                throw new InvalidOperationException("Yakou deck resource is missing.");
            }
            var catalog = JObject.Parse(resource.text);
            if ((string)catalog["CatalogVersion"] != _library.CatalogVersion)
            {
                throw new InvalidOperationException("Yakou deck and card versions differ.");
            }
            _deckChoices.Clear();
            foreach (JObject deck in (JArray)catalog["Decks"])
            {
                string nameKey = (string)deck["NameKey"];
                if (string.IsNullOrEmpty(nameKey))
                {
                    continue;
                }
                string id = (string)deck["Id"];
                string descriptionKey = nameKey.Replace(".name", ".desc");
                if (!_library.Localization.ContainsKey(nameKey)
                    || !_library.Localization.ContainsKey(descriptionKey))
                {
                    throw new InvalidOperationException("Yakou deck localization is missing: " + id);
                }
                _deckChoices.Add((id, nameKey, descriptionKey));
            }
            if (_deckChoices.Count != 3)
            {
                throw new InvalidOperationException("Yakou Alpha 01 requires three selectable decks.");
            }
        }

        private void DrawDeckChoices()
        {
            GUILayout.Label(YakouUiText.Get("ui.yakou.deck.title"));
            foreach (var deck in _deckChoices)
            {
                GUILayout.BeginVertical("box");
                if (GUILayout.Button(YakouUiText.Get(deck.NameKey), GUILayout.Height(LineHeight)))
                {
                    _selectedDeckId = deck.Id;
                }
                GUILayout.Label(YakouUiText.Get(deck.DescriptionKey));
                GUILayout.EndVertical();
            }
            if (!string.IsNullOrEmpty(_selectedDeckId))
            {
                var selected = _deckChoices.First(deck => deck.Id == _selectedDeckId);
                GUILayout.Label(YakouUiText.Get("ui.yakou.deck.selected", YakouUiText.Get(selected.NameKey)));
            }
        }

        private void RequireDeckChoice()
        {
            if (string.IsNullOrEmpty(_selectedDeckId))
            {
                throw new InvalidOperationException(YakouUiText.Get("ui.yakou.deck.choose_before_join"));
            }
        }

        private void DrawPlayer(JToken player)
        {
            int id = (int)player["PlayerId"];
            GUILayout.Label(YakouUiText.Get("ui.yakou.dev.player_status", YakouUiText.Player(id, _session.Seat),
                (int)player["Life"], (int)player["Ki"], (int)player["DeckCount"], (int)player["HandCount"]));
            DrawCards("ui_battle_hand", player["Hand"] as JArray);
            DrawCards("ui_battle_field", player["FieldSlots"] as JArray);
            DrawCards("ui_battle_graveyard", player["Graveyard"] as JArray);
            DrawCards("ui_battle_exile", player["Exile"] as JArray);
        }

        private void DrawCards(string title, JArray cards)
        {
            if (cards == null)
            {
                return;
            }
            GUILayout.Label(YakouUiText.Get("ui.yakou.dev.zone_choices", YakouUiText.Get(title)));
            for (int index = 0; index < cards.Count; ++index)
            {
                var card = cards[index];
                if (card.Type == JTokenType.Null)
                {
                    if (title == "ui_battle_field")
                    {
                        GUILayout.Label(YakouUiText.Get("ui.yakou.dev.empty_slot", index + 1));
                    }
                    continue;
                }
                int id = (int)card["Id"];
                int definition = (int)card["DefinitionId"];
                var info = _library.Find(definition);
                string prefix = title == "ui_battle_field"
                    ? YakouUiText.Get("ui.yakou.action.field_slot", index + 1) + " · " : "";
                GUILayout.BeginHorizontal();
                string countdown = card["AttackCounter"] != null
                    ? YakouUiText.Get("ui.yakou.dev.countdown", (int)card["AttackCounter"]) : "";
                GUILayout.Label(prefix + YakouUiText.Get("ui.yakou.dev.card_stats", info?.Name ?? ("#" + definition),
                    id, (int?)card["Attack"] ?? 0, (int?)card["Hp"] ?? 0, countdown), GUILayout.MinWidth(240));
                if (GUILayout.Button(YakouUiText.Get("ui.yakou.dev.source")))
                {
                    _card = id.ToString();
                }
                if (GUILayout.Button(YakouUiText.Get("ui.yakou.dev.target")))
                {
                    _target = id.ToString();
                    _blocker = id.ToString();
                    _replacement = id.ToString();
                }
                if (GUILayout.Button(YakouUiText.Get(_costs.Contains(id)
                    ? "ui.yakou.dev.selected_cost" : "ui.yakou.dev.cost")))
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
                        if (GUILayout.Button(YakouUiText.Get("ui.yakou.dev.manifest_form",
                            _library.Find(form)?.Name ?? form.ToString())))
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
            GUILayout.Label(YakouUiText.Get("ui.yakou.dev.effect_selection",
                YakouUiText.SelectionStatus((string)selection["Status"]),
                YakouUiText.SelectionOperation((string)selection["Operation"])));
            if ((string)selection["Status"] != "Ready")
            {
                GUILayout.Label(YakouUiText.Get("ui.yakou.status.unsupported_effect"));
                return;
            }
            if ((int?)selection["MinCount"] == 0 && GUILayout.Button(YakouUiText.Get("ui.yakou.action.decline_optional_effect")))
            {
                Try(() => Send("ChooseEffect", new JObject { ["TargetId"] = 0 }));
            }
            foreach (var candidate in (JArray)selection["Candidates"])
            {
                int id = (int)candidate;
                if (GUILayout.Button(YakouUiText.Get("ui.yakou.dev.choose_effect_target", id)))
                {
                    Try(() => Send("ChooseEffect", new JObject { ["TargetId"] = id }));
                }
            }
        }

        private void DrawControls(JObject state)
        {
            if (_session.Busy)
            {
                GUILayout.Label(YakouUiText.Get("ui.yakou.status.command_pending"));
                if (_session.HasPendingCommand && GUILayout.Button(YakouUiText.Get("ui.yakou.dev.retry_request")))
                {
                    Try(_session.RetryPending);
                }
                return;
            }
            GUILayout.Label(YakouUiText.Get("ui.yakou.dev.id_hint"));
            _card = Field(YakouUiText.Get("ui.yakou.dev.source_card"), _card);
            _target = Field(YakouUiText.Get("ui.yakou.dev.target_attacker"), _target);
            _replacement = Field(YakouUiText.Get("ui.yakou.dev.replacement_blocker"), _replacement);
            _definition = Field(YakouUiText.Get("ui.yakou.dev.manifest_definition"), _definition);
            _slot = Field(YakouUiText.Get("ui.yakou.dev.summon_slot"), _slot);
            GUILayout.Label(YakouUiText.Get("ui.yakou.dev.selected_costs", string.Join(", ", _costs.OrderBy(value => value))));
            var own = ((JArray)state["Players"]).FirstOrDefault(player => (int)player["PlayerId"] == _session.Seat);
            foreach (var choice in own?["SummonChoices"] as JArray ?? new JArray())
            {
                int card = (int)choice["CardId"];
                string method = (string)choice["Method"];
                int required = (int)choice["RequiredCount"];
                string label = required == 0
                    ? YakouUiText.Get("ui.yakou.action.summon", YakouUiText.Method(method)) + " #" + card
                    : YakouUiText.Get("ui.yakou.dev.summon_choice", card, YakouUiText.Method(method),
                        required, YakouUiText.SourceZone((string)choice["SourceZone"]),
                        choice["AvailableSlots"].ToString(Newtonsoft.Json.Formatting.None));
                if (GUILayout.Button(label))
                {
                    Try(() => Send("Summon", new JObject { ["CardId"] = card, ["Method"] = method,
                        ["Materials"] = CostArray(), ["Slot"] = Number(_slot) }));
                }
            }
            ActionButton(YakouUiText.Get("ui.yakou.action.start_turn"), "StartTurn");
            ActionButton(YakouUiText.Get("ui.yakou.action.begin_raid"), "BeginRaid");
            _attacker = Field(YakouUiText.Get("ui.yakou.dev.attacker_id"), _attacker);
            _blocker = Field(YakouUiText.Get("ui.yakou.dev.blocker_id"), _blocker);
            if (GUILayout.Button(YakouUiText.Get("ui.yakou.dev.add_blocker")))
            {
                Try(() => _blockers[Number(_attacker)] = Number(_blocker));
            }
            GUILayout.Label(YakouUiText.Get("ui.yakou.dev.assignments",
                string.Join(", ", _blockers.Select(pair => pair.Key + " → " + pair.Value))));
            if (GUILayout.Button(YakouUiText.Get("ui.yakou.dev.assign_blockers")))
            {
                var assignments = new JArray(_blockers.Select(pair => new JObject { ["Attacker"] = pair.Key, ["Blocker"] = pair.Value }));
                Try(() => Send("AssignBlockers", new JObject { ["Blockers"] = assignments }));
            }
            ActionButton(YakouUiText.Get("ui.yakou.action.pass_raid_response"), "PassRaidResponse");
            ActionButton(YakouUiText.Get("ui.yakou.action.resolve_raid"), "ResolveRaid");
            ActionButton(YakouUiText.Get("ui.yakou.action.pass_spell_response"), "PassSpellResponse");
            Button(YakouUiText.Get("ui.yakou.dev.activate_main_spell"), () => Send("ActivateMainSpell", new JObject { ["CardId"] = Number(_card) }));
            Button(YakouUiText.Get("ui.yakou.dev.open_spell_response"), () => Send("BeginSpellResponse", new JObject { ["CardId"] = Number(_card), ["TargetId"] = Number(_target) }));
            Button(YakouUiText.Get("ui.yakou.action.response_negate"), () => Send("RespondWithNegation", new JObject { ["CardId"] = Number(_card), ["Costs"] = CostArray() }));
            Button(YakouUiText.Get("ui.yakou.action.response_shield"), () => Send("RespondWithRaidShield", new JObject { ["CardId"] = Number(_card), ["TargetId"] = Number(_target) }));
            Button(YakouUiText.Get("ui.yakou.action.response_tactic"), () => Send("RespondWithRaidTactic", new JObject { ["CardId"] = Number(_card), ["TargetId"] = Number(_target), ["ReplacementBlocker"] = Number(_replacement), ["Costs"] = CostArray() }));
            Button(YakouUiText.Get("ui.yakou.dev.manifest_selected"), () => Send("Manifest", new JObject { ["CardId"] = Number(_card), ["DefinitionId"] = Number(_definition) }));
            Button(YakouUiText.Get("ui.yakou.action.postpone"), () => Send("Postpone", new JObject { ["CardId"] = Number(_card) }));
            Button(YakouUiText.Get("ui.yakou.dev.redraw_selected"), () => Send("Redraw", new JObject { ["Cards"] = CostArray() }));
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
                throw new ArgumentException("InvalidNumericId");
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
                _error = error.Message == "InvalidNumericId" ? "InvalidNumericId" : error.Message;
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
