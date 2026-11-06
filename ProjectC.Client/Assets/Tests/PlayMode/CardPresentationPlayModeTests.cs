#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.Utils;
using NUnit.Framework;
using ProjectC.Client;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ProjectC.Tests
{
    public sealed class CardPresentationPlayModeTests
    {
        private GameObject _instance;

        private static T Find<T>(Component root, string name) where T : Component
        {
            Assert.IsTrue(root.transform.Bind(name, out T result), name);
            return result;
        }

        private static void AssertFace(Component root, int id, int attack, int hp)
        {
            var artwork = Find<AspectFillImage>(root, "ArtworkImage");
            Assert.AreSame(CardArtProvider.Load(id), artwork.sprite);
            Assert.IsNotNull(artwork.sprite);
            Assert.IsNotNull(artwork.GetComponentInParent<RectMask2D>());
            string suffix = root is CardWidget ? "Text" : "";
            Assert.AreEqual(attack.ToString(), Find<TMP_Text>(root, "AttackBadge" + suffix).text);
            Assert.AreEqual(hp.ToString(), Find<TMP_Text>(root, "HealthBadge" + suffix).text);
            Assert.IsFalse(root.GetComponentsInChildren<TMP_Text>(true)
                .Where(t => t.name == "Crest" || t.name == "Monogram" + suffix).Single().gameObject.activeSelf);
            var rect = (RectTransform)root.transform;
            Assert.That(rect.rect.width / rect.rect.height, Is.InRange(.72f, .76f));
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_instance != null)
            {
                UnityEngine.Object.Destroy(_instance);
            }
            if (ClientApp.Instance != null)
            {
                UnityEngine.Object.Destroy(ClientApp.Instance.gameObject);
            }
            ClientApp.StorageOverride = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator BattleArtworkBadgesTransformSelectionAndHover()
        {
            _instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Battle/BattleScreen.prefab"));
            var ui = _instance.GetComponent<BattlePresenter>();
            var client = ui.Client;
            client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"art-test\",\"PlayerId\":1,\"GameNumber\":1}");
            var hand = Enumerable.Range(1, 6).Select(i => new CardView
            {
                Id = i,
                DefinitionId = 1000 + i,
                Attack = 3,
                Hp = 3,
                MaxHp = 5,
                CanSummon = true,
                RulesText = "Card presentation validation."
            }).ToArray();
            var state = new StateView
            {
                Started = true,
                TurnStarted = true,
                ActivePlayerId = 1,
                TurnNumber = 1,
                FieldLimit = 5,
                Players = new[]
                {
                    new PlayerView { PlayerId = 1, Life = 3, DeckCount = 20, HandCount = hand.Length,
                        Hand = hand, Field = new[] { new CardView { Id = 20, DefinitionId = 1002,
                            Attack = 5, Hp = 2, MaxHp = 3, CanAttack = true } }, Graveyard = Array.Empty<CardView>() },
                    new PlayerView { PlayerId = 2, Life = 3, DeckCount = 20, HandCount = 3,
                        Hand = Array.Empty<CardView>(), Field = new[] { new CardView { Id = 30,
                            DefinitionId = 1004, Attack = 3, Hp = 3, MaxHp = 3 } }, Graveyard = Array.Empty<CardView>() }
                }
            };
            client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot",
                ProtocolVersion = 2,
                MatchId = "art-test",
                GameNumber = 1,
                SequenceNumber = 1,
                Payload = state
            }));
            yield return null;
            yield return new WaitForSecondsRealtime(.4f);
            var card = ui.Hand.Widgets[0];
            AssertFace(card, 1001, 3, 3);
            AssertFace(ui.OwnField.Widgets[0], 1002, 5, 2);
            Assert.IsFalse(card.GetComponentsInChildren<Transform>(true).Any(child => child.name == "Status"));
            card.SetSelected(true);
            Assert.IsTrue(card.IsSelected);
            Assert.IsNotNull(card.Title);
            card.OnPointerEnter(null);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.Greater(card.Face.localScale.x, 1);
            Assert.AreEqual(card.transform.parent.childCount - 1, card.transform.GetSiblingIndex());
            card.OnPointerExit(null);
            var artwork = card.ArtworkImage;
            hand[0].DefinitionId = 1007;
            card.Bind(hand[0], false, false, false, true);
            Assert.AreSame(artwork, card.ArtworkImage);
            Assert.AreSame(CardArtProvider.Load(1007), artwork.sprite, "Manifestation refreshes artwork on the existing widget");
            hand[0].CardType = 1;
            card.Bind(hand[0], false, false, false, true);
            Assert.IsFalse(Find<TMP_Text>(card, "AttackBadgeText").transform.parent.gameObject.activeSelf);
            Assert.IsFalse(Find<TMP_Text>(card, "HealthBadgeText").transform.parent.gameObject.activeSelf);
            hand[0].CardType = 0;
            card.Bind(hand[0], false, false, false, true);
            card.Bind(hand[0], true, false, false, true);
            Assert.IsNull(artwork.sprite);
            card.Bind(hand[0], false, false, false, true);
            ui.Detail.gameObject.SetActive(false);
            yield return new WaitForSecondsRealtime(.3f);
            yield return BattlePrefabPlayModeTests.Capture(ui, 1920, 1080, "CardUI-Battle.png");
            // A full hand must retain the same prefab shape and permit hover sorting.
            hand = Enumerable.Range(1, 10).Select(i => new CardView
            {
                Id = i,
                DefinitionId = 1001 + i % 6,
                Attack = 3,
                Hp = 3,
                MaxHp = 5,
                CanSummon = true
            }).ToArray();
            ui.Hand.Render(hand, 0, false, 0, false, null, null, null);
            ui.Hand.Layout.Snap();
            Assert.GreaterOrEqual(ui.Hand.Layout.Spacing, ui.Hand.Layout.CardSize.x * ui.Hand.Layout.CardScale,
                "Dense hands should scale uniformly instead of covering the neighboring badges");
            var canvas = (RectTransform)ui.Hand.transform.parent;
            foreach (var widget in ui.Hand.Widgets)
            {
                var corners = new Vector3[4];
                Find<TMP_Text>(widget, "HealthBadgeText").rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    Assert.GreaterOrEqual(canvas.InverseTransformPoint(corner).y, canvas.rect.yMin,
                        "Health badges must stay inside the canvas");
                }
            }
            card = ui.Hand.Widgets[4];
            card.OnPointerEnter(null);
            yield return new WaitForSecondsRealtime(.3f);
            yield return BattlePrefabPlayModeTests.Capture(ui, 1280, 720, "CardUI-FullHand.png");
        }

        [UnityTest]
        public IEnumerator DeckLibraryDeckContentsAndDetailShareArtwork()
        {
            ClientApp.StorageOverride = Path.Combine(Application.temporaryCachePath, "CardUI-" + Guid.NewGuid().ToString("N"));
            yield return SceneManager.LoadSceneAsync("LobbyScene");
            yield return null;
            yield return SceneManager.LoadSceneAsync("DeckEditorScene");
            yield return null;
            var ui = UnityEngine.Object.FindFirstObjectByType<DeckEditorPresenter>();
            Find<Button>(ui, "ImportStarter").onClick.Invoke();
            var card = ui.LibraryContent.GetComponentsInChildren<DeckTileView>().First(t => t.CardId == 1001);
            var definition = ClientApp.Instance.Library.Find(1001);
            AssertFace(card, 1001, definition.Attack, definition.MaxHp);
            var deckCard = ui.DeckContent.GetComponentsInChildren<DeckTileView>().First(t => t.CardId == 1001);
            AssertFace(deckCard, 1001, definition.Attack, definition.MaxHp);
            card.OnPointerEnter(null);
            var detail = Find<DeckTileView>(ui, "DetailCard");
            AssertFace(detail, 1001, definition.Attack, definition.MaxHp);
            foreach (var spell in ui.LibraryContent.GetComponentsInChildren<DeckTileView>()
                .Where(t => ClientApp.Instance.Library.Find(t.CardId).Type == "Spell"))
            {
                Assert.IsFalse(Find<TMP_Text>(spell, "AttackBadge").transform.parent.gameObject.activeSelf);
                Assert.IsFalse(Find<TMP_Text>(spell, "HealthBadge").transform.parent.gameObject.activeSelf);
            }
            yield return Capture(ui.GetComponent<Canvas>(), "CardUI-DeckEditor.png");
            detail.Render(int.MaxValue, null, 0, false, null, null, false);
            Assert.IsNull(Find<AspectFillImage>(detail, "ArtworkImage").sprite);
            Assert.IsTrue(Find<TMP_Text>(detail, "Crest").gameObject.activeSelf);
            Assert.IsNull(CardArtProvider.Load(0));
        }

        private static IEnumerator Capture(Canvas canvas, string name)
        {
            var cameraObject = new GameObject("Card UI capture", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            var texture = new RenderTexture(1920, 1080, 24);
            camera.targetTexture = texture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases();
            yield return null;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            pixels.Apply();
            string directory = Path.Combine(Application.dataPath, "../Artifacts");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name), pixels.EncodeToPNG());
            RenderTexture.active = previous;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            camera.targetTexture = null;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}
#endif
