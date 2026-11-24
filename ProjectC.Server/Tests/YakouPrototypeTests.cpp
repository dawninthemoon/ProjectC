#include "YakouPrototype.hpp"
#include "CardCatalog.hpp"
#include <algorithm>
#include <filesystem>
#include <iostream>
#include <map>
#include <set>
#include <stdexcept>

using namespace ProjectC::Network;

namespace
{
void Check(bool condition, char const* message)
{
    if (!condition) throw std::runtime_error(message);
}
template<class Callback> void Reject(Callback run)
{
    bool rejected = false;
    try { run(); } catch (std::exception const&) { rejected = true; }
    Check(rejected, "Malformed Yakou data was accepted");
}
}

int main()
{
    try
    {
        auto const data = std::filesystem::path(DefaultDataDirectory()) / "YakouPrototype";
        auto prototype = YakouPrototype::Load(data);
        Check(prototype.Cards["Cards"].size() == 55, "Expected 55 Alpha 01 definitions");
        Check(prototype.Decks["Decks"].size() == 4, "Expected four Alpha 01 decks");
        std::set<int> definitionIds;
        for (auto const& card : prototype.Cards["Cards"])
        {
            Check(definitionIds.insert(card.at("Id").get<int>()).second,
                  "Duplicate Alpha 01 definition ID");
            Check(card.at("CatalogVersion") == "yakou-2026-11-23-alpha01-v1",
                  "Mixed Alpha 01 card versions");
        }
        for (int id = 4001; id <= 4021; ++id)
        {
            Check(definitionIds.contains(id), "Missing authored Alpha 01 card ID");
        }
        std::set<std::string> localizedKeys;
        for (auto const& row : prototype.Localize)
        {
            auto const key = row.at("lmk").get<std::string>();
            localizedKeys.insert(key);
            if (key.starts_with("ui.yakou.deck.") || key.starts_with("card_40"))
            {
                Check(!row.at("korean").get<std::string>().empty() &&
                          !row.at("english").get<std::string>().empty() &&
                          !row.at("japanese").get<std::string>().empty(),
                      "Incomplete Alpha 01 localization");
            }
        }
        for (int id = 4001; id <= 4021; ++id)
        {
            Check(localizedKeys.contains("card_" + std::to_string(id) + "_name") &&
                      localizedKeys.contains("card_" + std::to_string(id) + "_desc"),
                  "Missing new card localization key");
        }
        Check(std::count_if(localizedKeys.begin(), localizedKeys.end(),
                  [](std::string const& key) { return key.starts_with("ui.yakou.deck."); }) == 13,
              "Expected thirteen localized deck selection keys");
        for (auto const& deck : prototype.Decks["Decks"])
        {
            Check(deck["Cards"].size() == 30, "Expected 30 deck copies");
            std::map<int, int> copies;
            for (auto const& id : deck["Cards"])
            {
                int const definitionId = id.get<int>();
                Check(definitionIds.contains(definitionId) && ++copies[definitionId] <= 3,
                      "Invalid Alpha 01 deck card or copy count");
            }
        }
        Check(!prototype.Localize.empty() && !prototype.Config.empty(),
              "Expected prototype text/config");
        Check(!prototype.RuntimeReady && !prototype.UnsupportedRuntimeEffects.empty(),
              "Unsupported card actions must be surfaced; not silently accepted for matches");

        auto validate = [&](Json c, Json d, Json l, Json g) {
            return YakouPrototype::Validate(c, d, l, g);
        };
        auto c = prototype.Cards;
        c["Cards"][1]["Id"] = c["Cards"][0]["Id"];
        Reject([&] { validate(c, prototype.Decks, prototype.Localize, prototype.Config); });
        c = prototype.Cards;
        c["Cards"][0]["NameKey"] = "missing_name";
        Reject([&] { validate(c, prototype.Decks, prototype.Localize, prototype.Config); });
        c = prototype.Cards;
        auto& devour = *std::find_if(c["Cards"].begin(), c["Cards"].end(), [](Json const& card) {
            return card["Id"] == 2008;
        });
        devour["SummonCostJson"]["Devour"]["Count"] = 0;
        Reject([&] { validate(c, prototype.Decks, prototype.Localize, prototype.Config); });
        c = prototype.Cards;
        c["Cards"][0]["AllowedManifestations"] = Json::array({999999});
        Reject([&] { validate(c, prototype.Decks, prototype.Localize, prototype.Config); });
        auto d = prototype.Decks;
        d["Decks"][0]["Cards"][0] = 2101; // manifestation cannot be in deck
        Reject([&] { validate(prototype.Cards, d, prototype.Localize, prototype.Config); });
        d = prototype.Decks;
        d["Decks"][0]["Cards"].erase(d["Decks"][0]["Cards"].end() - 1);
        Reject([&] { validate(prototype.Cards, d, prototype.Localize, prototype.Config); });
        auto g = prototype.Config;
        g["ki.baseRegenPerTurn"] = 1;
        Reject([&] { validate(prototype.Cards, prototype.Decks, prototype.Localize, g); });
        auto authored = *std::find_if(prototype.Cards["Cards"].begin(),
                                      prototype.Cards["Cards"].end(), [](Json const& x) {
            return x["Id"] == 2008;
        });
        Check(YakouRequiredCost(authored, "Devour").SourceZone == "FriendlyField" &&
                  YakouRequiredCost(authored, "Devour").ExactCount == 1,
              "Devour 1 must take one allied field material");
        authored["SummonCostJson"]["Devour"]["Count"] = 2;
        Check(YakouRequiredCost(authored, "Devour").ExactCount == 2,
              "Devour 2 must take two materials (global default is not a cap)");
        Reject([&] { (void)YakouRequiredCost(authored, "Discard"); });
        Check(!ValidateYakouExactSelection({10, 11, 12}, {}, 2), "0/2 must not confirm");
        Check(!ValidateYakouExactSelection({10, 11, 12}, {10}, 2), "1/2 must not confirm");
        Check(ValidateYakouExactSelection({10, 11, 12}, {10, 11}, 2), "2/2 must confirm");
        Check(!ValidateYakouExactSelection({10, 11, 12}, {10, 11, 12}, 2),
              "3/2 must not confirm");
        Check(!ValidateYakouExactSelection({10, 11, 12}, {10, 10}, 2),
              "Repeated materials must not confirm");
        Check(!ValidateYakouExactSelection({10, 11, 12}, {10, 99}, 2),
              "Unknown/unowned material must not confirm");
        Check(ValidateYakouExactSelection({10, 11, 12}, {10, 11, 12}, 3),
              "Necromancy 3 should accept three distinct candidates");
        std::cout << "Yakou prototype validation and exact selection passed\n";
        return 0;
    }
    catch (std::exception const& e)
    {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
