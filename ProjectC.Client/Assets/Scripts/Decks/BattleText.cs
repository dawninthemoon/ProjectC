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

        public static void StyleHeading(TMP_Text label, bool prominent = false)
        {
            if (label == null)
            {
                return;
            }
            label.color = new Color(1f, .94f, .78f);
            LocalizedBattleFont.SetRole(label, BattleFontRole.Heading);
        }

        public static void StyleValue(TMP_Text label)
        {
            if (label != null)
            {
                label.color = new Color(1f, .96f, .84f);
                LocalizedBattleFont.SetRole(label, BattleFontRole.Number);
            }
        }

        public static void StyleBody(TMP_Text label)
        {
            if (label != null)
            {
                LocalizedBattleFont.SetRole(label, BattleFontRole.Body);
            }
        }

        public static void StyleHud(TMP_Text label)
        {
            if (label != null)
            {
                LocalizedBattleFont.SetRole(label, BattleFontRole.Hud);
            }
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
