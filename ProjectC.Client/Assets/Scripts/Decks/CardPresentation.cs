using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ProjectC.Client
{
    // Local catalog data is presentation-only. Commands continue to carry server IDs.
    public static class CardPresentation
    {
        static CardLibrary _fallback;
        static CardLibrary _yakou;
        static CardLibrary _alpha02;
        public static CardLibrary Library => ClientApp.Instance?.Library ?? (_fallback ??= CardLibrary.LoadResources());
        public static CardLibrary YakouLibrary
        {
            get
            {
                bool alpha02 = (string)ClientApp.Instance?.Network?.Yakou?.State?["CatalogVersion"] == "projectc-alpha02-v1";
                var library = alpha02 ? (_alpha02 ??= CardLibrary.LoadAlpha02Resources())
                    : (_yakou ??= CardLibrary.LoadYakouResources());
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
            _alpha02 = null;
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

        public static string TypedCosts(LibraryCard card)
        {
            if (!(card?.Data?["SummonCostJson"] is JObject blocks)) return "";
            var methods = new List<string>();
            foreach (var block in blocks.Properties())
            {
                if (!(block.Value["Costs"] is JArray costs) || costs.Count == 0) continue;
                var parts = costs.Select(cost => (string)cost["Type"] == "Resource"
                    ? YakouUiText.ResourceName((string)cost["ResourceId"]) + " " + (int)cost["Amount"]
                    : YakouUiText.Method((string)cost["Type"]) + " " + (int)cost["Count"]);
                methods.Add(string.Join(" + ", parts));
            }
            return string.Join(" / ", methods);
        }

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
