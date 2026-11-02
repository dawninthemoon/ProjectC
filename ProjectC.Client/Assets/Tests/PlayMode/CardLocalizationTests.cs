using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ProjectC.Client;
using TMPro;
using UnityEngine;

namespace ProjectC.Tests
{
    public sealed class CardLocalizationTests
    {
        static string Resource(string path) => Resources.Load<TextAsset>(path).text;
        static LocalizationService Localize() => LocalizationService.Load(Resource("ProjectC_Data - Localize"));

        [Test]
        public void NewFactionsUsePublishedStatsTextCostsAndExistingArtworkFallback()
        {
            var library = CardLibrary.LoadResources();
            var source = CardDataLoader.Load(Resource("ProjectC_Data - CardData"));
            var localize = Localize();
            foreach (int id in Enumerable.Range(3001, 6).Concat(Enumerable.Range(4001, 6)))
            {
                var row = source.Cards.Single(card => (int)card["Id"] == id);
                var card = library.Find(id);
                Assert.IsNotNull(card, "Missing definition " + id);
                Assert.AreEqual(localize.GetText((string)row["NameKey"]), card.Name);
                Assert.AreEqual(localize.GetText((string)row["DescriptionKey"]), card.Description);
                Assert.IsFalse(card.Description.Contains("{0}"));
                Assert.AreEqual((int?)row["Attack"] ?? 0, card.Attack);
                Assert.AreEqual((int?)row["MaxHp"] ?? 0, card.MaxHp);
                CollectionAssert.AreEqual(row["Archetypes"].Values<string>(), card.Archetypes);
                Assert.AreSame(Resources.Load<Sprite>("CardSprite/CardSprite_" + id), CardArtProvider.Load(id));
            }
            CollectionAssert.AreEqual(new[] { 1 }, library.Find(3003).ActivationYokaiCosts);
            CollectionAssert.AreEqual(new[] { 2 }, library.Find(4003).ActivationYokaiCosts);
            StringAssert.Contains("2", CardPresentation.Costs(library.Find(4003)));
        }

        [Test]
        public void EverySamplePreservesExportedNameCompositionAndCopyLimits()
        {
            var library = CardLibrary.LoadResources();
            var samples = CardLibrary.Samples(Resource("CardData/HyakkiDecks"), library);
            var rows = JArray.Parse(Resource("ProjectC_Data - DeckData"));
            CollectionAssert.AreEquivalent(new[] { "HyakkiYagyo", "HyakkiSample", "CrimsonSample", "FrostSample" },
                samples.Select(deck => deck.DeckId));
            foreach (var sample in samples)
            {
                Assert.IsTrue(DeckValidator.Validate(sample, library).CanPlay);
                if (sample.DeckId == "HyakkiYagyo")
                {
                    continue;
                }
                var exported = rows.Where(row => (string)row["DeckId"] == sample.DeckId).ToArray();
                Assert.AreEqual((string)exported[0]["\uB371 \uC774\uB984"], sample.Name);
                var expected = exported.SelectMany(row => Enumerable.Repeat(int.Parse((string)row["CardId"]),
                    int.Parse((string)row["\uB9E4\uC218"])));
                CollectionAssert.AreEquivalent(expected, sample.Cards);
                var copy = sample.Copy();
                copy.Cards.Clear();
                Assert.AreEqual(30, sample.Cards.Count, "Personal edits must not mutate sample templates");
            }
        }

        [Test]
        public void ActualSheetLoadsNumericStringsAndEmbeddedEffects()
        {
            var data = CardDataLoader.Load(Resource("ProjectC_Data - CardData"));
            Assert.AreEqual(JArray.Parse(Resource("ProjectC_Data - CardData")).Count, data.Cards.Count);
            Assert.AreEqual(JTokenType.Integer, data.Cards[0]["Id"].Type);
            Assert.AreEqual(1001, (int)data.Cards[0]["Id"]);
            Assert.AreEqual(2, (int)data.Cards[0]["Attack"]);
            Assert.IsInstanceOf<JArray>(data.Cards[0]["Effects"]);
            Assert.IsInstanceOf<JArray>(data.Cards[0]["Archetypes"]);
            Assert.AreEqual("Search", (string)data.Cards[0]["Effects"][0]["Actions"][0]["Operation"]);
        }

        [Test]
        public void ActualNamesAndDescriptionsUseKeys()
        {
            var library = CardLibrary.Load(Resource("ProjectC_Data - CardData"), Localize());
            var card = library.Find(1006);
            Assert.AreEqual("card_1006_name", card.NameKey);
            Assert.AreEqual("card_1006_desc", card.DescriptionKey);
            Assert.AreEqual("백귀야행－월하의 요호", card.Name);
            StringAssert.Contains("요력 2", card.Description);
            Assert.IsFalse(card.Description.Contains("{0}"));
            Assert.IsTrue(card.DescriptionText.Segments.Any(segment => segment.KeywordId == "Manifestation"));
        }

