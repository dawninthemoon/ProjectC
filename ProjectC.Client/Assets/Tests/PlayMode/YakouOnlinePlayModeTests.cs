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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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

        [Test]
        public void AllYakouCardSpritesAreImportedAndGameCoreSpritesRemainAvailable()
        {
            var imageObject = new GameObject("Card art resource check", typeof(Image));
            try
            {
                var artwork = imageObject.GetComponent<Image>();
                var definitions = CardLibrary.LoadYakouResources().Cards.ToArray();
                Assert.AreEqual(55, definitions.Length);
                foreach (var definition in definitions)
                {
                    var sprite = Resources.Load<Sprite>("YakouCardSprite/CardSprite_" + definition.Id);
                    Assert.IsNotNull(sprite, "Yakou sprite " + definition.Id);
                    CardArtProvider.ApplyYakou(artwork, definition.Id);
                    Assert.AreSame(sprite, artwork.sprite, "Yakou art path " + definition.Id);
                }
                var gameCore = Resources.Load<Sprite>("CardSprite/CardSprite_1001");
                Assert.IsNotNull(gameCore);
                CardArtProvider.Apply(artwork, 1001);
                Assert.AreSame(gameCore, artwork.sprite);
                var cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleCard.prefab");
                Assert.IsNotNull(cardPrefab);
                var gameCoreObject = Object.Instantiate(cardPrefab);
                try
                {
                    var widget = gameCoreObject.GetComponent<CardWidget>();
                    Assert.IsNotNull(widget);
                    widget.Bind(new CardView { Id = 1, DefinitionId = 1001, Attack = 2, Hp = 3, MaxHp = 3 },
                        false, false, false, true);
                    Assert.AreSame(gameCore, widget.ArtworkImage.sprite);
                }
                finally
                {
                    Object.DestroyImmediate(gameCoreObject);
                }
            }
            finally
            {
                Object.DestroyImmediate(imageObject);
            }
        }

        [UnityTest]
        public IEnumerator ExistingGameCoreArtworkRendersInBattleScene()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity",
                new LoadSceneParameters(LoadSceneMode.Additive));
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            Assert.IsTrue(scene.IsValid());
            var screen = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BattlePresenter>(true)).FirstOrDefault();
            Assert.IsNotNull(screen);
            Assert.IsNull(screen.GetComponent<YakouBattlePresenter>());
            var client = screen.Client;
            client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"art-regression\",\"PlayerId\":1,\"GameNumber\":1}");
            var card = new CardView { Id = 1, DefinitionId = 1001, Attack = 2, Hp = 3, MaxHp = 3 };
            client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "art-regression", GameNumber = 1,
                SequenceNumber = 1,
                Payload = new StateView
                {
                    Started = true, TurnStarted = true, ActivePlayerId = 1, TurnNumber = 1,
                    FieldLimit = 5, Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 20, HandCount = 1, Hand = new[] { card },
                            Field = new[] { new CardView { Id = 2, DefinitionId = 1001, Attack = 2, Hp = 3, MaxHp = 3 } },
                            Graveyard = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 20, HandCount = 1,
                            Hand = Array.Empty<CardView>(), Field = Array.Empty<CardView>(),
                            Graveyard = Array.Empty<CardView>() },
                    },
                },
            }));
            yield return null;
            yield return null;
            var gameCoreSprite = Resources.Load<Sprite>("CardSprite/CardSprite_1001");
            Assert.AreSame(gameCoreSprite, screen.Hand.Widgets[0].ArtworkImage.sprite);
            Assert.AreSame(gameCoreSprite, screen.OwnField.Widgets[0].ArtworkImage.sprite);
            Assert.IsTrue(screen.EnemyHand.Widgets[0].Hidden);
            screen.Detail.Show(card);
            Assert.IsTrue(screen.Detail.Bind("ArtworkImage", out AspectFillImage detailArt));
            Assert.AreSame(gameCoreSprite, detailArt.sprite);
            yield return Capture("07-gamecore-authored-battle-scene");
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [Test]
        public void YakouStatusAndActionsUseTheSelectedLanguage()
        {
            var localization = CardPresentation.Library.Localization;
            string original = localization.Language;
            try
            {
                foreach (string language in new[] { "korean", "english", "japanese" })
                {
                    localization.Language = language;
                    var yakou = CardPresentation.YakouLibrary.Localization;
                    string phase = YakouUiText.Phase("RaidResponse");
                    string method = YakouUiText.Method("Normal");
                    string button = YakouUiText.Get("ui.yakou.action.summon", method);
                    string status = YakouUiText.Get("ui.yakou.hud.status", phase,
                        YakouUiText.Player(1, 1), YakouUiText.Player(2, 1), 2, 3);
                    string rejection = YakouUiText.Error("DuplicateRequestId");
                    Assert.AreEqual(yakou.GetText("ui.yakou.phase.raid_response"), phase);
                    StringAssert.Contains(method, button);
                    StringAssert.Contains(phase, status);
                    Assert.AreNotEqual("DuplicateRequestId", rejection);
                    Assert.AreNotEqual("소환: Normal", button);
                    Assert.IsFalse(status.Contains("RaidResponse"));
                    Assert.IsFalse(status.Contains("PlayerId"));
                    Assert.IsFalse(rejection.Contains("DuplicateRequestId"));
                }
            }
            finally
            {
                localization.Language = original;
            }
        }

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
                _server = null;
            }
            if (_fixtureDirectory != null && Directory.Exists(_fixtureDirectory))
            {
                Directory.Delete(_fixtureDirectory, true);
            }
        }

        [UnityTest]
        public IEnumerator DifferentAlphaDecksRenderOnlyTheirOwnCards()
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
            string data = Path.GetFullPath(Path.Combine(Application.dataPath, "../../ProjectC.Server/Data"));
            _server = Process.Start(new ProcessStartInfo(executable,
                "--port " + port + " --yakou-prototype on --data \"" + data + "\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            var startup = _server.StandardOutput.ReadLineAsync();
            yield return Until(() => startup.IsCompleted);
            StringAssert.Contains("listening", startup.Result);
            _first = new GameObject("Alpha raid client", typeof(MultiplayerDebugScreen));
            var app = _first.AddComponent<ClientApp>();
            typeof(ClientApp).GetProperty("Environment").GetSetMethod(true)
                .Invoke(app, new object[] { ServerEnvironment.Local });
            app.enabled = false;
            _second = new GameObject("Alpha guard client", typeof(MultiplayerDebugScreen));
            var a = _first.GetComponent<MultiplayerDebugScreen>();
            var b = _second.GetComponent<MultiplayerDebugScreen>();
            string address = "ws://127.0.0.1:" + port;
            a.ConnectTo(address);
            b.ConnectTo(address);
            yield return Until(() => a.ConnectionReady && b.ConnectionReady);
            a.Yakou.Create("YakouAlphaRaid");
            yield return Until(() => a.Yakou.State != null);
            b.Yakou.Join(a.Yakou.MatchId, "YakouAlphaGuard");
            yield return Until(() => b.Yakou.State != null);
            Assert.AreEqual("YakouAlphaRaid", a.Yakou.SelectedDeckId);
            Assert.AreEqual("YakouAlphaGuard", b.Yakou.SelectedDeckId);
            Assert.AreEqual(5, ((JArray)a.Yakou.State["Players"][0]["Hand"]).Count);
            Assert.AreEqual(0, ((JArray)a.Yakou.State["Players"][1]["Hand"]).Count);
            Assert.AreEqual(0, ((JArray)b.Yakou.State["Players"][0]["Hand"]).Count);
            Assert.AreEqual(5, ((JArray)b.Yakou.State["Players"][1]["Hand"]).Count);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleScreen.prefab");
            Assert.IsNotNull(prefab);
            _battle = Object.Instantiate(prefab);
            yield return null;
            Assert.IsTrue(_battle.GetComponent<YakouBattlePresenter>().Bind("YourHandImage", out CardZoneView raidHand));
            Assert.IsTrue(_battle.GetComponent<YakouBattlePresenter>().Bind("OpponentHandImage", out CardZoneView raidHidden));
            Assert.IsTrue(raidHand.Widgets.All(widget => widget.ArtworkImage.sprite ==
                Resources.Load<Sprite>("YakouCardSprite/CardSprite_" + widget.Card.DefinitionId)));
            Assert.IsTrue(raidHidden.Widgets.All(widget => widget.Hidden && widget.Card == null));
            raidHand.Widgets[0].OnPointerClick(null);
            yield return Capture("08-alpha-raid-hand-inspector");
            typeof(ClientApp).GetProperty("Network").GetSetMethod(true).Invoke(app, new object[] { b });
            _battle.SetActive(false);
            _opponentBattle = Object.Instantiate(prefab);
            yield return null;
            Assert.IsTrue(_opponentBattle.GetComponent<YakouBattlePresenter>().Bind("YourHandImage", out CardZoneView guardHand));
            Assert.IsTrue(_opponentBattle.GetComponent<YakouBattlePresenter>().Bind("OpponentHandImage", out CardZoneView guardHidden));
            Assert.IsTrue(guardHand.Widgets.All(widget => widget.ArtworkImage.sprite ==
                Resources.Load<Sprite>("YakouCardSprite/CardSprite_" + widget.Card.DefinitionId)));
            Assert.IsTrue(guardHidden.Widgets.All(widget => widget.Hidden && widget.Card == null));
            guardHand.Widgets[0].OnPointerClick(null);
            yield return Capture("09-alpha-guard-hand-inspector");
        }

        [UnityTest]
        public IEnumerator AlphaMultiTargetSpellUsesBattleScreenClicks()
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
            _fixtureDirectory = Path.Combine(Application.temporaryCachePath,
                "YakouAlphaChoices-" + Guid.NewGuid().ToString("N"));
            foreach (string source in Directory.GetFiles(sourceData, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(_fixtureDirectory, source.Substring(sourceData.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(source, destination);
            }
            string deckPath = Path.Combine(_fixtureDirectory, "YakouPrototype", "Decks.json");
            var decks = JObject.Parse(File.ReadAllText(deckPath));
            var raid = ((JArray)decks["Decks"]).First(deck => (string)deck["Id"] == "YakouAlphaRaid");
            var ordered = ((JArray)raid["Cards"]).Values<int>().ToList();
            int[] opening = { 4008, 4012, 4009, 4013, 3007 };
            foreach (int id in opening)
            {
                Assert.IsTrue(ordered.Remove(id));
            }
            ordered.InsertRange(0, opening);
            raid["Cards"] = new JArray(ordered);
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
            _first = new GameObject("Alpha choice client", typeof(MultiplayerDebugScreen));
            var app = _first.AddComponent<ClientApp>();
            typeof(ClientApp).GetProperty("Environment").GetSetMethod(true)
                .Invoke(app, new object[] { ServerEnvironment.Local });
            app.enabled = false;
            _second = new GameObject("Alpha choice opponent", typeof(MultiplayerDebugScreen));
            var a = _first.GetComponent<MultiplayerDebugScreen>();
            var b = _second.GetComponent<MultiplayerDebugScreen>();
            string address = "ws://127.0.0.1:" + port;
            a.ConnectTo(address);
            b.ConnectTo(address);
            yield return Until(() => a.ConnectionReady && b.ConnectionReady);
            a.Yakou.Create("YakouAlphaRaid");
            yield return Until(() => a.Yakou.State != null);
            b.Yakou.Join(a.Yakou.MatchId, "YakouAlphaGuard");
            yield return Until(() => b.Yakou.State != null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleScreen.prefab");
            _battle = Object.Instantiate(prefab);
            yield return null;
            var presenter = _battle.GetComponent<YakouBattlePresenter>();
            Assert.IsTrue(presenter.Bind("YourHandImage", out CardZoneView hand));
            Assert.IsTrue(presenter.Bind("OwnFieldImage", out CardZoneView field));
            yield return Send(a, new JObject { ["Type"] = "StartTurn" });
            int first = (int)a.Yakou.State["Players"][0]["Hand"][0]["Id"];
            int spell = (int)a.Yakou.State["Players"][0]["Hand"][1]["Id"];
            int second = (int)a.Yakou.State["Players"][0]["Hand"][2]["Id"];
            int cost = (int)a.Yakou.State["Players"][0]["Hand"][3]["Id"];
            yield return Send(a, new JObject { ["Type"] = "Summon", ["CardId"] = first,
                ["Method"] = "Normal", ["Materials"] = new JArray(), ["Slot"] = 0 });
            yield return Send(a, new JObject { ["Type"] = "BeginRaid" });
            yield return Send(b, new JObject { ["Type"] = "StartTurn" });
            yield return Send(b, new JObject { ["Type"] = "BeginRaid" });
            yield return Send(a, new JObject { ["Type"] = "StartTurn" });
            yield return Send(a, new JObject { ["Type"] = "Summon", ["CardId"] = second,
                ["Method"] = "Normal", ["Materials"] = new JArray(), ["Slot"] = 1 });
            yield return null;
            hand.Widgets.Find(widget => widget.Card.Id == spell).OnPointerClick(null);
            ActionButton(YakouUiText.Get("ui.yakou.action.paid_spell_cost")).onClick.Invoke();
            hand.Widgets.Find(widget => widget.Card.Id == cost).OnPointerClick(null);
            _battle.GetComponent<BattlePresenter>().EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui_battle_confirm_selection")).onClick.Invoke();
            yield return Until(() => !a.Yakou.Busy && a.Yakou.State?["PendingSelection"] != null);
            Assert.AreEqual(2, (int)a.Yakou.State["PendingSelection"]["MaxCount"]);
            Assert.IsNull(b.Yakou.State["PendingSelection"]);
            yield return Until(() => field.Widgets.Count == 2 &&
                field.Widgets.All(widget => !widget.IsInFlight));
            field.Widgets.Find(widget => widget.Card.Id == first).OnPointerClick(null);
            field.Widgets.Find(widget => widget.Card.Id == second).OnPointerClick(null);
            var selectedTargets = (System.Collections.Generic.HashSet<int>)typeof(YakouBattlePresenter)
                .GetField("_pendingTargets", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).GetValue(presenter);
            Assert.AreEqual(2, selectedTargets.Count,
                "Both friendly field cards must be selected before submitting the effect");
            yield return Capture("10-alpha-two-target-selection");
            _battle.GetComponent<BattlePresenter>().EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui_battle_confirm_selection")).onClick.Invoke();
            yield return Until(() => !a.Yakou.Busy && a.Yakou.State?["PendingSelection"] == null);
            Assert.AreEqual(0, (int)a.Yakou.State["Players"][0]["FieldSlots"][0]["AttackCounter"]);
            Assert.AreEqual(0, (int)a.Yakou.State["Players"][0]["FieldSlots"][1]["AttackCounter"]);
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
            Assert.IsTrue(hand.Widgets.All(widget => widget.ArtworkImage.sprite ==
                Resources.Load<Sprite>("YakouCardSprite/CardSprite_" + widget.Card.DefinitionId)));
            Assert.IsTrue(yakou.Bind("OpponentHandImage", out CardZoneView hidden));
            Assert.AreEqual(5, hidden.Widgets.Count);
            Assert.IsTrue(hidden.Widgets.TrueForAll(widget => widget.Hidden && widget.Card == null));
            Assert.IsTrue(hidden.Widgets.TrueForAll(widget => !widget.ArtworkImage.gameObject.activeSelf));
            yield return Capture("01-private-hands");
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
            ActionButton(YakouUiText.Get("ui.yakou.action.start_turn")).onClick.Invoke();
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
            var selectedCard = hand.Widgets.Find(widget => widget.Card.Id == card);
            selectedCard.OnPointerClick(null);
            Assert.IsTrue(yakou.Bind("CardInspectorImage", out CardDetailView detail));
            Assert.IsTrue(detail.Bind("ArtworkImage", out AspectFillImage detailArtwork));
            Assert.AreSame(Resources.Load<Sprite>("YakouCardSprite/CardSprite_" + selectedCard.Card.DefinitionId),
                detailArtwork.sprite);
            ActionButton(YakouUiText.Get("ui.yakou.action.summon", YakouUiText.Method("Normal"))).onClick.Invoke();
            ActionButton(YakouUiText.Get("ui.yakou.action.field_slot", 1)).onClick.Invoke();
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
            Assert.AreSame(Resources.Load<Sprite>("YakouCardSprite/CardSprite_" + field.Widgets[0].Card.DefinitionId),
                field.Widgets[0].ArtworkImage.sprite);
            Assert.IsTrue(field.Widgets[0].Title.gameObject.activeInHierarchy);
            Assert.IsTrue(yakou.Bind("RejectionReasonText", out TMP_Text battleStatus));
            Assert.AreEqual(YakouUiText.Phase("Main"), screen.PhaseLabel.text);
            Assert.AreEqual(((int)a.Yakou.State["TurnNumber"]).ToString(), screen.TurnLabel.text);
            Assert.IsTrue(yakou.Bind("TimerText", out TMP_Text turnBadge));
            Assert.AreEqual(screen.TurnLabel.text, turnBadge.text);
            Assert.IsTrue(yakou.Bind("TurnOwnerText", out TMP_Text turnOwner));
            Assert.AreEqual(BattleText.Get("ui.turn.yours"), turnOwner.text);
            Assert.IsFalse(battleStatus.text.Contains("PlayerId"));
            Assert.IsFalse(battleStatus.text.Contains("PriorityPlayerId"));
            _opponentBattle.SetActive(false);
            yield return Capture("02-summoned-field");
            _opponentBattle.SetActive(true);
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
            ActionButton(YakouUiText.Get("ui.yakou.action.summon", YakouUiText.Method("Normal")), _opponentBattle).onClick.Invoke();
            ActionButton(YakouUiText.Get("ui.yakou.action.field_slot", 1), _opponentBattle).onClick.Invoke();
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
            ActionButton(YakouUiText.Get("ui.yakou.action.confirm_blockers"), _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (string)b.Yakou.State["Phase"] == "RaidResponse");
            Assert.AreEqual(blocker, (int)b.Yakou.State["Attacks"][0]["BlockerId"]);
            int shield = (int)b.Yakou.State["Players"][1]["Hand"].First(item => (int)item["DefinitionId"] == 3016)["Id"];
            opponentHand.Widgets.Find(widget => widget.Card.Id == shield).OnPointerClick(null);
            ActionButton(YakouUiText.Get("ui.yakou.action.response_shield"), _opponentBattle).onClick.Invoke();
            opponentOwnField.Widgets.Find(widget => widget.Card.Id == blocker).OnPointerClick(null);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui.yakou.action.play_response"), _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && ((JArray)b.Yakou.State["ResponseStack"]).Count == 1);
            yield return Until(() => a.Yakou.Sequence == b.Yakou.Sequence);
            a.DisconnectNetwork();
            a.ReconnectNetwork();
            yield return Until(() => a.Yakou.Bound && a.Yakou.State != null &&
                (int)a.Yakou.State["PriorityPlayerId"] == 1 && ((JArray)a.Yakou.State["ResponseStack"]).Count == 1);
            var locale = CardPresentation.Library.Localization;
            locale.Language = "english";
            yakou.SendMessage("Refresh");
            Assert.AreEqual(YakouUiText.Phase("RaidResponse"), screen.PhaseLabel.text);
            Assert.AreEqual(BattleText.Get("ui.turn.yours"), turnOwner.text);
            StringAssert.Contains(YakouUiText.Get("ui.yakou.hud.status", YakouUiText.Phase("RaidResponse"),
                YakouUiText.Player((int)a.Yakou.State["ActivePlayerId"], a.Yakou.Seat),
                YakouUiText.Player((int)a.Yakou.State["PriorityPlayerId"], a.Yakou.Seat),
                (int)a.Yakou.State["Players"][0]["Ki"], (int)a.Yakou.State["Players"][1]["Ki"]), battleStatus.text);
            _opponentBattle.SetActive(false);
            yield return Capture("04-response-english");
            _opponentBattle.SetActive(true);
            locale.Language = "korean";
            yakou.SendMessage("Refresh");
            long beforePass = a.Yakou.Sequence;
            screen.EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui.yakou.action.pass_raid_response")).onClick.Invoke();
            yield return Until(() => !a.Yakou.Busy && a.Yakou.Sequence == beforePass + 1);
            yield return Until(() => b.Yakou.Sequence == a.Yakou.Sequence);
            int tactic = (int)b.Yakou.State["Players"][1]["Hand"].First(item => (int)item["DefinitionId"] == 2011)["Id"];
            int cost = (int)b.Yakou.State["Players"][1]["Hand"].First(item => (int)item["DefinitionId"] == 3018)["Id"];
            opponentHand.Widgets.Find(widget => widget.Card.Id == tactic).OnPointerClick(null);
            ActionButton(YakouUiText.Get("ui.yakou.action.response_tactic"), _opponentBattle).onClick.Invoke();
            opponentEnemyField.Widgets.Find(widget => widget.Card.Id == card).OnPointerClick(null);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui.yakou.action.response_cost", 0, 1), _opponentBattle).onClick.Invoke();
            opponentHand.Widgets.Find(widget => widget.Card.Id == cost).OnPointerClick(null);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui.yakou.action.play_response"), _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && ((JArray)b.Yakou.State["ResponseStack"]).Count == 2);
            Assert.IsTrue(opponentUi.Bind("RejectionReasonText", out TMP_Text responseStatus));
            StringAssert.Contains(CardLibrary.LoadYakouResources().Find(2011).Name, responseStatus.text);
            _battle.SetActive(false);
            yield return Capture("03-response-priority");
            _battle.SetActive(true);
            var battleLog = opponentUi.GetComponentInChildren<BattleLogView>(true);
            Assert.IsTrue(battleLog.Entries.Any(entry => entry.Text(2).Contains("응전 추가")));
            opponentScreen.LogToggle.onClick.Invoke();
            Assert.IsTrue(battleLog.IsOpen);
            opponentScreen.LogToggle.onClick.Invoke();
            Assert.IsFalse(battleLog.IsOpen);
            yield return Until(() => a.Yakou.Sequence == b.Yakou.Sequence);
            beforePass = a.Yakou.Sequence;
            screen.EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui.yakou.action.pass_raid_response")).onClick.Invoke();
            yield return Until(() => !a.Yakou.Busy && a.Yakou.Sequence == beforePass + 1);
            yield return Until(() => b.Yakou.Sequence == a.Yakou.Sequence);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui.yakou.action.pass_raid_response"), _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (int)b.Yakou.State["PriorityPlayerId"] == 0);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui.yakou.action.resolve_raid"), _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (string)b.Yakou.State["Phase"] == "TurnStart");
            Assert.AreEqual("TurnStart", (string)b.Yakou.State["Phase"]);
            Assert.AreEqual(2, (int)b.Yakou.State["ActivePlayerId"]);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui.yakou.action.start_turn"), _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (string)b.Yakou.State["Phase"] == "Main");
            var secondCards = (JArray)b.Yakou.State["Players"][1]["Hand"];
            int map = (int)secondCards.First(item => (int)item["DefinitionId"] == 3012)["Id"];
            int discard = (int)secondCards.First(item => (int)item["DefinitionId"] != 3012 &&
                (int)item["DefinitionId"] != 2002)["Id"];
            opponentHand.Widgets.Find(widget => widget.Card.Id == map).OnPointerClick(null);
            ActionButton(YakouUiText.Get("ui.yakou.action.paid_spell_cost"), _opponentBattle).onClick.Invoke();
            opponentHand.Widgets.Find(widget => widget.Card.Id == discard).OnPointerClick(null);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui_battle_confirm_selection"), _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && (string)b.Yakou.State["PendingSelection"]?["Operation"] == "LookAndChoose");
            Assert.AreEqual(3, ((JArray)b.Yakou.State["PendingSelection"]["CandidateCards"]).Count);
            yield return Until(() => a.Yakou.Sequence == b.Yakou.Sequence);
            Assert.IsNull(a.Yakou.State["PendingSelection"]);
            b.DisconnectNetwork();
            b.ReconnectNetwork();
            yield return Until(() => b.Yakou.Bound && (string)b.Yakou.State?["PendingSelection"]?["Operation"] == "LookAndChoose");
            locale.Language = "japanese";
            opponentUi.SendMessage("Refresh");
            Assert.AreEqual(YakouUiText.Phase("Main"), opponentScreen.PhaseLabel.text);
            Assert.IsTrue(opponentUi.Bind("RejectionReasonText", out TMP_Text selectionStatus));
            StringAssert.Contains(YakouUiText.Get("ui_battle_select_target"), selectionStatus.text);
            _battle.SetActive(false);
            yield return Capture("05-selection-japanese");
            _battle.SetActive(true);
            locale.Language = "korean";
            opponentUi.SendMessage("Refresh");
            var revealed = b.Yakou.State["PendingSelection"]["CandidateCards"][0];
            string revealedName = CardLibrary.LoadYakouResources().Find((int)revealed["DefinitionId"]).Name;
            int revealedId = (int)revealed["Id"];
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui.yakou.action.choose_candidate", revealedName), _opponentBattle).onClick.Invoke();
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui_battle_confirm_selection"), _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && b.Yakou.State["PendingSelection"] == null);
            Assert.IsTrue(((JArray)b.Yakou.State["Players"][1]["Hand"]).Any(item => (int)item["Id"] == revealedId));
            int necromancer = (int)b.Yakou.State["Players"][1]["Hand"].First(item => (int)item["DefinitionId"] == 2002)["Id"];
            opponentHand.Widgets.Find(widget => widget.Card.Id == necromancer).OnPointerClick(null);
            ActionButton(YakouUiText.Get("ui.yakou.action.summon", YakouUiText.Method("Necromancy")), _opponentBattle).onClick.Invoke();
            var summon = ((JArray)b.Yakou.State["Players"][1]["SummonChoices"])
                .First(item => (int)item["CardId"] == necromancer && (string)item["Method"] == "Necromancy");
            int material = (int)summon["Candidates"][0];
            Assert.IsTrue(opponentUi.Bind("YourGraveyardImage", out CardZoneView opponentGrave));
            opponentGrave.Widgets.Find(widget => widget.Card.Id == material).OnPointerClick(null);
            b.DisconnectNetwork();
            b.ReconnectNetwork();
            yield return Until(() => b.Yakou.Bound && b.Yakou.State != null);
            opponentHand.Widgets.Find(widget => widget.Card.Id == necromancer).OnPointerClick(null);
            ActionButton(YakouUiText.Get("ui.yakou.action.summon", YakouUiText.Method("Necromancy")), _opponentBattle).onClick.Invoke();
            opponentGrave.Widgets.Find(widget => widget.Card.Id == material).OnPointerClick(null);
            int slot = (int)summon["AvailableSlots"][0];
            ActionButton(YakouUiText.Get("ui.yakou.action.field_slot", slot + 1), _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && b.Yakou.State["PendingSelection"] != null);
            int target = (int)b.Yakou.State["PendingSelection"]["Candidates"][0];
            opponentGrave.Widgets.Find(widget => widget.Card.Id == target).OnPointerClick(null);
            b.DisconnectNetwork();
            b.ReconnectNetwork();
            yield return Until(() => b.Yakou.Bound && b.Yakou.State?["PendingSelection"] != null);
            Assert.IsNull(a.Yakou.State["PendingSelection"]);
            opponentGrave.Widgets.Find(widget => widget.Card.Id == target).OnPointerClick(null);
            opponentScreen.EndTurn.onClick.Invoke();
            ActionButton(YakouUiText.Get("ui_battle_confirm_selection"), _opponentBattle).onClick.Invoke();
            yield return Until(() => !b.Yakou.Busy && b.Yakou.State["PendingSelection"] == null);
            Assert.IsTrue(((JArray)b.Yakou.State["Players"][1]["Hand"]).Any(item => (int)item["Id"] == target));
            _battle.SetActive(false);
            _opponentBattle.SetActive(false);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity",
                new LoadSceneParameters(LoadSceneMode.Additive));
            var battleScene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            Assert.IsTrue(battleScene.IsValid());
            var sceneBattle = battleScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BattlePresenter>(true)).FirstOrDefault();
            Assert.IsNotNull(sceneBattle);
            var sceneYakou = sceneBattle.GetComponent<YakouBattlePresenter>();
            Assert.IsNotNull(sceneYakou);
            Assert.IsFalse(sceneBattle.enabled);
            yield return null;
            Assert.IsTrue(sceneYakou.Bind("YourHandImage", out CardZoneView sceneHand));
            Assert.IsTrue(sceneHand.Widgets.All(widget => widget.ArtworkImage.sprite ==
                Resources.Load<Sprite>("YakouCardSprite/CardSprite_" + widget.Card.DefinitionId)));
            Assert.IsTrue(sceneYakou.Bind("OpponentHandImage", out CardZoneView sceneEnemyHand));
            Assert.IsTrue(sceneEnemyHand.Widgets.TrueForAll(widget => widget.Hidden && widget.Card == null));
            Assert.IsTrue(sceneYakou.Bind("OwnFieldImage", out CardZoneView sceneField));
            var sceneCard = sceneField.Widgets.FirstOrDefault();
            if (sceneCard != null)
            {
                sceneCard.OnPointerClick(null);
                Assert.IsTrue(sceneYakou.Bind("CardInspectorImage", out CardDetailView sceneDetail));
                Assert.IsTrue(sceneDetail.Bind("ArtworkImage", out AspectFillImage sceneDetailArt));
                Assert.AreSame(sceneCard.ArtworkImage.sprite, sceneDetailArt.sprite);
            }
            yield return Capture("06-authored-battle-scene");
            yield return SceneManager.UnloadSceneAsync(battleScene);
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

        private static IEnumerator Capture(string name)
        {
            string directory = Environment.GetEnvironmentVariable("PROJECTC_YAKOU_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory) || Application.isBatchMode)
            {
                yield break;
            }
            yield return new WaitForEndOfFrame();
            var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), screenshot.EncodeToPNG());
            Object.Destroy(screenshot);
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
