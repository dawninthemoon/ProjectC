using System.Globalization;
using TMPro;
using UnityEngine;

namespace ProjectC.Client
{
    public static class BattleText
    {
        public static string Get(string key, params object[] arguments)
        {
            var text = CardPresentation.Library.Localization.GetText(key);
            return arguments.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, arguments);
        }

        // Shared treatment for prominent battle labels; keep their existing layout and font asset.
        public static void StyleHeading(TMP_Text label, bool prominent = false)
        {
            if (label == null)
            {
                return;
            }
            label.color = new Color(1f, .94f, .78f);
            // Each role uses its authored shared TMP preset rather than mutating the font asset.
        }

        public static void StyleValue(TMP_Text label)
        {
            StyleHeading(label);
            if (label != null)
            {
                label.color = new Color(1f, .96f, .84f);
            }
        }

        public static void StyleBody(TMP_Text label)
        {
            StyleHeading(label);
        }

        public static string Effect(CardView card, ActivationView activation)
        {
            string key = $"effect_{card.DefinitionId}_{activation.EffectId}_name";
            var localization = CardPresentation.Library.Localization;
            return activation.IsManifest ? Get("ui.effect.manifest")
                : localization.ContainsKey(key) ? localization.GetText(key)
                : Get("ui.effect.activate");
        }
    }
}
