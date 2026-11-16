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
            _server = Process.Start(new ProcessStartInfo(executable, "--port " + port + " --yakou-prototype on")
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
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("방어 배정 확정", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (string)b.Yakou.State["Phase"] == "RaidResponse");
            Assert.AreEqual(blocker, (int)b.Yakou.State["Attacks"][0]["BlockerId"]);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("습격 응전 패스", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (int)b.Yakou.State["PriorityPlayerId"] == 1);
            yield return Until(() => a.Yakou.Sequence == b.Yakou.Sequence);
            long beforePass = a.Yakou.Sequence;
            screen.EndTurn.onClick.Invoke();
            ActionButton("습격 응전 패스").onClick.Invoke();
            yield return Until(() => !a.Yakou.Busy && a.Yakou.Sequence == beforePass + 1);
            yield return Until(() => b.Yakou.Sequence == a.Yakou.Sequence);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton("습격 해결", _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (string)b.Yakou.State["Phase"] == "TurnStart");
            Assert.AreEqual("TurnStart", (string)b.Yakou.State["Phase"]);
            Assert.AreEqual(2, (int)b.Yakou.State["ActivePlayerId"]);
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
