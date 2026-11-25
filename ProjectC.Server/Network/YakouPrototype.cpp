#include "YakouPrototype.hpp"
#include <algorithm>
#include <fstream>
#include <map>
#include <limits>
#include <set>
#include <stdexcept>
#include <unordered_set>
#include <utility>

namespace ProjectC::Network
{
namespace
{
Json ReadFile(std::filesystem::path const& path)
{
    if (!std::filesystem::is_regular_file(path) || std::filesystem::file_size(path) > 1024 * 1024)
    {
        throw std::runtime_error("Missing or oversized Yakou prototype file: " + path.string());
    }
    std::ifstream file(path, std::ios::binary);
    if (!file)
    {
        throw std::runtime_error("Cannot open Yakou prototype file: " + path.string());
    }
    return ParseJson(std::string(std::istreambuf_iterator<char>(file), {}), 1024 * 1024);
}

void Require(bool condition, std::string const& detail)
{
    if (!condition)
    {
        throw std::runtime_error("Invalid Yakou prototype: " + detail);
    }
}

int PositiveId(Json const& value, std::string const& detail)
{
    Require(value.is_number_integer(), detail + ": integer required");
    auto const id = value.get<std::int64_t>();
    Require(id > 0 && id <= 1000000, detail + ": out of range");
    return static_cast<int>(id);
}

void ValidateCosts(Json const& card, std::string const& detail)
{
    auto const& costs = card.at("SummonCostJson");
    Require(costs.is_object(), detail + ": SummonCostJson must be an object");
    auto const& methods = card.at("SummonMethods");
    for (auto const& method : methods)
    {
        Require(method.is_string(), detail + ": method must be string");
        std::string const name = method.get<std::string>();
        Require(name == "Normal" || name == "Devour" || name == "Necromancy" ||
                    name == "Discard" || name == "Resource", detail + ": unknown summon method " + name);
        Require(costs.contains(name), detail + ": missing cost for " + name);
    }
    for (auto it = costs.begin(); it != costs.end(); ++it)
    {
        auto const& key = it.key();
        Require(key == "Normal" || key == "Devour" || key == "Necromancy" ||
                    key == "Discard" || key == "Activation" || key == "Manifestation" || key == "Resource",
                detail + ": unknown cost kind " + key);
        Require(it.value().is_object(), detail + ": cost must be object: " + key);
        if (it.value().contains("Costs"))
        {
            // A cost block cannot mix the old and new cost encodings.
            Require(it.value().size() == 1 ||
                        (key == "Manifestation" && it.value().size() == 2 && it.value().contains("Condition")),
                    detail + ": ambiguous typed cost block");
            auto const parsed = YakouParseTypedCost(it.value().at("Costs"));
            if (key == "Normal")
                Require(parsed.OrdinaryCount == 0 && parsed.Resources.empty(),
                        detail + ": Normal summon must be free");
            else if (key == "Resource")
                Require(!parsed.Resources.empty() && parsed.OrdinaryType != "Devour",
                        detail + ": Resource summon needs a special resource");
            else if (key == "Activation")
                Require(parsed.OrdinaryType != "Devour", detail + ": spell cannot devour");
            else if (key == "Manifestation")
                Require(parsed.OrdinaryType.empty(), detail + ": manifestation card-selection payment not wired");
            else
                Require(parsed.OrdinaryType == key && parsed.OrdinaryCount > 0,
                        detail + ": summon method and ordinary cost disagree");
            continue;
        }
        Require(key != "Resource", detail + ": resource summon needs typed Costs");
        if (key == "Devour" || key == "Necromancy" || key == "Discard")
        {
            Require(it.value().contains("Count") &&
                        it.value()["Count"].is_number_integer() &&
                        it.value()["Count"].get<int>() > 0 &&
                        it.value()["Count"].get<int>() <= 30,
                    detail + ": invalid exact cost for " + key);
        }
        if (key == "Activation")
        {
            for (auto inner = it.value().begin(); inner != it.value().end(); ++inner)
            {
                Require(inner.key() == "Necromancy" || inner.key() == "Discard",
                        detail + ": invalid activation cost");
                Require(inner.value().is_number_integer() &&
                            inner.value().get<int>() > 0 && inner.value().get<int>() <= 30,
                        detail + ": invalid activation count");
            }
        }
    }
}

} // namespace


YakouTypedCost YakouParseTypedCost(Json const& costs)
{
    Require(costs.is_array(), "Costs must be an array");
    YakouTypedCost result;
    for (auto const& cost : costs)
    {
        Require(cost.is_object() && cost.contains("Type") && cost.at("Type").is_string(),
                "Cost type is missing");
        auto const type = cost.at("Type").get<std::string>();
        if (type == "Resource")
        {
            Require(cost.size() == 3 && cost.contains("ResourceId") &&
                        cost.at("ResourceId").is_string() && cost.contains("Amount"),
                    "Malformed special resource cost");
            auto const resource = cost.at("ResourceId").get<std::string>();
            int const amount = PositiveId(cost.at("Amount"), "resource amount");
            Require(!resource.empty() && resource.size() <= 64 && amount <= 100000,
                    "Invalid special resource cost");
            auto& total = result.Resources[resource];
            Require(total <= 100000 - amount, "Resource amount overflow");
            total += amount;
        }
        else
        {
            Require((type == "Devour" || type == "Necromancy" || type == "Discard") &&
                        cost.size() == 2 && cost.contains("Count") &&
                        result.OrdinaryType.empty(), "Unsupported or ambiguous ordinary cost");
            int const count = PositiveId(cost.at("Count"), "ordinary cost count");
            Require(count <= 30, "Ordinary cost exceeds deck size");
            result.OrdinaryType = type;
            result.OrdinaryCount = static_cast<std::size_t>(count);
        }
    }
    return result;
}

YakouResourceDefinitions YakouReadResourceDefinitions(Json const& config)
{
    YakouResourceDefinitions result;
    if (!config.contains("resource.definitions"))
        return result; // Preserve the original Alpha 01 Ki contract.
    auto const& definitions = config.at("resource.definitions");
    Require(definitions.is_object(), "resource.definitions must be an object");
    Require(config.contains("deck.maxSpecialResourceTypes") &&
                config.at("deck.maxSpecialResourceTypes") == 2,
            "Alpha02 deck resource limit must be two");
    for (auto it = definitions.begin(); it != definitions.end(); ++it)
    {
        auto const& d = it.value();
        Require(!it.key().empty() && it.key().size() <= 64 && d.is_object() &&
                    d.contains("InitialValue") && d.contains("MaxValue") &&
                    d.contains("RegenPerOwnTurn") && d.at("InitialValue").is_number_integer() &&
                    d.at("RegenPerOwnTurn").is_number_integer(), "Invalid resource definition");
        int const maximum = PositiveId(d.at("MaxValue"), "resource maximum");
        auto const initial = d.at("InitialValue").get<std::int64_t>();
        auto const regen = d.at("RegenPerOwnTurn").get<std::int64_t>();
        Require(maximum <= 100000 && initial >= 0 && initial <= maximum &&
                    regen >= 0 && regen <= maximum, "Invalid resource bounds");
        result.emplace(it.key(), YakouResourceDefinition{
            static_cast<int>(initial), maximum, static_cast<int>(regen)});
    }
    return result;
}

namespace
{
void AddResourceTypes(Json const& object, std::set<std::string>& types)
{
    if (object.is_object())
    {
        if (object.contains("Costs"))
        {
            auto const parsed = YakouParseTypedCost(object.at("Costs"));
            for (auto const& [resource, amount] : parsed.Resources)
                types.insert(resource);
        }
        // Compatibility with the original authored manifestation cost.
        if (object.contains("Ki"))
        {
            PositiveId(object.at("Ki"), "legacy Ki cost");
            types.insert("Ki");
        }
        for (auto const& [key, value] : object.items())
            if (key != "Costs") AddResourceTypes(value, types);
    }
    else if (object.is_array())
        for (auto const& value : object) AddResourceTypes(value, types);
}

void AddCardResourceTypes(Json const& card, std::set<std::string>& types)
{
    AddResourceTypes(card.at("SummonCostJson"), types);
    for (auto const& effect : card.at("Effects"))
        for (auto const& action : effect.at("Actions"))
            if (action.contains("OptionalCost"))
                AddResourceTypes(action.at("OptionalCost"), types);
}
} // namespace

std::set<std::string> YakouDeckResourceTypes(Json const& cards, Json const& config,
                                              std::vector<int> const& deck)
{
    auto const definitions = YakouReadResourceDefinitions(config);
    if (!config.contains("resource.definitions")) return {"Ki"};
    std::set<std::string> types;
    std::set<int> visited;
    std::vector<int> pending = deck;
    while (!pending.empty())
    {
        int const id = pending.back();
        pending.pop_back();
        if (!visited.insert(id).second) continue;
        auto const it = std::find_if(cards.at("Cards").begin(), cards.at("Cards").end(),
                                     [id](Json const& c) { return c.at("Id") == id; });
        Require(it != cards.at("Cards").end(), "Unknown resource-bearing card");
        AddCardResourceTypes(*it, types);
        for (auto const& target : it->at("AllowedManifestations"))
            pending.push_back(PositiveId(target, "manifestation resource link"));
    }
    for (auto const& type : types)
        Require(definitions.contains(type), "Unknown special resource " + type);
    Require(types.size() <= 2, "Deck exceeds two special resource types");
    return types;
}

YakouCostRequirement YakouRequiredCost(Json const& card, std::string const& method)
{
    Require(card.is_object() && card.contains("SummonMethods") &&
                card["SummonMethods"].is_array() &&
                card.contains("SummonCostJson") &&
                card["SummonCostJson"].is_object(), "missing card cost definition");
    bool allowed = false;
    for (auto const& entry : card["SummonMethods"])
    {
        if (entry.is_string() && entry.get<std::string>() == method) allowed = true;
    }
    Require(allowed && card["SummonCostJson"].contains(method),
            "summon method not declared on card: " + method);
    auto const& cost = card["SummonCostJson"][method];
    if (cost.contains("Costs"))
    {
        auto const parsed = YakouParseTypedCost(cost.at("Costs"));
        auto const zone = parsed.OrdinaryType == "Devour" ? "FriendlyField" :
                          parsed.OrdinaryType == "Necromancy" ? "FriendlyGraveyard" :
                          parsed.OrdinaryType == "Discard" ? "FriendlyHand" : "";
        return {zone, parsed.OrdinaryCount, parsed.Resources};
    }
    if (method == "Normal") return YakouCostRequirement{"", 0};
    Require(cost.is_object() && cost.contains("Count") &&
                cost["Count"].is_number_integer() && cost["Count"].get<int>() > 0 &&
                cost["Count"].get<int>() <= 30, "invalid authored cost count");
    if (method == "Devour") return YakouCostRequirement{"FriendlyField", static_cast<std::size_t>(cost["Count"].get<int>())};
    if (method == "Necromancy") return YakouCostRequirement{"FriendlyGraveyard", static_cast<std::size_t>(cost["Count"].get<int>())};
    if (method == "Discard") return YakouCostRequirement{"FriendlyHand", static_cast<std::size_t>(cost["Count"].get<int>())};
    throw std::runtime_error("Unknown Yakou method: " + method);
}

bool ValidateYakouExactSelection(std::vector<int> const& candidates,
                                 std::vector<int> const& selected,
                                 std::size_t requiredCount)
{
    if (requiredCount == 0 || selected.size() != requiredCount)
    {
        return false;
    }
    std::unordered_set<int> allowed(candidates.begin(), candidates.end());
    std::unordered_set<int> seen;
    if (allowed.size() != candidates.size())
    {
        return false;
    }
    for (int id : selected)
    {
        if (id <= 0 || !allowed.contains(id) || !seen.insert(id).second)
        {
            return false;
        }
    }
    return true;
}

YakouPrototype YakouPrototype::Load(std::filesystem::path const& directory)
{
    return Validate(ReadFile(directory / "Cards.json"),
                    ReadFile(directory / "Decks.json"),
                    ReadFile(directory / "Localize.json"),
                    ReadFile(directory / "GameConfig.json"));
}

YakouPrototype YakouPrototype::Validate(Json const& cards,
                                        Json const& decks,
                                        Json const& localize,
                                        Json const& config)
{
    Require(cards.is_object() && cards.contains("CatalogVersion") &&
                cards["CatalogVersion"].is_string() && cards.contains("Cards") &&
                cards["Cards"].is_array(), "Cards.json format");
    Require(decks.is_object() && decks.contains("CatalogVersion") &&
                decks["CatalogVersion"] == cards["CatalogVersion"] &&
                decks.contains("Decks") && decks["Decks"].is_array(),
            "Decks.json version/format");
    Require(localize.is_array() && config.is_object(), "localization/config format");

    // This is the prototype's declared rule contract, not the legacy Decks.Config.
    for (auto key : {"deck.size", "hero.startHp", "field.monsterSlots",
                     "hand.startCount", "ki.start", "ki.max", "ki.baseRegenPerTurn",
                     "manifest.kiCost", "raid.autoAttackAtZero", "chain.order"})
    {
        Require(config.contains(key), std::string("missing setting ") + key);
    }
    Require(config["deck.size"] == 30 && config["hero.startHp"] == 20 &&
                config["field.monsterSlots"] == 5 && config["hand.startCount"] == 5,
            "unsupported prototype setup");
    Require(config["ki.start"] == 3 && config["ki.max"] == 3 &&
                config["ki.baseRegenPerTurn"] == 0 &&
                config["manifest.kiCost"] == 2 &&
                config["raid.autoAttackAtZero"] == true &&
                config["chain.order"] == "LastInFirstOut",
            "prototype Ki/raid/chain contract");
    Require(config.contains("summon.normalPerTurn") &&
                config["summon.normalPerTurn"] == 1 &&
                config.contains("summon.devourMaterialCount") &&
                config["summon.devourMaterialCount"] == 1,
            "prototype summon defaults");

    auto const resourceDefinitions = YakouReadResourceDefinitions(config);
    std::set<std::string> localizationKeys;
    for (auto const& row : localize)
    {
        Require(row.is_object() && row.contains("lmk") && row["lmk"].is_string(),
                "invalid localization row");
        auto key = row["lmk"].get<std::string>();
        Require(!key.empty() && localizationKeys.insert(key).second, "duplicate localization");
        Require(row.contains("korean") && row["korean"].is_string() &&
                    !row["korean"].get<std::string>().empty(), "missing Korean text");
    }

    std::set<int> ids;
    std::set<int> eligible;
    std::vector<std::pair<int, int>> manifestationLinks;
    std::vector<std::string> unsupported;
    for (auto const& card : cards["Cards"])
    {
        Require(card.is_object() && card.contains("Id") && card.contains("Type") &&
                    card["Type"].is_string() && card.contains("SummonMethods") &&
                    card["SummonMethods"].is_array() && card.contains("Effects") &&
                    card["Effects"].is_array() && card.contains("AllowedManifestations") &&
                    card["AllowedManifestations"].is_array() && card.contains("SummonCostJson"),
                "invalid card structure");
        int const id = PositiveId(card["Id"], "card ID");
        std::string const at = "card " + std::to_string(id);
        Require(ids.insert(id).second, at + ": duplicate ID");
        Require(card.contains("NameKey") && card["NameKey"].is_string() &&
                    localizationKeys.contains(card["NameKey"].get<std::string>()) &&
                    card.contains("DescriptionKey") && card["DescriptionKey"].is_string() &&
                    localizationKeys.contains(card["DescriptionKey"].get<std::string>()),
                at + ": missing localization key");
        auto const type = card["Type"].get<std::string>();
        Require(type == "Monster" || type == "Spell" || type == "Manifestation",
                at + ": unknown card type");
        if (type == "Monster" || type == "Manifestation")
        {
            Require(card.contains("Attack") && card["Attack"].is_number_integer() &&
                        card["Attack"].get<int>() >= 0 &&
                        card.contains("MaxHp") && card["MaxHp"].is_number_integer() &&
                        card["MaxHp"].get<int>() > 0 &&
                        card.contains("AttackInterval") &&
                        card["AttackInterval"].is_number_integer() &&
                        card["AttackInterval"].get<int>() > 0,
                    at + ": invalid monster stats/countdown");
        }
        bool const inDeck = !card.value("MainDeckEligible", true);
        Require(type != "Manifestation" || inDeck,
                at + ": manifestation eligible for main deck");
        if (type != "Manifestation" && !inDeck)
        {
            eligible.insert(id);
        }
        ValidateCosts(card, at);
        if (config.contains("resource.definitions"))
        {
            std::set<std::string> types;
            AddCardResourceTypes(card, types);
            for (auto const& resource : types)
                Require(resourceDefinitions.contains(resource), at + ": unknown resource " + resource);
        }
        for (auto const& linked : card["AllowedManifestations"])
        {
            manifestationLinks.emplace_back(id, PositiveId(linked, at + ": manifestation"));
        }
        std::set<int> effectIds;
        for (auto const& effect : card["Effects"])
        {
            Require(effect.is_object() && effect.contains("Id") &&
                        effect.contains("Trigger") && effect["Trigger"].is_string() &&
                        effect.contains("Actions") && effect["Actions"].is_array(),
                    at + ": effect format");
            Require(effectIds.insert(PositiveId(effect["Id"], at + ": effect ID")).second,
                    at + ": duplicate effect ID");
            for (auto const& action : effect["Actions"])
            {
                Require(action.is_object() && action.contains("Operation") &&
                            action["Operation"].is_string(),
                        at + ": invalid effect action");
                // The legacy runtime cannot execute this authoring dialect. Report
                // every operation explicitly instead of silently dropping effects.
                unsupported.push_back(at + ": " + effect["Trigger"].get<std::string>() +
                                      "/" + action["Operation"].get<std::string>());
            }
        }
    }
    for (auto const& [source, target] : manifestationLinks)
    {
        Require(ids.contains(target), "card " + std::to_string(source) +
                     ": missing manifestation " + std::to_string(target));
        auto it = std::find_if(cards["Cards"].begin(), cards["Cards"].end(),
                               [target](Json const& c) { return c["Id"] == target; });
        Require(it != cards["Cards"].end() && (*it)["Type"] == "Manifestation",
                "manifestation target is not a manifestation");
    }
    Require(!decks["Decks"].empty(), "no sample decks");
    std::set<std::string> deckIds;
    for (auto const& deck : decks["Decks"])
    {
        Require(deck.is_object() && deck.contains("Id") && deck["Id"].is_string() &&
                    deckIds.insert(deck["Id"].get<std::string>()).second &&
                    deck.contains("Cards") && deck["Cards"].is_array() &&
                    deck["Cards"].size() == 30, "invalid deck/size");
        (void)YakouDeckResourceTypes(cards, config, deck["Cards"].get<std::vector<int>>());
        std::map<int, int> copies;
        for (auto const& card : deck["Cards"])
        {
            int const id = PositiveId(card, "deck card");
            Require(eligible.contains(id), "deck contains ineligible/unknown card " +
                                             std::to_string(id));
            Require(++copies[id] <= 3, "too many copies: " + std::to_string(id));
        }
    }

    return YakouPrototype{cards, decks, localize, config, std::move(unsupported), false};
}
} // namespace ProjectC::Network
