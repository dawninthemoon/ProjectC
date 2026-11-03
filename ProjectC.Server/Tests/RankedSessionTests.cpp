#include "MatchSession.hpp"
#include <filesystem>
#include <source_location>
#include <stdexcept>

using namespace ProjectC::Network;
namespace
{
void Require(bool value, std::source_location where = std::source_location::current())
{
    if (!value)
    {
        throw std::runtime_error("Ranked session assertion at " + std::to_string(where.line()));
    }
}
} // namespace
void RunRankedSessionTests()
{
    auto loaded =
        CardCatalog::Load((std::filesystem::path(DefaultDataDirectory()) / "HyakkiYagyo").string());
    loaded.Config.EnableMulligan = false;
    auto catalog = std::make_shared<CardCatalog>(std::move(loaded));
    for (auto const& scenario : {"Surrender", "Disconnect", "Aborted"})
    {
        std::vector<ReplayRecord> records;
        std::vector<Json> messages;
        auto a = std::make_shared<ClientSession>();
        auto b = std::make_shared<ClientSession>();
        a->Send = b->Send = [&](Json const& message) { messages.push_back(message); };
        MatchSession match("ranked-fixture",
                           catalog,
                           std::make_shared<IdentityAllocator>(),
                           [&](ReplayRecord record) { records.push_back(std::move(record)); });
        match.Handle(
            a, Json{{"Type", "CreateMatch"}, {"ProtocolVersion", 2}, {"RequestId", "create"}});
        match.Handle(b,
                     Json{{"Type", "JoinMatch"},
                          {"ProtocolVersion", 2},
                          {"RequestId", "join"},
                          {"MatchId", match.Id()}});
        Json first{{"playerId", "11111111-1111-4111-8111-111111111111"},
                   {"cardIds", catalog->Decks[0].Cards}};
        Json second{{"playerId", "22222222-2222-4222-8222-222222222222"},
                    {"cardIds", catalog->Decks[1].Cards}};
        match.SetRankedDecks({first, second});
        auto winner = b->Player;
        auto loser = a->Player;
        Require(match.Status() == MatchStatus::Playing && match.Snapshot(loser)["Started"] == true);
        if (std::string(scenario) == "Surrender")
        {
            match.Handle(a,
                         Json{{"Type", "LeaveMatch"},
                              {"ProtocolVersion", 2},
                              {"RequestId", "leave"},
                              {"MatchId", match.Id()},
                              {"GameNumber", 1}});
        }
        else
        {
            match.Disconnect(a);
            if (std::string(scenario) == "Aborted")
            {
                match.Disconnect(b);
                winner = 0;
            }
            match.Tick(Clock::now() + std::chrono::seconds(119));
            Require(match.Status() == MatchStatus::Playing);
            match.Tick(Clock::now() + std::chrono::seconds(121));
        }
        Require(match.Status() == MatchStatus::Finished);
        Require(records.size() == 1 && records[0].Verify());
        Require(records[0].ResultReason == scenario && records[0].ResultWinner == winner);
        Require(match.Snapshot(loser)["Finished"] == true &&
                match.Snapshot(loser)["WinnerPlayerId"] == winner);
        match.Close("test cleanup");
        Require(records.size() == 1);
    }
}
