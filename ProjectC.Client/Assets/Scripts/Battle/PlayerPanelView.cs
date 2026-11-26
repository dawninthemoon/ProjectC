using System;
using System.Collections.Generic;
using Game.Utils;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class PlayerPanelView : MonoBehaviour
    {
        [Serializable]
        private struct ResourceCrest
        {
            public string ResourceId;
            public Sprite Sprite;
        }

        [Serializable]
        private struct ResourceSlot
        {
            public GameObject Root;
            public Image Icon;
            public TMP_Text Amount;
        }

        public TMP_Text Life;

        [SerializeField] private ResourceCrest[] _resourceCrests;
        [SerializeField] private ResourceSlot[] _resourceSlots;
        private bool _bound;
        private readonly HashSet<string> _warnedResourceIds = new HashSet<string>();

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

        public void RenderResources(JObject resources, int? legacyKi = null)
        {
            if (_resourceSlots == null || _resourceSlots.Length != 2)
            {
                throw new InvalidOperationException("Player panel requires two authored resource crest slots.");
            }
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            if (resources != null)
            {
                foreach (var property in resources.Properties())
                {
                    values.Add(property.Name, (int)property.Value["Current"]);
                }
            }
            else if (legacyKi.HasValue)
            {
                values.Add("Ki", legacyKi.Value);
            }
            var orderedIds = new List<string>();
            if (_resourceCrests != null)
            {
                foreach (var crest in _resourceCrests)
                {
                    if (values.ContainsKey(crest.ResourceId) && !orderedIds.Contains(crest.ResourceId))
                    {
                        orderedIds.Add(crest.ResourceId);
                    }
                }
            }
            var unknownIds = new List<string>();
            foreach (var resourceId in values.Keys)
            {
                if (!orderedIds.Contains(resourceId))
                {
                    unknownIds.Add(resourceId);
                }
            }
            unknownIds.Sort(StringComparer.Ordinal);
            orderedIds.AddRange(unknownIds);
            if (orderedIds.Count > _resourceSlots.Length)
            {
                Debug.LogWarning($"Player panel has {orderedIds.Count} resources but only two crest slots.", this);
            }
            for (int index = 0; index < _resourceSlots.Length; index++)
            {
                var slot = _resourceSlots[index];
                if (slot.Root == null || slot.Icon == null || slot.Amount == null)
                {
                    throw new InvalidOperationException("Player panel resource crest slot has a missing prefab reference.");
                }
                if (index >= orderedIds.Count)
                {
                    slot.Root.SetActive(false);
                    continue;
                }
                string resourceId = orderedIds[index];
                Sprite sprite = FindCrest(resourceId);
                if (sprite == null && _warnedResourceIds.Add(resourceId))
                {
                    Debug.LogWarning($"No resource crest sprite is mapped for '{resourceId}'.", this);
                }
                slot.Icon.enabled = sprite != null;
                if (slot.Icon.sprite != sprite)
                {
                    slot.Icon.sprite = sprite;
                }
                string amount = values[resourceId].ToString();
                if (slot.Amount.text != amount)
                {
                    slot.Amount.text = amount;
                }
                slot.Root.SetActive(true);
            }
        }

        private Sprite FindCrest(string resourceId)
        {
            if (_resourceCrests != null)
            {
                foreach (var crest in _resourceCrests)
                {
                    if (string.Equals(crest.ResourceId, resourceId, StringComparison.Ordinal))
                    {
                        return crest.Sprite;
                    }
                }
            }
            return null;
        }
    }
}
