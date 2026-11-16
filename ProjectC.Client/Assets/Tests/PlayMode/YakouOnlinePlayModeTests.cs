#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ProjectC.Client;
using Game.Utils;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class YakouOnlinePlayModeTests
    {
        private const float TimeoutSeconds = 15f;
        private Process _server;
        private GameObject _first;
        private GameObject _second;
        private GameObject _battle;
        private GameObject _opponentBattle;
        private string _fixtureDirectory;

        [TearDown]
        public void Cleanup()
        {
            if (_battle != null)
            {
                Object.DestroyImmediate(_battle);
            }
            if (_opponentBattle != null)
            {
                Object.DestroyImmediate(_opponentBattle);
            }
            if (_first != null)
            {
                Object.DestroyImmediate(_first);
            }
            if (_second != null)
            {
                Object.DestroyImmediate(_second);
            }
            if (_server != null)
            {
                if (!_server.HasExited)
                {
                    _server.Kill();
                }
                _server.Dispose();
            }
            if (_fixtureDirectory != null && Directory.Exists(_fixtureDirectory))
            {
                Directory.Delete(_fixtureDirectory, true);
            }
        }

        [UnityTest]
        public IEnumerator TwoUnityTransportsPlayReconnectAndKeepHandsPrivate()
        {
            string executable = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
            {
                if (args[i] == "-projectcServerPath")
                {
                    executable = args[i + 1];
                }
            }
            Assert.IsTrue(File.Exists(executable), "Pass -projectcServerPath");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            string sourceData = Path.GetFullPath(Path.Combine(Application.dataPath, "../../ProjectC.Server/Data"));
            _fixtureDirectory = Path.Combine(Application.temporaryCachePath, "YakouResponse-" + Guid.NewGuid().ToString("N"));
            foreach (string source in Directory.GetFiles(sourceData, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(_fixtureDirectory, source.Substring(sourceData.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(source, destination);
            }
            string deckPath = Path.Combine(_fixtureDirectory, "YakouPrototype", "Decks.json");
            var decks = JObject.Parse(File.ReadAllText(deckPath));
            var cards = ((JArray)decks["Decks"][0]["Cards"]).Values<int>().ToList();
            foreach (int id in new[] { 2001, 2002, 2011, 2010, 2010 })
            {
                Assert.IsTrue(cards.Remove(id));
            }
            Assert.IsTrue(cards.Remove(3012));
            cards.Insert(0, 3012);
            cards.InsertRange(0, new[] { 2001, 2002, 3016, 2011, 3018 });
            decks["Decks"][0]["Cards"] = new JArray(cards);
            File.WriteAllText(deckPath, decks.ToString());
            _server = Process.Start(new ProcessStartInfo(executable,
                "--port " + port + " --yakou-prototype on --data \"" + _fixtureDirectory + "\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            var startup = _server.StandardOutput.ReadLineAsync();
            yield return Until(() => startup.IsCompleted);
            StringAssert.Contains("listening", startup.Result);
            _first = new GameObject("Yakou client one", typeof(MultiplayerDebugScreen));
            var app = _first.AddComponent<ClientApp>();
            typeof(ClientApp).GetProperty("Environment").GetSetMethod(true)
                .Invoke(app, new object[] { ServerEnvironment.Local });
            // This test drives reconnection explicitly without front-end scene navigation.
            app.enabled = false;
            _second = new GameObject("Yakou client two", typeof(MultiplayerDebugScreen));
            var a = _first.GetComponent<MultiplayerDebugScreen>();
            var b = _second.GetComponent<MultiplayerDebugScreen>();
            string address = "ws://127.0.0.1:" + port;
            a.ConnectTo(address);
            b.ConnectTo(address);
            yield return Until(() => a.ConnectionReady && b.ConnectionReady);
            a.Yakou.Create();
            yield return Until(() => a.Yakou.State != null);
            b.Yakou.Join(a.Yakou.MatchId);
            yield return Until(() => b.Yakou.State != null);
            Assert.AreEqual(5, ((JArray)a.Yakou.State["Players"][0]["Hand"]).Count);
            Assert.AreEqual(0, ((JArray)a.Yakou.State["Players"][1]["Hand"]).Count);
            Assert.AreEqual(0, ((JArray)b.Yakou.State["Players"][0]["Hand"]).Count);
            Assert.AreEqual(5, ((JArray)b.Yakou.State["Players"][1]["Hand"]).Count);
            b.Yakou.Command(new JObject { ["Type"] = "StartTurn" });
            yield return Until(() => !b.Yakou.Busy);
            Assert.AreEqual("Rejected", b.Yakou.Error);
            Assert.AreEqual(0, b.Yakou.Sequence);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleScreen.prefab");
            Assert.IsNotNull(prefab);
            _battle = Object.Instantiate(prefab);
            yield return null;
            var yakou = _battle.GetComponent<YakouBattlePresenter>();
            var screen = _battle.GetComponent<BattlePresenter>();
            Assert.IsNotNull(yakou);
            Assert.IsFalse(screen.enabled);
            Assert.IsTrue(yakou.Bind("YourHandImage", out CardZoneView hand));
            Assert.AreEqual(5, hand.Widgets.Count);
            Assert.IsTrue(yakou.Bind("OpponentHandImage", out CardZoneView hidden));
            Assert.AreEqual(5, hidden.Widgets.Count);
            Assert.IsTrue(hidden.Widgets.TrueForAll(widget => widget.Hidden && widget.Card == null));
            var networkProperty = typeof(ClientApp).GetProperty("Network").GetSetMethod(true);
            networkProperty.Invoke(app, new object[] { b });
            _opponentBattle = Object.Instantiate(prefab);
            networkProperty.Invoke(app, new object[] { a });
            yield return null;
            var opponentUi = _opponentBattle.GetComponent<YakouBattlePresenter>();
            var opponentScreen = _opponentBattle.GetComponent<BattlePresenter>();
            Assert.IsNotNull(opponentUi);
            Assert.IsTrue(opponentUi.Bind("YourHandImage", out CardZoneView secondHand));
            Assert.IsTrue(opponentUi.Bind("OpponentHandImage", out CardZoneView secondHidden));
            Assert.AreEqual(5, secondHand.Widgets.Count);
            Assert.IsTrue(secondHidden.Widgets.TrueForAll(widget => widget.Hidden && widget.Card == null));
            screen.EndTurn.onClick.Invoke();
            ActionButton("턴 시작").onClick.Invoke();
            yield return Until(() => a.Yakou.Sequence == 1 && !a.Yakou.Busy);
            var choices = (JArray)a.Yakou.State["Players"][0]["SummonChoices"];
            JToken normal = null;
            foreach (var choice in choices)
            {
                if ((string)choice["Method"] == "Normal")
                {
                    normal = choice;
                    break;
                }
            }
            Assert.IsNotNull(normal);
            int card = (int)normal["CardId"];
            hand.Widgets.Find(widget => widget.Card.Id == card).OnPointerClick(null);
            ActionButton("소환: Normal").onClick.Invoke();
            ActionButton("필드 슬롯 1").onClick.Invoke();
            yield return Until(() => a.Yakou.Sequence == 2 && !a.Yakou.Busy);
            yield return Until(() => screen.Effects.ActiveSummons == 0);
            Assert.AreEqual(card, (int)a.Yakou.State["Players"][0]["FieldSlots"][0]["Id"]);
            Assert.IsTrue(yakou.Bind("OwnFieldImage", out CardZoneView field));
            Assert.AreEqual(card, field.Widgets[0].Card.Id);
            Assert.IsTrue(field.Widgets[0].Card.Yakou);
            Assert.AreEqual((int)a.Yakou.State["Players"][0]["FieldSlots"][0]["AttackCounter"],
                field.Widgets[0].Card.YakouCountdown);
            Assert.AreEqual(CardLibrary.LoadYakouResources().Find(field.Widgets[0].Card.DefinitionId).Name,
                CardPresentation.Name(field.Widgets[0].Card));
            a.DisconnectNetwork();
            Assert.IsFalse(a.Yakou.Bound);
            a.ReconnectNetwork();
            yield return Until(() => a.Yakou.Bound && a.Yakou.State != null);
            Assert.AreEqual(card, (int)a.Yakou.State["Players"][0]["FieldSlots"][0]["Id"]);
            Assert.AreEqual(0, ((JArray)a.Yakou.State["Players"][1]["Hand"]).Count);
            yield return Send(a, new JObject { ["Type"] = "BeginRaid" });
            yield return Until(() => b.Yakou.Sequence == a.Yakou.Sequence);
            yield return Send(b, new JObject { ["Type"] = "StartTurn" });
            var otherNormal = ((JArray)b.Yakou.State["Players"][1]["SummonChoices"])
                .First(choice => (string)choice["Method"] == "Normal");
            int blocker = (int)otherNormal["CardId"];
            Assert.IsTrue(opponentUi.Bind("YourHandImage", out CardZoneView opponentHand));
            opponentHand.Widgets.Find(widget => widget.Card.Id == blocker).OnPointerClick(null);
            ActionButton("소환: Normal", _opponentBattle).onClick.Invoke();
            ActionButton("필드 슬롯 1", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (int?)b.Yakou.State["Players"][1]["FieldSlots"][0]?["Id"] == blocker);
            yield return Until(() => opponentScreen.Effects.ActiveSummons == 0);
            yield return Send(b, new JObject { ["Type"] = "BeginRaid" });
            yield return Until(() => a.Yakou.Sequence == b.Yakou.Sequence);
            for (int turns = 0; (string)a.Yakou.State["Phase"] != "AssignBlockers" && turns < 8; ++turns)
            {
                var active = (int)a.Yakou.State["ActivePlayerId"] == 1 ? a : b;
                var other = active == a ? b : a;
                yield return Send(active, new JObject { ["Type"] = "StartTurn" });
                yield return Send(active, new JObject { ["Type"] = "BeginRaid" });
                yield return Until(() => other.Yakou.Sequence == active.Yakou.Sequence);
            }
            Assert.AreEqual("AssignBlockers", (string)a.Yakou.State["Phase"]);
            Assert.IsTrue(opponentUi.Bind("OpponentFieldImage", out CardZoneView opponentEnemyField));
            Assert.IsTrue(opponentUi.Bind("OwnFieldImage", out CardZoneView opponentOwnField));
            opponentEnemyField.Widgets.Find(widget => widget.Card.Id == card).OnPointerClick(null);
            opponentOwnField.Widgets.Find(widget => widget.Card.Id == blocker).OnPointerClick(null);
            yield return null;
            Assert.IsTrue(opponentUi.GetComponentsInChildren<DefenseBezierGraphic>(true)
                .Any(line => line.gameObject.activeInHierarchy && !line.raycastTarget));
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("방어 배정 확정", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (string)b.Yakou.State["Phase"] == "RaidResponse");
            Assert.AreEqual(blocker, (int)b.Yakou.State["Attacks"][0]["BlockerId"]);
            int shield = (int)b.Yakou.State["Players"][1]["Hand"].First(item => (int)item["DefinitionId"] == 3016)["Id"];
            opponentHand.Widgets.Find(widget => widget.Card.Id == shield).OnPointerClick(null);
            ActionButton("피해 감소 응전", _opponentBattle).onClick.Invoke();
            opponentOwnField.Widgets.Find(widget => widget.Card.Id == blocker).OnPointerClick(null);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("응전 제출", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && ((JArray)b.Yakou.State["ResponseStack"]).Count == 1);
            yield return Until(() => a.Yakou.Sequence == b.Yakou.Sequence);
            a.DisconnectNetwork();
            a.ReconnectNetwork();
            yield return Until(() => a.Yakou.Bound && a.Yakou.State != null &&
                (int)a.Yakou.State["PriorityPlayerId"] == 1 && ((JArray)a.Yakou.State["ResponseStack"]).Count == 1);
            long beforePass = a.Yakou.Sequence;
            screen.EndTurn.onClick.Invoke();
            ActionButton("습격 응전 패스").onClick.Invoke();
            yield return Until(() => !a.Yakou.Busy && a.Yakou.Sequence == beforePass + 1);
            yield return Until(() => b.Yakou.Sequence == a.Yakou.Sequence);
            int tactic = (int)b.Yakou.State["Players"][1]["Hand"].First(item => (int)item["DefinitionId"] == 2011)["Id"];
            int cost = (int)b.Yakou.State["Players"][1]["Hand"].First(item => (int)item["DefinitionId"] == 3018)["Id"];
            opponentHand.Widgets.Find(widget => widget.Card.Id == tactic).OnPointerClick(null);
            ActionButton("습격 전술 응전", _opponentBattle).onClick.Invoke();
            opponentEnemyField.Widgets.Find(widget => widget.Card.Id == card).OnPointerClick(null);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("응전 비용 선택 0/1", _opponentBattle).onClick.Invoke();
            opponentHand.Widgets.Find(widget => widget.Card.Id == cost).OnPointerClick(null);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("응전 제출", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && ((JArray)b.Yakou.State["ResponseStack"]).Count == 2);
            StringAssert.Contains(CardLibrary.LoadYakouResources().Find(2011).Name, opponentScreen.PhaseLabel.text);
            Assert.IsTrue(opponentUi.GetComponentInChildren<BattleLogView>(true).Entries
                .Any(entry => entry.Text(2).Contains("응전 추가")));
            yield return Until(() => a.Yakou.Sequence == b.Yakou.Sequence);
            beforePass = a.Yakou.Sequence;
            screen.EndTurn.onClick.Invoke();
            ActionButton("습격 응전 패스").onClick.Invoke();
            yield return Until(() => !a.Yakou.Busy && a.Yakou.Sequence == beforePass + 1);
            yield return Until(() => b.Yakou.Sequence == a.Yakou.Sequence);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("습격 응전 패스", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (int)b.Yakou.State["PriorityPlayerId"] == 0);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("습격 해결", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (string)b.Yakou.State["Phase"] == "TurnStart");
            Assert.AreEqual("TurnStart", (string)b.Yakou.State["Phase"]);
            Assert.AreEqual(2, (int)b.Yakou.State["ActivePlayerId"]);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("턴 시작", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (string)b.Yakou.State["Phase"] == "Main");
            var secondCards = (JArray)b.Yakou.State["Players"][1]["Hand"];
            int map = (int)secondCards.First(item => (int)item["DefinitionId"] == 3012)["Id"];
            int discard = (int)secondCards.First(item => (int)item["DefinitionId"] != 3012 &&
                (int)item["DefinitionId"] != 2002)["Id"];
            opponentHand.Widgets.Find(widget => widget.Card.Id == map).OnPointerClick(null);
            ActionButton("주문 비용 선택", _opponentBattle).onClick.Invoke();
            opponentHand.Widgets.Find(widget => widget.Card.Id == discard).OnPointerClick(null);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("선택 확정", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (string)b.Yakou.State["PendingSelection"]?["Operation"] == "LookAndChoose");
            Assert.AreEqual(3, ((JArray)b.Yakou.State["PendingSelection"]["CandidateCards"]).Count);
            yield return Until(() => a.Yakou.Sequence == b.Yakou.Sequence);
            Assert.IsNull(a.Yakou.State["PendingSelection"]);
            b.DisconnectNetwork();
            b.ReconnectNetwork();
            yield return Until(() => b.Yakou.Bound && (string)b.Yakou.State?["PendingSelection"]?["Operation"] == "LookAndChoose");
            var revealed = b.Yakou.State["PendingSelection"]["CandidateCards"][0];
            string revealedName = CardLibrary.LoadYakouResources().Find((int)revealed["DefinitionId"]).Name;
            int revealedId = (int)revealed["Id"];
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("선택: " + revealedName, _opponentBattle).onClick.Invoke();
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("선택한 효과 대상 확정", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && b.Yakou.State["PendingSelection"] == null);
            Assert.IsTrue(((JArray)b.Yakou.State["Players"][1]["Hand"]).Any(item => (int)item["Id"] == revealedId));
            int necromancer = (int)b.Yakou.State["Players"][1]["Hand"].First(item => (int)item["DefinitionId"] == 2002)["Id"];
            opponentHand.Widgets.Find(widget => widget.Card.Id == necromancer).OnPointerClick(null);
            ActionButton("소환: Necromancy", _opponentBattle).onClick.Invoke();
            var summon = ((JArray)b.Yakou.State["Players"][1]["SummonChoices"])
                .First(item => (int)item["CardId"] == necromancer && (string)item["Method"] == "Necromancy");
            int material = (int)summon["Candidates"][0];
            Assert.IsTrue(opponentUi.Bind("YourGraveyardImage", out CardZoneView opponentGrave));
            opponentGrave.Widgets.Find(widget => widget.Card.Id == material).OnPointerClick(null);
            b.DisconnectNetwork();
            b.ReconnectNetwork();
            yield return Until(() => b.Yakou.Bound && b.Yakou.State != null);
            opponentHand.Widgets.Find(widget => widget.Card.Id == necromancer).OnPointerClick(null);
            ActionButton("소환: Necromancy", _opponentBattle).onClick.Invoke();
            opponentGrave.Widgets.Find(widget => widget.Card.Id == material).OnPointerClick(null);
            int slot = (int)summon["AvailableSlots"][0];
            ActionButton("필드 슬롯 " + (slot + 1), _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && b.Yakou.State["PendingSelection"] != null);
            int target = (int)b.Yakou.State["PendingSelection"]["Candidates"][0];
            opponentGrave.Widgets.Find(widget => widget.Card.Id == target).OnPointerClick(null);
            b.DisconnectNetwork();
            b.ReconnectNetwork();
            yield return Until(() => b.Yakou.Bound && b.Yakou.State?["PendingSelection"] != null);
            Assert.IsNull(a.Yakou.State["PendingSelection"]);
            opponentGrave.Widgets.Find(widget => widget.Card.Id == target).OnPointerClick(null);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("선택한 효과 대상 확정", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && b.Yakou.State["PendingSelection"] == null);
            Assert.IsTrue(((JArray)b.Yakou.State["Players"][1]["Hand"]).Any(item => (int)item["Id"] == target));
            b.Yakou.Leave();
            yield return Until(() => a.Yakou.MatchId == null && b.Yakou.MatchId == null);
        }

        private Button ActionButton(string text, GameObject root = null)
        {
            foreach (var button in (root != null ? root : _battle).GetComponentsInChildren<Button>(true))
            {
                var label = button.GetComponentInChildren<TMP_Text>();
                if (button.gameObject.activeInHierarchy && label != null && label.text == text)
                {
                    return button;
                }
            }
            Assert.Fail("Missing battle action: " + text);
            return null;
        }

        private static IEnumerator Send(MultiplayerDebugScreen client, JObject command)
        {
            long sequence = client.Yakou.Sequence;
            client.Yakou.Command(command);
            yield return Until(() => !client.Yakou.Busy);
            Assert.AreEqual(sequence + 1, client.Yakou.Sequence, client.Yakou.Error);
        }

        private static IEnumerator Until(Func<bool> condition, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "Yakou network condition timed out at line " + line);
                yield return null;
            }
        }
    }
}
#endif
