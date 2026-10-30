#include "MatchManager.hpp"
#include <iostream>
#include <stdexcept>
namespace ProjectC::Network
{
MatchManager::MatchManager(ServerConfig config) : config(std::move(config)), catalog(std::make_shared<CardCatalog>(CardCatalog::Load(this->config.DataDirectory)))
{
    if(this->config.MaxMatches==0 || this->config.MaxMatches>1024 || this->config.MaxReplays==0
        || this->config.WaitingTimeoutSeconds<1 || this->config.PlayingTimeoutSeconds<1
        || this->config.FinishedTimeoutSeconds<1 || this->config.ReplayRetentionSeconds<1)
        throw std::runtime_error("Invalid server configuration");
}
MatchSession const* MatchManager::Find(std::string const& id) const
{
    auto found=matches.find(id); return found==matches.end()?nullptr:found->second.get();
}
void MatchManager::Archive(ReplayRecord record)
{
    if(!record.Verify()) throw std::runtime_error("Replay verification failed");
    std::cout << "Match " << record.MatchId << " game=" << record.GameNumber << " replayVerified=true\n";
    replays.push_back(std::move(record));
    while(replays.size()>config.MaxReplays) replays.pop_front();
}
void MatchManager::Receive(std::shared_ptr<ClientSession> const& client, std::string const& message)
{
    std::string request;
    try
    {
        auto input=ParseJson(message);
        request=String(input,"RequestId",64);
        if(Number(input,"ProtocolVersion",ProtocolVersion)!=ProtocolVersion) throw std::runtime_error("ProtocolVersion");
        auto type=String(input,"Type");
        auto reject=[&](std::string const& reason) {
            auto error=Envelope(type=="SubmitCommand"?"CommandRejected":"ServerError",request,client->MatchId);
            error["Reason"]=reason; client->Send(error);
        };
        if(type=="ListDecks")
        {
            Fields(input,{"Type","ProtocolVersion","RequestId"});
            auto response=Envelope("DeckCatalog",request); response["Payload"]=catalog->PublicDecks(); client->Send(response); return;
        }
        if(type=="CreateMatch")
        {
            Fields(input,{"Type","ProtocolVersion","RequestId","DeckId"});
            if(!client->MatchId.empty()) {reject("AlreadyJoined");return;}
            if(input.contains("DeckId") && !catalog->FindDeck(String(input,"DeckId",64))) {reject("InvalidDeck");return;}
            Cleanup();
            if(matches.size()>=config.MaxMatches) {reject("MatchLimit");return;}
            auto id="M"+std::to_string(++nextMatch)+"-"+Token().substr(0,8);
            auto match=std::make_unique<MatchSession>(id,catalog,identities,[this](ReplayRecord record){Archive(std::move(record));});
            auto* session=match.get(); matches.emplace(id,std::move(match));
            std::cout << "Match " << id << " created activeMatches=" << matches.size() << std::endl;
            try {session->Handle(client,input);} catch(...) {matches.erase(id);throw;}
            return;
        }
        auto id=String(input,"MatchId",64);
        auto found=matches.find(id);
        if(found==matches.end()) {reject("MatchNotFound");return;}
        if(!client->MatchId.empty() && client->MatchId!=id) {reject("NotInMatch");return;}
        if(type!="JoinMatch" && client->MatchId!=id) {reject("NotJoined");return;}
        found->second->Handle(client,input);
        // Closed sessions are collected after the handler, never during a live session method.
        Cleanup();
    }
    catch(std::exception const&)
    {
        auto error=Envelope("ServerError",request,client->MatchId); error["Reason"]="InvalidRequest"; client->Send(error);
    }
}
void MatchManager::Disconnect(std::shared_ptr<ClientSession> const& client)
{
    auto found=matches.find(client->MatchId);
    if(found!=matches.end()) found->second->Disconnect(client);
    client->Player=0; client->MatchId.clear();
}
void MatchManager::Cleanup(Clock::time_point now)
{
    for(auto it=matches.begin();it!=matches.end();)
    {
        auto& match=*it->second;
        int timeout=match.Status()==MatchStatus::Playing?config.PlayingTimeoutSeconds:
            match.Status()==MatchStatus::Finished?config.FinishedTimeoutSeconds:config.WaitingTimeoutSeconds;
        if(match.Status()!=MatchStatus::Closed && now-match.LastActivity()>=std::chrono::seconds(timeout)) match.Close("Expired");
        if(match.Status()==MatchStatus::Closed)
        {
            auto id=it->first; it=matches.erase(it);
            std::cout << "Match " << id << " removed activeMatches=" << matches.size() << std::endl;
        }
        else ++it;
    }
    while(!replays.empty() && now-replays.front().StoredAt>=std::chrono::seconds(config.ReplayRetentionSeconds)) replays.pop_front();
}
void MatchManager::Shutdown()
{
    for(auto& [id,match]:matches) match->Close("ServerShutdown");
    matches.clear();
    std::cout << "Server shutdown activeMatches=0" << std::endl;
}
}
