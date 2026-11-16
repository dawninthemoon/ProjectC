#include "YakouSession.hpp"
#include "CardCatalog.hpp"

#include <algorithm>
#include <array>
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
template<class Action> void Reject(Action action)
{
    bool rejected = false;
    try { action(); }
    catch (std::invalid_argument const&) { rejected = true; }
    Check(rejected, "Server-only session action must reject invalid binding");
}
std::vector<int> Deck(YakouPrototype const& proto, std::vector<int> front)
{
    auto deck = proto.Decks.at("Decks").at(0).at("Cards").get<std::vector<int>>();
    for (int id : front)
    {
        auto pos = std::find(deck.begin(), deck.end(), id);
        Check(pos != deck.end(), "Fixture card unavailable");
        deck.erase(pos);
    }
    front.insert(front.end(), deck.begin(), deck.end());
    return front;
}
}
int main()
{
    try
    {
        auto proto = YakouPrototype::Load(
            std::filesystem::path(DefaultDataDirectory()) / "YakouPrototype");
        YakouSession session(proto, 1,
            {Deck(proto, {2001, 2009, 2002, 3001, 2005}),
             Deck(proto, {2001, 2002, 2005, 2009, 3001})});
        Reject([&] { (void)session.Snapshot("rogue"); });
        Reject([&] { (void)session.Dispatch("rogue", "r1", {{"Type","StartTurn"}}); });
        Reject([&] { session.Bind(3, "socket-3"); });
        session.Bind(1, "socket-1");
        session.Bind(2, "socket-2");
        Reject([&] { session.Bind(2, "socket-1"); });
        Check(session.Snapshot("socket-1").at("Players").at(1).at("Hand").empty() &&
              session.Snapshot("socket-2").at("Players").at(0).at("Hand").empty(),
              "Session must project opponent's hand privately");

        auto start = session.Dispatch("socket-1", "start", {{"Type","StartTurn"}});
        Check(start.at("Accepted") == true && start.at("Sequence") == 1 &&
              session.Sequence() == 1,
              "Bound first seat can start the game exactly once");
        auto const state = session.Match().ServerStateHash();
        auto retry = session.Dispatch("socket-1", "start", {{"Type","StartTurn"}});
        Check(retry == start && session.Match().ServerStateHash() == state &&
              session.Sequence() == 1 && session.Audit().size() == 1,
              "Same request replay must return exactly the cached response");
        auto conflict = session.Dispatch("socket-1", "start", {{"Type","BeginRaid"}});
        Check(conflict.at("Reason") == "DuplicateRequestId" &&
              session.Sequence() == 1 && session.Audit().size() == 1,
              "Reusing ID for a different command must not act");
        auto rejected = session.Dispatch("socket-2", "wrong-turn", {{"Type","StartTurn"}});
        Check(rejected.at("Accepted") == false &&
              rejected.at("Reason") == "Rejected" &&
              session.Match().ServerStateHash() == state &&
              session.Sequence() == 1 &&
              session.Audit().back().BeforeHash == session.Audit().back().AfterHash,
              "Wrong-seat action must not advance sequence or mutate state");
        auto const playerHand = session.Match().Player(1).Hand;
        auto summon = session.Dispatch("socket-1", "summon", 
            {{"Type","Summon"}, {"CardId",playerHand[0]}, {"Method","Normal"},
             {"Materials",Json::array()}, {"Slot",0}});
        Check(summon.at("Accepted") == true &&
              summon.at("Sequence") == 2 &&
              session.Match().PendingEffects().empty() &&
              session.Audit().size() == 3,
              "Bound summon must atomically apply triggered effect and audit result");
        auto oldHash = session.Match().ServerStateHash();
        session.Unbind("socket-1");
        Reject([&] { (void)session.Dispatch("socket-1","after-disconnect",
                                          {{"Type","BeginRaid"}}); });
        session.Bind(1, "socket-1-reconnected");
        Reject([&] { (void)session.Snapshot("socket-1"); });
        Check(session.Snapshot("socket-1-reconnected").at("Players").at(1).at("Hand").empty(),
              "New bound transport should regain only its own private projection");
        auto previous = session.Dispatch("socket-1-reconnected", "summon",
            {{"Type","Summon"}, {"CardId",playerHand[0]}, {"Method","Normal"},
             {"Materials",Json::array()}, {"Slot",0}});
        Check(previous == summon && session.Match().ServerStateHash() == oldHash &&
              session.Sequence() == 2,
              "Reconnected seat must not repeat previously accepted command");
        auto fresh = session.Dispatch("socket-1-reconnected", "begin-raid",
                                     {{"Type","BeginRaid"}});
        Check(fresh.at("Accepted") == true && session.Sequence() == 3,
              "Fresh post-reconnect request must continue the same match");
        Check(session.Audit().size() == 4 &&
              session.Audit()[0].Seat == 1 &&
              session.Audit()[1].Seat == 2 &&
              session.Audit()[2].Seat == 1 &&
              session.Audit()[3].Seat == 1,
              "Private audit must record exactly the first execution per ID");
        std::cout << "Yakou isolated session binding, retries and reconnect passed\n";
        return 0;
    }
    catch (std::exception const& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
