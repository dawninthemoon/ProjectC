using System;
using System.Collections.Generic;
using System.Linq;
using Game.Utils;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    // The legacy CardView is used only as input to shared card rendering. Rules,
    // choices and phase transitions remain in the server's Yakou snapshot.
    public sealed class YakouBattlePresenter : MonoBehaviour
    {
        private YakouSessionClient _session;
        private CardZoneView _hand, _ownField, _enemyField, _enemyHand, _ownGrave, _enemyGrave;
        private PlayerPanelView _ownPlayer, _enemyPlayer;
        private CardDetailView _detail;
        private ContextActionView _actions;
        private BattlePresenter _screen;
        private BattleLogView _log;
        private BattleTurnHudView _turnHud;
        private TurnFrameGlow _turnFrame;
        private TMP_Text _feedback;
        private TMP_Text _turnNumberBadge;
        private string _feedbackMessage;
        private string _statusLine;
        private TMP_Text _ownDeckCount, _enemyDeckCount, _ownGraveCount, _enemyGraveCount, _ownExileCount, _enemyExileCount;
        private JObject _state;
        private JObject _summonChoice;
        private readonly HashSet<int> _materials = new HashSet<int>();
        private readonly Dictionary<int, int> _blockers = new Dictionary<int, int>();
        private int _selected, _attacker;
        private string _responseType, _responseSelection;
        private JObject _responseOption;
        private int _responseCard, _responseTarget, _responseReplacement;
        private int _pendingTarget;
        private readonly HashSet<int> _pendingTargets = new HashSet<int>();
        private readonly HashSet<int> _responseCosts = new HashSet<int>();
        private RectTransform _lineRoot;
        private readonly Dictionary<int, DefenseBezierGraphic> _defenseLines = new Dictionary<int, DefenseBezierGraphic>();
        private long _sequence = -1;
        private long _loggedSequence = -1;
        private JObject _previousPublicState;
        private readonly HashSet<int> _fieldEntries = new HashSet<int>();
        private readonly HashSet<int> _fieldChanges = new HashSet<int>();
        private const float DefaultFieldEntryDuration = .66f;
        private const int PaidMapSpellDefinitionId = 3012;

        private void Awake()
        {
            _screen = GetComponent<BattlePresenter>();
            _session = ClientApp.Instance.Network.Yakou;
            _turnHud = GetComponentInChildren<BattleTurnHudView>(true);
            _turnFrame = GetComponentInChildren<TurnFrameGlow>(true);
            var legacyLog = GetComponentInChildren<BattleLogView>(true);
            if (legacyLog != null)
            {
                _log = legacyLog;
                _log.InitializeYakou(_screen, () => _session.Seat);
                _log.enabled = true;
            }
            if (!this.Bind("YourHandImage", out _hand) || !this.Bind("OwnFieldImage", out _ownField)
                || !this.Bind("OpponentFieldImage", out _enemyField) || !this.Bind("OpponentHandImage", out _enemyHand)
                || !this.Bind("YourGraveyardImage", out _ownGrave) || !this.Bind("OpponentGraveyardImage", out _enemyGrave)
                || !this.Bind("OwnPlayerImage", out _ownPlayer) || !this.Bind("EnemyPlayerImage", out _enemyPlayer)
                || !this.Bind("CardInspectorImage", out _detail) || !this.Bind("ContextActionImage", out _actions)
                || !this.Bind("RejectionReasonText", out _feedback)
                || !this.Bind("TimerText", out _turnNumberBadge)
                || !this.Bind("OwnDeckCountText", out _ownDeckCount) || !this.Bind("EnemyDeckCountText", out _enemyDeckCount)
                || !this.Bind("OwnGraveCountText", out _ownGraveCount) || !this.Bind("EnemyGraveCountText", out _enemyGraveCount)
                || !this.Bind("OwnExileCountText", out _ownExileCount) || !this.Bind("EnemyExileCountText", out _enemyExileCount)
                || _screen.EndTurn == null)
            {
                throw new InvalidOperationException("Yakou battle requires the existing battle card, player and action views.");
            }
            _actions.gameObject.SetActive(false);
            _feedback.enableAutoSizing = true;
            _feedback.fontSizeMin = 14;
            _feedback.fontSizeMax = 19;
            // The authored feedback anchor sits on the hand row. Yakou uses two
            // status lines, so place it in the open lane between field and hand.
            _feedback.rectTransform.anchoredPosition = new Vector2(650, -674);
            _feedback.rectTransform.sizeDelta = new Vector2(700, 66);
            _screen.PhaseLabel.enableAutoSizing = true;
            _screen.PhaseLabel.fontSizeMin = 13;
            _screen.PhaseLabel.fontSizeMax = 26;
            if (this.Bind("OwnCaptionText", out TMP_Text ownCaption))
            {
                ownCaption.text = YakouUiText.Get("ui_battle_my_graveyard");
            }
            if (this.Bind("EnemyCaptionText", out TMP_Text enemyCaption))
            {
                enemyCaption.text = YakouUiText.Get("ui_battle_opponent_graveyard");
            }
            if (_screen.ResultLeave != null)
            {
                _screen.ResultLeave.GetComponentInChildren<TMP_Text>().text = YakouUiText.Get("ui.yakou.action.return_lobby");
            }
            // GameCore's resource bar expects a maximum absent from the Yakou
            // snapshot. The server-supplied current Ki is shown in status text.
            if (this.Bind("SpecialCosts", out BattleSpecialCostsView legacyCosts))
            {
                legacyCosts.gameObject.SetActive(false);
            }
            if (this.Bind("EnemyYokaiPanelImage", out RectTransform enemyResource))
            {
                enemyResource.gameObject.SetActive(false);
            }
            _screen.Effects?.ResetFeedback();
            var lineLayer = new GameObject("YakouDefenseLines", typeof(RectTransform));
            _lineRoot = (RectTransform)lineLayer.transform;
            _lineRoot.SetParent(transform, false);
            _lineRoot.anchorMin = Vector2.zero;
            _lineRoot.anchorMax = Vector2.one;
            _lineRoot.offsetMin = _lineRoot.offsetMax = Vector2.zero;
            _detail.Show(null);
            _screen.EndTurn.onClick.AddListener(PrimaryActions);
            if (_screen.LogToggle != null && _log != null)
            {
                _screen.LogToggle.onClick.AddListener(_log.Toggle);
            }
            if (this.Bind("OwnExileBrowseButton", out Button ownExile))
            {
                ownExile.onClick.AddListener(() => ShowExile(true, ownExile.transform.position));
            }
            if (this.Bind("EnemyExileBrowseButton", out Button enemyExile))
            {
                enemyExile.onClick.AddListener(() => ShowExile(false, enemyExile.transform.position));
            }
            if (_screen.ResultLeave != null)
            {
                _screen.ResultLeave.onClick.AddListener(() => _session.Leave());
            }
            if (_screen.Resync != null)
            {
                _screen.Resync.onClick.AddListener(Resync);
            }
            if (_screen.LobbyToggle != null)
            {
                _screen.LobbyToggle.onClick.AddListener(() => ClientApp.Instance.Navigate("LobbyScene"));
            }
            _session.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Changed -= Refresh;
            }
        }

        private void LateUpdate()
        {
            foreach (var line in _defenseLines.Values)
            {
                line.gameObject.SetActive(false);
            }
            if (_state == null || (_state["Attacks"] is not JArray attacks))
            {
                return;
            }
            foreach (var attack in attacks)
            {
                int attacker = (int)attack["AttackerId"];
                int blocker = _blockers.TryGetValue(attacker, out int draft) ? draft : (int)attack["BlockerId"];
                var attacking = _ownField.Widgets.Concat(_enemyField.Widgets).FirstOrDefault(widget => widget.Card?.Id == attacker);
                var defending = _ownField.Widgets.Concat(_enemyField.Widgets).FirstOrDefault(widget => widget.Card?.Id == blocker);
                if (attacking == null || defending == null)
                {
                    continue;
                }
                if (!_defenseLines.TryGetValue(attacker, out var line))
                {
                    var root = new GameObject("YakouDefenseLink", typeof(RectTransform), typeof(CanvasRenderer), typeof(DefenseBezierGraphic));
                    var rect = (RectTransform)root.transform;
                    rect.SetParent(_lineRoot, false);
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                    line = root.GetComponent<DefenseBezierGraphic>();
                    line.raycastTarget = false;
                    line.color = new Color(1, .76f, .3f, 1);
                    _defenseLines.Add(attacker, line);
                }
                line.gameObject.SetActive(true);
                line.SetEndpoints(attacking.WorldCenter, defending.WorldCenter);
            }
        }

        private static CardView Card(JToken item)
        {
            if (item == null || item.Type == JTokenType.Null)
            {
                return null;
            }
            return new CardView
            {
                Yakou = true,
                Id = (int)item["Id"],
                DefinitionId = (int)item["DefinitionId"],
                CardType = (int?)item["CardType"] ?? 0,
                Attack = (int?)item["Attack"] ?? 0,
                Hp = (int?)item["Hp"] ?? 0,
                MaxHp = (int?)item["MaxHp"] ?? 0,
                YakouCountdown = (int?)item["AttackCounter"] ?? -1,
            };
        }

        private static CardView[] Cards(JToken value)
        {
            return (value as JArray ?? new JArray()).Select(Card).Where(card => card != null).ToArray();
        }

        private void Refresh()
        {
            if (string.IsNullOrEmpty(_session.MatchId))
            {
                ClientApp.Instance.Navigate("LobbyScene");
                return;
            }
            if (_session.Sequence != _sequence)
            {
                _sequence = _session.Sequence;
                _summonChoice = null;
                _materials.Clear();
                _blockers.Clear();
                _attacker = 0;
                _responseType = null;
                _responseOption = null;
                _responseSelection = null;
                _responseCosts.Clear();
                _pendingTarget = 0;
                _pendingTargets.Clear();
                _feedbackMessage = null;
                _actions.gameObject.SetActive(false);
            }
            _state = _session.State;
            if (_state == null)
            {
                _previousPublicState = null;
                _loggedSequence = -1;
                _summonChoice = null;
                _materials.Clear();
                _responseType = null;
                _responseOption = null;
                _responseSelection = null;
                _responseCosts.Clear();
                _pendingTarget = 0;
                _pendingTargets.Clear();
                _actions.gameObject.SetActive(false);
                foreach (var zone in new[] { _hand, _ownField, _enemyField, _enemyHand, _ownGrave, _enemyGrave })
                {
                    zone.Render(null, 0, false, 0, false, null, null, null);
                }
                _detail.Show(null);
                _statusLine = null;
                ShowFeedback(!string.IsNullOrEmpty(_session.Error)
                    ? YakouUiText.Error(_session.Error) : YakouUiText.Get("ui.yakou.status.wait_snapshot"));
                _screen.EndTurn.interactable = false;
                return;
            }
            if (_log != null && _session.Sequence != _loggedSequence)
            {
                _log.SetYakouTurn((int)_state["TurnNumber"]);
                if (_previousPublicState != null)
                {
                    LogPublicChanges(_previousPublicState, _state);
                }
                _previousPublicState = (JObject)_state.DeepClone();
                _loggedSequence = _session.Sequence;
            }
            var players = (JArray)_state["Players"];
            var own = players.First(player => (int)player["PlayerId"] == _session.Seat);
            var enemy = players.First(player => (int)player["PlayerId"] != _session.Seat);
            _hand.Render(Cards(own["Hand"]), 0, false, _selected, false, ClickCard, Hover, Unhover);
            _ownField.Render(Cards(own["FieldSlots"]), 0, false, _selected, false, ClickCard, Hover, Unhover);
            _enemyField.Render(Cards(enemy["FieldSlots"]), 0, false, _selected, false, ClickCard, Hover, Unhover);
            ArrangeField(_ownField, (JArray)own["FieldSlots"]);
            ArrangeField(_enemyField, (JArray)enemy["FieldSlots"]);
            _enemyHand.Render(null, (int)enemy["HandCount"], true, 0, false, null, null, null);
            _ownGrave.Render(Cards(own["Graveyard"]), 0, false, _selected, false, ClickCard, Hover, Unhover);
            _enemyGrave.Render(Cards(enemy["Graveyard"]), 0, false, _selected, false, ClickCard, Hover, Unhover);
            PresentFieldChanges();
            _ownPlayer.Render(new PlayerView { Life = (int)own["Life"] }, true, false);
            _enemyPlayer.Render(new PlayerView { Life = (int)enemy["Life"] }, false, false);
            _ownDeckCount.text = (string)own["DeckCount"];
            _enemyDeckCount.text = (string)enemy["DeckCount"];
            _ownGraveCount.text = ((JArray)own["Graveyard"]).Count.ToString();
            _enemyGraveCount.text = ((JArray)enemy["Graveyard"]).Count.ToString();
            _ownExileCount.text = ((JArray)own["Exile"]).Count.ToString();
            _enemyExileCount.text = ((JArray)enemy["Exile"]).Count.ToString();
            _screen.TurnLabel.text = ((int)_state["TurnNumber"]).ToString();
            _turnNumberBadge.text = _screen.TurnLabel.text;
            string phase = (string)_state["Phase"];
            _screen.PhaseLabel.text = YakouUiText.Phase(phase);
            _statusLine = YakouUiText.Get("ui.yakou.hud.status", _screen.PhaseLabel.text,
                YakouUiText.Player((int)_state["ActivePlayerId"], _session.Seat),
                YakouUiText.Player((int)_state["PriorityPlayerId"], _session.Seat),
                (int)own["Ki"], (int)enemy["Ki"]);
            var stack = _state["ResponseStack"] as JArray;
            _screen.EndTurn.interactable = _session.Bound && !_session.Busy;
            _screen.EndTurn.GetComponentInChildren<TMP_Text>().text = YakouUiText.Get("ui.yakou.action.menu");
            bool finished = (string)_state["Phase"] == "Finished";
            _turnHud?.Render((int)_state["TurnNumber"], (int)_state["ActivePlayerId"], false, 0,
                _session.Seat, true, finished, _state["PendingSelection"] != null);
            _turnFrame?.SetOpponentTurn((int)_state["ActivePlayerId"] != _session.Seat);
            if (_screen.ResultPanel != null)
            {
                _screen.ResultPanel.SetActive(finished);
                if (finished && _screen.ResultLabel != null)
                {
                    _screen.ResultLabel.text = YakouUiText.Get((int)_state["WinnerPlayerId"] == _session.Seat
                        ? "ui_battle_victory" : "ui_battle_defeat");
                }
                if (_screen.ResultRematch != null)
                {
                    _screen.ResultRematch.gameObject.SetActive(false);
                }
            }
            var selection = _state["PendingSelection"] as JObject;
            _feedbackMessage = null;
            if (selection != null && (string)selection["Status"] == "Unavailable")
            {
                _feedbackMessage = YakouUiText.Get("ui.yakou.status.unsupported_effect");
            }
            else if (selection != null && (string)selection["Status"] == "Ready")
            {
                _feedbackMessage = YakouUiText.Get("ui_battle_select_target");
            }
            else if (!string.IsNullOrEmpty(_session.Error))
            {
                _feedbackMessage = YakouUiText.Error(_session.Error);
            }
            else if (stack != null && stack.Count > 0)
            {
                _feedbackMessage = YakouUiText.Get("ui.yakou.response.order", string.Join(" → ",
                    stack.Reverse().Select(link => CardPresentation.Name(Card(link["Card"])))));
            }
            RenderFeedback();
            UpdateHints();
        }

        private void ShowFeedback(string message)
        {
            _feedbackMessage = message;
            RenderFeedback();
        }

        private void RenderFeedback()
        {
            _feedback.text = string.IsNullOrEmpty(_statusLine) ? _feedbackMessage ?? string.Empty
                : string.IsNullOrEmpty(_feedbackMessage) ? _statusLine : _statusLine + "\n" + _feedbackMessage;
        }

        private void Hover(CardWidget widget) => _detail.Show(widget.Card);

        private void LogPublicChanges(JObject before, JObject after)
        {
            if ((string)before["Phase"] != (string)after["Phase"] ||
                (int)before["ActivePlayerId"] != (int)after["ActivePlayerId"])
            {
                _log.Add(YakouUiText.Get("ui.yakou.log.phase", YakouUiText.Phase((string)after["Phase"]),
                    YakouUiText.Player((int)after["ActivePlayerId"], _session.Seat)));
                if ((int)before["ActivePlayerId"] != (int)after["ActivePlayerId"])
                {
                    _screen.Effects?.Turn((int)after["ActivePlayerId"] == _session.Seat, (int)after["TurnNumber"]);
                }
            }
            var oldPlayers = (JArray)before["Players"];
            foreach (var player in (JArray)after["Players"])
            {
                int id = (int)player["PlayerId"];
                var old = oldPlayers.First(item => (int)item["PlayerId"] == id);
                if ((int)old["Life"] != (int)player["Life"])
                {
                    _log.Add(YakouUiText.Get("ui.yakou.log.life", YakouUiText.Player(id, _session.Seat),
                        (int)old["Life"], (int)player["Life"]));
                }
                var oldField = Cards(old["FieldSlots"]).ToDictionary(card => card.Id);
                var newField = Cards(player["FieldSlots"]).ToDictionary(card => card.Id);
                foreach (var card in newField.Values)
                {
                    if (!oldField.TryGetValue(card.Id, out var previous))
                    {
                        _log.Add(YakouUiText.Get("ui.yakou.log.field_enter", CardPresentation.Name(card)));
                        _fieldEntries.Add(card.Id);
                    }
                    else if (previous.Hp != card.Hp || previous.Attack != card.Attack)
                    {
                        _log.Add(YakouUiText.Get("ui.yakou.log.stats", CardPresentation.Name(card),
                            previous.Attack, card.Attack, previous.Hp, card.Hp));
                        _fieldChanges.Add(card.Id);
                    }
                }
                foreach (var card in oldField.Values.Where(card => !newField.ContainsKey(card.Id)))
                {
                    _log.Add(YakouUiText.Get("ui.yakou.log.field_leave", CardPresentation.Name(card)));
                }
            }
            var oldStack = before["ResponseStack"] as JArray ?? new JArray();
            var newStack = after["ResponseStack"] as JArray ?? new JArray();
            if (newStack.Count > oldStack.Count)
            {
                foreach (var link in newStack.Skip(oldStack.Count))
                {
                    _log.Add(YakouUiText.Get("ui.yakou.log.response_added", CardPresentation.Name(Card(link["Card"]))));
                }
            }
            else if (oldStack.Count > 0 && newStack.Count == 0)
            {
                _log.Add(YakouUiText.Get("ui.yakou.log.response_resolved"));
            }
        }

        private void PresentFieldChanges()
        {
            foreach (var zone in new[] { _ownField, _enemyField })
            {
                foreach (var widget in zone.Widgets)
                {
                    int id = widget.Card?.Id ?? 0;
                    if (_fieldEntries.Contains(id) && _screen.Effects != null)
                    {
                        _screen.Effects.Summon(zone, widget,
                            _screen.Effects.Settings?.SummonDuration ?? DefaultFieldEntryDuration);
                    }
                    else if (_fieldChanges.Contains(id))
                    {
                        widget.Pulse();
                    }
                }
            }
            _fieldEntries.Clear();
            _fieldChanges.Clear();
        }

        private void ShowExile(bool own, Vector3 position)
        {
            if (_state == null)
            {
                return;
            }
            var player = ((JArray)_state["Players"]).First(item => ((int)item["PlayerId"] == _session.Seat) == own);
            var options = new List<KeyValuePair<string, Action>>();
            foreach (var entry in (JArray)player["Exile"])
            {
                var card = Card(entry);
                options.Add(Option(CardPresentation.Name(card), () => SelectCard(card, position)));
            }
            _actions.ShowChoices(position, options);
        }

        private static void ArrangeField(CardZoneView view, JArray slots)
        {
            var positions = new List<int>();
            for (int index = 0; index < slots.Count; ++index)
            {
                if (slots[index].Type != JTokenType.Null)
                {
                    positions.Add(index);
                }
            }
            view.Layout.ArrangeSlots(view.Widgets, positions, slots.Count);
        }
        private void Unhover() { }

        private void ClickCard(CardWidget widget)
        {
            SelectCard(widget.Card, widget.WorldCenter);
        }

        private void SelectCard(CardView card, Vector3 position)
        {
            if (!_session.Bound || _session.Busy || _state == null)
            {
                return;
            }
            int id = card.Id;
            _selected = id;
            _detail.Show(card);
            foreach (var zone in new[] { _hand, _ownField, _enemyField, _ownGrave, _enemyGrave })
            {
                foreach (var visible in zone.Widgets)
                {
                    int visibleId = visible.Card?.Id ?? 0;
                    visible.SetSelected(visibleId == id || _materials.Contains(visibleId) || _responseCosts.Contains(visibleId));
                }
            }
            var pending = _state["PendingSelection"] as JObject;
            if (pending != null && (string)pending["Status"] == "Ready")
            {
                if (((JArray)pending["Candidates"]).Values<int>().Contains(id))
                {
                    if ((int?)pending["MaxCount"] > 1)
                    {
                        if (!_pendingTargets.Add(id))
                        {
                            _pendingTargets.Remove(id);
                        }
                        if (_pendingTargets.Count > (int)pending["MaxCount"])
                        {
                            _pendingTargets.Remove(id);
                        }
                    }
                    else
                    {
                        _pendingTarget = id;
                    }
                    _actions.gameObject.SetActive(false);
                    ShowFeedback(YakouUiText.Get("ui.yakou.feedback.target_selected"));
                    UpdateHints();
                }
                return;
            }
            if (_responseType != null && _responseSelection != null)
            {
                string candidateField = _responseSelection == "cost" ? "CostCandidates"
                    : _responseSelection == "replacement" ? "ReplacementCandidates" : "TargetCandidates";
                if (_responseOption != null && !((_responseOption[candidateField] as JArray) ?? new JArray()).Values<int>().Contains(id))
                {
                    return;
                }
                if (_responseSelection == "cost")
                {
                    if (!_responseCosts.Add(id))
                    {
                        _responseCosts.Remove(id);
                    }
                }
                else if (_responseSelection == "target")
                {
                    _responseTarget = id;
                }
                else if (_responseSelection == "replacement")
                {
                    _responseReplacement = id;
                }
                ShowFeedback(YakouUiText.Get("ui.yakou.feedback.response_ready"));
                UpdateHints();
                return;
            }
            if (_summonChoice != null)
            {
                if (((JArray)_summonChoice["Candidates"]).Values<int>().Contains(id))
                {
                    if (!_materials.Add(id))
                    {
                        _materials.Remove(id);
                    }
                    UpdateHints();
                    ShowSummonProgress(position);
                }
                return;
            }
            if ((string)_state["Phase"] == "AssignBlockers" && (int)_state["ActivePlayerId"] != _session.Seat)
            {
                if (((JArray)_state["Attacks"]).Any(attack => (int)attack["AttackerId"] == id))
                {
                    _attacker = id;
                    ShowFeedback(YakouUiText.Get("ui.yakou.feedback.choose_blocker"));
                }
                else if (_attacker != 0 && OwnFieldContains(id))
                {
                    _blockers[_attacker] = id;
                    ShowFeedback(YakouUiText.Get("ui.yakou.feedback.blockers_count", _blockers.Count));
                    _attacker = 0;
                }
                return;
            }
            var options = new List<KeyValuePair<string, Action>>();
            var own = ((JArray)_state["Players"]).First(player => (int)player["PlayerId"] == _session.Seat);
            bool ownMain = (string)_state["Phase"] == "Main"
                && (int)_state["ActivePlayerId"] == _session.Seat
                && _state["PendingSelection"] == null;
            foreach (var entry in own["SummonChoices"] as JArray ?? new JArray())
            {
                if ((int)entry["CardId"] == id)
                {
                    var choice = (JObject)entry;
                    options.Add(Option(YakouUiText.Get("ui.yakou.action.summon",
                        YakouUiText.Method((string)choice["Method"])), () =>
                    {
                        _summonChoice = choice;
                        _materials.Clear();
                        ShowSummonProgress(position);
                        UpdateHints();
                    }));
                }
            }
            if (ownMain && card.CardType == 1 && (own["Hand"] as JArray).Any(item => (int)item["Id"] == id))
            {
                var paid = (own["PaidSpellOptions"] as JArray ?? new JArray())
                    .FirstOrDefault(item => (int)item["CardId"] == id) as JObject;
                if (paid != null)
                {
                    options.Add(Option(YakouUiText.Get("ui.yakou.action.paid_spell_cost"), () =>
                    {
                        BeginResponse("ActivateMainSpell", id);
                        _responseOption = new JObject
                        {
                            ["CostCandidates"] = paid["CostCandidates"].DeepClone(),
                            ["CostCount"] = (int)paid["CostCount"],
                            ["TargetCandidates"] = new JArray(),
                            ["ReplacementCandidates"] = new JArray(),
                        };
                        SelectResponsePart("cost");
                        ShowFeedback(YakouUiText.Get("ui.yakou.feedback.paid_spell_no_response"));
                    }));
                }
                else if (card.DefinitionId != PaidMapSpellDefinitionId)
                {
                    options.Add(Option(YakouUiText.Get("ui.yakou.action.cast_spell"), () => Send("BeginSpellResponse", new JObject { ["CardId"] = id, ["TargetId"] = 0 })));
                    options.Add(Option(YakouUiText.Get("ui.yakou.action.cast_spell_target"), () => BeginResponse("BeginSpellResponse", id)));
                }
            }
            if (card.CardType == 1 && (own["Hand"] as JArray).Any(item => (int)item["Id"] == id))
            {
                foreach (var response in _state["ResponseOptions"] as JArray ?? new JArray())
                {
                    if ((int)response["CardId"] == id)
                    {
                        var offered = (JObject)response;
                        string type = (string)offered["Type"];
                        string label = YakouUiText.Get(type == "RespondWithRaidShield" ? "ui.yakou.action.response_shield"
                            : type == "RespondWithRaidTactic" ? "ui.yakou.action.response_tactic" : "ui.yakou.action.response_negate");
                        options.Add(Option(label, () => BeginOfferedResponse(offered)));
                    }
                }
            }
            if (ownMain && OwnFieldContains(id))
            {
                var definition = CardPresentation.LibraryFor(card).Find(card.DefinitionId);
                foreach (int form in definition?.AllowedManifestations ?? Array.Empty<int>())
                {
                    int selectedForm = form;
                    options.Add(Option(YakouUiText.Get("ui.yakou.action.manifest",
                        CardPresentation.LibraryFor(card).Find(form)?.Name),
                        () => Send("Manifest", new JObject { ["CardId"] = id, ["DefinitionId"] = selectedForm })));
                }
                options.Add(Option(YakouUiText.Get("ui.yakou.action.postpone"), () => Send("Postpone", new JObject { ["CardId"] = id })));
            }
            _actions.ShowChoices(position, options);
        }

        private bool OwnFieldContains(int id)
        {
            var own = ((JArray)_state["Players"]).First(player => (int)player["PlayerId"] == _session.Seat);
            return (own["FieldSlots"] as JArray).Any(card => card.Type != JTokenType.Null && (int)card["Id"] == id);
        }

        private void ShowSummonProgress(Vector3 position)
        {
            int required = (int)_summonChoice["RequiredCount"];
            ShowFeedback(YakouUiText.Get("ui.yakou.feedback.summon_cost", _materials.Count, required));
            var options = new List<KeyValuePair<string, Action>>();
            if (_materials.Count == required)
            {
                foreach (int slot in ((JArray)_summonChoice["AvailableSlots"]).Values<int>())
                {
                    int selectedSlot = slot;
                    options.Add(Option(YakouUiText.Get("ui.yakou.action.field_slot", slot + 1), () => Send("Summon", new JObject
                    {
                        ["CardId"] = (int)_summonChoice["CardId"], ["Method"] = (string)_summonChoice["Method"],
                        ["Materials"] = new JArray(_materials), ["Slot"] = selectedSlot,
                    })));
                }
            }
            options.Add(Option(YakouUiText.Get("ui_battle_cancel"), () =>
            {
                _summonChoice = null;
                _materials.Clear();
                _actions.gameObject.SetActive(false);
            }));
            _actions.ShowChoices(position, options);
        }

        private void PrimaryActions()
        {
            if (_session.Busy || _state == null)
            {
                return;
            }
            var options = new List<KeyValuePair<string, Action>>();
            if (_responseType != null)
            {
                if (_responseType == "BeginSpellResponse" ||
                    (_responseOption != null && ((JArray)_responseOption["TargetCandidates"]).Count > 0))
                {
                    options.Add(Option(YakouUiText.Get("ui.yakou.action.response_target"), () => SelectResponsePart("target")));
                }
                if (_responseOption != null && ((JArray)_responseOption["ReplacementCandidates"]).Count > 0)
                {
                    options.Add(Option(YakouUiText.Get("ui.yakou.action.replacement_blocker"), () => SelectResponsePart("replacement")));
                }
                if (_responseType == "Redraw" || (_responseOption != null && (int)_responseOption["CostCount"] > 0))
                {
                    string required = _responseOption != null ? ((int)_responseOption["CostCount"]).ToString() : "?";
                    options.Add(Option(YakouUiText.Get("ui.yakou.action.response_cost", _responseCosts.Count, required),
                        () => SelectResponsePart("cost")));
                }
                options.Add(Option(_responseType == "ActivateMainSpell" || _responseType == "Redraw"
                    ? YakouUiText.Get("ui_battle_confirm_selection") : YakouUiText.Get("ui.yakou.action.play_response"), SubmitResponse));
                options.Add(Option(YakouUiText.Get("ui_battle_cancel_selection"), () =>
                {
                    _responseType = null;
                    _responseCosts.Clear();
                    _actions.gameObject.SetActive(false);
                }));
                _actions.ShowChoices(_screen.EndTurn.transform.position, options);
                return;
            }
            string phase = (string)_state["Phase"];
            int active = (int)_state["ActivePlayerId"];
            int priority = (int)_state["PriorityPlayerId"];
            if (phase == "TurnStart" && active == _session.Seat)
            {
                options.Add(Option(YakouUiText.Get("ui.yakou.action.start_turn"), () => Send("StartTurn", new JObject())));
            }
            if (phase == "Main" && active == _session.Seat)
            {
                options.Add(Option(YakouUiText.Get("ui.yakou.action.begin_raid"), () => Send("BeginRaid", new JObject())));
                options.Add(Option(YakouUiText.Get("ui.yakou.action.redraw"), () =>
                {
                    BeginResponse("Redraw", 0);
                    SelectResponsePart("cost");
                }));
            }
            if (phase == "AssignBlockers" && active != _session.Seat)
            {
                options.Add(Option(YakouUiText.Get("ui.yakou.action.confirm_blockers"), () => Send("AssignBlockers", new JObject
                {
                    ["Blockers"] = new JArray(_blockers.Select(pair => new JObject { ["Attacker"] = pair.Key, ["Blocker"] = pair.Value })),
                })));
            }
            if (phase == "RaidResponse" && priority == _session.Seat)
            {
                options.Add(Option(YakouUiText.Get("ui.yakou.action.pass_raid_response"), () => Send("PassRaidResponse", new JObject())));
            }
            if (phase == "RaidResponse" && priority == 0 && active != _session.Seat)
            {
                options.Add(Option(YakouUiText.Get("ui.yakou.action.resolve_raid"), () => Send("ResolveRaid", new JObject())));
            }
            if (phase == "SpellResponse" && priority == _session.Seat)
            {
                options.Add(Option(YakouUiText.Get("ui.yakou.action.pass_spell_response"), () => Send("PassSpellResponse", new JObject())));
            }
            var pending = _state["PendingSelection"] as JObject;
            if (pending != null && (string)pending["Status"] == "Ready")
            {
                bool multiple = (int?)pending["MaxCount"] > 1;
                bool ready = multiple ? _pendingTargets.Count >= (int)pending["MinCount"] &&
                    _pendingTargets.Count <= (int)pending["MaxCount"] : _pendingTarget != 0;
                if (ready)
                {
                    options.Add(Option(YakouUiText.Get("ui_battle_confirm_selection"), () => Send("ChooseEffect",
                        multiple ? new JObject { ["TargetIds"] = new JArray(_pendingTargets.OrderBy(id => id)) }
                                 : new JObject { ["TargetId"] = _pendingTarget })));
                    options.Add(Option(YakouUiText.Get("ui.yakou.action.reselect_effect"), () =>
                    {
                        _pendingTarget = 0;
                        _pendingTargets.Clear();
                        _actions.gameObject.SetActive(false);
                        UpdateHints();
                    }));
                }
                foreach (var card in pending["CandidateCards"] as JArray ?? new JArray())
                {
                    var visible = Card(card);
                    options.Add(Option(YakouUiText.Get("ui.yakou.action.choose_candidate", CardPresentation.Name(visible)),
                        () => SelectCard(visible, _screen.EndTurn.transform.position)));
                }
                if ((int?)pending["MinCount"] == 0)
                {
                    options.Add(Option(YakouUiText.Get("ui.yakou.action.decline_optional_effect"), () => Send("ChooseEffect", new JObject { ["TargetId"] = 0 })));
                }
            }
            options.Add(Option(YakouUiText.Get("ui.yakou.action.leave_room"), () => _session.Leave()));
            _actions.ShowChoices(_screen.EndTurn.transform.position, options);
        }

        private static KeyValuePair<string, Action> Option(string name, Action action) =>
            new KeyValuePair<string, Action>(name, action);

        private void BeginResponse(string type, int card)
        {
            _responseType = type;
            _responseOption = null;
            _responseCard = card;
            _responseTarget = 0;
            _responseReplacement = 0;
            _responseSelection = "target";
            _responseCosts.Clear();
            _actions.gameObject.SetActive(false);
            ShowFeedback(YakouUiText.Get("ui.yakou.feedback.begin_response"));
        }

        private void BeginOfferedResponse(JObject offered)
        {
            BeginResponse((string)offered["Type"], (int)offered["CardId"]);
            _responseOption = (JObject)offered.DeepClone();
            _responseSelection = ((JArray)offered["TargetCandidates"]).Count > 0 ? "target" : "cost";
            UpdateHints();
        }

        private void UpdateHints()
        {
            if (_state == null)
            {
                return;
            }
            JArray candidates = null;
            var pending = _state["PendingSelection"] as JObject;
            if (pending != null && (string)pending["Status"] == "Ready")
            {
                candidates = pending["Candidates"] as JArray;
            }
            else if (_summonChoice != null)
            {
                candidates = _summonChoice["Candidates"] as JArray;
            }
            else if (_responseOption != null && _responseSelection != null)
            {
                string field = _responseSelection == "cost" ? "CostCandidates"
                    : _responseSelection == "replacement" ? "ReplacementCandidates" : "TargetCandidates";
                candidates = _responseOption[field] as JArray;
            }
            var valid = new HashSet<int>((candidates ?? new JArray()).Values<int>());
            var responseCards = new HashSet<int>((_state["ResponseOptions"] as JArray ?? new JArray())
                .Select(option => (int)option["CardId"]));
            foreach (var zone in new[] { _hand, _ownField, _enemyField, _ownGrave, _enemyGrave })
            {
                foreach (var widget in zone.Widgets)
                {
                    int id = widget.Card?.Id ?? 0;
                    bool chosen = id == _pendingTarget || _pendingTargets.Contains(id) ||
                        _materials.Contains(id) || _responseCosts.Contains(id)
                        || (_responseOption != null && (id == _responseTarget || id == _responseReplacement));
                    widget.SetSelected(chosen);
                    widget.SetHint(candidates == null ? (responseCards.Contains(id) ? TargetHint.Available : TargetHint.None)
                        : chosen ? TargetHint.Focused : valid.Contains(id) ? TargetHint.Available : TargetHint.Unavailable);
                }
            }
        }

        private void SelectResponsePart(string part)
        {
            _responseSelection = part;
            _actions.gameObject.SetActive(false);
            UpdateHints();
        }

        private void SubmitResponse()
        {
            var fields = new JObject();
            if (_responseType != "Redraw")
            {
                fields["CardId"] = _responseCard;
            }
            if (_responseType == "BeginSpellResponse")
            {
                fields["TargetId"] = _responseTarget;
            }
            if (_responseType == "Redraw")
            {
                fields["Cards"] = new JArray(_responseCosts);
            }
            if (_responseType == "RespondWithRaidShield" || _responseType == "RespondWithRaidTactic")
            {
                fields["TargetId"] = _responseTarget;
            }
            if (_responseType == "RespondWithRaidTactic")
            {
                fields["ReplacementBlocker"] = _responseReplacement;
            }
            if (_responseType == "RespondWithRaidTactic" || _responseType == "RespondWithNegation" || _responseType == "ActivateMainSpell")
            {
                fields["Costs"] = new JArray(_responseCosts);
            }
            Send(_responseType, fields);
        }

        private void Send(string type, JObject fields)
        {
            fields["Type"] = type;
            try
            {
                _session.Command(fields);
                _actions.gameObject.SetActive(false);
            }
            catch (Exception error)
            {
                ShowFeedback(YakouUiText.Error(error.Message));
            }
        }

        private void Resync()
        {
            if (_session.Bound)
            {
                _session.RequestSnapshot();
            }
        }
    }
}
