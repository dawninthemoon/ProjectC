using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEngine;

namespace ProjectC.Tests
{
    public sealed class DeckStorageTests
    {
        string directory;
        CardLibrary library;

        [SetUp]
        public void Setup()
        {
            directory = Path.Combine(
                Application.temporaryCachePath,
                "DeckStorage-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(directory);
            library = CardLibrary.Load(Resources.Load<TextAsset>("CardData/HyakkiCards").text);
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                File.Delete(file);
            }
            Directory.Delete(directory);
        }

        [Test]
        public void CreateRenameDuplicateDeleteAndRestart()
        {
            var store = new DeckRepository(directory);
            store.Load();
            var deck = new PersonalDeck();
            Assert.IsTrue(store.Save(deck, out _));
            Assert.IsFalse(
                DeckValidator.Validate(deck, library).CanPlay,
                "Empty decks may be saved"
            );
            deck.Name = "행렬 시험";
            deck.Cards.Add(1001);
            Assert.IsTrue(store.Save(deck, out _));
            var copy = deck.Copy();
            copy.DeckId = Guid.NewGuid().ToString("N");
            copy.Name += " 사본";
            Assert.IsTrue(store.Save(copy, out _));
            var restarted = new DeckRepository(directory);
            restarted.Load();
            Assert.AreEqual(copy.DeckId, restarted.SelectedId);
            Assert.AreEqual(2, restarted.Decks.Count);
            Assert.AreEqual("행렬 시험", restarted.Find(deck.DeckId).Name);
            CollectionAssert.AreEqual(new[] { 1001 }, restarted.Selected.Cards);
            Assert.IsTrue(restarted.Delete(copy.DeckId, out _));
            Assert.AreEqual(deck.DeckId, restarted.SelectedId);
            restarted = new DeckRepository(directory);
            restarted.Load();
            Assert.AreEqual(1, restarted.Decks.Count);
            Assert.AreEqual(deck.DeckId, restarted.SelectedId);
        }

        [Test]
        public void DeckConstraintsUnknownDefinitionsAndDraftCancel()
        {
            var deck = CardLibrary.Starter(Resources.Load<TextAsset>("CardData/HyakkiDecks").text);
            Assert.IsTrue(DeckValidator.Validate(deck, library).CanPlay);
            Assert.IsNotNull(DeckValidator.CannotAdd(deck, library, 2004));
            deck.Cards.RemoveAt(0);
            Assert.IsNotNull(DeckValidator.CannotAdd(deck, library, 1007));
            Assert.IsNotNull(DeckValidator.CannotAdd(deck, library, 999999));
            deck.Cards.Clear();
            deck.Cards.AddRange(new[] { 1001, 1001, 1001 });
            Assert.IsNotNull(DeckValidator.CannotAdd(deck, library, 1001));
            deck.Cards.Add(999999);
            var store = new DeckRepository(directory);
            Assert.IsTrue(store.Save(deck, out _));
            store.Load();
            Assert.Contains(999999, store.Selected.Cards);
            Assert.IsTrue(
                DeckValidator
                    .Validate(store.Selected, library)
                    .Errors.Any(e => e.Contains("999999"))
            );
            var session = new DeckEditSession();
            session.Begin(store.Selected);
            session.Draft.Name = "변경";
            session.Draft.Cards.Clear();
            Assert.IsTrue(session.Dirty);
            session.Cancel();
            Assert.IsFalse(session.Dirty);
            Assert.AreEqual(4, session.Draft.Cards.Count);
            Assert.IsFalse(library.Find(1007).MainDeckEligible);
            Assert.IsTrue(
                library.Find(1001).MainDeckEligible,
                "Omitted MainDeckEligible defaults to true"
            );
            Assert.AreEqual(1, library.Filter("등불", "Monster", "HyakkiYagyo", "Bronze").Count());
        }

        [Test]
        public void CorruptJsonPreservesOriginalAndRecoversBackup()
        {
            var store = new DeckRepository(directory);
            var first = new PersonalDeck { Name = "이전" };
            Assert.IsTrue(store.Save(first, out _));
            first.Name = "최신";
            Assert.IsTrue(store.Save(first, out _));
            string path = Path.Combine(directory, "PersonalDecks.json");
            File.WriteAllText(path, "{broken");
            store.Load();
            Assert.IsNotEmpty(store.Warning);
            Assert.AreEqual("이전", store.Selected.Name);
            Assert.IsTrue(store.Save(store.Selected, out _));
            Assert.IsTrue(Directory.GetFiles(directory, "*.preserved-*").Any());
            Assert.AreEqual(
                "{broken",
                File.ReadAllText(Directory.GetFiles(directory, "*.preserved-*").Single())
            );
            Assert.That(() => CardLibrary.Load("{\"Cards\":[],\"Cards\":[]}"), Throws.Exception);
            File.WriteAllText(path, "{\"Version\":1,\"SelectedDeckId\":{},\"Decks\":[]}");
            Assert.DoesNotThrow(() => store.Load());
            Assert.IsNotEmpty(store.Warning);
        }

        [Test]
        public void DuplicateDeckIdsDoNotOverwriteCards()
        {
            File.WriteAllText(
                Path.Combine(directory, "PersonalDecks.json"),
                "{\"Version\":1,\"SelectedDeckId\":\"same\",\"Decks\":[{\"DeckId\":\"same\",\"Name\":\"A\",\"Cards\":[1001]},{\"DeckId\":\"same\",\"Name\":\"B\",\"Cards\":[1002]}]}"
            );
            var store = new DeckRepository(directory);
            store.Load();
            Assert.AreEqual(2, store.Decks.Count);
            Assert.AreNotEqual(store.Decks[0].DeckId, store.Decks[1].DeckId);
            Assert.IsNotEmpty(store.Warning);
            Assert.AreEqual(1002, store.Decks[1].Cards.Single());
            Assert.AreEqual("same", store.SelectedId);
        }
    }
}
