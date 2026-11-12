using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProjectC.Client
{
    public sealed class CardDataDocument
    {
        public string CatalogVersion { get; internal set; }
        public JArray Cards { get; internal set; }
    }

    public static class CardDataLoader
    {
        const int MaximumJsonLength = 2 * 1024 * 1024;
        const int MaximumDepth = 64;
        const int MaximumCards = 10000;
        const int MaximumDefinitionId = 1000000;
        const int MaximumStat = 100000;

        public static JToken Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumJsonLength)
            {
                throw new FormatException("Invalid card/localization JSON size.");
            }
            using (var reader = new JsonTextReader(new StringReader(text))
            {
                MaxDepth = MaximumDepth,
                DateParseHandling = DateParseHandling.None,
            })
            {
                var value = JToken.Load(reader, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                });
                if (reader.Read())
                {
                    throw new FormatException("Unexpected trailing JSON data.");
                }
                return value;
            }
        }

        public static CardDataDocument Load(string text) => Load(text, false);

        internal static CardDataDocument Load(string text, bool allowManifestations)
        {
            var root = Parse(text);
            var rows = root as JArray ?? root["Cards"] as JArray;
            if (rows == null || rows.Count > MaximumCards)
            {
                throw new FormatException("A card array is required.");
            }
            string version = root is JObject ? (string)root["CatalogVersion"] ?? "" : "";
            var cards = new JArray();
            var ids = new System.Collections.Generic.HashSet<int>();
            foreach (var token in rows)
            {
                if (!(token is JObject source))
                {
                    throw new FormatException("A card row must be an object.");
                }
                var card = (JObject)source.DeepClone();
                foreach (string field in new[] { "Archetypes", "AllowedManifestations", "Effects", "DescriptionArgs", "Traits" })
                {
                    if (card[field]?.Type == JTokenType.String)
                    {
                        string value = (string)card[field];
                        card[field] = string.IsNullOrWhiteSpace(value) ? new JArray() : Parse(value);
                    }
                    if (card[field] != null && !(card[field] is JArray))
                    {
                        throw new FormatException(field + " must be an array.");
                    }
                }
                int id = ReadInteger(card["Id"], 1, MaximumDefinitionId);
                if (!ids.Add(id))
                {
                    throw new FormatException("Duplicate card definition #" + id);
                }
                card["Id"] = id;
                string type = RequiredText(card, "Type");
                if (type != "Monster" && type != "Spell" && !(allowManifestations && type == "Manifestation"))
                {
                    throw new FormatException("Unknown card type: " + type);
                }
                if (type == "Manifestation" && card["MainDeckEligible"]?.Type != JTokenType.Boolean)
                {
                    throw new FormatException("Manifestations require explicit deck eligibility.");
                }
                if (type == "Manifestation" && (bool)card["MainDeckEligible"])
                {
                    throw new FormatException("Manifestations cannot be main-deck eligible.");
                }
                bool hasMonsterStats = type == "Monster" || type == "Manifestation";
                foreach (string field in new[] { "Attack", "MaxHp" })
                {
                    if (card[field]?.Type == JTokenType.String && string.IsNullOrWhiteSpace((string)card[field]))
                    {
                        card.Remove(field);
                    }
                    if (card[field] != null || hasMonsterStats)
                    {
                        card[field] = ReadInteger(card[field], field == "MaxHp" && hasMonsterStats ? 1 : 0, MaximumStat);
                    }
                }
                bool keyed = card["NameKey"] != null || card["DescriptionKey"] != null;
                if (keyed)
                {
                    RequiredText(card, "NameKey");
                    RequiredText(card, "DescriptionKey");
                }
                else
                {
                    RequiredText(card, "Name");
                }
                if (card["MainDeckEligible"] != null && card["MainDeckEligible"].Type != JTokenType.Boolean)
                {
                    throw new FormatException("MainDeckEligible must be a boolean.");
                }
                foreach (var archetype in card["Archetypes"] ?? new JArray())
                {
                    if (archetype.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)archetype))
                    {
                        throw new FormatException("Invalid card archetype.");
                    }
                }
                if (card["AllowedManifestations"] is JArray manifestations)
                {
                    for (int index = 0; index < manifestations.Count; ++index)
                    {
                        manifestations[index] = ReadInteger(manifestations[index], 1, MaximumDefinitionId);
                    }
                }
                if ((card["Effects"] ?? new JArray()).Any(effect => !(effect is JObject)))
                {
                    throw new FormatException("An effect must be an object.");
                }
                foreach (var argument in card["DescriptionArgs"] ?? new JArray())
                {
                    if (!(argument is JObject spec))
                    {
                        throw new FormatException("DescriptionArgs entries must be objects.");
                    }
                    CardTextFormatter.ValidatePath(RequiredText(spec, "path"));
                    if (spec.Properties().Any(property => property.Name != "path" && property.Name != "default")
                        || spec["default"] is JContainer)
                    {
                        throw new FormatException("Invalid description argument.");
                    }
                }
                if (card["CatalogVersion"] != null)
                {
                    string rowVersion = RequiredText(card, "CatalogVersion");
                    if (version.Length != 0 && version != rowVersion)
                    {
                        throw new FormatException("Mixed CatalogVersion values.");
                    }
                    version = rowVersion;
                    card.Remove("CatalogVersion");
                }
                cards.Add(card);
            }
            return new CardDataDocument { CatalogVersion = version, Cards = cards };
        }

        internal static string RequiredText(JObject value, string field)
        {
            if (value[field]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)value[field]))
            {
                throw new FormatException("Missing text field: " + field);
            }
            return (string)value[field];
        }

        internal static int ReadInteger(JToken value, int minimum, int maximum)
        {
            string text = value?.Type == JTokenType.String || value?.Type == JTokenType.Integer
                ? value.ToString() : "";
            if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number)
                || number < minimum || number > maximum)
            {
                throw new FormatException("Invalid integer: " + text);
            }
            return number;
        }
    }
}
