#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class TurnButtonArtworkPlayModeTests
    {
        [UnityTest]
        public IEnumerator SuppliedArtworkUsesCompactPhaseLabelsAndPreservesRaycasts()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            foreach (var module in EventSystem.current.GetComponents<BaseInputModule>())
            {
                module.enabled = false;
            }
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            var locale = CardPresentation.Library.Localization;
            string originalLanguage = locale.Language;
            locale.Language = "korean";
            ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"turn-art\",\"PlayerId\":1,\"GameNumber\":1}");
            var label = ui.EndTurn.GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == "Label");
            string[] phases = { "메인 1", "배틀", "메인 2", "엔드" };
            string[] actions = { "배틀", "메인 2", "턴 종료", "턴 종료" };
            BattlePhaseKind[] phaseKinds = { BattlePhaseKind.Main1, BattlePhaseKind.Battle, BattlePhaseKind.Main2, BattlePhaseKind.End };
            for (int phase = 0; phase < phases.Length; phase++)
            {
                ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
                {
                    Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "turn-art", GameNumber = 1, SequenceNumber = phase + 1,
                    Payload = new StateView
                    {
                        Started = true, TurnStarted = true, ActivePlayerId = 1, Phase = (int)phaseKinds[phase], TurnNumber = 3, FieldLimit = 10, MaxYokaiPower = 10,
                        Players = new[]
                        {
                            new PlayerView { PlayerId = 1, Life = 18, DeckCount = 25, YokaiPower = 2, Hand = Cards(5, 1), Field = Cards(3, 40), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                            new PlayerView { PlayerId = 2, Life = 20, DeckCount = 25, HandCount = 5, Hand = Array.Empty<CardView>(), Field = Cards(3, 60), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                        },
                    },
                }));
                yield return new WaitForSecondsRealtime(phase == 0 ? 1.5f : .3f);
                yield return Capture("phase-" + phase);
                Assert.AreEqual(phases[phase], ui.PhaseLabel.text);
                Assert.AreEqual(actions[phase], label.text);
                AssertReadable(label);
                AssertReadable(ui.PhaseLabel);
            }
            var frame = ui.EndTurn.GetComponent<Image>();
            Assert.AreEqual("TurnFrame_Background_HiRes", frame.sprite.name);
            Assert.IsTrue(frame.preserveAspect);
            var sparkle = ui.EndTurn.GetComponentsInChildren<Image>(true).Single(image => image.name == "TurnGlowImage");
            Assert.AreEqual("TurnSparkles_Overlay_HiRes", sparkle.sprite.name);
            Assert.IsFalse(sparkle.raycastTarget);
            Assert.AreEqual("UI/Default", sparkle.material.shader.name);
            Assert.IsFalse(ui.EndTurn.transform.Find("TurnPhasePlate").gameObject.activeSelf);
            var buttonRect = ui.EndTurn.GetComponent<RectTransform>();
            Assert.AreEqual(new Vector2(234, 360), buttonRect.rect.size);
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, buttonRect.TransformPoint(buttonRect.rect.center)),
            };
            var hits = new List<RaycastResult>();
            ui.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(buttonRect));
            ui.enabled = false;
            foreach (string language in new[] { "korean", "english", "japanese" })
            {
                locale.Language = language;
                ui.PhaseLabel.text = BattleText.Get("ui_battle_main_phase_2");
                label.text = BattleText.Get("ui_battle_end_turn");
                foreach (bool active in new[] { true, false })
                {
                    ui.EndTurn.interactable = active;
                    ui.EndTurn.GetComponent<TurnFrameGlow>().SetOpponentTurn(!active);
                    yield return new WaitForSecondsRealtime(.15f);
                    AssertReadable(label);
                    AssertReadable(ui.PhaseLabel);
                    Assert.That(sparkle.color.a, active ? Is.GreaterThan(0f) : Is.EqualTo(0f));
                    yield return Capture(language + (active ? "-active" : "-disabled"));
                }
            }
            foreach (int hp in new[] { 9, 18, 20, 123 })
            {
                ui.OwnPlayer.Render(new PlayerView { PlayerId = 1, Life = hp }, true, false);
                ui.EnemyPlayer.Render(new PlayerView { PlayerId = 2, Life = hp }, false, false);
                AssertReadable(ui.OwnPlayer.Life);
                AssertReadable(ui.EnemyPlayer.Life);
                Assert.AreEqual("Cinzel-Bold", ui.OwnPlayer.Life.font.sourceFontFile.name);
            }
            foreach (string language in new[] { "korean", "japanese" })
            {
                locale.Language = language;
                ui.PhaseLabel.text = BattleText.Get("ui_battle_main_phase_2");
                label.text = BattleText.Get("ui_battle_end_turn");
                ui.Detail.Show(ui.Hand.Widgets[0].Card);
                yield return null;
                yield return null;
                Assert.AreEqual(language == "japanese" ? "NotoSerifJP-Bold" : "NotoSerifKR-Bold", ui.Detail.Title.font.sourceFontFile.name);
                Assert.AreEqual(language == "japanese" ? "PretendardJP-Medium" : "Pretendard-Medium", ui.Detail.Rules.font.sourceFontFile.name);
                yield return Capture("detail-" + language);
            }
            locale.Language = originalLanguage;
            var empty = SceneManager.CreateScene("TurnArtworkCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static void AssertReadable(TMP_Text text)
        {
            text.ForceMeshUpdate();
            Assert.IsFalse(text.isTextOverflowing, text.text);
        }

        private static CardView[] Cards(int count, int start)
        {
            int[] ids = { 1001, 3001, 4002, 3004, 4005 };
            return Enumerable.Range(0, count).Select(i => new CardView
            {
                Id = start + i, DefinitionId = ids[i % ids.Length], Attack = i + 1, Hp = i + 2, MaxHp = i + 2,
            }).ToArray();
        }

        private static IEnumerator Capture(string name)
        {
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                Directory.CreateDirectory("Artifacts/TurnButtonGameReady");
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes("Artifacts/TurnButtonGameReady/" + name + ".png", texture.EncodeToPNG());
                Object.Destroy(texture);
            }
        }
    }
}
#endif
