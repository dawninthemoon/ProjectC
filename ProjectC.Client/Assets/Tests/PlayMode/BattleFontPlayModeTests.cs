#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class BattleFontPlayModeTests
    {
        [UnityTest]
        public IEnumerator FontRolesSwitchWithLanguageAndUseMatchingAtlasMaterials()
        {
            var palette = Resources.Load<BattleFontPalette>("UI/BattleFontPalette");
            Assert.IsNotNull(palette);
            var tile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FrontEnd/DeckTile.prefab");
            var cardName = tile.GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == "CardName");
            Assert.AreEqual(BattleFontRole.Heading, cardName.GetComponent<LocalizedBattleFont>().Role);
            var root = new GameObject("Font verification", typeof(RectTransform), typeof(Canvas));
            var labels = new Dictionary<BattleFontRole, TMP_Text>();
            var locale = CardPresentation.Library.Localization;
            string previous = locale.Language;
            try
            {
                foreach (BattleFontRole role in System.Enum.GetValues(typeof(BattleFontRole)))
                {
                    var go = new GameObject(role.ToString(), typeof(RectTransform), typeof(TextMeshProUGUI));
                    go.transform.SetParent(root.transform, false);
                    var text = go.GetComponent<TMP_Text>();
                    labels.Add(role, text);
                    go.AddComponent<LocalizedBattleFont>().Configure(role, palette);
                }
                foreach (string language in new[] { "korean", "japanese" })
                {
                    locale.Language = language;
                    yield return null;
                    yield return null;
                    bool japanese = language == "japanese";
                    AssertFont(labels[BattleFontRole.Heading], japanese ? "NotoSerifJP-Bold" : "NotoSerifKR-Bold");
                    AssertFont(labels[BattleFontRole.Body], japanese ? "PretendardJP-Medium" : "Pretendard-Medium");
                    AssertFont(labels[BattleFontRole.Hud], japanese ? "PretendardJP-SemiBold" : "Pretendard-SemiBold");
                    AssertFont(labels[BattleFontRole.Number], "Cinzel-Bold");
                    string sample = japanese ? "メイン バトル ターン終了 効果" : "메인 배틀 턴 종료 효과 설명";
                    foreach (BattleFontRole role in new[] { BattleFontRole.Heading, BattleFontRole.Body, BattleFontRole.Hud })
                    {
                        foreach (char character in sample)
                        {
                            Assert.IsTrue(labels[role].font.HasCharacter(character, false, true), role + ": " + character);
                        }
                    }
                    foreach (char character in "0123456789/+-")
                    {
                        Assert.IsTrue(labels[BattleFontRole.Number].font.HasCharacter(character, false, true), "Number: " + character);
                    }
                }
            }
            finally
            {
                locale.Language = previous;
                Object.DestroyImmediate(root);
            }
        }

        private static void AssertFont(TMP_Text text, string name)
        {
            Assert.AreEqual(name, text.font.sourceFontFile.name);
            Assert.AreSame(text.font.material, text.fontSharedMaterial);
            Assert.AreSame(text.font.atlasTexture, text.fontSharedMaterial.mainTexture);
        }
    }
}
#endif
