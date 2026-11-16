using System;
using System.Collections.Generic;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class ContextActionView : MonoBehaviour
    {
        public Button ActionPrefab;
        readonly List<Button> buttons = new List<Button>();

        public void ShowChoices(Vector3 position, IReadOnlyList<KeyValuePair<string, Action>> choices)
        {
            if (!this.Bind("ActionContentImage", out RectTransform content) || ActionPrefab == null)
            {
                throw new InvalidOperationException("Context actions require ActionContentImage and ActionPrefab.");
            }
            foreach (var button in buttons)
            {
                button.gameObject.SetActive(false);
                button.onClick.RemoveAllListeners();
            }
            for (int index = 0; index < choices.Count; ++index)
            {
                if (index == buttons.Count)
                {
                    buttons.Add(Instantiate(ActionPrefab, content));
                }
                var choice = choices[index];
                var button = buttons[index];
                button.gameObject.SetActive(true);
                button.GetComponentInChildren<TMP_Text>().text = choice.Key;
                button.onClick.AddListener(() => choice.Value());
            }
            gameObject.SetActive(choices.Count > 0);
            if (choices.Count > 0)
            {
                ((RectTransform)transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    choices.Count * 48 + 20);
                PopupPosition.Place((RectTransform)transform, position);
            }
        }

        public void Show(CardWidget widget, Action<ActivationView> activate)
        {
            this.Bind("ActionContentImage", out RectTransform content);
            foreach (var b in buttons)
            {
                b.gameObject.SetActive(false);
                b.onClick.RemoveAllListeners();
            }
            int count = 0;
            if (widget?.Card?.Activations != null)
            {
                foreach (var action in widget.Card.Activations)
                {
                    if (!action.CanActivate)
                    {
                        continue;
                    }
                    if (count == buttons.Count)
                    {
                        buttons.Add(Instantiate(ActionPrefab, content));
                    }
                    var button = buttons[count++];
                    button.gameObject.SetActive(true);
                    string label = BattleText.Effect(widget.Card, action);
                    button.GetComponentInChildren<TMP_Text>().text = action.YokaiCost > 0
                        ? BattleText.Get("ui.effect.cost", label, action.YokaiCost) : label;
                    button.onClick.AddListener(() => activate(action));
                }
            }
            gameObject.SetActive(count > 0);
            if (count > 0)
            {
                ((RectTransform)transform).SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    count * 48 + 20
                );
                PopupPosition.Place(
                    (RectTransform)transform,
                    widget.WorldCenter + Vector3.up * 125
                );
            }
        }
    }
}
