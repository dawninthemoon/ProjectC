#include "YakouTransportHub.hpp"
#include "CardCatalog.hpp"
#include "Protocol.hpp"

#include <algorithm>
#include <cstdint>
#include <filesystem>
#include <limits>
#include <map>
#include <random>
#include <stdexcept>
#include <utility>

namespace ProjectC::Network
{
namespace
{
constexpr auto ResumeRetryWindow = std::chrono::seconds(30);
constexpr std::size_t ResumeRetryRequestIdLength = 32;
constexpr std::size_t MaximumYakouDeckIdLength = 64;
constexpr char DefaultYakouDeckId[] = "HyakkiSampleV02";
constexpr char PersonalYakouDeckId[] = "Personal";
constexpr std::size_t PersonalYakouDeckSize = 30;
constexpr int MaximumPersonalYakouCopies = 3;
bool IsResumeRetryId(std::string const& id)
{
    return id.size() == ResumeRetryRequestIdLength &&
           std::all_of(id.begin(), id.end(), [](char c)
           {
               return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
           });
}
Json const* FindDeck(YakouPrototype const& catalog, std::string const& id)
{
    for (auto const& deck : catalog.Decks.at("Decks"))
    {
        if (deck.at("Id") == id)
        {
            return &deck;
        }
    }
    return nullptr;
}
std::uint64_t PrivateSeed()
{
    // Token() uses the operating system's secure entropy provider.
    return std::stoull(Token().substr(0,16),nullptr,16);
}
std::array<std::vector<int>, 2> Decks(YakouPrototype const& catalog,
                                      std::array<std::string, 2> const& ids,
                                      std::array<std::uint64_t,2> const* seeds = nullptr,
                                      std::array<std::vector<int>, 2> const* personal = nullptr)
{
    std::array<std::vector<int>, 2> result;
    for (int seat = 0; seat < 2; ++seat)
    {
        if (ids[seat] == PersonalYakouDeckId && personal != nullptr)
        {
            result[seat] = (*personal)[seat];
        }
        else if (auto const* deck = FindDeck(catalog, ids[seat]); deck != nullptr)
        {
            result[seat] = deck->at("Cards").get<std::vector<int>>();
        }
        else
        {
            throw std::invalid_argument("Unknown Yakou deck ID");
        }
        if (seeds)
        {
            std::mt19937_64 rng((*seeds)[seat]);
            auto& order = result[seat];
            for (std::size_t n = order.size(); n > 1; --n)
            {
                auto const bound = static_cast<std::uint64_t>(n);
                auto const threshold = (std::uint64_t{0} - bound) % bound;
                std::uint64_t value;
                do { value = rng(); } while (value < threshold);
                std::swap(order[n-1],order[static_cast<std::size_t>(value % bound)]);
            }
        }
    }
    return result;
}
std::string PersonalDeckError(YakouPrototype const& catalog, Json const& value,
                              std::vector<int>& result)
{
    if (!catalog.Config.contains("resource.definitions"))
    {
        return "PersonalDeckUnavailable";
    }
    if (!value.is_array() || value.size() != PersonalYakouDeckSize)
    {
        return "DeckCardCount";
    }
    std::map<int, int> counts;
    for (auto const& entry : value)
    {
        if (!entry.is_number_integer() ||
            (entry.is_number_unsigned() && (entry.get<std::uint64_t>() == 0 ||
                entry.get<std::uint64_t>() >
                    static_cast<std::uint64_t>(std::numeric_limits<int>::max()))) ||
            (!entry.is_number_unsigned() && (entry.get<std::int64_t>() <= 0 ||
                entry.get<std::int64_t>() > std::numeric_limits<int>::max())))
        {
            return "DeckUnknownCard";
        }
        int const id = entry.get<int>();
        auto const definition = std::find_if(catalog.Cards.at("Cards").begin(),
            catalog.Cards.at("Cards").end(),
            [id](Json const& card) { return card.at("Id") == id; });
        if (definition == catalog.Cards.at("Cards").end())
        {
            return "DeckUnknownCard";
        }
        if (!definition->value("MainDeckEligible", true))
        {
            return "DeckIneligibleCard";
        }
        if (++counts[id] > MaximumPersonalYakouCopies)
        {
            return "DeckCopyLimit";
        }
        result.push_back(id);
    }
    try
    {
        (void)YakouDeckResourceTypes(catalog.Cards, catalog.Config, result);
    }
    catch (std::exception const&)
    {
        return "DeckResourceLimit";
    }
    return {};
}
bool IsYakouType(std::string const& name)
{
    return name == "YakouCreate" || name == "YakouJoin" ||
           name == "YakouResume" || name == "YakouCommand" ||
           name == "YakouSnapshot" || name == "YakouLeave";
}
void Reply(std::shared_ptr<ClientSession> const& client,
           std::string const& type, std::string const& requestId,
           std::string const& matchId, Json const& body)
{
    auto message = Envelope(type, requestId, matchId);
    message["Payload"] = body;
    client->Send(message);
}
}

YakouTransportHub::YakouTransportHub(std::string const& dataDirectory,
                                     std::size_t maxRooms, bool alpha02)
    : catalog_(alpha02
        ? YakouPrototype::LoadAuthoringSnapshot(std::filesystem::path(dataDirectory) /
                                                "Alpha02" / "AuthoringSnapshot.json",true)
        : YakouPrototype::Load(std::filesystem::path(dataDirectory) / "YakouPrototype")),
      maxRooms_(maxRooms)
{
    (void)Decks(catalog_, {DefaultYakouDeckId, DefaultYakouDeckId});
}

void YakouTransportHub::SnapshotBoth(std::string const& id, Room const& room,
                                     std::string const& requestId) const
{
    for (int seat = 1; seat <= 2; ++seat)
    {
        auto client = room.Clients[seat - 1].lock();
        if (client && client->Connected && client->YakouMatchId == id)
            Reply(client, "YakouSnapshot", requestId, id,
                  {{"Seat", seat}, {"DeckId", room.DeckIds[seat - 1]},
                   {"Sequence", room.Session->Sequence()},
                   {"State", room.Session->Snapshot(client->YakouConnectionKey)}});
    }
}

void YakouTransportHub::CloseRoom(std::string const& id, std::string const& reason)
{
    auto it = rooms_.find(id);
    if (it == rooms_.end()) return;
    for (auto& weak : it->second.Clients)
    {
        auto client = weak.lock();
        if (!client) continue;
        if (client->YakouMatchId == id)
        {
            client->YakouMatchId.clear();
            client->YakouConnectionKey.clear();
            Reply(client, "YakouClosed", "", id, {{"Reason", reason}});
        }
    }
    rooms_.erase(it);
}

void YakouTransportHub::Receive(std::shared_ptr<ClientSession> const& client,
                                Json const& input, std::string const& requestId,
                                std::string const& type)
{
    auto reject = [&](std::string const& reason)
    {
        Reply(client, "YakouRejected", requestId, client->YakouMatchId,
              {{"Reason", reason}});
    };
    if (!IsYakouType(type))
    {
        reject("UnknownYakouCommand");
        return;
    }
    if (type == "YakouCreate")
    {
        Fields(input, {"Type", "ProtocolVersion", "RequestId", "DeckId", "Cards"});
        if (!client->MatchId.empty() || !client->YakouMatchId.empty() ||
            client->ApiPending)
        {
            reject("AlreadyJoined");
            return;
        }
        if (rooms_.size() >= maxRooms_)
        {
            reject("MatchLimit");
            return;
        }
        bool const personal = input.contains("Cards");
        if (personal && input.contains("DeckId"))
        {
            reject("AmbiguousDeck");
            return;
        }
        std::vector<int> personalCards;
        if (personal)
        {
            auto const error = PersonalDeckError(catalog_, input.at("Cards"), personalCards);
            if (!error.empty())
            {
                reject(error);
                return;
            }
        }
        std::string const deckId = personal ? PersonalYakouDeckId : input.contains("DeckId")
            ? String(input, "DeckId", MaximumYakouDeckIdLength) : DefaultYakouDeckId;
        if (!personal && FindDeck(catalog_, deckId) == nullptr)
        {
            reject("InvalidDeckId");
            return;
        }
        std::string const id = "Y" + Token().substr(0, 24);
        Room room;
        room.DeckIds = {deckId, DefaultYakouDeckId};
        room.PersonalDecks[0] = std::move(personalCards);
        bool const alpha02 = catalog_.Config.contains("resource.definitions");
        if (alpha02)
        {
            room.DeckSeeds = {PrivateSeed(),PrivateSeed()};
            room.MatchSeed = PrivateSeed();
        }
        room.Session = std::make_unique<YakouSession>(catalog_, 1,
            Decks(catalog_,room.DeckIds,alpha02 ? &room.DeckSeeds : nullptr,
                  &room.PersonalDecks),room.MatchSeed);
        room.Tokens[0] = Token();
        client->YakouMatchId = id;
        client->YakouConnectionKey = Token();
        room.Session->Bind(1, client->YakouConnectionKey);
        room.Clients[0] = client;
        auto [it, inserted] = rooms_.emplace(id, std::move(room));
        if (!inserted) throw std::runtime_error("Duplicate Yakou room ID");
        Reply(client, "YakouCreated", requestId, id,
              {{"Seat", 1}, {"DeckId", deckId},
               {"ResumeToken", it->second.Tokens[0]}});
        SnapshotBoth(id, it->second);
        return;
    }
    if (type == "YakouJoin")
    {
        Fields(input, {"Type", "ProtocolVersion", "RequestId", "MatchId", "DeckId", "Cards"});
        if (!client->MatchId.empty() || !client->YakouMatchId.empty() ||
            client->ApiPending)
        {
            reject("AlreadyJoined");
            return;
        }
        const auto id = String(input, "MatchId", 64);
        auto found = rooms_.find(id);
        if (found == rooms_.end()) { reject("MatchNotFound"); return; }
        auto& room = found->second;
        if (room.Joined) { reject("MatchFull"); return; }
        bool const personal = input.contains("Cards");
        if (personal && input.contains("DeckId"))
        {
            reject("AmbiguousDeck");
            return;
        }
        std::vector<int> personalCards;
        if (personal)
        {
            auto const error = PersonalDeckError(catalog_, input.at("Cards"), personalCards);
            if (!error.empty())
            {
                reject(error);
                return;
            }
        }
        std::string const deckId = personal ? PersonalYakouDeckId : input.contains("DeckId")
            ? String(input, "DeckId", MaximumYakouDeckIdLength) : DefaultYakouDeckId;
        if (!personal && FindDeck(catalog_, deckId) == nullptr)
        {
            reject("InvalidDeckId");
            return;
        }
        // Before the second seat joins, gameplay commands are rejected. Rebuild
        // the unopened match with both authoritative decks, then restore seat 1.
        // Seat one's seed and therefore opening hand remain unchanged on join.
        bool const alpha02 = catalog_.Config.contains("resource.definitions");
        auto deckIds = room.DeckIds;
        auto personalDecks = room.PersonalDecks;
        deckIds[1] = deckId;
        personalDecks[1] = std::move(personalCards);
        auto nextSession = std::make_unique<YakouSession>(catalog_, 1,
            Decks(catalog_,deckIds,alpha02 ? &room.DeckSeeds : nullptr,
                  &personalDecks),room.MatchSeed);
        if (auto first = room.Clients[0].lock(); first && !first->YakouConnectionKey.empty())
        {
            nextSession->Bind(1, first->YakouConnectionKey);
        }
        room.DeckIds = std::move(deckIds);
        room.PersonalDecks = std::move(personalDecks);
        room.Session = std::move(nextSession);
        room.Tokens[1] = Token();
        client->YakouMatchId = id;
        client->YakouConnectionKey = Token();
        room.Session->Bind(2, client->YakouConnectionKey);
        room.Clients[1] = client;
        room.Joined = true;
        room.LastActivity = Clock::now();
        Reply(client, "YakouJoined", requestId, id,
              {{"Seat", 2}, {"DeckId", deckId},
               {"ResumeToken", room.Tokens[1]}});
        SnapshotBoth(id, room);
        return;
    }
    if (type == "YakouResume")
    {
        Fields(input, {"Type", "ProtocolVersion", "RequestId", "MatchId", "ResumeToken"});
        if (!client->MatchId.empty() || !client->YakouMatchId.empty() ||
            client->ApiPending)
        {
            reject("AlreadyJoined");
            return;
        }
        auto id = String(input, "MatchId", 64);
        auto token = String(input, "ResumeToken", 128);
        auto found = rooms_.find(id);
        if (found == rooms_.end()) { reject("MatchNotFound"); return; }
        auto& room = found->second;
        int seat = token == room.Tokens[0] ? 1 : token == room.Tokens[1] && room.Joined ? 2 : 0;
        bool retry = false;
        if (seat == 0)
        {
            for (int index = 0; index < 2; ++index)
            {
                if ((index == 0 || room.Joined) && token == room.PreviousTokens[index]
                    && requestId == room.ResumeRequests[index] && Clock::now() < room.ResumeRetryUntil[index])
                {
                    seat = index + 1;
                    retry = true;
                    break;
                }
            }
        }
        if (seat == 0) { reject("InvalidResumeToken"); return; }
        if (auto old = room.Clients[seat - 1].lock())
        {
            old->YakouMatchId.clear();
            old->YakouConnectionKey.clear();
            Reply(old, "YakouReplaced", "", id, {{"Reason", "Reconnected"}});
        }
        client->YakouMatchId = id;
        client->YakouConnectionKey = Token();
        room.Session->Bind(seat, client->YakouConnectionKey);
        room.Clients[seat - 1] = client;
        // Allow only the exact, short-lived retry of an unacknowledged rotation.
        // A later authenticated request retires the previous token immediately.
        if (!retry)
        {
            if (IsResumeRetryId(requestId))
            {
                room.PreviousTokens[seat - 1] = room.Tokens[seat - 1];
                room.ResumeRequests[seat - 1] = requestId;
                room.ResumeRetryUntil[seat - 1] = Clock::now() + ResumeRetryWindow;
            }
            else
            {
                room.PreviousTokens[seat - 1].clear();
                room.ResumeRequests[seat - 1].clear();
            }
            room.Tokens[seat - 1] = Token();
        }
        room.LastActivity = Clock::now();
        Reply(client, "YakouResumed", requestId, id,
              {{"Seat", seat}, {"DeckId", room.DeckIds[seat - 1]},
               {"ResumeToken", room.Tokens[seat - 1]}});
        SnapshotBoth(id, room);
        return;
    }

    Fields(input, type == "YakouCommand" ?
           std::initializer_list<char const*>{"Type","ProtocolVersion","RequestId","MatchId","Command"} :
           std::initializer_list<char const*>{"Type","ProtocolVersion","RequestId","MatchId"});
    auto id = String(input, "MatchId", 64);
    if (client->YakouMatchId != id || client->YakouConnectionKey.empty())
    {
        reject("NotJoined");
        return;
    }
    auto found = rooms_.find(id);
    if (found == rooms_.end()) { reject("MatchNotFound"); return; }
    auto& room = found->second;
    room.LastActivity = Clock::now();
    for (int index = 0; index < 2; ++index)
    {
        if (auto bound = room.Clients[index].lock(); bound && bound == client)
        {
            room.PreviousTokens[index].clear();
            room.ResumeRequests[index].clear();
        }
    }
    if (type == "YakouLeave")
    {
        CloseRoom(id, "PlayerLeft");
        return;
    }
    if (type == "YakouSnapshot")
    {
        Reply(client, "YakouSnapshot", requestId, id,
              {{"Sequence", room.Session->Sequence()},
               {"State", room.Session->Snapshot(client->YakouConnectionKey)}});
        return;
    }
    if (!room.Joined) { reject("WaitingForOpponent"); return; }
    auto const before = room.Session->Sequence();
    auto const result = room.Session->Dispatch(
        client->YakouConnectionKey, requestId, input.at("Command"));
    Reply(client, "YakouCommandResult", requestId, id,
          {{"Accepted", result.at("Accepted")}, {"Reason", result.at("Reason")},
           {"Sequence", result.at("Sequence")}});
    if (room.Session->Sequence() != before)
        SnapshotBoth(id, room);
}

void YakouTransportHub::Disconnect(std::shared_ptr<ClientSession> const& client)
{
    if (client->YakouMatchId.empty()) return;
    auto found = rooms_.find(client->YakouMatchId);
    if (found != rooms_.end())
        found->second.Session->Unbind(client->YakouConnectionKey);
    client->YakouMatchId.clear();
    client->YakouConnectionKey.clear();
}

void YakouTransportHub::Cleanup(Clock::time_point now,
                                std::chrono::seconds waiting,
                                std::chrono::seconds playing)
{
    std::vector<std::string> expired;
    for (auto const& [id, room] : rooms_)
        if (now - room.LastActivity >= (room.Joined ? playing : waiting))
            expired.push_back(id);
    for (auto const& id : expired) CloseRoom(id, "Expired");
}

void YakouTransportHub::Shutdown()
{
    while (!rooms_.empty())
        CloseRoom(rooms_.begin()->first, "ServerShutdown");
}
} // namespace ProjectC::Network
