#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public sealed class BattleDetail6PlayModeTests
    {
        [UnityTest]
        public IEnumerator FixedInspectorAndHeroValuesRemainInteractive()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"detail6\",\"PlayerId\":1,\"GameNumber\":1}");
            ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "detail6", GameNumber = 1, SequenceNumber = 1,
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
            yield return new WaitForSecondsRealtime(1.2f);
            ui.enabled = false;
            Assert.IsFalse(ui.Detail.gameObject.activeSelf);
            yield return Capture("closed");
            ui.Interaction.Click(ui.OwnField.Widgets[0]);
            yield return null;
            Assert.IsTrue(ui.Detail.gameObject.activeSelf);
            Assert.AreEqual(CardPresentation.Name(ui.OwnField.Widgets[0].Card), ui.Detail.Title.text);
            float longHeight = ((RectTransform)ui.Detail.transform).rect.height;
            yield return Capture("own-field-long");
            ui.Interaction.Click(ui.EnemyField.Widgets[1]);
            yield return null;
            Assert.AreEqual(CardPresentation.Name(ui.EnemyField.Widgets[1].Card), ui.Detail.Title.text);
            yield return Capture("enemy-field");
            ui.Interaction.Click(ui.Hand.Widgets[2]);
            yield return null;
            Assert.AreEqual(CardPresentation.Name(ui.Hand.Widgets[2].Card), ui.Detail.Title.text);
            yield return Capture("hand");
            var shortCard = Card(90, 4005);
            ui.Detail.Show(shortCard);
            yield return null;
            float shortHeight = ((RectTransform)ui.Detail.transform).rect.height;
            Assert.Less(shortHeight, longHeight, "A shorter description should need less height.");
            Assert.IsFalse(ui.Detail.Stats.gameObject.activeSelf, "Spell cards should not have attack and health.");
            yield return Capture("short-spell");
            var localize = CardPresentation.Library.Localization;
            string original = localize.Language;
            foreach (string language in new[] { "korean", "japanese", "english" })
            {
                localize.Language = language;
                ui.Detail.Show(ui.OwnField.Widgets[0].Card);
                yield return null;
                Assert.AreEqual(CardPresentation.Name(ui.OwnField.Widgets[0].Card), ui.Detail.Title.text);
                yield return Capture("language-" + language);
            }
            localize.Language = original;
            Assert.IsTrue(ui.Detail.Title.font.TryAddCharacters("\u3042A\ud55c", out string missing), missing);
            string sample = ui.Detail.Rules.text;
            ui.Detail.Rules.text = string.Join("\n", Enumerable.Repeat(sample, 16));
            typeof(CardDetailView).GetMethod("ResizePanel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui.Detail, null);
            yield return null;
            var rect = (RectTransform)ui.Detail.transform;
            var scroll = ui.Detail.GetComponentInChildren<ScrollRect>(true);
            Assert.That(rect.rect.width, Is.InRange(330, 400));
            Assert.LessOrEqual(rect.rect.height, 780.1f);
            Assert.Greater(scroll.content.rect.height, scroll.viewport.rect.height);
            yield return Capture("overflow-top");
            scroll.verticalNormalizedPosition = 0f;
            yield return null;
            yield return Capture("overflow-bottom");
            ui.HideCardInfo();
            foreach (int value in new[] { 9, 18, 20, 123 })
            {
                ui.OwnPlayer.Render(new PlayerView { PlayerId = 1, Life = value }, true, true);
                ui.EnemyPlayer.Render(new PlayerView { PlayerId = 2, Life = value }, false, false);
                yield return null;
                ui.OwnPlayer.Life.ForceMeshUpdate();
                ui.EnemyPlayer.Life.ForceMeshUpdate();
                Assert.IsFalse(ui.OwnPlayer.Life.isTextOverflowing);
                Assert.IsFalse(ui.EnemyPlayer.Life.isTextOverflowing);
                yield return Capture("hp-" + value);
            }
            ui.OwnField.Render(Cards(5, 40), 5, false, 0, false, null, null, null);
            ui.EnemyField.Render(Cards(5, 60), 5, false, 0, false, null, null, null);
            ui.Hand.Render(Cards(10, 1), 0, false, 0, false, null, null, null);
            ui.Detail.Show(ui.OwnField.Widgets[0].Card);
            yield return new WaitForSecondsRealtime(.3f);
            yield return Capture("field5-hand10");
            var background = ui.GetComponentsInChildren<BattleHitArea>().First(area => area.Kind == BattleHitArea.AreaKind.Background);
            ui.Interaction.ClickArea(background);
            Assert.IsFalse(ui.Detail.gameObject.activeSelf);
            ui.Interaction.Click(ui.OwnField.Widgets[1]);
            Assert.IsTrue(ui.Detail.gameObject.activeSelf, "Card inspection must work after closing.");
            var empty = SceneManager.CreateScene("Detail6Cleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static CardView[] Cards(int count, int first)
        {
            int[] definitions = { 1008, 1003, 4005, 3004, 4002, 1002, 4001, 3002, 1006, 1001 };
            return Enumerable.Range(0, count).Select(i => Card(first + i, definitions[i % definitions.Length])).ToArray();
        }

        private static CardView Card(int id, int definition)
        {
            var item = CardPresentation.Library.Find(definition);
            return new CardView { Id = id, DefinitionId = definition, CardType = item.Type == "Spell" ? 1 : 0,
                Attack = item.Attack, Hp = item.MaxHp, MaxHp = item.MaxHp };
        }

        private static IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(.4f);
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory("Artifacts/BattleDetail6Synthetic");
                File.WriteAllBytes("Artifacts/BattleDetail6Synthetic/" + name + ".png", texture.EncodeToPNG());
                Object.Destroy(texture);
            }
        }
    }
}
#endif
