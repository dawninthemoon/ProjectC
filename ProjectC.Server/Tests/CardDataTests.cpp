#include "CardCatalog.hpp"
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>

using namespace ProjectC::Network;

namespace
{
Json Read(std::filesystem::path const& path)
{
    std::ifstream file(path);
    if (!file)
    {
        throw std::runtime_error("Cannot read fixture: " + path.string());
    }
    return Json::parse(file);
}

void Check(bool valid, char const* message)
{
    if (!valid)
    {
        throw std::runtime_error(message);
    }
}

void Rejected(Json const& cards, Json const& decks)
{
    bool rejected = false;
    try
    {
        CardCatalog::FromJson(cards, decks);
    }
    catch (std::exception const&)
    {
        rejected = true;
    }
    Check(rejected, "Invalid card data accepted");
}
} // namespace

int main()
{
    try
    {
        auto data = std::filesystem::path(DefaultDataDirectory());
        auto repo = data.parent_path().parent_path();
        auto decks = Read(data / "HyakkiYagyo" / "Decks.json");
        auto sheet = Read(repo / "ProjectC.Client" / "Assets" / "Resources" /
                          "ProjectC_Data - CardData.json");
        auto raw = CardCatalog::FromJson(sheet, decks);
        Check(raw.Definitions.size() == sheet.size(), "Sheet card count");
        auto const& first = raw.Definitions.front();
        Check(first.Id == 1001 && first.Attack == 2 && first.MaxHp == 2,
              "Numeric strings must become integers");
        Check(first.NameKey == "card_1001_name" && first.DescriptionKey == "card_1001_desc" &&
                  first.Name == first.NameKey,
              "Name-free sheets must load with separate text keys");
        Check(first.Effects.size() == 1 && first.DescriptionArgs.size() == 2 &&
                  first.DescriptionArgs.front().Default == "1",
              "Embedded arrays and description metadata");
        // The new spreadsheet columns must survive normalization without enabling
        // any new game rules prematurely. Legacy catalogs still load unchanged.
        auto extended = sheet;
        extended[0]["AttackInterval"] = "2";
        extended[0]["SummonMethods"] = R"json(["Normal","Necromancy"])json";
        extended[0]["SummonCostJson"] = R"json({"Normal":{},"Necromancy":{"Count":1}})json";
        auto parsed = CardCatalog::FromJson(extended, decks);
        Check(parsed.Definitions.front().AttackInterval == 2 &&
                  parsed.Definitions.front().SummonMethods.size() == 2 &&
                  parsed.Definitions.front().SummonCostJson.find("Necromancy") != std::string::npos,
              "Yakou summon metadata must parse from Sheets strings");
        auto malformed = extended;
        malformed[0]["SummonCostJson"] = "{invalid";
        Rejected(malformed, decks);
        malformed = extended;
        malformed[0]["SummonMethods"] = R"json(["SummonEverything"])json";
        Rejected(malformed, decks);
        malformed = extended;
        malformed[0]["AttackInterval"] = "999";
        Rejected(malformed, decks);
        auto normalized = CardCatalog::Load((data / "HyakkiYagyo").string());
        Check(!normalized.PersonalDeckError(normalized.Decks.front().Cards).size(),
              "Published deck remains playable");
        auto manifest = std::find_if(normalized.Definitions.begin(),
                                     normalized.Definitions.end(),
                                     [](auto const& card) { return card.Id == 1007; });
        Check(manifest != normalized.Definitions.end() && !manifest->MainDeckEligible,
              "Published metadata must retain the manifest-only restriction");
        auto unity = Read(repo / "ProjectC.Client" / "Assets" / "Resources" / "CardData" /
                          "HyakkiCards.json");
        Check(unity == Read(data / "HyakkiYagyo" / "Cards.json"),
              "Server and Unity catalogs differ");
        auto bad = sheet;
        auto runtimeCards = Read(data / "HyakkiYagyo" / "Cards.json");
        for (auto const* field : {"Operation", "Trigger", "Amount", "Zones"})
        {
            auto invalid = runtimeCards;
            auto& effect = invalid["Cards"][0]["Effects"][0];
            if (std::string(field) == "Operation")
            {
                effect["Actions"][0][field] = "Unsupported";
            }
            else if (std::string(field) == "Trigger")
            {
                effect[field] = "OnTypo";
            }
            else if (std::string(field) == "Amount")
            {
                effect["Costs"] = Json::array({{{"Kind", "YokaiPower"}}});
            }
            else
            {
                effect["Actions"][0]["Targets"][field] = Json::array();
            }
            std::string diagnostic;
            try
            {
                CardCatalog::FromJson(invalid, decks);
            }
            catch (std::exception const& error)
            {
                diagnostic = error.what();
            }
            Check(diagnostic.find("Card 1001.Effects[0]") != std::string::npos,
                  "Invalid effect must report its card and effect location");
        }
        bad[0]["Id"] = "1001.5";
        Rejected(bad, decks);
        bad = sheet;
        bad[0]["Effects"] = "not JSON";
        Rejected(bad, decks);
        bad = sheet;
        bad[1]["CatalogVersion"] = "different";
        Rejected(bad, decks);
        bad = sheet;
        bad[0]["DescriptionArgs"] = R"json([{"path":"Effects[0];eval()"}])json";
        Rejected(bad, decks);
        bad = sheet;
        bad[0].erase("NameKey");
        Rejected(bad, decks);
        auto legacy = CardCatalog::Load(data.string());
        Check(!legacy.Definitions.front().Name.empty() &&
                  legacy.Definitions.front().NameKey.empty(),
              "Legacy Name schema must remain supported");
        std::cout
            << "Sheet parsing, text keys, metadata, catalog parity and legacy loading passed\n";
    }
    catch (std::exception const& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
