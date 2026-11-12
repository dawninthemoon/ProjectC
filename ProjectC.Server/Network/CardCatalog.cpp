#include "CardCatalog.hpp"
#include "ProjectC/GameCommand.hpp"
#include "ProjectC/Zone.hpp"
#include <algorithm>
#include <charconv>
#include <filesystem>
#include <fstream>
#include <map>
#include <random>
#include <regex>
#include <stdexcept>
#include <utility>
namespace ProjectC::Network
{
using namespace GameCore;
namespace
{
constexpr std::size_t MaximumCardDataBytes = 1024 * 1024;
constexpr std::size_t MaximumDescriptionPathLength = 256;
constexpr std::size_t MaximumDescriptionArguments = 64;

Json NormalizeCards(Json const& input)
{
    Json result = input.is_array() ? Json{{"Cards", input}} : input;
    if (!result.is_object() || !result.contains("Cards") || !result["Cards"].is_array())
    {
        throw std::runtime_error("Expected Cards array");
    }
    std::string version = result.value("CatalogVersion", std::string{});
    for (auto& row : result["Cards"])
    {
        if (!row.is_object())
        {
            throw std::runtime_error("Expected card object");
        }
        for (auto field :
             {"Archetypes", "AllowedManifestations", "Effects", "DescriptionArgs", "Traits", "SummonMethods"})
        {
            if (row.contains(field) && row[field].is_string())
            {
                auto text = row[field].get<std::string>();
                row[field] = text.empty() ? Json::array() : ParseJson(text, MaximumCardDataBytes);
            }
            if (row.contains(field) && !row[field].is_array())
            {
                throw std::runtime_error("Expected card data array");
            }
        }
        for (auto field : {"Id", "Attack", "MaxHp", "AttackInterval"})
        {
            if (!row.contains(field) || !row[field].is_string())
            {
                continue;
            }
            auto const text = row[field].get<std::string>();
            if (text.empty() && std::string(field) != "Id")
            {
                row.erase(field);
                continue;
            }
            std::int64_t number = 0;
            auto parsed = std::from_chars(text.data(), text.data() + text.size(), number);
            if (parsed.ec != std::errc{} || parsed.ptr != text.data() + text.size())
            {
                throw std::runtime_error("Invalid card integer string");
            }
            row[field] = number;
        }
        if (row.contains("SummonCostJson") && row["SummonCostJson"].is_string())
        {
            auto const costText = row["SummonCostJson"].get<std::string>();
            row["SummonCostJson"] = costText.empty() ? Json::object()
                                                    : ParseJson(costText, MaximumCardDataBytes);
        }
        if (row.contains("SummonCostJson") && !row["SummonCostJson"].is_object())
        {
            throw std::runtime_error("SummonCostJson must be a JSON object");
        }
        if (row.contains("CatalogVersion"))
        {
            auto rowVersion = String(row, "CatalogVersion", 64);
            if (!version.empty() && rowVersion != version)
            {
                throw std::runtime_error("Mixed CatalogVersion values");
            }
            version = rowVersion;
            row.erase("CatalogVersion");
        }
    }
    if (!version.empty())
    {
        result["CatalogVersion"] = version;
    }
    return result;
}

Json File(std::filesystem::path const& path)
{
    if (std::filesystem::file_size(path) > 1024 * 1024)
    {
        throw std::runtime_error("Data file too large");
    }
    std::ifstream stream(path);
    if (!stream)
    {
        throw std::runtime_error("Cannot open data file");
    }
    return ParseJson(std::string(std::istreambuf_iterator<char>(stream), {}), 1024 * 1024);
}
template <typename T>
T Enum(Json const& value, char const* key, std::initializer_list<std::pair<char const*, T>> names)
{
    auto name = String(value, key);
    for (auto const& [text, item] : names)
    {
        if (name == text)
        {
            return item;
        }
    }
    throw std::runtime_error("Invalid enum in card data");
}
Zone Location(Json const& value)
{
    return Enum<Zone>(Json{{"Zone", value}},
                      "Zone",
                      {{"Deck", Zone::Deck},
                       {"Hand", Zone::Hand},
                       {"Field", Zone::Field},
                       {"Graveyard", Zone::Graveyard},
                       {"Exile", Zone::Exile},
                       {"HiddenExile", Zone::HiddenExile}});
}
std::string Text(Json const& value, char const* key)
{
    auto const& field = value.at(key);
    if (!field.is_string())
    {
        throw std::runtime_error("Expected card data text");
    }
    auto text = field.get<std::string>();
    if (text.empty() || text.size() > 256)
    {
        throw std::runtime_error("Invalid card data text length");
    }
    for (unsigned char c : text)
    {
        if (c < 32)
        {
            throw std::runtime_error("Control character in card data text");
        }
    }
    return text;
}
void Array(Json const& value, std::size_t maximum)
{
    if (!value.is_array() || value.size() > maximum)
    {
        throw std::runtime_error("Invalid data array");
    }
}
TargetRule Rule(Json const& value)
{
    Fields(value,
           {"Category",
            "Zones",
            "MinCount",
            "MaxCount",
            "RequiredTrait",
            "CardType",
            "RequiredArchetype",
            "MaxAttack"});
    TargetRule rule;
    rule.Category = Enum<TargetCategory>(value,
                                         "Category",
                                         {{"Self", TargetCategory::Self},
                                          {"FriendlyMonster", TargetCategory::FriendlyMonster},
                                          {"EnemyMonster", TargetCategory::EnemyMonster},
                                          {"AnyMonster", TargetCategory::AnyMonster},
                                          {"FriendlyPlayer", TargetCategory::FriendlyPlayer},
                                          {"EnemyPlayer", TargetCategory::EnemyPlayer},
                                          {"FriendlyDeck", TargetCategory::FriendlyDeck},
                                          {"FriendlyHand", TargetCategory::FriendlyHand},
                                          {"FriendlyGraveyard", TargetCategory::FriendlyGraveyard},
                                          {"EnemyGraveyard", TargetCategory::EnemyGraveyard}});
    if (value.contains("Zones"))
    {
        rule.Zones.clear();
        Array(value["Zones"], 4);
        for (auto const& zone : value["Zones"])
        {
            rule.Zones.push_back(Location(zone));
        }
    }
    if (value.contains("MinCount"))
    {
        rule.MinCount = Number(value, "MinCount", 128);
    }
    if (value.contains("MaxCount"))
    {
        rule.MaxCount = Number(value, "MaxCount", 128);
    }
    if (value.contains("RequiredTrait"))
    {
        rule.RequiredTrait = Text(value, "RequiredTrait");
    }
    if (value.contains("CardType"))
    {
        rule.Type = Enum<CardType>(
            value,
            "CardType",
            {{"Monster", CardType::Monster}, {"Spell", CardType::Spell}, {"Any", CardType::Any}});
    }
    if (value.contains("RequiredArchetype"))
    {
        rule.RequiredArchetype = Text(value, "RequiredArchetype");
    }
    if (value.contains("MaxAttack"))
    {
        rule.MaxAttack = static_cast<int>(Number(value, "MaxAttack", 100000));
    }
    if (rule.Zones.empty() || rule.MinCount > rule.MaxCount || rule.MaxCount == 0)
    {
        throw std::runtime_error("Targets: empty zones or invalid selection count");
    }
    auto requireZone = [&](Zone zone)
    {
        if (rule.Zones != std::vector<Zone>{zone})
        {
            throw std::runtime_error("Targets.Category conflicts with Targets.Zones");
        }
    };
    switch (rule.Category)
    {
    case TargetCategory::FriendlyDeck:
        requireZone(Zone::Deck);
        break;
    case TargetCategory::FriendlyHand:
        requireZone(Zone::Hand);
        break;
    case TargetCategory::FriendlyGraveyard:
    case TargetCategory::EnemyGraveyard:
        requireZone(Zone::Graveyard);
        break;
    case TargetCategory::FriendlyMonster:
    case TargetCategory::EnemyMonster:
    case TargetCategory::AnyMonster:
        requireZone(Zone::Field);
        if (rule.Type == CardType::Spell)
        {
            throw std::runtime_error("Monster target category cannot target CardType Spell");
        }
        break;
    default:
        break;
    }
    return rule;
}
EffectDefinition Effect(Json const& value)
{
    Fields(value,
           {"Id",
            "Type",
            "Trigger",
            "Actions",
            "ActivationZone",
            "Costs",
            "PerTurnLimit",
            "RequiredGraveyardArchetype",
            "MinGraveyardCount",
            "RequireGraveyardEntryThisTurn"});
    EffectDefinition effect;
    effect.Id = static_cast<int>(Number(value, "Id", 1000000));
    effect.Type = Enum<EffectType>(
        value,
        "Type",
        {{"Activated", EffectType::Activated}, {"Triggered", EffectType::Triggered}});
    if (value.contains("Trigger"))
    {
        effect.Trigger =
            Enum<TriggerCondition>(value,
                                   "Trigger",
                                   {{"None", TriggerCondition::None},
                                    {"OnSummoned", TriggerCondition::OnSummoned},
                                    {"OnDestroyed", TriggerCondition::OnDestroyed},
                                    {"OnDamaged", TriggerCondition::OnDamaged},
                                    {"OnTurnStarted", TriggerCondition::OnTurnStarted},
                                    {"OnTurnEnded", TriggerCondition::OnTurnEnded},
                                    {"OnNormalSummoned", TriggerCondition::OnNormalSummoned},
                                    {"OnRevived", TriggerCondition::OnRevived},
                                    {"OnManifested", TriggerCondition::OnManifested}});
    }
    if (value.contains("ActivationZone"))
    {
        effect.ActivationZone = Location(value["ActivationZone"]);
    }
    if (value.contains("PerTurnLimit"))
    {
        effect.PerTurnLimit = static_cast<int>(Number(value, "PerTurnLimit", 128));
    }
    if (value.contains("RequiredGraveyardArchetype"))
    {
        effect.RequiredGraveyardArchetype = Text(value, "RequiredGraveyardArchetype");
    }
    if (value.contains("MinGraveyardCount"))
    {
        effect.MinGraveyardCount = Number(value, "MinGraveyardCount", 128);
    }
    if (value.contains("RequireGraveyardEntryThisTurn"))
    {
        effect.RequireGraveyardEntryThisTurn = Text(value, "RequireGraveyardEntryThisTurn");
    }
    if (value.contains("Costs"))
    {
        Array(value["Costs"], 16);
        for (auto const& entry : value["Costs"])
        {
            Fields(entry, {"Kind", "Amount", "Targets"});
            CostDefinition cost;
            cost.Kind = Enum<CostKind>(entry,
                                       "Kind",
                                       {{"YokaiPower", CostKind::YokaiPower},
                                        {"Life", CostKind::Life},
                                        {"Discard", CostKind::Discard},
                                        {"Tribute", CostKind::Tribute},
                                        {"HiddenExileTop", CostKind::HiddenExileTop}});
            if (entry.contains("Amount"))
            {
                cost.Amount = static_cast<int>(Number(entry, "Amount", 100000));
            }
            if (entry.contains("Targets"))
            {
                cost.Targets = Rule(entry["Targets"]);
            }
            if ((cost.Kind == CostKind::Discard || cost.Kind == CostKind::Tribute) &&
                !entry.contains("Targets"))
            {
                throw std::runtime_error("Card cost requires target rule");
            }
            if (cost.Kind != CostKind::Discard && cost.Kind != CostKind::Tribute &&
                !entry.contains("Amount"))
            {
                throw std::runtime_error("Resource cost requires Amount");
            }
            effect.Costs.push_back(cost);
        }
    }
    Array(value.at("Actions"), 16);
    for (auto const& entry : value["Actions"])
    {
        Fields(entry,
               {"Operation",
                "Targets",
                "Amount",
                "AttackDelta",
                "MaxHpDelta",
                "TransformDefinitionId",
                "AllTargets",
                "TemporaryStats",
                "PreventDirectAttackThisTurn",
                "YokaiGainArchetype",
                "YokaiGainAmount"});
        EffectAction action;
        action.Operation =
            Enum<EffectOperation>(entry,
                                  "Operation",
                                  {{"Draw", EffectOperation::Draw},
                                   {"Mill", EffectOperation::Mill},
                                   {"Salvage", EffectOperation::Salvage},
                                   {"Search", EffectOperation::Search},
                                   {"SummonFromDeck", EffectOperation::SummonFromDeck},
                                   {"SummonFromGraveyard", EffectOperation::SummonFromGraveyard},
                                   {"ReturnToHand", EffectOperation::ReturnToHand},
                                   {"Destroy", EffectOperation::Destroy},
                                   {"DealDamage", EffectOperation::DealDamage},
                                   {"Heal", EffectOperation::Heal},
                                   {"ModifyStats", EffectOperation::ModifyStats},
                                   {"Transform", EffectOperation::Transform},
                                   {"Discard", EffectOperation::Discard},
                                   {"SendToGraveyard", EffectOperation::SendToGraveyard},
                                   {"SummonFromHand", EffectOperation::SummonFromHand},
                                   {"Exile", EffectOperation::Exile},
                                   {"GainYokaiPower", EffectOperation::GainYokaiPower}});
        action.Targets = Rule(entry.at("Targets"));
        if (entry.contains("Amount"))
        {
            action.Amount = static_cast<int>(Number(entry, "Amount", 100000));
        }
        for (auto key : {"AttackDelta", "MaxHpDelta"})
        {
            if (entry.contains(key))
            {
                if (!entry[key].is_number_integer() ||
                    (entry[key].is_number_unsigned() && entry[key].get<std::uint64_t>() > 100000))
                {
                    throw std::runtime_error("Invalid stat delta");
                }
                auto delta = entry[key].get<std::int64_t>();
                if (delta < -100000 || delta > 100000)
                {
                    throw std::runtime_error("Invalid stat delta");
                }
                if (std::string(key) == "AttackDelta")
                {
                    action.AttackDelta = static_cast<int>(delta);
                }
                else
                {
                    action.MaxHpDelta = static_cast<int>(delta);
                }
            }
        }
        if (entry.contains("TransformDefinitionId"))
        {
            action.TransformDefinitionId =
                static_cast<int>(Number(entry, "TransformDefinitionId", 1000000));
        }
        if (entry.contains("AllTargets"))
        {
            action.AllTargets = Boolean(entry, "AllTargets");
        }
        if (entry.contains("TemporaryStats"))
        {
            action.TemporaryStats = Boolean(entry, "TemporaryStats");
        }
        if (entry.contains("PreventDirectAttackThisTurn"))
        {
            action.PreventDirectAttackThisTurn = Boolean(entry, "PreventDirectAttackThisTurn");
        }
        if (entry.contains("YokaiGainArchetype"))
        {
            action.YokaiGainArchetype = Text(entry, "YokaiGainArchetype");
        }
        if (entry.contains("YokaiGainAmount"))
        {
            action.YokaiGainAmount = static_cast<int>(Number(entry, "YokaiGainAmount", 100000));
        }
        effect.Actions.push_back(action);
    }
    return effect;
}
} // namespace
std::string DefaultDataDirectory()
{
    return PROJECTC_DATA_DIR;
}
CardCatalog CardCatalog::Load(std::string const& directory)
{
    try
    {
        return FromJson(File(std::filesystem::path(directory) / "Cards.json"),
                        File(std::filesystem::path(directory) / "Decks.json"));
    }
    catch (std::exception const& error)
    {
        throw std::runtime_error(directory + "/Cards.json or Decks.json: " + error.what());
    }
}
CardCatalog CardCatalog::FromJson(Json const& cardInput, Json const& decks)
{
    auto const cards = NormalizeCards(cardInput);
    Fields(cards, {"Cards", "CatalogVersion"});
    Fields(decks, {"Config", "Decks"});
    CardCatalog catalog;
    if (cards.contains("CatalogVersion"))
    {
        catalog.Version = String(cards, "CatalogVersion", 64);
    }
    auto const& config = decks.at("Config");
    Fields(config,
           {"InitialLife",
            "InitialHandSize",
            "DrawPerTurn",
            "FieldLimit",
            "HandLimit",
            "MinDeckSize",
            "MaxDeckSize",
            "MaxCardCopies",
            "InitialYokaiPower",
            "YokaiPowerPerTurn",
            "MaxYokaiPower",
            "SummoningSickness",
            "EnableMulligan"});
    catalog.Config.InitialLife = static_cast<int>(Number(config, "InitialLife", 100000));
    catalog.Config.InitialHandSize = Number(config, "InitialHandSize", 128);
    catalog.Config.DrawPerTurn = Number(config, "DrawPerTurn", 128);
    catalog.Config.FieldLimit = Number(config, "FieldLimit", 32);
    catalog.Config.HandLimit = Number(config, "HandLimit", 128);
    catalog.Config.MinDeckSize = Number(config, "MinDeckSize", 128);
    catalog.Config.MaxDeckSize = Number(config, "MaxDeckSize", 128);
    catalog.Config.MaxCardCopies = Number(config, "MaxCardCopies", 128);
    if (config.contains("InitialYokaiPower"))
    {
        catalog.Config.InitialYokaiPower =
            static_cast<int>(Number(config, "InitialYokaiPower", 100000));
    }
    if (config.contains("YokaiPowerPerTurn"))
    {
        catalog.Config.YokaiPowerPerTurn =
            static_cast<int>(Number(config, "YokaiPowerPerTurn", 100000));
    }
    if (config.contains("MaxYokaiPower"))
    {
        catalog.Config.MaxYokaiPower = static_cast<int>(Number(config, "MaxYokaiPower", 100000));
    }
    if (config.contains("SummoningSickness"))
    {
        catalog.Config.SummoningSickness = Boolean(config, "SummoningSickness");
    }
    if (config.contains("EnableMulligan"))
    {
        catalog.Config.EnableMulligan = Boolean(config, "EnableMulligan");
    }
    catalog.Config.AllowManualDraw = false;
    if (!ValidateConfig(catalog.Config) || catalog.Config.MinDeckSize == 0 ||
        catalog.Config.MinDeckSize > catalog.Config.MaxDeckSize ||
        catalog.Config.MaxCardCopies == 0 || catalog.Config.FieldLimit == 0)
    {
        throw std::runtime_error("Invalid deck configuration");
    }
    Array(cards.at("Cards"), 1024);
    for (auto const& entry : cards["Cards"])
    {
        Fields(entry,
               {"Id",
                "Name",
                "Attack",
                "MaxHp",
                "Traits",
                "AllowedManifestations",
                "Effects",
                "Type",
                "Archetypes",
                "Rarity",
                "ArtKey",
                "MainDeckEligible",
                "NameKey",
                "DescriptionKey",
                "DescriptionArgs",
                "AttackInterval",
                "SummonMethods",
                "SummonCostJson"});
        CardDefinition card;
        card.Id = static_cast<int>(Number(entry, "Id", 1000000));
        if (entry.contains("NameKey") || entry.contains("DescriptionKey"))
        {
            card.NameKey = Text(entry, "NameKey");
            card.DescriptionKey = Text(entry, "DescriptionKey");
        }
        // Legacy clients still receive Name. Keyed catalogs carry a neutral key,
        // never a hardcoded translation; current clients localize DefinitionId.
        card.Name = entry.contains("Name") ? Text(entry, "Name") : card.NameKey;
        if (card.Name.empty())
        {
            throw std::runtime_error("Name or localization keys are required");
        }
        if (entry.contains("DescriptionArgs"))
        {
            Array(entry["DescriptionArgs"], MaximumDescriptionArguments);
            static std::regex const pathGrammar(
                R"(^[A-Za-z_][A-Za-z0-9_]*(\[[0-9]+\]|\.[A-Za-z_][A-Za-z0-9_]*)*$)");
            for (auto const& argument : entry["DescriptionArgs"])
            {
                Fields(argument, {"path", "default"});
                DescriptionArgument parsed;
                parsed.Path = String(argument, "path", MaximumDescriptionPathLength);
                if (!std::regex_match(parsed.Path, pathGrammar))
                {
                    throw std::runtime_error("Invalid description data path");
                }
                if (argument.contains("default"))
                {
                    auto const& fallback = argument["default"];
                    if (!fallback.is_primitive() || fallback.is_null())
                    {
                        throw std::runtime_error("Description default must be a scalar");
                    }
                    parsed.Default =
                        fallback.is_string() ? fallback.get<std::string>() : fallback.dump();
                }
                card.DescriptionArgs.push_back(std::move(parsed));
            }
        }
        if (entry.contains("Type"))
        {
            // Manifestations share the monster runtime type, but cannot appear in decks.
            card.IsManifestation = entry["Type"] == "Manifestation";
            card.Type = Enum<CardType>(entry,
                                       "Type",
                                       {{"Monster", CardType::Monster},
                                        {"Spell", CardType::Spell},
                                        {"Manifestation", CardType::Monster}});
            if (card.IsManifestation)
            {
                card.MainDeckEligible = false;
            }
        }
        if (card.Type == CardType::Monster)
        {
            card.Attack = static_cast<int>(Number(entry, "Attack", 100000));
            card.MaxHp = static_cast<int>(Number(entry, "MaxHp", 100000));
        }
        if (entry.contains("AttackInterval"))
        {
            card.AttackInterval = static_cast<int>(Number(entry, "AttackInterval", 64));
            if (card.Type != CardType::Monster)
            {
                throw std::runtime_error("AttackInterval is monster-only");
            }
        }
        if (entry.contains("SummonMethods"))
        {
            Array(entry["SummonMethods"], 8);
            for (auto const& method : entry["SummonMethods"])
            {
                auto name = Text(Json{{"Value", method}}, "Value");
                if (name != "Normal" && name != "Devour" && name != "Necromancy" &&
                    name != "Discard")
                {
                    throw std::runtime_error("Unknown summon method");
                }
                if (std::find(card.SummonMethods.begin(), card.SummonMethods.end(), name) !=
                    card.SummonMethods.end())
                {
                    throw std::runtime_error("Duplicate summon method");
                }
                card.SummonMethods.push_back(std::move(name));
            }
            if (card.Type != CardType::Monster || card.IsManifestation)
            {
                if (!card.SummonMethods.empty())
                {
                    throw std::runtime_error("SummonMethods must be empty for spells and manifestations");
                }
            }
        }
        if (entry.contains("SummonCostJson"))
        {
            if (!entry["SummonCostJson"].is_object())
            {
                throw std::runtime_error("SummonCostJson must be an object");
            }
            card.SummonCostJson = entry["SummonCostJson"].dump();
        }
        if (entry.contains("Archetypes"))
        {
            Array(entry["Archetypes"], 16);
            for (auto const& archetype : entry["Archetypes"])
            {
                card.Archetypes.push_back(Text(Json{{"Value", archetype}}, "Value"));
            }
        }
        if (entry.contains("Rarity"))
        {
            card.Rarity = Text(entry, "Rarity");
        }
        if (entry.contains("ArtKey"))
        {
            card.ArtKey = Text(entry, "ArtKey");
        }
        if (entry.contains("MainDeckEligible"))
        {
            card.MainDeckEligible = Boolean(entry, "MainDeckEligible");
            if (card.IsManifestation && card.MainDeckEligible)
            {
                throw std::runtime_error("Manifestation cannot be a main-deck card");
            }
        }
        if (entry.contains("Traits"))
        {
            Array(entry["Traits"], 16);
            for (auto const& trait : entry["Traits"])
            {
                card.Traits.push_back(Text(Json{{"Value", trait}}, "Value"));
            }
        }
        if (entry.contains("AllowedManifestations"))
        {
            Array(entry["AllowedManifestations"], 16);
            for (auto const& id : entry["AllowedManifestations"])
            {
                card.AllowedManifestations.push_back(
                    static_cast<int>(Number(Json{{"Id", id}}, "Id", 1000000)));
            }
        }
        if (entry.contains("Effects"))
        {
            Array(entry["Effects"], 16);
            for (auto const& effect : entry["Effects"])
            {
                EffectDefinition parsed;
                try
                {
                    parsed = Effect(effect);
                }
                catch (std::exception const& error)
                {
                    throw std::runtime_error("Card " + std::to_string(card.Id) + ".Effects[" +
                                             std::to_string(card.Effects.size()) +
                                             "]: " + error.what());
                }
                if (card.Type == CardType::Spell && !effect.contains("ActivationZone"))
                {
                    parsed.ActivationZone = Zone::Hand;
                }
                card.Effects.push_back(parsed);
            }
        }
        catalog.Definitions.push_back(card);
    }
    Array(decks.at("Decks"), 32);
    for (auto const& entry : decks["Decks"])
    {
        Fields(entry, {"Id", "Name", "Cards"});
        DeckDefinition deck{String(entry, "Id", 64), Text(entry, "Name"), {}};
        if (catalog.FindDeck(deck.Id))
        {
            throw std::runtime_error("Duplicate deck ID");
        }
        Array(entry.at("Cards"), 128);
        for (auto const& id : entry["Cards"])
        {
            deck.Cards.push_back(static_cast<int>(Number(Json{{"Id", id}}, "Id", 1000000)));
        }
        if (!catalog.ValidateDeck(deck.Cards))
        {
            throw std::runtime_error("Invalid deck composition");
        }
        catalog.Decks.push_back(deck);
    }
    if (catalog.Decks.empty())
    {
        throw std::runtime_error("At least one playable deck required");
    }
    // Core validates IDs, stats, effects, target rules and transformation links before serving
    // clients.
    for (auto const& deck : catalog.Decks)
    {
        auto sample = catalog.Initialize({1, 2}, {deck.Id, deck.Id}, 1, 1);
        if (!ValidateGameState(sample) ||
            (!sample.Config.EnableMulligan &&
             ProcessCommand(sample, StartTurnCommand{1}).Code != CommandError::Success))
        {
            throw std::runtime_error("Invalid card definitions or initial draw configuration");
        }
    }
    return catalog;
}
DeckDefinition const* CardCatalog::FindDeck(std::string const& id) const
{
    for (auto const& deck : Decks)
    {
        if (deck.Id == id)
        {
            return &deck;
        }
    }
    return nullptr;
}
bool CardCatalog::ValidateDeck(std::vector<CardDefinitionId> const& cards) const
{
    if (cards.size() < Config.MinDeckSize || cards.size() > Config.MaxDeckSize ||
        cards.size() < Config.InitialHandSize)
    {
        return false;
    }
    std::map<int, std::size_t> counts;
    for (auto id : cards)
    {
        bool known = false;
        for (auto const& definition : Definitions)
        {
            if (definition.Id == id)
            {
                known = definition.MainDeckEligible;
            }
        }
        if (!known || ++counts[id] > Config.MaxCardCopies)
        {
            return false;
        }
    }
    return true;
}
std::string CardCatalog::PersonalDeckError(std::vector<CardDefinitionId> const& cards) const
{
    if (Config.MinDeckSize != PersonalDeckCardCount ||
        Config.MaxDeckSize != PersonalDeckCardCount ||
        Config.MaxCardCopies != MaximumCopiesPerPersonalDeckCard)
    {
        return "DeckRulesMismatch";
    }
    if (cards.size() != PersonalDeckCardCount)
    {
        return "DeckCardCount";
    }
    std::map<int, std::size_t> counts;
    for (auto id : cards)
    {
        auto definition = std::find_if(
            Definitions.begin(), Definitions.end(), [&](auto const& d) { return d.Id == id; });
        if (definition == Definitions.end())
        {
            return "DeckUnknownCard";
        }
        if (!definition->MainDeckEligible)
        {
            return "DeckIneligibleCard";
        }
        if (++counts[id] > Config.MaxCardCopies)
        {
            return "DeckCopyLimit";
        }
    }
    return ValidateDeck(cards) ? "" : "DeckRulesMismatch";
}
Json CardCatalog::PublicDecks() const
{
    Json result = Json::array();
    for (auto const& deck : Decks)
    {
        result.push_back({{"Id", deck.Id}, {"Name", deck.Name}, {"CardCount", deck.Cards.size()}});
    }
    return result;
}
GameState CardCatalog::Initialize(std::array<int, 2> players,
                                  std::array<std::string, 2> const& decks,
                                  std::uint64_t seed,
                                  int firstCardId) const
{
    std::array<std::vector<CardDefinitionId>, 2> cards;
    for (int i = 0; i < 2; ++i)
    {
        auto deck = FindDeck(decks[i]);
        if (!deck)
        {
            throw std::runtime_error("Invalid selected deck");
        }
        cards[i] = deck->Cards;
    }
    return InitializeCards(players, cards, seed, firstCardId);
}
GameState CardCatalog::InitializeCards(std::array<int, 2> players,
                                       std::array<std::vector<CardDefinitionId>, 2> const& decks,
                                       std::uint64_t seed,
                                       int firstCardId) const
{
    std::mt19937_64 random(seed);
    std::array<PlayerSetup, 2> setup;
    for (int i = 0; i < 2; ++i)
    {
        if (!ValidateDeck(decks[i]))
        {
            throw std::runtime_error("Invalid selected deck");
        }
        setup[i] = {players[i], decks[i]};
        for (std::size_t n = setup[i].Deck.size(); n > 1; --n)
        {
            auto const bound = static_cast<std::uint64_t>(n);
            auto const threshold = (std::uint64_t{0} - bound) % bound;
            std::uint64_t value;
            do
            {
                value = random();
            } while (value < threshold);
            std::swap(setup[i].Deck[n - 1], setup[i].Deck[value % bound]);
        }
    }
    GameState state;
    if (InitializeGame(state, Config, Definitions, setup, players[0], seed) !=
        InitializationResult::Success)
    {
        throw std::runtime_error("Invalid card data");
    }
    for (auto& player : state.Players)
    {
        for (auto zone : {Zone::Deck, Zone::Hand, Zone::Field, Zone::Graveyard})
        {
            for (auto& card : *GetZone(player, zone))
            {
                card.Id += firstCardId - 1;
            }
        }
    }
    return state;
}
} // namespace ProjectC::Network
