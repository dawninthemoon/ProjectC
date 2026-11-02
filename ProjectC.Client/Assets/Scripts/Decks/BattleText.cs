using System.Globalization;

namespace ProjectC.Client
{
    public static class BattleText
    {
        public static string Get(string key, params object[] arguments)
        {
            var text = CardPresentation.Library.Localization.GetText(key);
            return arguments.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, arguments);
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
