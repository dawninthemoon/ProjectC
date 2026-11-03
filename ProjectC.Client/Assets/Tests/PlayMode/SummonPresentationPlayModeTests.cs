#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using ProjectC.Client;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class SummonPresentationPlayModeTests
    {
        private const float DeadlineSeconds = 12;
        private BattlePresenter _ui;
        private MultiplayerDebugScreen _client;
        private Process _server;
        private GameObject _peer;
        private long _sequence;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            _client?.DisconnectNetwork();
            if (_peer != null)
            {
                Object.DestroyImmediate(_peer);
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
            if (SceneManager.GetSceneByName("BattleScene").isLoaded)
            {
                var empty = SceneManager.CreateScene("SummonCleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync("BattleScene");
            }
        }

        private IEnumerator Load()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            _ui = Object.FindFirstObjectByType<BattlePresenter>();
            _client = _ui.Client;
        }

        private static CardView Card(int id)
        {
            return new CardView { Id = id, DefinitionId = 1001, Attack = 3, Hp = 3, MaxHp = 3 };
        }

        private static StateView State(int[] field, int attack = 3)
        {
            var cards = field.Select(Card).ToArray();
            if (cards.Length > 0)
            {
                cards[0].Attack = attack;
            }
            return new StateView
            {
                Started = true, TurnStarted = true, TurnNumber = 1, ActivePlayerId = 1, FieldLimit = 5,
                Players = new[]
                {
                    new PlayerView { PlayerId = 1, Life = 20, Hand = new[] { 1, 2, 3 }.Except(field).Select(Card).ToArray(),
                        HandCount = 3 - field.Length, DeckCount = 25, Field = cards, Graveyard = Array.Empty<CardView>() },
                    new PlayerView { PlayerId = 2, Life = 20, Hand = Array.Empty<CardView>(), HandCount = 5,
                        DeckCount = 25, Field = Array.Empty<CardView>(), Graveyard = Array.Empty<CardView>() }
                }
            };
        }

        private void Snapshot(StateView state)
        {
            _client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage { Type = "StateSnapshot", ProtocolVersion = 2,
                MatchId = "summon-cues", GameNumber = 1, SequenceNumber = _sequence, Payload = state }));
        }

        private void Events(params EventView[] events)
        {
            ++_sequence;
            _client.ApplyMessage(JsonUtility.ToJson(new EventsMessage { Type = "GameEvents", ProtocolVersion = 2,
                MatchId = "summon-cues", GameNumber = 1, SequenceNumber = _sequence, Payload = events }));
        }

        private void QueueSummon(int id)
        {
            Events(new EventView { Type = 4, TargetPlayerId = 1, TargetCardId = id, FromZone = 1, ToZone = 2 },
                new EventView { Type = 11, TargetPlayerId = 1, TargetCardId = id, DefinitionId = 1001 });
            Snapshot(State(Enumerable.Range(1, id).ToArray()));
        }

        private IEnumerator Synthetic()
        {
            yield return Load();
            _sequence = 1;
            _client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"summon-cues\",\"PlayerId\":1,\"GameNumber\":1}");
            Snapshot(State(Array.Empty<int>()));
            yield return null;
            yield return null;
            Assert.AreEqual(0, _ui.Effects.SummonsPresented);
        }

        private static IEnumerator Until(Func<bool> condition, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (!condition())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "Summon presentation timed out at line " + line);
                yield return null;
            }
        }

        private IEnumerator Settled()
        {
            yield return Until(() => !_ui.PresentationBusy && _ui.Effects.ActiveSummons == 0);
            yield return null;
        }

        private void AssertRestored(int count, int attack = 3)
        {
            Assert.AreEqual(count, _ui.OwnField.Widgets.Count);
            Assert.AreEqual(0, _ui.Effects.ActiveSummons);
            Assert.AreEqual(0, _ui.GetComponentInChildren<BattleVfxPlayer>(true).ActiveCount);
            foreach (var widget in _ui.OwnField.Widgets)
            {
                Assert.IsFalse(widget.IsInFlight);
                Assert.AreSame(_ui.OwnField.Content, widget.transform.parent);
                Assert.That(widget.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1).Within(.001f));
                Assert.That(widget.transform.localScale.x, Is.EqualTo(1).Within(.001f));
            }
            if (count > 0)
            {
                Assert.AreEqual(attack, _ui.OwnField.Widgets[0].Card.Attack);
            }
        }

        [UnityTest]
        public IEnumerator RapidSummonsAndIncomingStatsCommitInOrder()
        {
            yield return Synthetic();
            _ui.SetHandExpanded(true);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.AreEqual(3, _ui.Hand.Widgets.Count);
            foreach (var handCard in _ui.Hand.Widgets)
            {
                Assert.That(Quaternion.Angle(handCard.transform.localRotation, Quaternion.identity), Is.LessThan(.1f));
            }
            QueueSummon(1);
            yield return Until(() => _ui.Effects.ActiveSummons == 1);
            QueueSummon(2);
            QueueSummon(3);
            Events(new EventView { Type = 17, TargetPlayerId = 1, TargetCardId = 1, AttackAfter = 9, HpAfter = 3 });
            Snapshot(State(new[] { 1, 2, 3 }, 9));
            Assert.AreEqual(9, _client.State.Players[0].Field[0].Attack);
            Assert.AreEqual(3, _ui.OwnField.Widgets[0].Card.Attack);
            float start = Time.realtimeSinceStartup;
            yield return Settled();
            Assert.Less(Time.realtimeSinceStartup - start, 2.5f);
            Assert.AreEqual(3, _ui.Effects.SummonsPresented);
            AssertRestored(3, 9);
            _ui.SetHandExpanded(true);
            yield return new WaitForSecondsRealtime(.25f);
            foreach (var card in _ui.Hand.Widgets)
            {
                Assert.That(Quaternion.Angle(card.transform.localRotation, Quaternion.identity), Is.LessThan(.1f));
            }
        }

        [UnityTest]
        public IEnumerator SameBatchSummonsOverlapAndLaterRemovalWaitsForReveal()
        {
            yield return Synthetic();
            Events(Enumerable.Range(1, 3).SelectMany(id => new[]
            {
                new EventView { Type = 4, TargetPlayerId = 1, TargetCardId = id, FromZone = 1, ToZone = 2 },
                new EventView { Type = 11, TargetPlayerId = 1, TargetCardId = id, DefinitionId = 1001 }
            }).Concat(new[]
            {
                new EventView { Type = 2, TargetPlayerId = 1, TargetCardId = 2 },
                new EventView { Type = 4, TargetPlayerId = 1, TargetCardId = 2, FromZone = 2, ToZone = 3 }
            }).ToArray());
            var after = State(new[] { 1, 3 });
            after.Players[0].Hand = Array.Empty<CardView>();
            after.Players[0].HandCount = 0;
            after.Players[0].Graveyard = new[] { Card(2) };
            after.Players[0].Graveyard[0].Hp = 0;
            Snapshot(after);
            yield return Until(() => _ui.Effects.ActiveSummons == 3);
            Assert.AreEqual(3, _ui.OwnField.Widgets.Count);
            Assert.AreEqual(3, _ui.GetComponentInChildren<BattleVfxPlayer>(true).ActiveCount);
            Assert.AreEqual(2, _client.State.Players[0].Field.Length);
            yield return Settled();
            AssertRestored(2);
            CollectionAssert.AreEqual(new[] { 1, 3 }, _ui.OwnField.Widgets.Select(w => w.Card.Id));
            Assert.AreEqual(2, _ui.OwnGraveyard.Widgets.Single().Card.Id);
        }

        [UnityTest]
        public IEnumerator SkipAndDisableRestoreLatestSnapshotWithoutResidualEffects()
        {
            yield return Synthetic();
            QueueSummon(1);
            yield return Until(() => _ui.Effects.ActiveSummons == 1);
            QueueSummon(2);
            QueueSummon(3);
            yield return null;
            _ui.SkipPresentation();
            yield return null;
            Assert.IsFalse(_ui.PresentationBusy);
            AssertRestored(3);
            // A new match epoch must also cancel a cue without replaying snapshot arrivals.
            _client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"summon-cues\",\"PlayerId\":1,\"GameNumber\":1}");
            _sequence = 10;
            Snapshot(State(Array.Empty<int>()));
            yield return null;
            _ui.SkipPresentation();
            QueueSummon(1);
            yield return Until(() => _ui.Effects.ActiveSummons == 1);
            _ui.gameObject.SetActive(false);
            yield return null;
            _ui.gameObject.SetActive(true);
            yield return null;
            yield return Settled();
            AssertRestored(1);
        }

        [UnityTest]
        public IEnumerator VfxPoolReusesInstancesAndMissingVfxDoesNotBlockReveal()
        {
            yield return Synthetic();
            _ui.SkipPresentation();
            Snapshot(State(new[] { 1 }));
            yield return Settled();
            var widget = _ui.OwnField.Widgets[0];
            var vfx = _ui.GetComponentInChildren<BattleVfxPlayer>(true);
            for (int i = 0; i < 100; ++i)
            {
                _ui.Effects.Summon(_ui.OwnField, widget, .01f);
                yield return Until(() => _ui.Effects.ActiveSummons == 0);
            }
            Assert.AreEqual(4, vfx.GetComponentsInChildren<BattleVfxCue>(true).Length);
            AssertRestored(1);
            Object.DestroyImmediate(vfx.gameObject);
            _ui.Effects.Summon(_ui.OwnField, widget, .05f);
            yield return Until(() => _ui.Effects.ActiveSummons == 0);
            Assert.IsFalse(widget.IsInFlight);
            Assert.AreEqual(1, widget.GetComponent<CanvasGroup>().alpha);
        }

        [UnityTest]
        public IEnumerator RealServerSummonShowsGatherRingBurstAndReadableSettledCard()
        {
            string executable = Environment.GetCommandLineArgs().SkipWhile(a => a != "-projectcServerPath").Skip(1).FirstOrDefault();
            if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
            {
                Assert.Ignore("Supply -projectcServerPath for real server verification.");
            }
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            string data = Path.Combine(Path.GetDirectoryName(executable), "Data", "HyakkiYagyo");
            _server = Process.Start(new ProcessStartInfo(executable, "--port " + port + " --mulligan off --data \"" + data + "\"")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
            var startup = _server.StandardOutput.ReadLineAsync();
            yield return Until(() => startup.IsCompleted);
            StringAssert.Contains("listening", startup.Result);
            yield return Load();
            _peer = new GameObject("Summon opponent", typeof(MultiplayerDebugScreen));
            var other = _peer.GetComponent<MultiplayerDebugScreen>();
            string address = "ws://127.0.0.1:" + port;
            _client.ConnectTo(address);
            other.ConnectTo(address);
            yield return Until(() => _client.Connected && other.Connected && _client.Decks.Length > 0 && other.Decks.Length > 0);
            _client.CreateMatch();
            yield return Until(() => _client.State != null);
            other.JoinMatch(_client.CurrentMatch);
            yield return Until(() => other.State != null);
            _client.SetReady("HyakkiYagyo");
            other.SetReady("HyakkiYagyo");
            yield return Until(() => _client.State?.Started == true && !_ui.PresentationBusy);
            yield return new WaitForSecondsRealtime(1.3f);
            var card = _client.State.Players[0].Hand.First(c => c.CanSummon);
            int count = _ui.Effects.SummonsPresented;
            Assert.IsTrue(_client.SendGameCommand("PlayCard", card.Id, 0));
            float summonDeadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (_ui.Effects.ActiveSummons == 0)
            {
                Assert.Less(Time.realtimeSinceStartup, summonDeadline, "Server-confirmed summon must start after choices resolve");
                var choice = _client.PendingChoice;
                if (choice != null)
                {
                    Assert.IsTrue(_client.SendChoice(choice.Candidates.Take(choice.MinCount).Select(c => c.Token).ToList()));
                    yield return Until(() => _client.PendingChoice != choice || _ui.Effects.ActiveSummons > 0);
                }
                else
                {
                    yield return null;
                }
            }
            float start = Time.realtimeSinceStartup;
            var widget = _ui.OwnField.Widgets.Single(w => w.Card.Id == card.Id);
            Assert.IsTrue(_client.State.Players[0].Field.Any(c => c.Id == card.Id));
            Assert.Less(widget.GetComponent<CanvasGroup>().alpha, .1f);
            Assert.IsTrue(widget.IsInFlight);
            // Buffer frames in memory; PNG encoding after the cue must not distort
            // its wall-clock duration or skip the brief burst peak.
            var frames = new List<(float Time, Texture2D Image)>();
            yield return _ui.StartCoroutine(RecordSummon(frames, start));
            float measuredDuration = Time.realtimeSinceStartup - start;
            Assert.That(measuredDuration, Is.InRange(.5f, .85f));
            Directory.CreateDirectory("Artifacts/SummonPresentation/Frames");
            foreach (var stage in new[] { ("01-Gather-1920.png", .12f), ("02-Ring-1920.png", .23f), ("03-Burst-1920.png", .37f) })
            {
                var frame = frames.OrderBy(f => Mathf.Abs(f.Time - stage.Item2)).First();
                File.WriteAllBytes("Artifacts/SummonPresentation/" + stage.Item1, frame.Image.EncodeToPNG());
            }
            var timeline = new System.Text.StringBuilder();
            for (int i = 0; i < frames.Count; ++i)
            {
                string name = "Frame-" + i.ToString("D3") + ".png";
                File.WriteAllBytes("Artifacts/SummonPresentation/Frames/" + name, frames[i].Image.EncodeToPNG());
                float duration = i + 1 < frames.Count ? frames[i + 1].Time - frames[i].Time : .4f;
                timeline.AppendLine("file '" + name + "'");
                timeline.AppendLine("duration " + duration.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Object.Destroy(frames[i].Image);
            }
            timeline.AppendLine("file 'Frame-" + (frames.Count - 1).ToString("D3") + ".png'");
            File.WriteAllText("Artifacts/SummonPresentation/Frames/timeline.txt", timeline.ToString());
            File.WriteAllText("Artifacts/SummonPresentation/Timing.txt", "Measured summon: " + measuredDuration + " seconds; captured frames=" + frames.Count);
            yield return Settled();
            Assert.AreEqual(count + 1, _ui.Effects.SummonsPresented);
            Assert.IsFalse(widget.IsInFlight);
            Assert.AreEqual(1, widget.GetComponent<CanvasGroup>().alpha);
            Assert.AreEqual(_client.State.Players[0].Field.Single(c => c.Id == card.Id).Attack, widget.Card.Attack);
            yield return Capture("04-Settled-1920.png");
        }

        private IEnumerator RecordSummon(List<(float Time, Texture2D Image)> frames, float start)
        {
            // Run end-of-frame capture as an engine coroutine, not a top-level
            // UnityTest yield (the test framework forbids that yield in batch mode).
            while (_ui.Effects.ActiveSummons > 0)
            {
                yield return new WaitForEndOfFrame();
                frames.Add((Time.realtimeSinceStartup - start, ScreenCapture.CaptureScreenshotAsTexture()));
                yield return null;
            }
        }

        private static IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            Directory.CreateDirectory("Artifacts/SummonPresentation");
            File.WriteAllBytes("Artifacts/SummonPresentation/" + name, texture.EncodeToPNG());
            Object.Destroy(texture);
        }
    }
}
#endif