        [TestCase(3, "3")]
        [TestCase(0, "0")]
        public void RealValueWinsOverDefaultIncludingZero(int amount, string expected)
        {
            var card = JObject.Parse("{\"Effects\":[{\"Actions\":[{\"Amount\":" + amount + "}]}]}");
            var args = JArray.Parse("[{\"path\":\"Effects[0].Actions[0].Amount\",\"default\":1}]");
            var formatter = new CardTextFormatter();
            Assert.AreEqual(expected, formatter.Format("{0}", card, args).PlainText);
            card["Effects"][0]["Actions"][0]["Amount"] = 7;
            Assert.AreEqual("7", formatter.Format("{0}", card, args).PlainText);
        }

        [Test]
        public void MultipleArgumentsAndAbsentPathDefault()
        {
            var formatter = new CardTextFormatter();
            var args = JArray.Parse("[{\"path\":\"Attack\"},{\"path\":\"Effects[3].Amount\",\"default\":1}]");
            Assert.AreEqual("2 / 1", formatter.Format("{0} / {1}", JObject.Parse("{\"Attack\":2}"), args).PlainText);
        }

        [Test]
        public void MissingPathWithoutDefaultFails()
        {
            Assert.Throws<FormatException>(() => new CardTextFormatter().Format("{0}", new JObject(),
                JArray.Parse("[{\"path\":\"Effects[0].Costs[0].Amount\"}]")));
        }

        [TestCase("{2}")]
        [TestCase("{-1}")]
        [TestCase("{0:eval()}")]
        [TestCase("{0")]
        public void InvalidArgumentIndicesAndTokensFail(string template)
        {
            Assert.Throws<FormatException>(() => new CardTextFormatter().Parse(template, new[] { "1" }));
        }

        [TestCase("Effects[*].Amount")]
        [TestCase("Effects[0];Run()")]
        [TestCase("$.Attack")]
        [TestCase("Effects[-1]")]
        public void OnlyReadOnlyDataPathsAreAccepted(string path)
        {
            Assert.Throws<FormatException>(() => CardTextFormatter.ValidatePath(path));
        }

        [Test]
        public void MissingLocalizationKeyIsVisibleAndReportedOnce()
        {
            var errors = new List<string>();
            var localize = LocalizationService.Load("[]", errors.Add);
            Assert.AreEqual("unknown", localize.GetText("unknown"));
            Assert.AreEqual("unknown", localize.GetText("unknown"));
            Assert.AreEqual(1, errors.Count);
        }

        [Test]
        public void DuplicateLocalizationKeysFail()
        {
            Assert.Throws<FormatException>(() => LocalizationService.Load("[{\"lmk\":\"x\",\"korean\":\"a\"},{\"lmk\":\"x\",\"korean\":\"b\"}]"));
        }

        [Test]
        public void LanguageFallbackIsRequestedThenKoreanThenEnglish()
        {
            var localize = LocalizationService.Load("[{\"lmk\":\"x\",\"korean\":\"KO\",\"english\":\"EN\",\"zh-tw\":\"TW\"},{\"lmk\":\"y\",\"english\":\"English only\"}]");
            localize.Language = "zh-tw";
            Assert.AreEqual("TW", localize.GetText("x"));
            localize.Language = "japanese";
            Assert.AreEqual("KO", localize.GetText("x"));
            Assert.AreEqual("English only", localize.GetText("y"));
        }

        [Test]
        public void KeywordAndNumericTokensKeepMetadata()
        {
            var value = new CardTextFormatter().Parse("{요력:@YokaiPower} {0}을 지불한다.", new[] { "3" });
            Assert.AreEqual("요력 3을 지불한다.", value.PlainText);
            Assert.AreEqual("YokaiPower", value.Segments[0].KeywordId);
            Assert.AreEqual("요력", value.Segments[0].Text);
            Assert.IsTrue(value.Segments[0].IsKeyword);
            Assert.IsFalse(value.Segments[1].IsKeyword);
        }

        [Test]
        public void PublishedCatalogPreservesManifestMetadataAndChangesDescriptionByDefinition()
        {
            var library = CardLibrary.LoadResources();
            Assert.IsFalse(library.Find(1007).MainDeckEligible);
            CollectionAssert.AreEqual(new[] { 1007 }, library.Find(1006).AllowedManifestations);
            Assert.AreNotEqual(library.Find(1006).Name, library.Find(1007).Name);
            Assert.AreNotEqual(library.Find(1006).Description, library.Find(1007).Description);
            StringAssert.Contains("피해 2", library.Find(1007).Description);
            Assert.AreEqual(JArray.Parse(Resource("ProjectC_Data - CardData")).Count, library.Cards.Count());
        }

