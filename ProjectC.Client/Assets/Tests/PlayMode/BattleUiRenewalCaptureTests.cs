#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class BattleUiRenewalCaptureTests
    {
        [UnityTest]
        public IEnumerator CaptureSyntheticPresentationStates()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"ui-capture\",\"PlayerId\":1,\"GameNumber\":1}");
            ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "ui-capture", GameNumber = 1, SequenceNumber = 1,
                Payload = new StateView
                {
                    Started = true, TurnStarted = true, ActivePlayerId = 1, TurnNumber = 3, FieldLimit = 5, MaxYokaiPower = 10,
                    Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 18, DeckCount = 25, YokaiPower = 2, Hand = Cards(5, 1), Field = Cards(3, 40), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 20, DeckCount = 25, HandCount = 5, Hand = Array.Empty<CardView>(), Field = Cards(3, 60), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                    },
                },
            }));
            yield return new WaitForSecondsRealtime(1f);
            ui.enabled = false;
            var label = ui.EndTurn.GetComponentInChildren<TMP_Text>();
            var frame = ui.EndTurn.GetComponent<TurnFrameGlow>();
            ui.EndTurn.interactable = true;
            frame.SetOpponentTurn(false);
            ui.PhaseLabel.text = BattleText.Get("ui.phase.main1");
            label.text = BattleText.Get("ui.phase.to_battle");
            yield return Capture("turn-main");
            ui.PhaseLabel.text = BattleText.Get("ui.phase.battle");
            label.text = BattleText.Get("ui.phase.to_main2");
            yield return Capture("turn-battle");
            ui.EndTurn.interactable = false;
            yield return Capture("turn-locked");
            frame.SetOpponentTurn(true);
            yield return Capture("turn-opponent");
            var card = ui.OwnField.Widgets[0];
            card.Card.CanAttack = true;
            card.SetActionPermissions(false, false);
            Assert.AreEqual(0f, Glow(card).color.a);
            yield return Capture("card-normal");
            card.SetActionPermissions(true, false);
            Assert.Greater(Glow(card).color.a, 0f);
            yield return Capture("card-actionable");
            card.SetSelected(true);
            yield return Capture("card-selected");
            card.SetSelected(false);
            ui.EnemyField.Widgets[0].SetHint(TargetHint.Available);
            yield return Capture("card-target");
            ui.OwnField.Render(Cards(5, 40), 5, false, 0, false, null, null, null);
            ui.EnemyField.Render(Cards(5, 60), 5, false, 0, false, null, null, null);
            ui.Hand.Render(Cards(10, 1), 0, false, 0, false, null, null, null);
            yield return new WaitForSecondsRealtime(.6f);
            yield return Capture("field5-hand10");
            var empty = SceneManager.CreateScene("BattleUiRenewalCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static Image Glow(CardWidget card)
        {
            return card.GetComponentsInChildren<Image>(true).Single(image => image.name == "FrameGlow");
        }

        private static CardView[] Cards(int count, int first)
        {
            int[] definitions = { 1001, 3001, 4002, 3004, 4005, 1003, 4001, 3002, 1006, 1002 };
            return Enumerable.Range(0, count).Select(i => new CardView
            {
                Id = first + i, DefinitionId = definitions[i % definitions.Length], Attack = 1 + i * 2,
                Hp = 2 + i, MaxHp = 2 + i,
            }).ToArray();
        }

        private static IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(.2f);
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory("Artifacts/BattleUiRenewalSynthetic");
                File.WriteAllBytes("Artifacts/BattleUiRenewalSynthetic/" + name + ".png", texture.EncodeToPNG());
                Object.Destroy(texture);
            }
        }
    }
}
#endif
