using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProjectC.Client
{
    public sealed class LibraryCard
    {
        public int Id,
            Attack,
            MaxHp;
        public string Type,
            Rarity,
            ArtKey;
        public string NameKey,
            DescriptionKey;
        internal string LegacyName, LegacyDescription;
        internal JObject Data;
        internal LocalizationService Localization;
        static readonly CardTextFormatter Formatter = new CardTextFormatter();
        public string Name => string.IsNullOrEmpty(NameKey) ? LegacyName : Localization.GetText(NameKey);
        public FormattedCardText DescriptionText => string.IsNullOrEmpty(DescriptionKey)
            ? new FormattedCardText(new[] { new TextSegment(LegacyDescription ?? "") })
            : Formatter.Format(Localization.GetText(DescriptionKey), Data, Data["DescriptionArgs"] as JArray);
        public string Description => DescriptionText.PlainText;
        // Printed cost feedback only. The server validates and pays actual activation costs.
        public int[] ActivationYokaiCosts => (Data?["Effects"] as JArray ?? new JArray())
            .Where(effect => (string)effect["Type"] == "Activated")
            .Select(effect => (effect["Costs"] as JArray ?? new JArray())
                .Where(cost => (string)cost["Kind"] == "YokaiPower")
                .Sum(cost => (int?)cost["Amount"] ?? 0)).ToArray();
        public int[] AllowedManifestations => Data?["AllowedManifestations"]?.Values<int>().ToArray() ?? Array.Empty<int>();
        public JObject CopyData() => (JObject)Data.DeepClone();
        public bool MainDeckEligible;
        public string[] Archetypes;
    }

    public sealed class CardLibrary
    {
        readonly Dictionary<int, LibraryCard> cards = new Dictionary<int, LibraryCard>();
        public string CatalogVersion { get; private set; } = "";
        public LocalizationService Localization { get; private set; }
        public IEnumerable<LibraryCard> Cards => cards.Values.OrderBy(c => c.Id);

        public LibraryCard Find(int id) => cards.TryGetValue(id, out var card) ? card : null;

        public static JObject ParseObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 2 * 1024 * 1024)
            {
                throw new FormatException("JSON 크기가 올바르지 않습니다.");
            }
            using (
                var reader = new JsonTextReader(new StringReader(text))
                {
                    MaxDepth = 64,
                    DateParseHandling = DateParseHandling.None,
                }
            )
            {
                var value = JObject.Load(
                    reader,
                    new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    }
                );
                if (reader.Read())
                {
                    throw new FormatException("JSON 뒤에 추가 데이터가 있습니다.");
                }
                return value;
            }
        }

        internal static int Integer(JToken value)
        {
            if (value?.Type != JTokenType.Integer || (long)value < 1 || (long)value > int.MaxValue)
            {
                throw new FormatException("카드 ID가 올바르지 않습니다.");
            }
            return (int)value;
        }

        public static CardLibrary LoadResources()
        {
            var localize = UnityEngine.Resources.Load<UnityEngine.TextAsset>("ProjectC_Data - Localize");
            var cards = UnityEngine.Resources.Load<UnityEngine.TextAsset>("CardData/HyakkiCards");
            if (localize == null || cards == null)
            {
                throw new InvalidOperationException("Card/localization Resources are missing.");
            }
            return Load(cards.text, LocalizationService.Load(localize.text));
        }

        public static CardLibrary Load(string text, LocalizationService localization = null)
        {
            var document = CardDataLoader.Load(text);
            var items = document.Cards;
            if (localization == null && items.Any(item => item["NameKey"] != null))
            {
                var resource = UnityEngine.Resources.Load<UnityEngine.TextAsset>("ProjectC_Data - Localize");
                if (resource == null)
                {
                    throw new InvalidOperationException("Localization Resources are missing.");
                }
                localization = LocalizationService.Load(resource.text);
            }
            var library = new CardLibrary
            {
                CatalogVersion = document.CatalogVersion,
                Localization = localization,
            };
            foreach (var item in items)
            {
                int id = Integer(item["Id"]);
                if (library.cards.ContainsKey(id))
                {
                    throw new FormatException("중복 카드 정의 #" + id);
                }
                if (
                    (item["Name"]?.Type != JTokenType.String && item["NameKey"]?.Type != JTokenType.String)
                    || item["Type"]?.Type != JTokenType.String
                )
                {
                    throw new FormatException("카드 이름과 타입이 필요합니다.");
                }
                string type = (string)item["Type"];
                if (type != "Monster" && type != "Spell")
                {
                    throw new FormatException("지원하지 않는 카드 타입입니다.");
                }
                if (
                    item["MainDeckEligible"] != null
                    && item["MainDeckEligible"].Type != JTokenType.Boolean
                )
                {
                    throw new FormatException("MainDeckEligible은 bool입니다.");
                }
                var card = new LibraryCard
                {
                    Id = id,
                    LegacyName = (string)item["Name"],
                    NameKey = (string)item["NameKey"],
                    DescriptionKey = (string)item["DescriptionKey"],
                    Data = (JObject)item,
                    Localization = localization,
                    Type = type,
                    Attack = (int?)item["Attack"] ?? 0,
                    MaxHp = (int?)item["MaxHp"] ?? 0,
                    Rarity = (string)item["Rarity"] ?? "Common",
                    ArtKey = (string)item["ArtKey"] ?? "",
                    MainDeckEligible = (bool?)item["MainDeckEligible"] ?? true,
                    Archetypes =
                        item["Archetypes"]?.Values<string>().ToArray() ?? Array.Empty<string>(),
                    LegacyDescription = item["DescriptionKey"] == null ? Describe(item) : "",
                };
                // Validate argument paths and indices before binding any UI.
                _ = card.DescriptionText;
                library.cards.Add(id, card);
            }
            return library;
        }

        public IEnumerable<LibraryCard> Filter(
            string name,
            string type,
            string archetype,
            string rarity
        ) =>
            Cards.Where(c =>
                (
                    string.IsNullOrEmpty(name)
                    || c.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                )
                && (string.IsNullOrEmpty(type) || c.Type == type)
                && (string.IsNullOrEmpty(archetype) || c.Archetypes.Contains(archetype))
                && (string.IsNullOrEmpty(rarity) || c.Rarity == rarity)
            );

        public static PersonalDeck Starter(string text)
        {
            var item = ParseObject(text)["Decks"]?.First;
            if (item == null || !(item["Cards"] is JArray values))
            {
                throw new FormatException("기본 덱이 없습니다.");
            }
            return new PersonalDeck
            {
                Name = (string)item["Name"] ?? "백귀야행",
                Cards = values.Select(Integer).ToList(),
            };
        }

        public static IReadOnlyList<PersonalDeck> Samples(string text, CardLibrary library)
        {
            var rows = ParseObject(text)["Decks"] as JArray;
            if (rows == null || rows.Count == 0)
            {
                throw new FormatException("Sample deck array is missing.");
            }
            var result = new List<PersonalDeck>();
            var ids = new HashSet<string>();
            foreach (var row in rows.OfType<JObject>())
            {
                string id = CardDataLoader.RequiredText(row, "Id");
                if (!ids.Add(id) || !(row["Cards"] is JArray values))
                {
                    throw new FormatException("Duplicate or invalid sample deck: " + id);
                }
                var deck = new PersonalDeck
                {
                    DeckId = id,
                    Name = CardDataLoader.RequiredText(row, "Name"),
                    Cards = values.Select(Integer).ToList(),
                };
                var validation = DeckValidator.Validate(deck, library);
                if (!validation.CanPlay)
                {
                    throw new FormatException("Invalid sample deck " + id + ": " + validation.Summary);
                }
                result.Add(deck);
            }
            if (result.Count != rows.Count)
            {
                throw new FormatException("Sample deck entries must be objects.");
            }
            return result.AsReadOnly();
        }

        static string Describe(JToken card)
        {
            var lines = new List<string>();
            foreach (var effect in card["Effects"] ?? new JArray())
            {
                string trigger = (string)effect["Trigger"] ?? "Activated";
                var labels = new Dictionary<string, string>
                {
                    { "Activated", "발동" },
                    { "OnNormalSummoned", "일반 소환 시" },
                    { "OnSummoned", "소환 시" },
                    { "OnRevived", "부활 시" },
                    { "OnManifested", "현현 시" },
                };
                lines.Add(labels.TryGetValue(trigger, out var label) ? label : trigger);
                foreach (var cost in effect["Costs"] ?? new JArray())
                {
                    lines.Add(
                        "비용: "
                            + ((string)cost["Kind"] ?? "?")
                            + " "
                            + ((int?)cost["Amount"] ?? (int?)cost["Targets"]?["MinCount"] ?? 1)
                    );
                }
                if ((int?)effect["PerTurnLimit"] > 0)
                {
                    lines.Add("같은 이름의 카드 합계 턴당 " + effect["PerTurnLimit"] + "회");
                }
                if (effect["RequireGraveyardEntryThisTurn"] != null)
                {
                    lines.Add("이번 턴 해당 카드군이 묘지에 들어갔을 때 발동");
                }
                if (effect["MinGraveyardCount"] != null)
                {
                    lines.Add("묘지 카드군 " + effect["MinGraveyardCount"] + "장 이상 필요");
                }
                foreach (var action in effect["Actions"] ?? new JArray())
                {
                    var operations = new Dictionary<string, string>
                    {
                        { "Search", "덱에서 손패로" },
                        { "SendToGraveyard", "덱에서 묘지로" },
                        { "SummonFromHand", "손패에서 특수 소환" },
                        { "SummonFromGraveyard", "묘지에서 부활" },
                        { "Salvage", "묘지에서 손패로" },
                        { "Transform", "현현" },
                        { "Draw", "드로우" },
                        { "Discard", "손패 버리기" },
                        { "Destroy", "파괴" },
                        { "DealDamage", "피해" },
                        { "Exile", "제외" },
                        { "ModifyStats", "능력치 변경" },
                    };
                    string op = (string)action["Operation"] ?? "?";
                    var target = action["Targets"];
                    string detail = operations.TryGetValue(op, out var translated)
                        ? translated
                        : op;
                    if (action["Amount"] != null)
                    {
                        detail += " " + action["Amount"];
                    }
                    if (target?["RequiredArchetype"] != null)
                    {
                        detail += " · " + target["RequiredArchetype"];
                    }
                    if (target?["CardType"] != null)
                    {
                        detail += " · " + target["CardType"];
                    }
                    if (target?["MaxAttack"] != null)
                    {
                        detail += " · 공격력 ≤ " + target["MaxAttack"];
                    }
                    if (action["TransformDefinitionId"] != null)
                    {
                        detail += " #" + action["TransformDefinitionId"];
                    }
                    if ((bool?)action["PreventDirectAttackThisTurn"] == true)
                    {
                        detail += " · 이번 턴 직접 공격 불가";
                    }
                    if (action["YokaiGainAmount"] != null)
                    {
                        detail += " · 조건 충족 시 요력 +" + action["YokaiGainAmount"];
                    }
                    if (action["AttackDelta"] != null)
                    {
                        detail += " · 공격력 +" + action["AttackDelta"];
                    }
                    if ((bool?)action["TemporaryStats"] == true)
                    {
                        detail += " (턴 종료까지)";
                    }
                    lines.Add(detail);
                }
            }
            return lines.Count == 0 ? "카드 효과 없음" : string.Join("\n", lines);
        }
    }
}