        [Test]
        public void InvalidSheetFieldsAndDuplicateIdsFail()
        {
            var sheet = (JArray)CardDataLoader.Parse(Resource("ProjectC_Data - CardData"));
            sheet[1]["Id"] = sheet[0]["Id"].DeepClone();
            Assert.Throws<FormatException>(() => CardDataLoader.Load(sheet.ToString()));
            sheet = (JArray)CardDataLoader.Parse(Resource("ProjectC_Data - CardData"));
            sheet[0]["Effects"] = "not JSON";
            Assert.Throws<Newtonsoft.Json.JsonReaderException>(() => CardDataLoader.Load(sheet.ToString()));
        }

        [Test]
        public void UnityInspectorLocalizesCurrentDefinitionAndResolvesKeywordClicks()
        {
            var root = new GameObject("Localized inspector", typeof(RectTransform), typeof(CardDetailView));
            try
            {
                foreach (string name in new[] { "Name", "Stats", "Rules", "Forms", "ArtPlaceholder", "CardType" })
                {
                    var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
                    label.transform.SetParent(root.transform, false);
                }
                var artwork = new GameObject("ArtworkImage", typeof(RectTransform), typeof(AspectFillImage));
                artwork.transform.SetParent(root.transform, false);
                var view = root.GetComponent<CardDetailView>();
                var card = new CardView
                {
                    Id = 1, DefinitionId = 1006, Attack = 99, Hp = 1, MaxHp = 4,
                    Name = "server fallback", RulesText = "English fallback",
                };
                view.Show(card);
                Assert.AreEqual("백귀야행－월하의 요호", view.Title.text);
                StringAssert.Contains("99", view.Stats.text, "Stats must remain server-authoritative");
                var links = view.Rules.GetComponent<KeywordLinkView>();
                StringAssert.Contains("요력 2", links.Content.PlainText);
                Assert.IsTrue(links.SelectKeyword("YokaiPower"));
                StringAssert.Contains(Localize().GetText("keyword_YokaiPower_desc"), view.Forms.text);
                card.DefinitionId = 1007; // Wire DefinitionId is server CurrentDefinitionId.
                view.Show(card);
                Assert.AreEqual("백귀야행－구미호 현현", view.Title.text);
                StringAssert.Contains("피해 2", links.Content.PlainText);
                Assert.IsFalse(links.SelectKeyword("YokaiPower"));
                Assert.IsTrue(links.SelectKeyword("Manifestation"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void KeywordRendererEscapesArgumentsAndPreservesLinks()
        {
            var root = new GameObject("Keyword text", typeof(RectTransform), typeof(TextMeshProUGUI));
            try
            {
                var text = new CardTextFormatter().Parse("{요력:@YokaiPower} {0}", new[] { "<sprite=0>" });
                var label = root.GetComponent<TMP_Text>();
                string selected = null;
                var links = KeywordLinkView.Render(label, text, Localize(), keyword => selected = keyword.Id);
                StringAssert.Contains("<link=\"keyword:YokaiPower\">", label.text);
                Assert.IsFalse(label.text.Contains("<sprite=0>"));
                Assert.IsTrue(links.SelectKeyword("YokaiPower"));
                Assert.AreEqual("YokaiPower", selected);
                Assert.IsFalse(links.SelectKeyword("Unknown"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CatalogMismatchIsVisibleInAllDeckModes()
        {
            var root = new GameObject("Catalog mismatch", typeof(MultiplayerDebugScreen));
            try
            {
                var client = root.GetComponent<MultiplayerDebugScreen>();
                client.UsePersonalDeck = false;
                string notice = null;
                client.NoticeReceived += message => notice = message;
                client.ApplyMessage("{\"Type\":\"DeckCatalog\",\"ProtocolVersion\":2,\"CatalogVersion\":\"different\",\"Payload\":[]}");
                Assert.IsNotEmpty(client.CatalogProblem);
                Assert.AreEqual(client.CatalogProblem, client.DeckProblem);
                Assert.AreEqual(client.CatalogProblem, notice);
                client.ApplyMessage("{\"Type\":\"DeckCatalog\",\"ProtocolVersion\":2,\"CatalogVersion\":\""
                    + CardPresentation.Library.CatalogVersion + "\",\"Payload\":[]}");
                Assert.IsEmpty(client.CatalogProblem);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
