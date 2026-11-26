#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ProjectC.Client;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class ResourceCrestPlayModeTests
    {
        [UnityTest]
        public IEnumerator AuthoredCrestsFollowServerAmountsAndLegacyKi()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            var screen = Object.FindFirstObjectByType<BattlePresenter>();
            Assert.IsNotNull(screen);
            screen.enabled = false;
            var own = screen.OwnPlayer;
            var enemy = screen.EnemyPlayer;
            Assert.IsNotNull(own);
            Assert.IsNotNull(enemy);
            Assert.IsNull(screen.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "ResourceContainer"));
            var ownSlots = Slots(own);
            var enemySlots = Slots(enemy);
            Assert.AreEqual(2, ownSlots.Length);
            Assert.AreEqual(2, enemySlots.Length);
            own.RenderResources(new JObject());
            enemy.RenderResources(new JObject());
            Assert.IsTrue(ownSlots.All(slot => !slot.gameObject.activeSelf));
            Assert.IsTrue(enemySlots.All(slot => !slot.gameObject.activeSelf));

            own.RenderResources(JObject.Parse("{\"Ki\":{\"Current\":0,\"Max\":9}}"));
            Assert.IsTrue(ownSlots[0].gameObject.activeSelf);
            Assert.IsFalse(ownSlots[1].gameObject.activeSelf);
            Assert.AreEqual("0", Amount(ownSlots[0]).text);
            Assert.AreEqual("ResourceCrest_Ki", Icon(ownSlots[0]).sprite.name);
            Assert.IsTrue(Icon(ownSlots[0]).preserveAspect);

            own.RenderResources(JObject.Parse("{\"Spirit\":{\"Current\":12,\"Max\":99},\"Ki\":{\"Current\":3,\"Max\":9}}"));
            Assert.AreEqual("3", Amount(ownSlots[0]).text);
            Assert.AreEqual("12", Amount(ownSlots[1]).text);
            Assert.AreEqual("ResourceCrest_Spirit", Icon(ownSlots[1]).sprite.name);
            own.RenderResources(JObject.Parse("{\"Ki\":{\"Current\":2,\"Max\":9},\"Spirit\":{\"Current\":11,\"Max\":99}}"));
            Assert.AreEqual("2", Amount(ownSlots[0]).text);
            Assert.AreEqual("11", Amount(ownSlots[1]).text);
            enemy.RenderResources(JObject.Parse("{\"Blood\":{\"Current\":5,\"Max\":10}}"));
            Assert.AreEqual("ResourceCrest_Blood", Icon(enemySlots[0]).sprite.name);
            Assert.AreEqual("5", Amount(enemySlots[0]).text);

            own.RenderResources(null);
            Assert.IsTrue(ownSlots.All(slot => !slot.gameObject.activeSelf));
            own.RenderResources(null, 7);
            Assert.AreEqual("7", Amount(ownSlots[0]).text);
            Assert.AreEqual("ResourceCrest_Ki", Icon(ownSlots[0]).sprite.name);
            own.RenderResources(new JObject(), 7);
            Assert.IsTrue(ownSlots.All(slot => !slot.gameObject.activeSelf),
                "An explicit empty Alpha02 Resources object must not gain legacy Ki.");
            yield return null;
        }

        private static RectTransform[] Slots(PlayerPanelView view)
        {
            return view.GetComponentsInChildren<RectTransform>(true)
                .Where(item => item.name.StartsWith("ResourceCrestSlot"))
                .OrderBy(item => item.name).ToArray();
        }

        private static Image Icon(RectTransform slot)
        {
            return slot.Find("CrestIcon").GetComponent<Image>();
        }

        private static TMP_Text Amount(RectTransform slot)
        {
            return slot.Find("AmountText").GetComponent<TMP_Text>();
        }
    }
}
#endif
