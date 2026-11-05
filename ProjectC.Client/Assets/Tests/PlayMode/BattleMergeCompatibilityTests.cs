#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class BattleMergeCompatibilityTests
    {
        [Test]
        public void LocalCardPrefabsSupportServerAvailabilityAndInputPermissions()
        {
            var field = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleFieldCard.prefab"));
            var hand = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleCard.prefab"));
            try
            {
                var card = new CardView { Id = 1, DefinitionId = 1001, Attack = 3, Hp = 4, MaxHp = 4, CanAttack = true, CanSummon = true };
                var fieldView = field.GetComponent<CardWidget>();
                fieldView.Bind(card, false, false, false, false, true);
                var glow = Glow(fieldView);
                Assert.AreEqual(0f, glow.color.a, "Input lock must suppress an otherwise available attack.");
                fieldView.SetActionPermissions(true, false);
                Assert.Greater(glow.color.a, 0f);
                Assert.Greater(glow.color.g, glow.color.r);
                fieldView.SetActionPermissions(false, false);
                Assert.AreEqual(0f, glow.color.a);
                fieldView.SetSelected(true);
                Assert.Greater(glow.color.r, glow.color.g);
                fieldView.Bind(card, false, false, true, false, false);
                Assert.Greater(glow.color.b, glow.color.r, "An enemy can still be an effect target.");
                fieldView.Bind(card, false, false, false, false, false);
                fieldView.SetActionPermissions(true, true);
                Assert.AreEqual(0f, glow.color.a, "Enemy availability must not look like a local action.");
                var handView = hand.GetComponent<CardWidget>();
                handView.Bind(card, false, false, false, true);
                Assert.Greater(Glow(handView).color.a, 0f, "Keep the local hand's server-provided playability highlight.");
                Assert.AreEqual(new Vector2(166, 183), fieldView.Face.rect.size);
                Assert.AreEqual(new Vector2(194, 248), handView.Face.rect.size);
            }
            finally
            {
                Object.DestroyImmediate(field);
                Object.DestroyImmediate(hand);
            }
        }

        private static Image Glow(CardWidget widget)
        {
            return widget.GetComponentsInChildren<Image>(true).Single(image => image.name == "FrameGlow");
        }
    }
}
#endif
