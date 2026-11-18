using System.Linq;
using UnityEngine;

namespace ProjectC.Client
{
    // Local catalog data is presentation-only. Commands continue to carry server IDs.
    public static class CardPresentation
    {
        static CardLibrary _fallback;
        static CardLibrary _yakou;
        public static CardLibrary Library => ClientApp.Instance?.Library ?? (_fallback ??= CardLibrary.LoadResources());
        public static CardLibrary YakouLibrary
        {
            get
            {
                var library = _yakou ??= CardLibrary.LoadYakouResources();
                library.Localization.Language = Library.Localization.Language;
                return library;
            }
        }
        public static CardLibrary LibraryFor(CardView card) => card != null && card.Yakou
            ? YakouLibrary : Library;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            _fallback = null;
            _yakou = null;
        }

        public static string Name(CardView card, string fallback = "") => card == null
            ? fallback : LibraryFor(card).Find(card.DefinitionId)?.Name ?? card.Name ?? fallback;

        public static FormattedCardText Description(CardView card)
        {
            if (!card.Yakou && ClientApp.Instance != null && !string.IsNullOrEmpty(ClientApp.Instance.Network.CatalogProblem))
            {
                // A mismatched local balance file must never supply guessed rule numbers.
                return new FormattedCardText(new[] { new TextSegment(card.RulesText ?? "") });
            }
            return LibraryFor(card).Find(card.DefinitionId)?.DescriptionText
                ?? new FormattedCardText(new[] { new TextSegment(card.RulesText ?? "") });
        }

        public static string Costs(LibraryCard card) => card == null ? "" : string.Join(" / ",
            card.ActivationYokaiCosts.Distinct().Select(cost => BattleText.Get("ui.card.yokai_cost", cost)));

        public static string Rules(CardView card) => card == null ? "" : Description(card).PlainText;

        public static string Manifestations(CardView card)
        {
            var library = LibraryFor(card);
            var definition = library.Find(card.DefinitionId);
            return definition == null ? card.ManifestationText ?? ""
                : string.Join("\n", definition.AllowedManifestations.Select(id => library.Find(id)?.Name ?? "#" + id));
        }
    }
}
