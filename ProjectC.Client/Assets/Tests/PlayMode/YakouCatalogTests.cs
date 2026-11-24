using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEngine;

namespace ProjectC.Tests
{
    public sealed class YakouCatalogTests
    {
        private static string Resource(string path)
        {
            var asset = Resources.Load<TextAsset>(path);
            Assert.IsNotNull(asset, path);
            return asset.text;
        }

        [Test]
        public void YakouCatalogKeepsConflictingLegacyIdsAndLocalizationsIsolated()
        {
            var legacy = CardLibrary.LoadResources();
            var legacyName = legacy.Find(2001).Name;
            var yakou = CardLibrary.LoadYakouResources();
            Assert.AreEqual("yakou-2026-11-23-alpha01-v1", yakou.CatalogVersion);
            Assert.AreEqual(55, yakou.Cards.Count());
            Assert.AreEqual("Spell", legacy.Find(2001).Type);
            Assert.AreEqual("Monster", yakou.Find(2001).Type);
            Assert.AreEqual("등불동자", yakou.Find(2001).Name);
            Assert.AreEqual(legacyName, legacy.Find(2001).Name);
            Assert.AreNotEqual(legacyName, yakou.Find(2001).Name,
                "Same numeric ID must not cause the two catalogs to share names");
            Assert.AreEqual(legacy.Localization.GetText("ui.choice.confirm"),
                yakou.Localization.GetText("ui.choice.confirm"), "Shared UI strings remain available");
            yakou.Localization.Language = "japanese";
            Assert.AreEqual("등불동자", yakou.Find(2001).Name, "Missing authored translations fall back to Korean");
            Assert.AreEqual("墓守", yakou.Find(4001).Name, "New cards use their authored Japanese text");
            Assert.AreEqual("korean", legacy.Localization.Language, "Profile languages do not mutate each other");
            foreach (var card in yakou.Cards)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(card.Name), "Missing name for " + card.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(card.Description), "Missing description for " + card.Id);
            }
        }

        [Test]
        public void ManifestationsAreDisplayableButCannotEnterTheMainDeck()
        {
            var yakou = CardLibrary.LoadYakouResources();
            foreach (int id in new[] { 2101, 2102 })
            {
                var form = yakou.Find(id);
                Assert.IsNotNull(form);
                Assert.AreEqual("Manifestation", form.Type);
                Assert.IsFalse(form.MainDeckEligible);
                Assert.IsNotNull(DeckValidator.CannotAdd(new PersonalDeck(), yakou, id));
            }
            var decks = CardLibrary.Samples(Resource("CardData/YakouPrototypeDecks"), yakou);
            Assert.AreEqual(4, decks.Count);
            Assert.AreEqual("HyakkiSampleV02", decks[0].DeckId);
            CollectionAssert.AreEquivalent(new[] { "HyakkiSampleV02", "YakouAlphaGrave",
                "YakouAlphaRaid", "YakouAlphaGuard" }, decks.Select(deck => deck.DeckId));
            foreach (var deck in decks)
            {
                Assert.IsTrue(DeckValidator.Validate(deck, yakou).CanPlay, deck.DeckId);
            }
            Assert.Throws<FormatException>(() => CardLibrary.Load(Resource("CardData/YakouPrototypeCards"),
                LocalizationService.Load(Resource("CardData/YakouPrototypeLocalize"))),
                "The legacy loading path must not silently accept a new rules profile");
        }

        [Test]
        public void AuthoredDescriptionsFormatForEverySupportedLanguage()
        {
            var yakou = CardLibrary.LoadYakouResources();
            foreach (string language in new[] { "korean", "japanese", "english" })
            {
                yakou.Localization.Language = language;
                foreach (var card in yakou.Cards)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(card.Description),
                        $"Missing {language} description for {card.Id}");
                    Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(card.Description, @"\{\d+\}"),
                        $"Unresolved {language} description argument for {card.Id}");
                }
            }
        }

        [Test]
        public void MissingManifestationReferenceIsRejected()
        {
            var cards = JObject.Parse(Resource("CardData/YakouPrototypeCards"));
            var source = cards["Cards"].First(card => ((JArray)card["AllowedManifestations"]).Count > 0);
            source["AllowedManifestations"][0] = 999999;
            Assert.Throws<FormatException>(() => CardLibrary.LoadYakou(cards.ToString(),
                Resource("ProjectC_Data - Localize"), Resource("CardData/YakouPrototypeLocalize")));
        }

        [Test]
        public void MalformedManifestationCannotBecomeMainDeckEligible()
        {
            var cards = JObject.Parse(Resource("CardData/YakouPrototypeCards"));
            var form = cards["Cards"].Single(card => (int)card["Id"] == 2102);
            form["MainDeckEligible"] = true;
            Assert.Throws<FormatException>(() => CardLibrary.LoadYakou(cards.ToString(),
                Resource("ProjectC_Data - Localize"), Resource("CardData/YakouPrototypeLocalize")));
        }
    }
}
