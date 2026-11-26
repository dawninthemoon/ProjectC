using System;
using System.Collections;
using Game.Utils;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace ProjectC.Client
{
    // The server supplies every link and outcome. This view only plays their public order.
    public sealed class YakouChainResolutionView : MonoBehaviour
    {
        private const int MaximumCards = 8;
        private const float CardPitch = 174f;
        private const float InitialDelay = .2f;
        private const float HighlightDelay = .3f;
        private const float RemovalDelay = .15f;
        private const float FinalDelay = .2f;
        private readonly RectTransform[] _slots = new RectTransform[MaximumCards];
        private readonly CardWidget[] _cards = new CardWidget[MaximumCards];
        private readonly TMP_Text[] _numbers = new TMP_Text[MaximumCards];
        private CanvasGroup _group;
        private Coroutine _playback;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            for (int index = 0; index < MaximumCards; ++index)
            {
                string suffix = (index + 1).ToString();
                if (!this.Bind("ChainSlot" + suffix, out _slots[index]) ||
                    !_slots[index].Bind("ChainCard" + suffix, out _cards[index]) ||
                    !_slots[index].Bind("ChainNumber" + suffix, out _numbers[index]))
                {
                    throw new InvalidOperationException("Yakou chain resolution prefab is incomplete.");
                }
            }
        }

        public void Play(JArray resolution)
        {
            Stop();
            if (resolution == null || resolution.Count == 0 || resolution.Count > MaximumCards)
            {
                return;
            }
            gameObject.SetActive(true);
            _playback = StartCoroutine(Animate(resolution));
        }

        public void Stop()
        {
            if (_playback != null)
            {
                StopCoroutine(_playback);
                _playback = null;
            }
            foreach (var slot in _slots)
            {
                if (slot != null)
                {
                    slot.gameObject.SetActive(false);
                }
            }
            gameObject.SetActive(false);
        }

        private IEnumerator Animate(JArray resolution)
        {
            _group.alpha = 1;
            int count = resolution.Count;
            // Resolution is LIFO. Fixed positions retain the activation order.
            for (int index = 0; index < count; ++index)
            {
                var entry = resolution[count - index - 1];
                var card = entry["Card"];
                var view = new CardView
                {
                    Yakou = true,
                    Id = (int)card["Id"],
                    DefinitionId = (int)card["DefinitionId"],
                    CardType = (int)card["CardType"],
                    Attack = (int)card["Attack"],
                    Hp = (int)card["Hp"],
                    MaxHp = (int)card["MaxHp"],
                };
                var slot = _slots[index];
                slot.gameObject.SetActive(true);
                slot.anchoredPosition = new Vector2((index - (count - 1) * .5f) * CardPitch, 0);
                slot.localScale = Vector3.one;
                _cards[index].Bind(view, false, false, false, false);
                _cards[index].SetActionPermissions(false, false, false);
                _numbers[index].text = (index + 1).ToString();
                _numbers[index].gameObject.SetActive(true);
                slot.GetComponent<CanvasGroup>().alpha = 1;
            }
            yield return new WaitForSecondsRealtime(InitialDelay);
            for (int order = 0; order < count; ++order)
            {
                int index = count - order - 1;
                var slot = _slots[index];
                slot.localScale = Vector3.one * 1.15f;
                for (int other = 0; other < index; ++other)
                {
                    _slots[other].GetComponent<CanvasGroup>().alpha = .55f;
                }
                yield return new WaitForSecondsRealtime(HighlightDelay);
                string result = (string)resolution[order]["Result"];
                if (result == "PendingSelection")
                {
                    Stop();
                    yield break;
                }
                if (result == "Negated" || result == "Fizzled")
                {
                    slot.GetComponent<CanvasGroup>().alpha = .3f;
                }
                yield return new WaitForSecondsRealtime(RemovalDelay);
                slot.gameObject.SetActive(false);
            }
            yield return new WaitForSecondsRealtime(FinalDelay);
            Stop();
        }
    }
}
