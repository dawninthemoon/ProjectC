using System.Linq;
using UnityEngine;

namespace ProjectC.Client
{
    // Local catalog data is presentation-only. Commands continue to carry server IDs.
    public static class CardPresentation
    {
        static CardLibrary _fallback;
        public static CardLibrary Library => ClientApp.Instance?.Library ?? (_fallback ??= CardLibrary.LoadResources());

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            _fallback = null;
        }

        public static string Name(CardView card, string fallback = "") => card == null
            ? fallback : Library.Find(card.DefinitionId)?.Name ?? card.Name ?? fallback;

        public static FormattedCardText Description(CardView card)
        {
            if (ClientApp.Instance != null && !string.IsNullOrEmpty(ClientApp.Instance.Network.CatalogProblem))
            {
                // A mismatched local balance file must never supply guessed rule numbers.
                return new FormattedCardText(new[] { new TextSegment(card.RulesText ?? "") });
            }
            return Library.Find(card.DefinitionId)?.DescriptionText
                ?? new FormattedCardText(new[] { new TextSegment(card.RulesText ?? "") });
        }

        public static string Rules(CardView card) => card == null ? "" : Description(card).PlainText;

        public static string Manifestations(CardView card)
        {
            var definition = Library.Find(card.DefinitionId);
            return definition == null ? card.ManifestationText ?? ""
                : string.Join("\n", definition.AllowedManifestations.Select(id => Library.Find(id)?.Name ?? "#" + id));
        }
    }
}
