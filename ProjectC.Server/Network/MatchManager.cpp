#include "MatchManager.hpp"
#include <algorithm>
#include <iostream>
#include <stdexcept>

namespace ProjectC::Network
{
namespace
{
constexpr std::size_t MinimumDeckSizeForMulligan = 10;
constexpr std::size_t MaximumAccessTokenCharacters = 8192;
constexpr int MinimumPositiveTimeoutSeconds = 1;
constexpr int MaximumMulliganTimeoutSeconds = 60 * 60;
constexpr std::size_t MaximumConfiguredMatches = 1024;
} // namespace

MatchManager::MatchManager(ServerConfig config) : config(std::move(config))
{
    if (!this->config.WebsiteApiUrl.empty())
    {
        website = std::make_unique<WebsiteApi>(this->config.WebsiteApiUrl,
                                               this->config.BattleApiKey,
                                               this->config.ResultOutboxDirectory,
                                               this->config.ResultOutboxPersistent);
    }
    auto loaded = CardCatalog::Load(this->config.DataDirectory);
    if (this->config.EnableMulligan)
    {
        loaded.Config.EnableMulligan = *this->config.EnableMulligan;
    }
    if (!GameCore::ValidateConfig(loaded.Config))
    {
        throw std::runtime_error(
            "Mulligan requires an initial hand of five and valid turn draw capacity");
    }
    for (auto const& deck : loaded.Decks)
    {
        if (loaded.Config.EnableMulligan && deck.Cards.size() < MinimumDeckSizeForMulligan)
        {
            throw std::runtime_error("Mulligan requires at least ten cards");
        }
    }
    catalog = std::make_shared<CardCatalog>(std::move(loaded));
    if (website &&
        (catalog->Version.empty() || catalog->Config.MinDeckSize != PersonalDeckCardCount ||
         catalog->Config.MaxDeckSize != PersonalDeckCardCount ||
         catalog->Config.MaxCardCopies != MaximumCopiesPerPersonalDeckCard))
    {
        throw std::runtime_error("Website matchmaking requires a versioned 30-card/3-copy catalog; "
                                 "configure PROJECTC_DATA_DIRECTORY or --data");
    }
    if (this->config.TurnDurationSeconds < MinimumPositiveTimeoutSeconds ||
        this->config.MulliganTimeoutSeconds < MinimumPositiveTimeoutSeconds ||
        this->config.MulliganTimeoutSeconds > MaximumMulliganTimeoutSeconds)
    {
        throw std::runtime_error("Invalid mulligan timeout");
    }
    if (this->config.MaxMatches == 0 || this->config.MaxMatches > MaximumConfiguredMatches ||
        this->config.MaxReplays == 0 ||
        this->config.WaitingTimeoutSeconds < MinimumPositiveTimeoutSeconds ||
        this->config.PlayingTimeoutSeconds < MinimumPositiveTimeoutSeconds ||
        this->config.FinishedTimeoutSeconds < MinimumPositiveTimeoutSeconds ||
        this->config.ReplayRetentionSeconds < MinimumPositiveTimeoutSeconds)
    {
        throw std::runtime_error("Invalid server configuration");
    }
}
MatchSession const* MatchManager::Find(std::string const& id) const
{
    auto found = matches.find(id);
    return found == matches.end() ? nullptr : found->second.get();
}
void MatchManager::Archive(ReplayRecord record)
{
    if (!record.Verify())
    {
        throw std::runtime_error("Replay verification failed");
    }
    std::cout << "Match " << record.MatchId << " game=" << record.GameNumber
              << " replayVerified=true\n";
    if (website)
    {
        auto ranked = rankedMatches.find(record.MatchId);
        if (ranked != rankedMatches.end())
        {
            Json winner = nullptr;
            std::string reason = "Aborted";
            if (!record.ResultReason.empty())
            {
                reason = record.ResultReason;
            }
            else if (record.FinalState.Status == GameCore::GameStatus::Finished)
            {
                reason = record.FinalState.WinnerPlayerId == 0 ? "Draw" : "Normal";
            }
            auto winnerId = record.ResultReason.empty() ? record.FinalState.WinnerPlayerId
                                                        : record.ResultWinner;
            if (reason != "Aborted" && reason != "Draw")
            {
                for (std::size_t i = 0; i < ranked->second.Players.size(); ++i)
                {
                    if (ranked->second.Players[i] == winnerId)
                    {
                        winner = ranked->second.Participants[i]["playerId"];
                    }
                }
            }
            website->StoreResult(Json{{"matchId", record.MatchId},
                                      {"participants", ranked->second.Participants},
                                      {"winnerPlayerId", winner},
                                      {"endReason", reason},
                                      {"endedAt", UtcNow()}});
        }
    }
    replays.push_back(std::move(record));
    while (replays.size() > config.MaxReplays)
    {
        replays.pop_front();
    }
}
void MatchManager::Receive(std::shared_ptr<ClientSession> const& client, std::string const& message)
{
    std::string request;
    try
    {
        auto input = ParseJson(message);
        request = String(input, "RequestId", 64);
        if (Number(input, "ProtocolVersion", ProtocolVersion) != ProtocolVersion)
        {
            throw std::runtime_error("ProtocolVersion");
        }
        auto type = String(input, "Type");
        auto reject = [&](std::string const& reason)
        {
            auto error = Envelope(type == "SubmitCommand" ? "CommandRejected" : "ServerError",
                                  request,
                                  client->MatchId);
            error["Reason"] = reason;
            client->Send(error);
        };
        if (type == "Authenticate")
        {
            Fields(input, {"Type", "ProtocolVersion", "RequestId", "AccessToken"});
            if (!website)
            {
                reject("WebsiteNotConfigured");
                return;
            }
            if (client->ApiPending || !client->MatchId.empty() ||
                AccountBusy(client->WebsitePlayerId, nullptr))
            {
                reject("AccountBusy");
                return;
            }
            auto jwt = String(input, "AccessToken", MaximumAccessTokenCharacters);
            auto response =
                website->Post("/api/internal/battle/players/verify", Json{{"accessToken", jwt}});
            client->ApiPending = true;
            pendingApi.push_back(
                {client, ++client->ApiGeneration, request, "", true, std::move(response)});
            return;
        }
        if (type == "ListDecks")
        {
            Fields(input, {"Type", "ProtocolVersion", "RequestId"});
            auto response = Envelope("DeckCatalog", request);
            response["Payload"] = catalog->PublicDecks();
            response["CatalogVersion"] = catalog->Version;
            response["PersonalDeckSupported"] =
                !catalog->Version.empty() && catalog->Config.MinDeckSize == PersonalDeckCardCount &&
                catalog->Config.MaxDeckSize == PersonalDeckCardCount &&
                catalog->Config.MaxCardCopies == MaximumCopiesPerPersonalDeckCard;
            client->Send(response);
            return;
        }
        if (type == "QueueMatch")
        {
            Fields(input, {"Type", "ProtocolVersion", "RequestId", "DeckId", "AccessToken"});

            if (!client->MatchId.empty())
            {
                reject("AlreadyJoined");
                return;
            }

            auto existing = std::find_if(matchmakingQueue.begin(),
                                         matchmakingQueue.end(),
                                         [&](MatchmakingEntry const& entry)
                                         {
                                             auto c = entry.Client.lock();
                                             return c == client;
                                         });

            if (existing != matchmakingQueue.end())
            {
                reject("AlreadyQueued");
                return;
            }

            if (website)
            {
                if (client->ApiPending || AccountBusy(client->WebsitePlayerId, nullptr))
                {
                    reject("AccountBusy");
                    return;
                }
                auto deckId = String(input, "DeckId", 64);
                if (!IsUuid(deckId))
                {
                    reject("InvalidDeck");
                    return;
                }
                auto jwt = String(input, "AccessToken", MaximumAccessTokenCharacters);
                auto response = website->Post("/api/internal/battle/players/verify",
                                              Json{{"accessToken", jwt}});
                client->ApiPending = true;
                pendingApi.push_back(
                    {client, ++client->ApiGeneration, request, deckId, true, std::move(response)});
                return;
            }
            std::string deckId;
            if (input.contains("DeckId"))
            {
                deckId = String(input, "DeckId", 64);

                if (!catalog->FindDeck(deckId))
                {
                    reject("InvalidDeck");
                    return;
                }
            }

            matchmakingQueue.push_back({client, deckId});
            auto response = Envelope("MatchmakingQueued", request);
            response["QueuePosition"] = matchmakingQueue.size();

            client->Send(response);
            TryMatchmaking();

            return;
        }
        if (type == "CancelQueue")
        {
            Fields(input, {"Type", "ProtocolVersion", "RequestId"});

            auto existing = std::find_if(matchmakingQueue.begin(),
                                         matchmakingQueue.end(),
                                         [&](MatchmakingEntry const& entry)
                                         {
                                             auto c = entry.Client.lock();
                                             return c == client;
                                         });

            if (client->ApiPending)
            {
                client->ApiPending = false;
                ++client->ApiGeneration;
                client->Send(Envelope("MatchmakingCancelled", request));
                return;
            }
            if (existing == matchmakingQueue.end())
            {
                reject("NotQueued");
                return;
            }

            matchmakingQueue.erase(existing);

            auto response = Envelope("MatchmakingCancelled", request);
            client->Send(response);
            return;
        }
        if ((type == "CreateMatch" || type == "JoinMatch") &&
            (client->ApiPending ||
             std::any_of(matchmakingQueue.begin(),
                         matchmakingQueue.end(),
                         [&](auto const& entry) { return entry.Client.lock() == client; })))
        {
            reject("AlreadyQueued");
            return;
        }
        if (type == "CreateMatch")
        {
            Fields(input, {"Type", "ProtocolVersion", "RequestId", "DeckId"});
            if (!client->MatchId.empty())
            {
                reject("AlreadyJoined");
                return;
            }
            if (input.contains("DeckId") && !catalog->FindDeck(String(input, "DeckId", 64)))
            {
                reject("InvalidDeck");
                return;
            }
            Cleanup();
            if (matches.size() >= config.MaxMatches)
            {
                reject("MatchLimit");
                return;
            }
            auto id = "M" + std::to_string(++nextMatch) + "-" + Token().substr(0, 8);
            auto match = std::make_unique<MatchSession>(
                id,
                catalog,
                identities,
                [this](ReplayRecord record) { Archive(std::move(record)); },
                config.MulliganTimeoutSeconds,
                config.MulliganAutoKeep,
                config.TurnDurationSeconds);
            auto* session = match.get();
            matches.emplace(id, std::move(match));
            std::cout << "Match " << id << " created activeMatches=" << matches.size() << std::endl;
            try
            {
                session->Handle(client, input);
            }
            catch (...)
            {
                matches.erase(id);
                throw;
            }
            return;
        }
        auto id = String(input, "MatchId", 64);
        auto found = matches.find(id);
        if (found == matches.end())
        {
            reject("MatchNotFound");
            return;
        }
        if (!client->MatchId.empty() && client->MatchId != id)
        {
            reject("NotInMatch");
            return;
        }
        if (type != "JoinMatch" && client->MatchId != id)
        {
            reject("NotJoined");
            return;
        }
        found->second->Tick(Clock::now());
        found->second->Handle(client, input);
        // Closed sessions are collected after the handler, never during a live session method.
        Cleanup();
    }
    catch (std::exception const&)
    {
        auto error = Envelope("ServerError", request, client->MatchId);
        error["Reason"] = "InvalidRequest";
        error["Message"] = "요청 JSON 또는 패킷 형식이 올바르지 않습니다.";
        client->Send(error);
    }
}

void MatchManager::TryMatchmaking()
{
    std::erase_if(matchmakingQueue,
                  [](MatchmakingEntry const& entry)
                  {
                      auto client = entry.Client.lock();
                      return !client || !client->MatchId.empty();
                  });

    while (matchmakingQueue.size() >= 2)
    {
        if (matches.size() >= config.MaxMatches)
        {
            return;
        }

        auto first = matchmakingQueue.front();
        matchmakingQueue.pop_front();

        auto second = matchmakingQueue.front();
        matchmakingQueue.pop_front();

        auto player1 = first.Client.lock();
        auto player2 = second.Client.lock();

        if (!player1 || !player2)
        {
            continue;
        }

        // 같은 연결이거나, 이미 다른 매치에 참여 중인 경우 방지
        if (player1 == player2 || !player1->MatchId.empty() || !player2->MatchId.empty())
        {
            continue;
        }

        auto id =
            website ? MatchUuid() : "M" + std::to_string(++nextMatch) + "-" + Token().substr(0, 8);
        auto match = std::make_unique<MatchSession>(
            id,
            catalog,
            identities,
            [this](ReplayRecord record) { Archive(std::move(record)); },
            config.MulliganTimeoutSeconds,
            config.MulliganAutoKeep,
            config.TurnDurationSeconds);

        auto* session = match.get();
        matches.emplace(id, std::move(match));

        try
        {
            Json createRequest = {
                {"Type", "CreateMatch"},
                {"ProtocolVersion", ProtocolVersion},
                {"RequestId", Token()},
            };

            if (!website && !first.DeckId.empty())
            {
                createRequest["DeckId"] = first.DeckId;
            }

            session->Handle(player1, createRequest);

            Json joinRequest = {
                {"Type", "JoinMatch"},
                {"ProtocolVersion", ProtocolVersion},
                {"RequestId", Token()},
                {"MatchId", id},
            };

            if (!website && !second.DeckId.empty())
            {
                joinRequest["DeckId"] = second.DeckId;
            }

            session->Handle(player2, joinRequest);

            if (player1->MatchId != id || player2->MatchId != id)
            {
                session->Close("MatchmakingFailed");
                matches.erase(id);
                continue;
            }

            auto found = Envelope("MatchmakingFound", Token(), id);

            player1->Send(found);
            player2->Send(found);

            if (website)
            {
                Json participants = Json::array();
                for (auto const& deck : {first.WebsiteDeck, second.WebsiteDeck})
                {
                    participants.push_back({{"playerId", deck.at("playerId")},
                                            {"deckId", deck.at("deckId")},
                                            {"snapshotId", deck.at("snapshotId")}});
                }
                rankedMatches[id] = {
                    participants, {player1->Player, player2->Player}, {player1, player2}};
                session->SetRankedDecks({first.WebsiteDeck, second.WebsiteDeck});
            }
            std::cout << "Matchmaking completed: " << id << " activeMatches=" << matches.size()
                      << std::endl;
        }
        catch (const std::exception& e)
        {
            std::cerr << "Matchmaking failed" << std::endl;
            session->Close("MatchmakingFailed");
            matches.erase(id);
            throw;
        }
    }
}

void MatchManager::Disconnect(std::shared_ptr<ClientSession> const& client)
{
    client->Connected = false;
    client->ApiPending = false;
    ++client->ApiGeneration;
    std::erase_if(matchmakingQueue,
                  [&](MatchmakingEntry const& entry)
                  {
                      auto c = entry.Client.lock();
                      return c == client;
                  });

    auto found = matches.find(client->MatchId);
    if (found != matches.end())
    {
        found->second->Disconnect(client);
    }
    client->Player = 0;
    client->MatchId.clear();
}
void MatchManager::Cleanup(Clock::time_point now)
{
    PollWebsite();
    for (auto it = matches.begin(); it != matches.end();)
    {
        auto& match = *it->second;
        match.Tick(now);
        int timeout =
            (match.Status() == MatchStatus::Playing || match.Status() == MatchStatus::Mulligan)
                ? config.PlayingTimeoutSeconds
            : match.Status() == MatchStatus::Finished ? config.FinishedTimeoutSeconds
                                                      : config.WaitingTimeoutSeconds;
        if (match.Status() != MatchStatus::Closed && match.Status() != MatchStatus::Mulligan &&
            now - match.LastActivity() >= std::chrono::seconds(timeout))
        {
            match.Close("Expired");
        }
        if (match.Status() == MatchStatus::Closed)
        {
            auto id = it->first;
            rankedMatches.erase(id);
            it = matches.erase(it);
            std::cout << "Match " << id << " removed activeMatches=" << matches.size() << std::endl;
        }
        else
        {
            ++it;
        }
    }
    while (!replays.empty() &&
           now - replays.front().StoredAt >= std::chrono::seconds(config.ReplayRetentionSeconds))
    {
        replays.pop_front();
    }

    TryMatchmaking();
}
bool MatchManager::AccountBusy(std::string const& playerId, ClientSession const* except) const
{
    if (playerId.empty())
    {
        return false;
    }
    for (auto const& entry : matchmakingQueue)
    {
        auto client = entry.Client.lock();
        if (client && client.get() != except && client->WebsitePlayerId == playerId)
        {
            return true;
        }
    }
    for (auto const& [id, ranked] : rankedMatches)
    {
        auto match = Find(id);
        if (match && match->Status() != MatchStatus::Finished &&
            match->Status() != MatchStatus::Closed)
        {
            for (auto const& participant : ranked.Participants)
            {
                if (participant.at("playerId") == playerId)
                {
                    return true;
                }
            }
        }
    }
    return false;
}
void MatchManager::PollWebsite()
{
    if (!website)
    {
        return;
    }
    for (auto it = pendingApi.begin(); it != pendingApi.end();)
    {
        if (it->Response.wait_for(std::chrono::seconds(0)) != std::future_status::ready)
        {
            ++it;
            continue;
        }
        auto operation = std::move(*it);
        it = pendingApi.erase(it);
        auto client = operation.Client.lock();
        if (!client || !client->Connected || client->ApiGeneration != operation.Generation)
        {
            continue;
        }
        auto reject = [&](std::string const& reason)
        {
            client->ApiPending = false;
            auto error = Envelope("ServerError", operation.RequestId);
            error["Reason"] = reason;
            client->Send(error);
        };
        try
        {
            auto response = operation.Response.get();
            if (!response.Success())
            {
                reject(response.Reason());
                continue;
            }
            if (operation.Verifying)
            {
                auto playerId = response.Body.at("playerId").get<std::string>();
                if (!IsUuid(playerId) ||
                    (!client->WebsitePlayerId.empty() && playerId != client->WebsitePlayerId))
                {
                    reject("AuthenticationFailed");
                    continue;
                }
                if (AccountBusy(playerId, client.get()))
                {
                    reject("AccountBusy");
                    continue;
                }
                client->WebsitePlayerId = playerId;
                if (operation.DeckId.empty())
                {
                    client->ApiPending = false;
                    client->Send(Envelope("Authenticated", operation.RequestId));
                    continue;
                }
                operation.Verifying = false;
                operation.Response = website->Post("/api/internal/battle/players/" + playerId +
                                                       "/decks/" + operation.DeckId + "/validate",
                                                   Json{{"catalogVersion", catalog->Version}});
                pendingApi.push_back(std::move(operation));
                // push_back may invalidate deque iterators; process the rest next tick.
                break;
            }
            auto const& deck = response.Body;
            if (deck.at("playerId") != client->WebsitePlayerId ||
                deck.at("deckId") != operation.DeckId ||
                deck.at("catalogVersion") != catalog->Version ||
                !IsUuid(deck.at("snapshotId").get<std::string>()) ||
                !catalog
                     ->PersonalDeckError(
                         deck.at("cardIds").get<std::vector<GameCore::CardDefinitionId>>())
                     .empty())
            {
                reject("InvalidDeck");
                continue;
            }
            if (AccountBusy(client->WebsitePlayerId, client.get()))
            {
                reject("AccountBusy");
                continue;
            }
            client->ApiPending = false;
            matchmakingQueue.push_back({client, operation.DeckId, deck});
            auto queued = Envelope("MatchmakingQueued", operation.RequestId);
            queued["QueuePosition"] = matchmakingQueue.size();
            client->Send(queued);
        }
        catch (std::exception const&)
        {
            reject("WebsiteUnavailable");
        }
    }
    for (auto const& response : website->TakeResults())
    {
        auto found = matches.find(response.value("matchId", ""));
        if (found != matches.end())
        {
            found->second->DeliverRankChanges(response.at("rankChanges"));
        }
        auto ranked = rankedMatches.find(response.value("matchId", ""));
        if (ranked != rankedMatches.end())
        {
            for (std::size_t i = 0; i < ranked->second.Clients.size(); ++i)
            {
                auto client = ranked->second.Clients[i].lock();
                if (client && client->Connected && client->MatchId.empty())
                {
                    for (auto const& change : response.at("rankChanges"))
                    {
                        if (change.at("playerId") == client->WebsitePlayerId)
                        {
                            auto updated = Envelope("RankUpdated", "", response.at("matchId"));
                            updated["Payload"] = change;
                            client->Send(updated);
                        }
                    }
                }
            }
        }
    }
}
void MatchManager::Shutdown()
{
    for (auto& [id, match] : matches)
    {
        match->Close("ServerShutdown");
    }
    matches.clear();
    matchmakingQueue.clear();
    std::cout << "Server shutdown activeMatches=0" << std::endl;
}
} // namespace ProjectC::Network
