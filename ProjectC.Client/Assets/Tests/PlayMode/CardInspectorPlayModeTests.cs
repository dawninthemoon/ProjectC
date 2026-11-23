#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.Utils;
using NUnit.Framework;
using ProjectC.Client;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class CardInspectorPlayModeTests
    {
        [UnityTest]
        public IEnumerator AuthoredBattleSceneInspectorRendersBothRulesetsAndThreeLanguages()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity",
                new LoadSceneParameters(LoadSceneMode.Additive));
            Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            Assert.IsTrue(scene.IsValid());
            BattlePresenter presenter = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BattlePresenter>(true)).FirstOrDefault();
            Assert.IsNotNull(presenter);
            CardDetailView detail = presenter.GetComponentInChildren<CardDetailView>(true);
            Assert.IsNotNull(detail);
            presenter.enabled = false;
            string originalLanguage = CardPresentation.Library.Localization.Language;
            try
            {
                foreach (string language in new[] { "korean", "english", "japanese" })
                {
                    CardPresentation.Library.Localization.Language = language;
                    yield return ShowAndCapture(detail, new CardView
                    {
                        Id = 1, DefinitionId = 2001, CardType = 0, Attack = 4, Hp = 5, MaxHp = 5,
                        Yakou = true,
                    }, "yakou-monster-" + language, true);
                    yield return ShowAndCapture(detail, new CardView
                    {
                        Id = 2, DefinitionId = 2009, CardType = 1, Yakou = true,
                    }, "yakou-spell-" + language, false);
                }
                CardPresentation.Library.Localization.Language = "english";
                var longCard = new CardView
                {
                    Id = 3, DefinitionId = 3018, CardType = 1, Yakou = true,
                };
                yield return ShowAndCapture(detail, longCard, "yakou-long-description-english", false);
                Assert.IsTrue(detail.Bind("DetailViewportImage", out RectTransform longViewport));
                ScrollRect longScroll = longViewport.GetComponent<ScrollRect>();
                Assert.Greater(longScroll.content.rect.height, longViewport.rect.height,
                    "Long descriptions must remain reachable by scrolling.");
                longScroll.verticalNormalizedPosition = 0;
                detail.Show(longCard);
                Assert.That(longScroll.verticalNormalizedPosition, Is.EqualTo(0).Within(0.01f),
                    "Refreshing the same card must preserve the reader's scroll position.");
                yield return Capture("yakou-long-description-english-scrolled");
                yield return ShowAndCapture(detail, new CardView
                {
                    Id = 4, DefinitionId = 1001, CardType = 0, Attack = 2, Hp = 3, MaxHp = 3,
                }, "gamecore-monster", true);
                yield return ShowAndCapture(detail, new CardView
                {
                    Id = 5, DefinitionId = 1101, CardType = 1,
                }, "gamecore-spell", false);
            }
            finally
            {
                CardPresentation.Library.Localization.Language = originalLanguage;
            }
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        private static IEnumerator ShowAndCapture(CardDetailView detail, CardView card, string name, bool stats)
        {
            detail.Show(card);
            yield return null;
            Assert.IsTrue(detail.Bind("ArtworkImage", out AspectFillImage artwork));
            Assert.IsNotNull(artwork.sprite, name + " artwork");
            Assert.IsTrue(detail.Bind("InspectorStats", out RectTransform statsGroup));
            Assert.AreEqual(stats, statsGroup.gameObject.activeSelf, name + " stats visibility");
            Assert.IsTrue(detail.Bind("DetailViewportImage", out RectTransform viewport));
            Assert.That(viewport.rect.height, Is.EqualTo(109).Within(1), name + " scroll viewport");
            Assert.IsTrue(detail.Bind("RulesText", out TMPro.TMP_Text rules));
            Assert.IsFalse(string.IsNullOrWhiteSpace(rules.text), name + " localized rules");
            Assert.IsTrue(detail.Bind("NameText", out TMPro.TMP_Text title));
            Assert.IsFalse(string.IsNullOrWhiteSpace(title.text), name + " localized name");
            if (CardPresentation.Library.Localization.Language == "japanese"
                && title.text.Any(character => character >= '\uAC00' && character <= '\uD7A3'))
            {
                var palette = Resources.Load<BattleFontPalette>("UI/BattleFontPalette");
                Assert.AreSame(palette.Resolve(BattleFontRole.Heading, "korean"), title.font,
                    name + " Korean glyph fallback");
            }
            Assert.IsTrue(detail.Bind("CardTypeText", out TMPro.TMP_Text type));
            StringAssert.StartsWith(BattleText.Get(card.CardType == 1 ? "ui.card.spell" : "ui.card.monster"),
                type.text, name + " localized type");
            if (stats)
            {
                Assert.IsTrue(statsGroup.Bind("AttackBadgeText", out TMPro.TMP_Text attack));
                Assert.AreEqual(card.Attack.ToString(), attack.text);
            }
            yield return Capture(name);
            var keyword = rules.GetComponent<KeywordLinkView>();
            if (keyword != null)
            {
                var first = keyword.Content.Segments.FirstOrDefault(segment => segment.IsKeyword);
                if (first != null)
                {
                    rules.ForceMeshUpdate();
                    int characterIndex = rules.textInfo.linkInfo[0].linkTextfirstCharacterIndex;
                    var character = rules.textInfo.characterInfo[characterIndex];
                    Vector3 localCenter = (character.bottomLeft + character.topRight) / 2;
                    Vector2 pointer = RectTransformUtility.WorldToScreenPoint(null,
                        rules.transform.TransformPoint(localCenter));
                    keyword.OnPointerClick(new PointerEventData(EventSystem.current) { position = pointer });
                    Assert.IsTrue(detail.Bind("FormsText", out TMPro.TMP_Text description));
                    Assert.IsTrue(description.gameObject.activeSelf, name + " keyword explanation");
                    StringAssert.Contains(CardPresentation.LibraryFor(card).Localization.GetText(
                        "keyword_" + first.KeywordId + "_name"), description.text);
                }
            }
        }

        private static IEnumerator Capture(string name)
        {
            string directory = Environment.GetEnvironmentVariable("PROJECTC_INSPECTOR_CAPTURE_DIR");
            if (!string.IsNullOrEmpty(directory) && !Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), screenshot.EncodeToPNG());
                Object.Destroy(screenshot);
            }
        }
    }
}
#endif
