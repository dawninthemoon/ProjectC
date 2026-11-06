#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Game.Utils;
using NUnit.Framework;
using ProjectC.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ProjectC.Tests
{
    public sealed class GameplayInputPlayModeTests
    {
        private GameObject _root, _eventSystem, _opponent;
        private BattlePresenter _ui;
        private MultiplayerDebugScreen _client;
        private System.Diagnostics.Process _server;
        private string _data;

        [SetUp]
        public void Setup()
        {
            _root = _eventSystem = _opponent = null;
            _server = null;
            _data = null;
        }

        private static IEnumerator Until(Func<bool> ready)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!ready())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "Input integration timed out.");
                yield return null;
            }
        }

        private void CreateUi()
        {
            _root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleScreen.prefab"));
            _ui = _root.GetComponent<BattlePresenter>();
            _client = _ui.Client;
            if (EventSystem.current == null)
            {
                _eventSystem = new GameObject("Input test EventSystem", typeof(EventSystem));
            }
        }

        private static CardView Card(int id) => new CardView { Id = id, DefinitionId = 1001, Attack = 2, Hp = 2, MaxHp = 2 };

        private void Snapshot(bool resolving, bool finished = false, long sequence = 1)
        {
            _client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "input", GameNumber = 1, SequenceNumber = sequence,
                Payload = new StateView { Started = true, TurnStarted = true, ActivePlayerId = 1, FieldLimit = 5,
                    ResolvingPlayerId = resolving ? 1 : 0, Finished = finished, Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Hand = new[] { Card(1) }, Field = new[] { Card(8) }, Graveyard = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Hand = Array.Empty<CardView>(), Field = new[] { Card(9), Card(10) }, Graveyard = Array.Empty<CardView>() },
                    } },
            }));
        }

        private void Choice(bool field, string id = "choice")
        {
            _client.ApplyMessage(JsonUtility.ToJson(new ActionChoiceMessage
            {
                Type = "ActionChoiceRequired", ProtocolVersion = 2, MatchId = "input", GameNumber = 1, SequenceNumber = 1,
                Payload = new ActionChoiceView { ChoiceId = id, MinCount = 1, MaxCount = 1, CanCancel = true,
                    Candidates = new[] { new ActionChoiceCandidateView { Token = "candidate", Zone = field ? 2 : 0,
                        CardId = field ? 9 : 0, PlayerId = field ? 2 : 1, Card = Card(field ? 9 : 0) } } },
            }));
            _ui.Interaction.RefreshServerChoice();
        }

        private IEnumerator SyntheticUi()
        {
            CreateUi();
            _client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"input\",\"PlayerId\":1,\"GameNumber\":1}");
            Snapshot(false);
            yield return null;
            yield return null;
        }

        private static GameObject Hit(Component target)
        {
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)target.transform;
            var canvas = target.GetComponentInParent<Canvas>().rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(EventSystem.current) { position = position };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.IsNotEmpty(hits);
            return hits[0].gameObject;
        }

        private static void Click(Component target)
        {
            var hit = Hit(target);
            ExecuteEvents.ExecuteHierarchy(hit, new PointerEventData(EventSystem.current)
            { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        }

        [Test]
        public void ScopesIntersectAndDisposeOutOfOrderWithoutOpeningABypass()
        {
            var input = new GameplayInputCoordinator();
            Assert.IsTrue(input.CanExecute(GameplayInputAction.EndTurn));
            int changes = 0;
            input.Changed += () => ++changes;
            var first = input.PushScope("selection", new[] { GameplayInputAction.SelectDeckCard, GameplayInputAction.ConfirmSelection, GameplayInputAction.OpenCardDetails });
            var second = input.PushScope("details", new[] { GameplayInputAction.OpenCardDetails, GameplayInputAction.EndTurn });
            Assert.AreNotEqual(first.Id, second.Id);
            Assert.IsFalse(input.CanExecute(GameplayInputAction.EndTurn));
            Assert.IsFalse(input.CanExecute(GameplayInputAction.ConfirmSelection));
            first.Dispose();
            first.Dispose();
            Assert.IsTrue(input.CanExecute(GameplayInputAction.EndTurn));
            Assert.IsFalse(input.CanExecute(GameplayInputAction.PlayCard));
            second.Dispose();
            Assert.IsTrue(input.CanExecute(GameplayInputAction.PlayCard));
            Assert.AreEqual(4, changes);
        }

        [UnityTest]
        public IEnumerator DeckPickerBlocksActualRaycastsAndFieldPickerAdmitsOnlyItsCandidate()
        {
            yield return SyntheticUi();
            Choice(false);
            yield return null;
            Assert.IsFalse(_client.Input.CanExecute(GameplayInputAction.EndTurn));
            Assert.IsFalse(_client.Input.CanExecute(GameplayInputAction.PlayCard));
            Assert.IsFalse(_client.Input.CanExecute(GameplayInputAction.Attack));
            Assert.AreSame(_ui.Modal.gameObject, Hit(_ui.EndTurn));
            var gallery = _ui.GetComponentInChildren<CardGalleryView>(true);
            var galleryCard = gallery.GetComponentsInChildren<CardWidget>().First();
            Assert.IsNotNull(Hit(galleryCard).GetComponentInParent<CardWidget>());
            Click(galleryCard);
            var picker = _ui.GetComponentInChildren<EffectTargetPicker>(true);
            Assert.IsTrue(picker.transform.Bind("ConfirmButton", out Button confirm));
            Assert.IsTrue(confirm.interactable);
            Assert.IsTrue(_client.Input.CanExecute(GameplayInputAction.ConfirmSelection));
            _client.ApplyMessage(JsonUtility.ToJson(new ActionChoiceMessage
            {
                Type = "ActionChoiceRequired", ProtocolVersion = 2, MatchId = "input", GameNumber = 1, SequenceNumber = 1,
                Payload = new ActionChoiceView { ChoiceId = "field", MinCount = 1, MaxCount = 1, CanCancel = true,
                    Candidates = new[] { new ActionChoiceCandidateView { Token = "field", Zone = 2, PlayerId = 2, CardId = 9, Card = Card(9) } } },
            }));
            _ui.Interaction.RefreshServerChoice();
            yield return null;
            var target = _ui.EnemyField.Widgets.First(value => value.Card?.Id == 9);
            var denied = _ui.EnemyField.Widgets.First(value => value.Card?.Id == 10);
            Assert.AreSame(target, Hit(target).GetComponentInParent<CardWidget>());
            Assert.AreSame(_ui.Modal.gameObject, Hit(denied));
            Click(target);
            Assert.IsTrue(confirm.interactable);
            Assert.AreSame(_ui.Modal.gameObject, Hit(_ui.EndTurn));
            Snapshot(false, sequence: 2);
            yield return null;
            Assert.AreEqual(0, _ui.Modal.ActiveCount);
            Assert.IsTrue(_client.Input.CanExecute(GameplayInputAction.EndTurn));
        }

        [Test]
        public void AuthoritativeChoicesSurviveAcknowledgementAndSupportRejectionAndSameSequenceCancellation()
        {
            var input = new GameplayInputCoordinator();
            var choice = new ActionChoiceView { ChoiceId = "first", CanCancel = true,
                Candidates = new[] { new ActionChoiceCandidateView { Zone = 0 } } };
            input.ApplySnapshot(new StateView { Started = true, ActivePlayerId = 1, ResolvingPlayerId = 1 }, 1, true);
            input.SetServerChoice(choice);
            input.CommandSent(true);
            input.SetServerChoice(choice);
            Assert.AreEqual(SelectionInputPhase.WaitingForServer, input.SelectionPhase);
            input.ApplySnapshot(new StateView { Started = true, ActivePlayerId = 1, ResolvingPlayerId = 1 }, 1, true);
            Assert.IsFalse(input.CanExecute(GameplayInputAction.ConfirmSelection));
            Assert.IsFalse(input.CanExecute(GameplayInputAction.EndTurn));
            input.CommandRejected();
            Assert.AreEqual(SelectionInputPhase.Selecting, input.SelectionPhase);
            Assert.IsTrue(input.CanExecute(GameplayInputAction.ConfirmSelection));
            input.CommandSent(true);
            input.SetServerChoice(new ActionChoiceView { ChoiceId = "second", Candidates = choice.Candidates });
            Assert.AreEqual(SelectionInputPhase.Selecting, input.SelectionPhase);
            Assert.IsFalse(input.CanExecute(GameplayInputAction.CancelSelection));
            input.CommandSent(true);
            // CancelAction can finish at the same sequence; the correlated snapshot decides completion.
            input.ApplySnapshot(new StateView { Started = true, ActivePlayerId = 1 }, 1, true);
            Assert.AreEqual(SelectionInputPhase.Resolved, input.SelectionPhase);
            Assert.IsTrue(input.CanExecute(GameplayInputAction.EndTurn));
            var old = input.PushScope("old owner", Array.Empty<GameplayInputAction>());
            input.Reset();
            var current = input.PushScope("current owner", Array.Empty<GameplayInputAction>());
            old.Dispose();
            Assert.AreEqual(1, input.ScopeCount);
            current.Dispose();
        }

        [UnityTest]
        public IEnumerator ModalLeasesNestAndDestroyedOwnerCannotLeaveAnOverlay()
        {
            yield return SyntheticUi();
            var firstOwner = new GameObject("First modal owner");
            var secondOwner = new GameObject("Second modal owner");
            try
            {
                var first = _ui.Modal.Acquire(firstOwner, null);
                var second = _ui.Modal.Acquire(secondOwner, null);
                yield return null;
                Assert.AreSame(_ui.Modal.gameObject, Hit(_ui.EndTurn));
                first.Dispose();
                first.Dispose();
                Assert.AreEqual(1, _ui.Modal.ActiveCount);
                Assert.AreSame(_ui.Modal.gameObject, Hit(_ui.EndTurn));
                UnityEngine.Object.Destroy(secondOwner);
                yield return null;
                yield return null;
                Assert.AreEqual(0, _ui.Modal.ActiveCount);
                second.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(firstOwner);
                if (secondOwner != null)
                {
                    UnityEngine.Object.DestroyImmediate(secondOwner);
                }
            }
        }

        [UnityTest]
        public IEnumerator HidingSelectionUiCannotUnlockServerChoiceAndDisconnectCleansBothLayers()
        {
            yield return SyntheticUi();
            Choice(false);
            _ui.Interaction.enabled = false;
            Assert.IsTrue(_client.Input.HasSelection);
            Assert.IsFalse(_client.Input.CanExecute(GameplayInputAction.EndTurn));
            Assert.AreEqual(0, _ui.Modal.ActiveCount);
            _ui.Interaction.enabled = true;
            _ui.Interaction.RefreshServerChoice();
            yield return null;
            Assert.AreEqual(1, _ui.Modal.ActiveCount);
            Assert.AreSame(_ui.Modal.gameObject, Hit(_ui.EndTurn));
            _client.enabled = false;
            Assert.AreEqual(0, _client.Input.ScopeCount);
            _client.enabled = true;
            Assert.IsTrue(_client.Input.HasSelection, "Re-enabling transport must restore its authoritative choice.");
            Assert.IsFalse(_client.Input.CanExecute(GameplayInputAction.EndTurn));
            _client.DisconnectNetwork();
            Assert.AreEqual(0, _client.Input.ScopeCount);
            Assert.AreEqual(0, _ui.Modal.ActiveCount);
            Assert.IsNull(_client.PendingChoice);
        }

        [UnityTest]
        public IEnumerator EndingMatchAndUnloadingSceneClearScopesAndModalUi()
        {
            yield return SyntheticUi();
            Choice(false);
            Snapshot(false, true, 2);
            yield return null;
            Assert.AreEqual(0, _client.Input.ScopeCount);
            Assert.AreEqual(0, _ui.Modal.ActiveCount);
            Assert.IsFalse(_client.Input.CanExecute(GameplayInputAction.EndTurn));
            var input = _client.Input;
            input.PushScope("temporary scene", Array.Empty<GameplayInputAction>());
            var scene = SceneManager.CreateScene("InputScope-" + Guid.NewGuid().ToString("N"));
            SceneManager.MoveGameObjectToScene(_root, scene);
            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.AreEqual(0, input.ScopeCount);
        }

        [UnityTest]
        public IEnumerator RealSpellRejectReselectAndDelayedSnapshotKeepCommandAndUiLocks()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../ProjectC.Server/out/build/x64-Debug/ProjectCNetworkServer.exe"));
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; ++i)
            {
                if (arguments[i] == "-projectcServerPath")
                {
                    path = arguments[i + 1];
                }
            }
            Assert.IsTrue(File.Exists(path));
            _data = Path.Combine(Application.temporaryCachePath, "GameplayInput-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_data);
            File.Copy(Path.Combine(Path.GetDirectoryName(path), "Data/HyakkiYagyo/Cards.json"), Path.Combine(_data, "Cards.json"));
            File.WriteAllText(Path.Combine(_data, "Decks.json"), "{\"Config\":{\"InitialLife\":20,\"InitialHandSize\":2,\"DrawPerTurn\":0,\"FieldLimit\":5,\"HandLimit\":10,\"MinDeckSize\":10,\"MaxDeckSize\":10,\"MaxCardCopies\":10,\"InitialYokaiPower\":2,\"YokaiPowerPerTurn\":1,\"MaxYokaiPower\":10,\"SummoningSickness\":false},\"Decks\":[{\"Id\":\"Choice\",\"Name\":\"Choice fixture\",\"Cards\":[1102,1102,1102,1102,1102,1102,1102,1102,1102,1102]}]}");
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var start = new System.Diagnostics.ProcessStartInfo(path, "--port " + port + " --data \"" + _data + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            start.EnvironmentVariables.Remove("BATTLE_API_KEY");
            start.EnvironmentVariables.Remove("WEBSITE_API_URL");
            _server = System.Diagnostics.Process.Start(start);
            var startup = _server.StandardOutput.ReadLineAsync();
            yield return Until(() => startup.IsCompleted);
            StringAssert.Contains("listening", startup.Result);
            CreateUi();
            _opponent = new GameObject("Input opponent", typeof(MultiplayerDebugScreen));
            var opponent = _opponent.GetComponent<MultiplayerDebugScreen>();
            _client.UsePersonalDeck = opponent.UsePersonalDeck = false;
            _client.SelectedDeck = opponent.SelectedDeck = "Choice";
            _client.ConnectTo("ws://127.0.0.1:" + port);
            opponent.ConnectTo("ws://127.0.0.1:" + port);
            yield return Until(() => _client.ConnectionReady && opponent.ConnectionReady);
            _client.CreateMatch();
            yield return Until(() => !string.IsNullOrEmpty(_client.CurrentMatch));
            opponent.JoinMatch(_client.CurrentMatch);
            yield return Until(() => !string.IsNullOrEmpty(opponent.CurrentMatch));
            _client.SetReady("Choice");
            opponent.SetReady("Choice");
            yield return Until(() => _client.State?.Started == true && opponent.State?.Started == true);
            yield return null;
            Assert.IsTrue(_ui.CanAct);
            int source = _client.State.Players[0].Hand[0].Id;
            Assert.IsTrue(_client.SendPlayerAction(source, 1));
            yield return Until(() => _client.PendingChoice != null && !_client.Busy);
            yield return Until(() => _ui.Interaction.State == BattleInputState.SelectingEffectTarget);
            Assert.IsFalse(_client.SendGameCommand("EndTurn"));
            Assert.IsFalse(_client.SendGameCommand("AdvancePhase"));
            Assert.IsFalse(_client.SendGameCommand("PlayCard", source));
            Assert.IsFalse(_client.SendGameCommand("Attack", source, source));
            Assert.AreSame(_ui.Modal.gameObject, Hit(_ui.EndTurn));
            Assert.IsTrue(_client.SendChoice(new List<string> { "invalid-choice" }));
            yield return Until(() => !_client.Busy && _client.Input.SelectionPhase == SelectionInputPhase.Selecting);
            Assert.IsNotNull(_client.PendingChoice);
            Assert.IsFalse(_client.Input.CanExecute(GameplayInputAction.EndTurn));
            yield return null;
            var gallery = _ui.GetComponentInChildren<CardGalleryView>(true);
            Click(gallery.GetComponentsInChildren<CardWidget>().First());
            var picker = _ui.GetComponentInChildren<EffectTargetPicker>(true);
            Assert.IsTrue(picker.transform.Bind("ConfirmButton", out Button confirm));
            Assert.IsTrue(confirm.interactable);
            confirm.onClick.Invoke();
            // Keep the live socket receiving, but hold its inbox away from the frame pump.
            var transportField = typeof(MultiplayerDebugScreen).GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic);
            var transport = transportField.GetValue(_client);
            transportField.SetValue(_client, null);
            Assert.AreEqual(SelectionInputPhase.WaitingForServer, _client.Input.SelectionPhase);
            Assert.IsFalse(confirm.interactable);
            string request = (string)typeof(MultiplayerDebugScreen).GetField("commandRequestId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_client);
            confirm.onClick.Invoke();
            Assert.AreEqual(request, typeof(MultiplayerDebugScreen).GetField("commandRequestId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_client));
            Assert.IsFalse(_client.SendChoice(new List<string> { "duplicate" }));
            _client.ApplyMessage(JsonUtility.ToJson(new MessageView { Type = "CommandAccepted", ProtocolVersion = 2,
                MatchId = _client.CurrentMatch, GameNumber = 1, SequenceNumber = 2, RequestId = request }));
            Assert.IsTrue(_client.Input.HasSelection, "CommandAccepted alone cannot resolve selection.");
            yield return new WaitForSecondsRealtime(.15f);
            Assert.AreEqual(1, _client.Sequence);
            Assert.AreSame(_ui.Modal.gameObject, Hit(_ui.EndTurn));
            transportField.SetValue(_client, transport);
            yield return Until(() => _client.Sequence == 2 && opponent.Sequence == 2 && !_client.Input.HasSelection);
            yield return null;
            Assert.AreEqual(0, _ui.Modal.ActiveCount);
            Assert.IsTrue(_ui.EndTurn.interactable);
            _client.DisconnectNetwork();
            Assert.AreEqual(0, _client.Input.ScopeCount);
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var obj in new[] { _root, _opponent, _eventSystem })
            {
                if (obj != null)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
            if (_server != null)
            {
                if (!_server.HasExited)
                {
                    _server.Kill();
                    _server.WaitForExit(5000);
                }
                _server.Dispose();
            }
            if (_data != null)
            {
                string target = Path.GetFullPath(_data);
                string root = Path.GetFullPath(Application.temporaryCachePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Assert.IsTrue(target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(target).StartsWith("GameplayInput-", StringComparison.Ordinal));
                Directory.Delete(target, true);
            }
        }
    }
}
#endif
