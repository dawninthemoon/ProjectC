using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ProjectC.Client
{
    // Client feedback mirrors the server's authored-cost scan; the server validates submitted IDs again.
    public static class Alpha02DeckValidator
    {
        private const int MaximumResourceTypes = 2;

        public static DeckValidation Validate(PersonalDeck deck, CardLibrary library)
        {
            var result = DeckValidator.Validate(deck, library);
            if (deck != null && ResourceIds(deck, library).Length > MaximumResourceTypes)
            {
                result.Errors.Add("특수 자원은 최대 2종까지 사용할 수 있습니다.");
            }
            return result;
        }

        public static string CannotAdd(PersonalDeck deck, CardLibrary library, int id)
        {
            string error = DeckValidator.CannotAdd(deck, library, id);
            if (error != null)
            {
                return error;
            }
            var candidate = deck.Copy();
            candidate.Cards.Add(id);
            return ResourceIds(candidate, library).Length > MaximumResourceTypes
                ? "특수 자원은 최대 2종까지 사용할 수 있습니다." : null;
        }

        public static string[] ResourceIds(PersonalDeck deck, CardLibrary library)
        {
            var types = new SortedSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<int>();
            if (deck == null || library == null)
            {
                return Array.Empty<string>();
            }
            foreach (int id in deck.Cards)
            {
                AddCard(id);
            }
            return types.ToArray();

            void AddCard(int id)
            {
                if (!visited.Add(id))
                {
                    return;
                }
                var card = library.Find(id);
                if (card == null)
                {
                    return;
                }
                AddCost(card.Data["SummonCostJson"]);
                foreach (var effect in card.Data["Effects"] as JArray ?? new JArray())
                {
                    foreach (var action in effect["Actions"] as JArray ?? new JArray())
                    {
                        AddCost(action["OptionalCost"]);
                    }
                }
                foreach (int form in card.AllowedManifestations)
                {
                    AddCard(form);
                }
            }

            void AddCost(JToken token)
            {
                if (token is JObject item)
                {
                    if (item["Costs"] is JArray costs)
                    {
                        foreach (var cost in costs)
                        {
                            if ((string)cost["Type"] == "Resource")
                            {
                                string resource = (string)cost["ResourceId"];
                                if (!string.IsNullOrEmpty(resource))
                                {
                                    types.Add(resource);
                                }
                            }
                        }
                    }
                    if (item["Ki"] != null)
                    {
                        types.Add("Ki");
                    }
                    foreach (var property in item.Properties())
                    {
                        if (property.Name != "Costs")
                        {
                            AddCost(property.Value);
                        }
                    }
                }
                else if (token is JArray array)
                {
                    foreach (var child in array)
                    {
                        AddCost(child);
                    }
                }
            }
        }
    }
}
