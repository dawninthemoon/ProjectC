#include "YakouPrototype.hpp"
#include "CardCatalog.hpp"
#include <algorithm>
#include <filesystem>
#include <iostream>
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
        Check(prototype.Cards["Cards"].size() == 34, "Expected 34 prototype definitions");
        Check(prototype.Decks["Decks"][0]["Cards"].size() == 30, "Expected 30 deck copies");
        Check(prototype.Localize.size() == 68 && prototype.Config.size() == 72,
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
