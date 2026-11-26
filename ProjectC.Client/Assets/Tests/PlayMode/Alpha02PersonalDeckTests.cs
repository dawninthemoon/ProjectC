#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEngine;

namespace ProjectC.Tests
{
    public sealed class Alpha02PersonalDeckTests
    {
        [Test]
        public void SampleCopyAndResourceRulesUseAuthoredCosts()
        {
            var library = CardLibrary.LoadAlpha02Resources();
            var snapshot = CardLibrary.LoadAlpha02Snapshot();
            var samples = CardLibrary.Samples(((JObject)snapshot["Decks"]).ToString(), library);
            Assert.AreEqual(6, samples.Count);
            var deck = samples.First(sample => sample.DeckId == "Alpha02Onmyoryo").Copy();
            Assert.IsTrue(Alpha02DeckValidator.Validate(deck, library).CanPlay);
            CollectionAssert.AreEquivalent(new[] { "Spirit" }, Alpha02DeckValidator.ResourceIds(deck, library));

            deck.Cards[0] = 4001; // Free cross-archetype card adds no resource restriction.
            Assert.IsTrue(Alpha02DeckValidator.Validate(deck, library).CanPlay);
            deck.Cards[1] = 5205;
            CollectionAssert.AreEquivalent(new[] { "Spirit", "Blood" },
                Alpha02DeckValidator.ResourceIds(deck, library));
            Assert.IsTrue(Alpha02DeckValidator.Validate(deck, library).CanPlay);
            deck.Cards[2] = 2003; // Linked manifestation 2101 requires Ki.
            CollectionAssert.AreEquivalent(new[] { "Ki", "Spirit", "Blood" },
                Alpha02DeckValidator.ResourceIds(deck, library));
            Assert.IsFalse(Alpha02DeckValidator.Validate(deck, library).CanPlay);
            deck.Cards[2] = 4001;
            Assert.IsTrue(Alpha02DeckValidator.Validate(deck, library).CanPlay);

            string directory = Path.Combine(Application.temporaryCachePath,
                "alpha02-deck-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repository = new DeckRepository(directory);
                deck.DeckId = Guid.NewGuid().ToString("N");
                Assert.IsTrue(repository.Save(deck, out string error), error);
                var restored = new DeckRepository(directory);
                restored.Load();
                CollectionAssert.AreEqual(deck.Cards, restored.Selected.Cards);
                Assert.IsTrue(Alpha02DeckValidator.Validate(restored.Selected, library).CanPlay);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
#endif
