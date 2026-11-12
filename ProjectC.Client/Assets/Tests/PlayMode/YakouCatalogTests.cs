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
            Assert.AreEqual("yakou-2026-11-12-prototype-v02", yakou.CatalogVersion);
            Assert.AreEqual(34, yakou.Cards.Count());
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
            Assert.AreEqual(1, decks.Count);
            Assert.AreEqual("HyakkiSampleV02", decks[0].DeckId);
            Assert.IsTrue(DeckValidator.Validate(decks[0], yakou).CanPlay);
            Assert.Throws<FormatException>(() => CardLibrary.Load(Resource("CardData/YakouPrototypeCards"),
                LocalizationService.Load(Resource("CardData/YakouPrototypeLocalize"))),
                "The legacy loading path must not silently accept a new rules profile");
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
