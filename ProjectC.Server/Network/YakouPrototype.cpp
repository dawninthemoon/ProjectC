#include "YakouPrototype.hpp"
#include <algorithm>
#include <fstream>
#include <map>
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
                    name == "Discard", detail + ": unknown summon method " + name);
        Require(costs.contains(name), detail + ": missing cost for " + name);
    }
    for (auto it = costs.begin(); it != costs.end(); ++it)
    {
        auto const& key = it.key();
        Require(key == "Normal" || key == "Devour" || key == "Necromancy" ||
                    key == "Discard" || key == "Activation" || key == "Manifestation",
                detail + ": unknown cost kind " + key);
        Require(it.value().is_object(), detail + ": cost must be object: " + key);
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
