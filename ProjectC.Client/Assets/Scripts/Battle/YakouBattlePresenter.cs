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
        private TMP_Text _feedback;
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
        private readonly HashSet<int> _responseCosts = new HashSet<int>();
        private RectTransform _lineRoot;
        private readonly Dictionary<int, DefenseBezierGraphic> _defenseLines = new Dictionary<int, DefenseBezierGraphic>();
        private long _sequence = -1;
        private long _loggedSequence = -1;
        private JObject _previousPublicState;
        private readonly HashSet<int> _fieldEntries = new HashSet<int>();
        private readonly HashSet<int> _fieldChanges = new HashSet<int>();
        private const float DefaultFieldEntryDuration = .66f;

        private void Awake()
        {
            _screen = GetComponent<BattlePresenter>();
            _session = ClientApp.Instance.Network.Yakou;
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
                || !this.Bind("OwnDeckCountText", out _ownDeckCount) || !this.Bind("EnemyDeckCountText", out _enemyDeckCount)
                || !this.Bind("OwnGraveCountText", out _ownGraveCount) || !this.Bind("EnemyGraveCountText", out _enemyGraveCount)
                || !this.Bind("OwnExileCountText", out _ownExileCount) || !this.Bind("EnemyExileCountText", out _enemyExileCount)
                || _screen.EndTurn == null)
            {
                throw new InvalidOperationException("Yakou battle requires the existing battle card, player and action views.");
            }
            _actions.gameObject.SetActive(false);
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
                _actions.gameObject.SetActive(false);
            }
            _state = _session.State;
            _feedback.text = _session.Error ?? "";
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
                _actions.gameObject.SetActive(false);
                foreach (var zone in new[] { _hand, _ownField, _enemyField, _enemyHand, _ownGrave, _enemyGrave })
                {
                    zone.Render(null, 0, false, 0, false, null, null, null);
                }
                _detail.Show(null);
                _feedback.text = _session.Error ?? "서버 스냅샷을 기다리는 중입니다.";
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
            _screen.PhaseLabel.text = (string)_state["Phase"] + " | Active " + (string)_state["ActivePlayerId"]
                + " | Priority " + (string)_state["PriorityPlayerId"] + " | Ki " + (string)own["Ki"] + "/" + (string)enemy["Ki"];
            var stack = _state["ResponseStack"] as JArray;
            if (stack != null && stack.Count > 0)
            {
                _screen.PhaseLabel.text += " | 해결순: " + string.Join(" → ", stack.Reverse().Select(link =>
                    CardPresentation.Name(Card(link["Card"]))));
            }
            _screen.EndTurn.interactable = _session.Bound && !_session.Busy;
            _screen.EndTurn.GetComponentInChildren<TMP_Text>().text = "Yakou 행동";
            bool finished = (string)_state["Phase"] == "Finished";
            if (_screen.ResultPanel != null)
            {
                _screen.ResultPanel.SetActive(finished);
                if (finished && _screen.ResultLabel != null)
                {
                    _screen.ResultLabel.text = (int)_state["WinnerPlayerId"] == _session.Seat ? "승리" : "패배";
                }
                if (_screen.ResultRematch != null)
                {
                    _screen.ResultRematch.gameObject.SetActive(false);
                }
            }
            var selection = _state["PendingSelection"] as JObject;
            if (selection != null && (string)selection["Status"] == "Unavailable")
            {
                _feedback.text = "서버에서 지원하지 않는 효과입니다.";
            }
            else if (selection != null && (string)selection["Status"] == "Ready")
            {
                _feedback.text = "효과 대상을 선택하세요.";
            }
            UpdateHints();
        }

        private void Hover(CardWidget widget) => _detail.Show(widget.Card);

        private void LogPublicChanges(JObject before, JObject after)
        {
            if ((string)before["Phase"] != (string)after["Phase"] ||
                (int)before["ActivePlayerId"] != (int)after["ActivePlayerId"])
            {
                _log.Add("단계: " + (string)after["Phase"] + " · 행동 플레이어 " + (string)after["ActivePlayerId"]);
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
                    _log.Add("플레이어 " + id + " 생명력: " + (string)old["Life"] + " → " + (string)player["Life"]);
                }
                var oldField = Cards(old["FieldSlots"]).ToDictionary(card => card.Id);
                var newField = Cards(player["FieldSlots"]).ToDictionary(card => card.Id);
                foreach (var card in newField.Values)
                {
                    if (!oldField.TryGetValue(card.Id, out var previous))
                    {
                        _log.Add(CardPresentation.Name(card) + " 필드에 등장");
                        _fieldEntries.Add(card.Id);
                    }
                    else if (previous.Hp != card.Hp || previous.Attack != card.Attack)
                    {
                        _log.Add(CardPresentation.Name(card) + " 공격력 " + previous.Attack + " → " + card.Attack
                            + ", 체력 " + previous.Hp + " → " + card.Hp);
                        _fieldChanges.Add(card.Id);
                    }
                }
                foreach (var card in oldField.Values.Where(card => !newField.ContainsKey(card.Id)))
                {
                    _log.Add(CardPresentation.Name(card) + " 필드에서 이동");
                }
            }
            var oldStack = before["ResponseStack"] as JArray ?? new JArray();
            var newStack = after["ResponseStack"] as JArray ?? new JArray();
            if (newStack.Count > oldStack.Count)
            {
                foreach (var link in newStack.Skip(oldStack.Count))
                {
                    _log.Add("응전 추가: " + CardPresentation.Name(Card(link["Card"])));
                }
            }
            else if (oldStack.Count > 0 && newStack.Count == 0)
            {
                _log.Add("응전 창 해결");
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
                    _pendingTarget = id;
                    _actions.gameObject.SetActive(false);
                    _feedback.text = "대상을 선택했습니다. 행동 버튼에서 확정하거나 다시 선택하세요.";
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
                _feedback.text = "응전 선택을 마치면 행동 버튼에서 제출하세요.";
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
                    _feedback.text = "방어할 아군 카드를 선택하세요.";
                }
                else if (_attacker != 0 && OwnFieldContains(id))
                {
                    _blockers[_attacker] = id;
                    _feedback.text = "방어 배정: " + _blockers.Count + "개. 행동 버튼에서 확정하세요.";
                    _attacker = 0;
                }
                return;
            }
            var options = new List<KeyValuePair<string, Action>>();
            var own = ((JArray)_state["Players"]).First(player => (int)player["PlayerId"] == _session.Seat);
            foreach (var entry in own["SummonChoices"] as JArray ?? new JArray())
            {
                if ((int)entry["CardId"] == id)
                {
                    var choice = (JObject)entry;
                    options.Add(Option("소환: " + (string)choice["Method"], () =>
                    {
                        _summonChoice = choice;
                        _materials.Clear();
                        ShowSummonProgress(position);
                        UpdateHints();
                    }));
                }
            }
            if (card.CardType == 1 && (own["Hand"] as JArray).Any(item => (int)item["Id"] == id))
            {
                var paid = (own["PaidSpellOptions"] as JArray ?? new JArray())
                    .FirstOrDefault(item => (int)item["CardId"] == id) as JObject;
                if (paid != null)
                {
                    options.Add(Option("주문 비용 선택", () =>
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
                        _feedback.text = "이 주문의 유료 발동에는 응전 창이 아직 지원되지 않습니다. 비용을 선택하세요.";
                    }));
                }
                else
                {
                    options.Add(Option("주문 응전 시작", () => Send("BeginSpellResponse", new JObject { ["CardId"] = id, ["TargetId"] = 0 })));
                    options.Add(Option("대상을 선택해 주문 응전 시작", () => BeginResponse("BeginSpellResponse", id)));
                }
                foreach (var response in _state["ResponseOptions"] as JArray ?? new JArray())
                {
                    if ((int)response["CardId"] == id)
                    {
                        var offered = (JObject)response;
                        string type = (string)offered["Type"];
                        string label = type == "RespondWithRaidShield" ? "피해 감소 응전"
                            : type == "RespondWithRaidTactic" ? "습격 전술 응전" : "주문 무효 응전";
                        options.Add(Option(label, () => BeginOfferedResponse(offered)));
                    }
                }
            }
            if (OwnFieldContains(id))
            {
                var definition = CardPresentation.LibraryFor(card).Find(card.DefinitionId);
                foreach (int form in definition?.AllowedManifestations ?? Array.Empty<int>())
                {
                    int selectedForm = form;
                    options.Add(Option("현현: " + CardPresentation.LibraryFor(card).Find(form)?.Name,
                        () => Send("Manifest", new JObject { ["CardId"] = id, ["DefinitionId"] = selectedForm })));
                }
                options.Add(Option("습격 연기", () => Send("Postpone", new JObject { ["CardId"] = id })));
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
            _feedback.text = "소환 비용 선택 " + _materials.Count + " / " + required;
            var options = new List<KeyValuePair<string, Action>>();
            if (_materials.Count == required)
            {
                foreach (int slot in ((JArray)_summonChoice["AvailableSlots"]).Values<int>())
                {
                    int selectedSlot = slot;
                    options.Add(Option("필드 슬롯 " + (slot + 1), () => Send("Summon", new JObject
                    {
                        ["CardId"] = (int)_summonChoice["CardId"], ["Method"] = (string)_summonChoice["Method"],
                        ["Materials"] = new JArray(_materials), ["Slot"] = selectedSlot,
                    })));
                }
            }
            options.Add(Option("취소", () =>
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
                if (_responseOption == null || ((JArray)_responseOption["TargetCandidates"]).Count > 0)
                {
                    options.Add(Option("응전 대상 선택", () => SelectResponsePart("target")));
                }
                if (_responseOption == null || ((JArray)_responseOption["ReplacementCandidates"]).Count > 0)
                {
                    options.Add(Option("대체 방어자 선택", () => SelectResponsePart("replacement")));
                }
                if (_responseOption == null || (int)_responseOption["CostCount"] > 0)
                {
                    string required = _responseOption != null ? ((int)_responseOption["CostCount"]).ToString() : "?";
                    options.Add(Option("응전 비용 선택 " + _responseCosts.Count + "/" + required, () => SelectResponsePart("cost")));
                }
                options.Add(Option(_responseType == "ActivateMainSpell" || _responseType == "Redraw"
                    ? "선택 확정" : "응전 제출", SubmitResponse));
                options.Add(Option("응전 선택 취소", () =>
                {
                    _responseType = null;
                    _responseCosts.Clear();
                    _actions.gameObject.SetActive(false);
                }));
                _actions.ShowChoices(_screen.EndTurn.transform.position, options);
                return;
            }
            string phase = (string)_state["Phase"];
            if (phase == "TurnStart")
            {
                options.Add(Option("턴 시작", () => Send("StartTurn", new JObject())));
            }
            if (phase == "Main")
            {
                options.Add(Option("습격 선언 / 턴 진행", () => Send("BeginRaid", new JObject())));
                options.Add(Option("교체할 손패 선택", () =>
                {
                    BeginResponse("Redraw", 0);
                    SelectResponsePart("cost");
                }));
            }
            if (phase == "AssignBlockers")
            {
                options.Add(Option("방어 배정 확정", () => Send("AssignBlockers", new JObject
                {
                    ["Blockers"] = new JArray(_blockers.Select(pair => new JObject { ["Attacker"] = pair.Key, ["Blocker"] = pair.Value })),
                })));
            }
            if (phase == "RaidResponse")
            {
                options.Add(Option("습격 응전 패스", () => Send("PassRaidResponse", new JObject())));
                options.Add(Option("습격 해결", () => Send("ResolveRaid", new JObject())));
            }
            if (phase == "SpellResponse")
            {
                options.Add(Option("주문 응전 패스", () => Send("PassSpellResponse", new JObject())));
            }
            var pending = _state["PendingSelection"] as JObject;
            if (pending != null && (string)pending["Status"] == "Ready")
            {
                if (_pendingTarget != 0)
                {
                    options.Add(Option("선택한 효과 대상 확정", () => Send("ChooseEffect", new JObject { ["TargetId"] = _pendingTarget })));
                    options.Add(Option("효과 대상 다시 선택", () =>
                    {
                        _pendingTarget = 0;
                        _actions.gameObject.SetActive(false);
                        UpdateHints();
                    }));
                }
                foreach (var card in pending["CandidateCards"] as JArray ?? new JArray())
                {
                    var visible = Card(card);
                    options.Add(Option("선택: " + CardPresentation.Name(visible),
                        () => SelectCard(visible, _screen.EndTurn.transform.position)));
                }
                if ((int?)pending["MinCount"] == 0)
                {
                    options.Add(Option("효과 선택 건너뛰기", () => Send("ChooseEffect", new JObject { ["TargetId"] = 0 })));
                }
            }
            options.Add(Option("방 나가기", () => _session.Leave()));
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
            _feedback.text = "대상을 선택하거나 행동 버튼에서 비용과 응전을 선택하세요.";
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
                    bool chosen = id == _pendingTarget || _materials.Contains(id) || _responseCosts.Contains(id)
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
                _feedback.text = error.Message;
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
