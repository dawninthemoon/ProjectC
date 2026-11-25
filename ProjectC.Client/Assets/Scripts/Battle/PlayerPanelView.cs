using System;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;

namespace ProjectC.Client
{
    public sealed class PlayerPanelView : MonoBehaviour
    {
        public TMP_Text Life;
        private bool _bound;
        private RectTransform _resourceContainer;
        private TMP_Text _resourceText;

        public void Render(PlayerView player, bool own, bool active, int maxYokai = 0)
        {
            if (!_bound)
            {
                if (!this.Bind("LifeText", out Life))
                {
                    throw new InvalidOperationException("Player panel requires its live health text.");
                }
                BattleText.StyleValue(Life);
                _bound = true;
            }
            Life.text = player.Life.ToString();
        }

        // Runtime fallback for the experimental catalog. The final themed art
        // and portrait anchors can replace this view without changing snapshots.
        public void RenderResources(JObject resources)
        {
            if (resources == null || !resources.HasValues)
            {
                if (_resourceContainer != null) _resourceContainer.gameObject.SetActive(false);
                return;
            }
            if (_resourceContainer == null)
            {
                var container = new GameObject("ResourceContainer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                _resourceContainer = (RectTransform)container.transform;
                _resourceContainer.SetParent(transform, false);
                _resourceContainer.anchorMin = _resourceContainer.anchorMax = new Vector2(.5f, 0f);
                _resourceContainer.pivot = new Vector2(.5f, 1f);
                _resourceContainer.anchoredPosition = new Vector2(0f, -6f);
                var background = container.GetComponent<Image>();
                background.color = new Color(.12f, .10f, .13f, .9f);
                background.raycastTarget = false;
                var label = new GameObject("ResourceAmounts", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                label.transform.SetParent(_resourceContainer, false);
                _resourceText = label.GetComponent<TextMeshProUGUI>();
                _resourceText.font = Life.font;
                _resourceText.fontSize = 18f;
                _resourceText.enableAutoSizing = true;
                _resourceText.fontSizeMin = 12f;
                _resourceText.fontSizeMax = 20f;
                _resourceText.alignment = TextAlignmentOptions.Center;
                _resourceText.color = new Color(.95f, .84f, .65f, 1f);
                _resourceText.raycastTarget = false;
                _resourceText.rectTransform.anchorMin = Vector2.zero;
                _resourceText.rectTransform.anchorMax = Vector2.one;
                _resourceText.rectTransform.offsetMin = new Vector2(4f, 2f);
                _resourceText.rectTransform.offsetMax = new Vector2(-4f, -2f);
            }
            _resourceContainer.sizeDelta = new Vector2(150f, resources.Count * 23f + 8f);
            _resourceContainer.gameObject.SetActive(true);
            _resourceText.text = YakouUiText.ResourceBalances(resources);
        }
    }
}
