#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class BattleAudioPlayModeTests
    {
        private const string UiKey = "ProjectC.Audio.UiSfxVolume";
        private const string BattleKey = "ProjectC.Audio.BattleSfxVolume";
        private readonly List<SoundCue> _heard = new List<SoundCue>();
        private ClientAudio _audio;
        private float _master, _ui, _battle;
        private bool _hadUi, _hadBattle;
        private System.Diagnostics.Process _spellServer;
        private GameObject _spellOpponent;
        private MultiplayerDebugScreen _spellClient;
        private string _spellFixture;

        [SetUp]
        public void Setup()
        {
            _master = AudioListener.volume;
            _hadUi = PlayerPrefs.HasKey(UiKey);
            _hadBattle = PlayerPrefs.HasKey(BattleKey);
            _ui = PlayerPrefs.GetFloat(UiKey, .65f);
            _battle = PlayerPrefs.GetFloat(BattleKey, .8f);
            _audio = ClientAudio.Shared;
            _audio.StopBattle();
            AudioListener.volume = 1;
            _audio.UiVolume = .7f;
            _audio.BattleVolume = .8f;
            _heard.Clear();
            _audio.SoundPlayed += Heard;
        }

        private void Heard(SoundCue cue)
        {
            _heard.Add(cue);
        }

        [TearDown]
        public void Cleanup()
        {
            if (_spellClient != null)
            {
                _spellClient.DisconnectNetwork();
            }
            if (_spellOpponent != null)
            {
                Object.DestroyImmediate(_spellOpponent);
            }
            if (_spellServer != null)
            {
                if (!_spellServer.HasExited)
                {
                    _spellServer.Kill();
                    _spellServer.WaitForExit(5000);
                }
                _spellServer.Dispose();
                _spellServer = null;
            }
            if (_spellFixture != null && System.IO.Directory.Exists(_spellFixture))
            {
                System.IO.Directory.Delete(_spellFixture, true);
            }
            _spellFixture = null;
            _spellOpponent = null;
            _spellClient = null;
            if (_audio != null)
            {
                _audio.SoundPlayed -= Heard;
                _audio.StopBattle();
                _audio.UiVolume = _ui;
                _audio.BattleVolume = _battle;
            }
            if (!_hadUi)
            {
                PlayerPrefs.DeleteKey(UiKey);
            }
            if (!_hadBattle)
            {
                PlayerPrefs.DeleteKey(BattleKey);
            }
            AudioListener.volume = _master;
        }

        [UnityTest]
        public IEnumerator LibraryPoolVolumesCooldownAndMissingClipAreSafe()
        {
            var library = Resources.Load<SoundLibrary>("Audio/ClientSoundLibrary");
            Assert.IsNotNull(library);
            Assert.AreEqual(Enum.GetValues(typeof(SoundCue)).Length, library.Sounds.Length);
            Assert.AreEqual(1, Object.FindObjectsByType<ClientAudio>(FindObjectsSortMode.None).Length);
            int voices = _audio.GetComponents<AudioSource>().Length;
            foreach (var sound in library.Sounds)
            {
                Assert.IsNotNull(sound.Clip, sound.Cue.ToString());
                Assert.Greater(sound.Clip.samples, 0);
                Assert.That(sound.Clip.length, Is.InRange(.02f, 1.1f));
                yield return new WaitForSecondsRealtime(.14f);
                Assert.IsTrue(ClientAudio.Play(sound.Cue), sound.Cue.ToString());
                Assert.IsFalse(ClientAudio.Play(sound.Cue), "Immediate duplicate " + sound.Cue);
            }
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(SoundCue)).Cast<SoundCue>().ToArray(), _heard);
            Assert.AreEqual(voices, _audio.GetComponents<AudioSource>().Length);
            yield return new WaitForSecondsRealtime(.14f);
            _audio.UiVolume = 0;
            Assert.IsFalse(ClientAudio.Play(SoundCue.CardSelect));
            Assert.IsTrue(ClientAudio.Play(SoundCue.Hit));
            _audio.UiVolume = .4f;
            _audio.BattleVolume = 0;
            Assert.IsTrue(ClientAudio.Play(SoundCue.ButtonClick));
            Assert.IsFalse(ClientAudio.Play(SoundCue.Summon));
            var uiVoice = _audio.GetComponents<AudioSource>().First(source => source.clip == library.Sounds.Single(s => s.Cue == SoundCue.ButtonClick).Clip);
            float uiGain = uiVoice.volume;
            _audio.BattleVolume = .3f;
            Assert.AreEqual(uiGain, uiVoice.volume, "Battle slider must not change UI gain");
            Assert.AreEqual(.4f, PlayerPrefs.GetFloat(UiKey));
            Assert.AreEqual(.3f, PlayerPrefs.GetFloat(BattleKey));
            AudioListener.volume = 0;
            Assert.IsFalse(ClientAudio.Play(SoundCue.Draw));
            AudioListener.volume = 1;
            var missing = library.Sounds.Single(sound => sound.Cue == SoundCue.Transform);
            var original = missing.Clip;
            try
            {
                missing.Clip = null;
                LogAssert.Expect(LogType.Warning, "Missing sound clip for Transform; gameplay continues without it.");
                Assert.IsFalse(ClientAudio.Play(SoundCue.Transform));
                Assert.IsFalse(ClientAudio.Play(SoundCue.Transform));
            }
            finally
            {
                missing.Clip = original;
            }
        }

        [UnityTest]
        public IEnumerator AcceptedEventMappingAndTurnResultDeduplication()
        {
            var before = new StateView { Players = new[] { new PlayerView { PlayerId = 1, Life = 18 } } };
            BattleSoundEvents.Present(new EventView { Type = 0 }, before);
            BattleSoundEvents.Present(new EventView { Type = 1, Damage = 2 }, before);
            BattleSoundEvents.Present(new EventView { Type = 1, Damage = 3 }, before);
            Assert.AreEqual(1, _heard.Count(cue => cue == SoundCue.Attack));
            Assert.AreEqual(1, _heard.Count(cue => cue == SoundCue.Hit));
            yield return new WaitForSecondsRealtime(.14f);
            BattleSoundEvents.Present(new EventView { Type = 19 }, before);
            BattleSoundEvents.Present(new EventView { Type = 13 }, before);
            Assert.AreEqual(1, _heard.Count(cue => cue == SoundCue.CardUse));
            Assert.AreEqual(0, _heard.Count(cue => cue == SoundCue.Effect));
            yield return new WaitForSecondsRealtime(.14f);
            foreach (int type in new[] { 2, 11, 13, 15 })
            {
                BattleSoundEvents.Present(new EventView { Type = type }, before);
            }
            foreach (var cue in new[] { SoundCue.Destroy, SoundCue.Summon, SoundCue.Effect, SoundCue.Transform })
            {
                Assert.Contains(cue, _heard);
            }
            string match = Guid.NewGuid().ToString();
            _audio.PlayTurn(match, 1, 1);
            _audio.PlayTurn(match, 1, 1);
            Assert.AreEqual(1, _heard.Count(cue => cue == SoundCue.TurnStart));
            _audio.PlayResult(match, 1, 1, 1);
            yield return new WaitForSecondsRealtime(.14f);
            _audio.PlayResult(match, 1, 1, 1);
            Assert.AreEqual(1, _heard.Count(cue => cue == SoundCue.Victory));
            _audio.PlayResult(match, 2, 2, 1);
            Assert.AreEqual(1, _heard.Count(cue => cue == SoundCue.Defeat));
        }

        [UnityTest]
        public IEnumerator ScenePresentationDrawSelectionAndSnapshotRestore()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            var client = ui.Client;
            string match = "audio-" + Guid.NewGuid().ToString("N");
            client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"" + match + "\",\"PlayerId\":1,\"GameNumber\":1}");
            var state = new StateView
            {
                Started = true, TurnStarted = true, ActivePlayerId = 1, TurnNumber = 1, Phase = 0, FieldLimit = 5,
                Players = new[]
                {
                    new PlayerView { PlayerId = 1, Life = 20, Hand = new[] { Card(1) }, HandCount = 1, DeckCount = 20,
                        Field = Array.Empty<CardView>(), Graveyard = Array.Empty<CardView>() },
                    new PlayerView { PlayerId = 2, Life = 20, Hand = Array.Empty<CardView>(), HandCount = 5, DeckCount = 20,
                        Field = Array.Empty<CardView>(), Graveyard = Array.Empty<CardView>() }
                }
            };
            Snapshot(client, match, 1, state);
            yield return null;
            yield return null;
            Assert.AreEqual(1, _heard.Count(cue => cue == SoundCue.TurnStart));
            ui.Hand.Widgets[0].OnPointerClick(null);
            Assert.Contains(SoundCue.CardSelect, _heard);
            ui.Interaction.ClickArea(ui.GetComponentsInChildren<BattleHitArea>().First(area => area.Kind == BattleHitArea.AreaKind.Background));
            Assert.Contains(SoundCue.CardDeselect, _heard);
            client.ApplyMessage(JsonUtility.ToJson(new EventsMessage
            {
                Type = "GameEvents", ProtocolVersion = 2, MatchId = match, GameNumber = 1, SequenceNumber = 2,
                Payload = new[] { new EventView { Type = 4, TargetPlayerId = 1, TargetCardId = 2, DefinitionId = 1001, FromZone = 0, ToZone = 1 } }
            }));
            state.Players[0].Hand = new[] { Card(1), Card(2) };
            state.Players[0].HandCount = 2;
            state.Players[0].DeckCount = 19;
            Snapshot(client, match, 2, state);
            yield return new WaitForSecondsRealtime(1);
            Assert.AreEqual(1, _heard.Count(cue => cue == SoundCue.Draw));
            int count = _heard.Count;
            client.ApplyMessage("{\"Type\":\"CommandRejected\",\"ProtocolVersion\":2,\"MatchId\":\"" + match + "\",\"GameNumber\":1,\"Reason\":\"GameRule:WrongPhase\"}");
            yield return null;
            Assert.AreEqual(count, _heard.Count, "Rejected commands must not emit battle sounds");
            client.DisconnectNetwork();
            Snapshot(client, match, 3, state);
            yield return null;
            yield return null;
            Assert.AreEqual(count, _heard.Count, "Snapshot restoration must not replay effects or the current turn");
            var button = ui.EndTurn.GetComponent<UiButtonSound>();
            Assert.IsNotNull(button);
            ui.EndTurn.interactable = true;
            button.OnPointerDown(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            Assert.Contains(SoundCue.ButtonClick, _heard);
            var empty = SceneManager.CreateScene("AudioTestCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        [UnityTest]
        public IEnumerator RealServerSceneProducesSelectSummonAttackHitAndTurn()
        {
            var scenario = new BattlefieldOnlinePlayModeTests();
            try
            {
                yield return scenario.SceneAllowsInvalidDropSummonAttackPhaseAndReconnect();
                foreach (var cue in new[] { SoundCue.CardSelect, SoundCue.Summon, SoundCue.Attack, SoundCue.Hit, SoundCue.TurnStart })
                {
                    Assert.Contains(cue, _heard);
                }
                Assert.LessOrEqual(_heard.Count(cue => cue == SoundCue.TurnStart), 3,
                    "Reconnect must not add a turn cue; very fast turns can be rate-limited");
            }
            finally
            {
                scenario.Cleanup();
            }
        }

        [UnityTest]
        public IEnumerator RealServerEffectsAndTransformationProduceTheirCues()
        {
            var scenario = new MultiplayerPlayModeTests();
            try
            {
                yield return scenario.ServerDefinedEffectsUseClickTargetsAndManifestContext();
                yield return null;
                foreach (var cue in new[] { SoundCue.Summon, SoundCue.Effect, SoundCue.Hit, SoundCue.Transform })
                {
                    Assert.Contains(cue, _heard);
                }
            }
            finally
            {
                scenario.Cleanup();
            }
        }

        [UnityTest]
        public IEnumerator RealServerDrawCombatVictoryAndRematchProduceTheirCues()
        {
            var scenario = new MultiplayerPlayModeTests();
            try
            {
                yield return scenario.DrawPresentationPreservesServerDragAttackAndRematch();
                foreach (var cue in new[] { SoundCue.Draw, SoundCue.Summon, SoundCue.Attack, SoundCue.Hit, SoundCue.Destroy, SoundCue.Victory })
                {
                    Assert.Contains(cue, _heard);
                }
                Assert.AreEqual(1, _heard.Count(cue => cue == SoundCue.Victory));
            }
            finally
            {
                scenario.Cleanup();
            }
        }

        [UnityTest]
        public IEnumerator SettingsSlidersBindPersistAndKeepOneListener()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            string previousStorage = ClientApp.StorageOverride;
            string storage = System.IO.Path.Combine(Application.temporaryCachePath, "AudioSettings-" + Guid.NewGuid().ToString("N"));
            ClientApp.StorageOverride = storage;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/LobbyScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
                yield return null;
                yield return null;
                var lobby = Object.FindFirstObjectByType<LobbyScenePresenter>();
                Assert.IsNotNull(lobby);
                var settings = lobby.GetComponentsInChildren<Transform>(true).Single(t => t.name == "SettingsPanel");
                settings.gameObject.SetActive(true);
                var uiVolume = settings.GetComponentsInChildren<Slider>().Single(slider => slider.name == "UiSfxVolume");
                var battleVolume = settings.GetComponentsInChildren<Slider>().Single(slider => slider.name == "BattleSfxVolume");
                uiVolume.value = .2f;
                battleVolume.value = .45f;
                Assert.AreEqual(.2f, _audio.UiVolume);
                Assert.AreEqual(.45f, _audio.BattleVolume);
                Assert.AreEqual(.2f, PlayerPrefs.GetFloat(UiKey));
                Assert.AreEqual(.45f, PlayerPrefs.GetFloat(BattleKey));
                Assert.AreEqual(1, Object.FindObjectsByType<ClientAudio>(FindObjectsSortMode.None).Length);
                Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(listener => listener.isActiveAndEnabled));
                yield return CaptureSettings(lobby.GetComponent<Canvas>());
            }
            finally
            {
                if (ClientApp.Instance != null)
                {
                    Object.DestroyImmediate(ClientApp.Instance.gameObject);
                }
                ClientApp.StorageOverride = previousStorage;
                if (System.IO.Directory.Exists(storage))
                {
                    System.IO.Directory.Delete(storage, true);
                }
            }
            var empty = SceneManager.CreateScene("AudioSettingsCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("LobbyScene");
        }

        [UnityTest]
        public IEnumerator UnityMixProducesNonSilentOutputWhenAudioDeviceIsAvailable()
        {
            yield return new WaitForSecondsRealtime(.15f);
            ClientAudio.Play(SoundCue.Summon);
            float peak = 0;
            var samples = new float[1024];
            double dspStart = AudioSettings.dspTime;
            float end = Time.realtimeSinceStartup + .5f;
            while (Time.realtimeSinceStartup < end)
            {
                AudioListener.GetOutputData(samples, 0);
                foreach (float sample in samples)
                {
                    peak = Mathf.Max(peak, Mathf.Abs(sample));
                }
                yield return null;
            }
            System.IO.Directory.CreateDirectory("Artifacts/BattleAudio");
            System.IO.File.WriteAllText("Artifacts/BattleAudio/DspOutput.json",
                "{\"peak\":" + peak.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"dspAdvanced\":" + (AudioSettings.dspTime > dspStart ? "true" : "false")
                + ",\"sampleRate\":" + AudioSettings.outputSampleRate + "}");
            if (peak <= 0)
            {
                Assert.Ignore("The batch editor has no active audio output; event/clip tests still validate dispatch.");
            }
            Assert.Greater(peak, .0001f);
            Assert.Less(peak, .99f);
        }

        [UnityTest]
        public IEnumerator RealServerSpellUseAndDefeatPlayOnce()
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
            Assert.IsTrue(System.IO.File.Exists(executable));
            string data = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(executable), "TestData");
            _spellFixture = System.IO.Path.Combine(Application.temporaryCachePath, "AudioSpellLoss-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_spellFixture);
            var cards = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(data, "Cards.json")));
            ((Newtonsoft.Json.Linq.JArray)cards["Cards"]).Add(Newtonsoft.Json.Linq.JObject.Parse(
                "{\"Id\":50000,\"Name\":\"Audio fixture draw spell\",\"Type\":\"Spell\",\"Effects\":[{\"Id\":1,\"Type\":\"Activated\",\"ActivationZone\":\"Hand\",\"Actions\":[{\"Operation\":\"Draw\",\"Amount\":1,\"Targets\":{\"Category\":\"FriendlyPlayer\",\"Zones\":[\"Field\"]}}]}]}"));
            var decks = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(data, "Decks.json")));
            decks["Decks"][0]["Id"] = "SpellAudio";
            decks["Decks"][0]["Cards"] = new Newtonsoft.Json.Linq.JArray(50000, 50000, 50000, 50000);
            decks["Decks"][1]["Id"] = "StrikerAudio";
            decks["Decks"][1]["Cards"] = new Newtonsoft.Json.Linq.JArray(10, 11, 10, 11);
            System.IO.File.WriteAllText(System.IO.Path.Combine(_spellFixture, "Cards.json"), cards.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(_spellFixture, "Decks.json"), decks.ToString());
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            _spellServer = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable,
                "--port " + port + " --mulligan off --data \"" + _spellFixture + "\"")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
            var startup = _spellServer.StandardOutput.ReadLineAsync();
            yield return Until(() => startup.IsCompleted);
            StringAssert.Contains("listening", startup.Result);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            _spellClient = ui.Client;
            _spellOpponent = new GameObject("Audio loss opponent", typeof(MultiplayerDebugScreen));
            var other = _spellOpponent.GetComponent<MultiplayerDebugScreen>();
            string address = "ws://127.0.0.1:" + port;
            _spellClient.ConnectTo(address);
            other.ConnectTo(address);
            yield return Until(() => _spellClient.Connected && other.Connected && _spellClient.Decks.Length > 0 && other.Decks.Length > 0);
            _spellClient.CreateMatch();
            yield return Until(() => _spellClient.State != null);
            other.JoinMatch(_spellClient.CurrentMatch);
            yield return Until(() => other.State != null);
            _spellClient.SetReady("SpellAudio");
            other.SetReady("StrikerAudio");
            yield return Until(() => _spellClient.State?.Started == true && other.State?.Started == true && !ui.PresentationBusy);
            yield return null;
            var spell = ui.Hand.Widgets[0];
            var pointer = new PointerEventData(EventSystem.current)
            { position = RectTransformUtility.WorldToScreenPoint(null, spell.WorldCenter), button = PointerEventData.InputButton.Left };
            long sequence = _spellClient.Sequence;
            spell.OnBeginDrag(pointer);
            pointer.position = RectTransformUtility.WorldToScreenPoint(null,
                ui.OwnField.transform.TransformPoint(((RectTransform)ui.OwnField.transform).rect.center));
            spell.OnDrag(pointer);
            spell.OnEndDrag(pointer);
            yield return Until(() => _spellClient.Sequence > sequence && !_spellClient.Busy && !ui.PresentationBusy);
            Assert.Contains(SoundCue.CardUse, _heard);
            Assert.Contains(SoundCue.Draw, _heard);
            yield return Send(_spellClient, "EndTurn");
            yield return Until(() => other.State.ActivePlayerId == other.PlayerId);
            int attacker = other.State.Players.Single(player => player.PlayerId == other.PlayerId).Hand[0].Id;
            yield return Send(other, "PlayCard", attacker);
            yield return Send(other, "EndTurn");
            yield return Until(() => _spellClient.State.ActivePlayerId == _spellClient.PlayerId && !_spellClient.Busy);
            yield return Send(_spellClient, "EndTurn");
            yield return Until(() => other.State.ActivePlayerId == other.PlayerId);
            yield return Send(other, "AdvancePhase");
            yield return Send(other, "DirectAttack", attacker);
            yield return Until(() => _spellClient.State.Finished && _heard.Contains(SoundCue.Defeat));
            Assert.AreEqual(other.PlayerId, _spellClient.State.WinnerPlayerId);
            Assert.AreEqual(1, _heard.Count(cue => cue == SoundCue.Defeat));
            var previous = _spellClient.State;
            _spellClient.RequestSnapshot();
            yield return Until(() => _spellClient.State != previous && !_spellClient.Busy);
            yield return null;
            Assert.AreEqual(1, _heard.Count(cue => cue == SoundCue.Defeat));
            var empty = SceneManager.CreateScene("AudioSpellCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static IEnumerator Send(MultiplayerDebugScreen client, string command, int card = 0)
        {
            long sequence = client.Sequence;
            Assert.IsTrue(client.SendGameCommand(command, card, 0));
            yield return Until(() => client.Sequence > sequence && !client.Busy);
        }

        private static IEnumerator Until(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 12;
            while (!condition())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "Audio server condition timed out");
                yield return null;
            }
        }

        private static IEnumerator CaptureSettings(Canvas canvas)
        {
            var cameraObject = new GameObject("Audio settings preview camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            var target = new RenderTexture(1920, 1080, 24);
            var previous = RenderTexture.active;
            var mode = canvas.renderMode;
            var oldCamera = canvas.worldCamera;
            float oldDistance = canvas.planeDistance;
            try
            {
                camera.targetTexture = target;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                yield return null;
                camera.Render();
                RenderTexture.active = target;
                var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                pixels.Apply();
                System.IO.Directory.CreateDirectory("Artifacts/BattleAudio");
                System.IO.File.WriteAllBytes("Artifacts/BattleAudio/Settings-1920.png", pixels.EncodeToPNG());
                Object.Destroy(pixels);
            }
            finally
            {
                RenderTexture.active = previous;
                canvas.renderMode = mode;
                canvas.worldCamera = oldCamera;
                canvas.planeDistance = oldDistance;
                camera.targetTexture = null;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(cameraObject);
            }
        }

        private static CardView Card(int id)
        {
            return new CardView { Id = id, DefinitionId = 1001, Attack = 2, Hp = 2, MaxHp = 2 };
        }

        private static void Snapshot(MultiplayerDebugScreen client, string match, long sequence, StateView state)
        {
            client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage { Type = "StateSnapshot", ProtocolVersion = 2,
                MatchId = match, GameNumber = 1, SequenceNumber = sequence, Payload = state }));
        }
    }
}
#endif
