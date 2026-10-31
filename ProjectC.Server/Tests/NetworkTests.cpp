#include "MatchManager.hpp"
#include <iostream>
#include <stdexcept>
#include <source_location>
#include <filesystem>
#include <fstream>
using namespace ProjectC::Network;
void RunHyakkiComboTests();
void Check(bool value, std::source_location where=std::source_location::current()) {if(!value) throw std::runtime_error("Network assertion at line "+std::to_string(where.line()));}
ServerConfig LegacyConfig()
{
    ServerConfig config;config.DataDirectory=(std::filesystem::path(DefaultDataDirectory()).parent_path()/"Tests"/"LegacyData").string();return config;
}
struct Peer
{
    MatchManager& Manager;
    std::shared_ptr<ClientSession> Client=std::make_shared<ClientSession>();
    std::vector<Json> Messages;
    int Counter=0;
    Peer(MatchManager& manager):Manager(manager) {Client->Send=[this](Json const& m){Messages.push_back(m);};}
    void Send(std::string type,Json fields=Json::object())
    {
        fields["Type"]=type;fields["ProtocolVersion"]=2;fields["RequestId"]=std::to_string(++Counter);Manager.Receive(Client,fields.dump());
    }
    Json Last(std::string const& type) const
    {
        for(auto it=Messages.rbegin();it!=Messages.rend();++it) if((*it)["Type"]==type) return *it;
        throw std::runtime_error("Missing message "+type);
    }
};
void InteractionTests()
{
    ServerConfig config;config.DataDirectory=(std::filesystem::path(DefaultDataDirectory()).parent_path()/"Tests"/"InteractionData").string();
    MatchManager manager(config);Peer a(manager),b(manager);
    a.Send("CreateMatch",{{"DeckId","Interaction"}});auto id=a.Client->MatchId;
    b.Send("JoinMatch",{{"MatchId",id},{"DeckId","Interaction"}});
    for(auto* p:{&a,&b}) p->Send("SetReady",{{"MatchId",id},{"GameNumber",0},{"Ready",true}});
    auto submit=[&](Peer& p,Json command) {p.Send("SubmitCommand",{{"MatchId",id},{"GameNumber",1},{"SequenceNumber",p.Last("StateSnapshot")["SequenceNumber"]},{"Command",command}});};
    int source=a.Last("StateSnapshot")["Payload"]["Players"][0]["Hand"][0]["Id"];
    int enemy=b.Last("StateSnapshot")["Payload"]["Players"][1]["Hand"][0]["Id"];
    int enemy2=b.Last("StateSnapshot")["Payload"]["Players"][1]["Hand"][1]["Id"];
    submit(a,{{"Type","PlayCard"},{"CardId",source}});submit(a,{{"Type","EndTurn"}});
    submit(b,{{"Type","PlayCard"},{"CardId",enemy}});submit(b,{{"Type","EndTurn"}});
    submit(a,{{"Type","EndTurn"}});submit(b,{{"Type","PlayCard"},{"CardId",enemy2}});submit(b,{{"Type","EndTurn"}});
    auto before=manager.Find(id)->Snapshot(a.Client->Player);
    auto options=before["Players"][0]["Field"][0]["Activations"];
    Check(options.size()==3 && options[0]["Selections"][0]["Candidates"].size()==2);
    Check(options[2]["Interactive"]==true && options[2]["Selections"].empty());
    Check(options[1]["IsManifest"]==true && options[1]["CanActivate"]==true);
    Check(manager.Find(id)->Snapshot(b.Client->Player)["Players"][0]["Field"][0]["Activations"].empty());
    Json targets=Json::array({Json::array({Json{{"PlayerId",b.Client->Player},{"CardId",enemy},{"Zone",2}},Json{{"PlayerId",b.Client->Player},{"CardId",enemy2},{"Zone",2}}})});
    Json command{{"Type","ActivateEffect"},{"CardId",source},{"EffectId",1},{"Targets",targets}};
    auto wrong=command;wrong["Targets"][0][0]["Zone"]=0;submit(a,wrong);Check(a.Last("CommandRejected")["Reason"]=="InvalidTarget");
    wrong=command;wrong["Targets"][0][1]=wrong["Targets"][0][0];submit(a,wrong);Check(a.Last("CommandRejected")["Reason"].get<std::string>().find("InvalidTarget")!=std::string::npos);
    wrong=command;wrong["CardId"]=enemy;submit(a,wrong);Check(a.Last("CommandRejected")["Reason"].get<std::string>().find("CardNotFound")!=std::string::npos);
    wrong=command;wrong["Amount"]=100;submit(a,wrong);Check(a.Last("ServerError")["Reason"]=="InvalidRequest");
    Check(manager.Find(id)->Snapshot(a.Client->Player)==before);
    submit(a,command);
    auto after=manager.Find(id)->Snapshot(a.Client->Player);
    Check(after["Players"][1]["Field"][0]["Hp"]==4 && after["Players"][1]["Field"][1]["Hp"]==4);
    submit(a,{{"Type","ActivateEffect"},{"CardId",source},{"EffectId",2},{"Targets",Json::array({Json::array({Json{{"PlayerId",a.Client->Player},{"CardId",source},{"Zone",2}}})})}});
    Check(manager.Find(id)->Snapshot(a.Client->Player)["Players"][0]["Field"][0]["DefinitionId"]==101);
    a.Send("CloseMatch",{{"MatchId",id},{"GameNumber",1}});Check(manager.Replays().front().Verify());
    std::cout<<"Activated effects: candidates, privacy, multi-target, tampering, manifestation and replay passed\n";
}
void ContentChoiceTests()
{
    auto root=std::filesystem::path(DefaultDataDirectory());
    auto directory=std::filesystem::temp_directory_path()/("ProjectCContent-"+Token());
    std::filesystem::create_directory(directory);
    struct Cleanup
    {
        std::filesystem::path Path;
        ~Cleanup() {std::filesystem::remove(Path/"Cards.json");std::filesystem::remove(Path/"Decks.json");std::filesystem::remove(Path);}
    } cleanup{directory};
    std::filesystem::copy_file(root/"HyakkiYagyo"/"Cards.json",directory/"Cards.json");
    Json decks;{std::ifstream file(root/"HyakkiYagyo"/"Decks.json");file>>decks;}
    decks["Config"]["InitialHandSize"]=2;decks["Config"]["DrawPerTurn"]=0;
    decks["Config"]["MinDeckSize"]=4;decks["Config"]["MaxDeckSize"]=40;decks["Config"]["MaxCardCopies"]=10;
    for(int definition:{1102,1002,2003})
    {
        decks["Decks"]=Json::array({Json{{"Id","Choice"},{"Name","Choice fixture"},{"Cards",std::vector<int>(10,definition)}}});
        {std::ofstream file(directory/"Decks.json");file<<decks.dump();}
        ServerConfig config;config.DataDirectory=directory.string();MatchManager manager(config);Peer a(manager),b(manager);
        a.Send("CreateMatch");auto id=a.Client->MatchId;b.Send("JoinMatch",{{"MatchId",id}});
        for(auto* peer:{&a,&b}) peer->Send("SetReady",{{"MatchId",id},{"GameNumber",0},{"Ready",true}});
        auto send=[&](Peer& peer,Json command){peer.Send("SubmitCommand",{{"MatchId",id},{"GameNumber",1},{"SequenceNumber",1},{"Command",command}});};
        auto before=manager.Find(id)->Snapshot(a.Client->Player);int source=before["Players"][0]["Hand"][0]["Id"];
        Check(before["Players"][0]["YokaiPower"]==2 && before["SummoningSickness"]==false);
        send(a,{{"Type","PlayerAction"},{"CardId",source},{"EffectId",definition==1002?0:1}});
        if(definition==2003)
        {
            auto snapshot=manager.Find(id)->Snapshot(b.Client->Player);
            Check(snapshot["Players"][0]["HiddenExileCount"]==5 && !snapshot["Players"][0].contains("HiddenExile"));
            auto events=b.Last("GameEvents")["Payload"];
            for(auto const& event:events)
                if(event["ToZone"]==5) Check(event["TargetCardId"]==0 && event["DefinitionId"]==0);
        }
        else
        {
            auto choice=a.Last("ActionChoiceRequired")["Payload"];
            Check(manager.Find(id)->Snapshot(a.Client->Player)["Players"]==before["Players"]);
            Check(manager.Find(id)->Snapshot(b.Client->Player)["ResolvingPlayerId"]==a.Client->Player);
            for(auto const& message:b.Messages) Check(message["Type"]!="ActionChoiceRequired");
            if(definition==1002) for(auto const& option:choice["Candidates"]) Check(option["CardId"]==0 && option["Card"]["Id"]==0);
            else for(auto const& option:choice["Candidates"]) Check(option["CardId"]!=source);
            send(a,{{"Type","EndTurn"}});Check(a.Last("CommandRejected")["Reason"]=="ActionPending");
            Json selection{{"Type","ResolveChoice"},{"ChoiceId",choice["ChoiceId"]},{"Choices",Json::array({choice["Candidates"][0]["Token"]})}};
            send(b,selection);Check(b.Last("CommandRejected")["Reason"]=="InvalidChoiceSession");
            auto forged=selection;forged["Choices"][0]="forged";send(a,forged);Check(a.Last("CommandRejected")["Reason"]=="InvalidChoice");
            auto token=a.Last("SessionUpdated")["SessionToken"];manager.Disconnect(a.Client);Peer resumed(manager);
            resumed.Send("JoinMatch",{{"MatchId",id},{"SessionToken",token}});
            Check(resumed.Last("ActionChoiceRequired")["Payload"]["ChoiceId"]==choice["ChoiceId"]);
            resumed.Send("SubmitCommand",{{"MatchId",id},{"GameNumber",1},{"SequenceNumber",1},{"Command",selection}});
            Check(resumed.Last("CommandAccepted")["SequenceNumber"]==2);
            auto after=manager.Find(id)->Snapshot(resumed.Client->Player);
            Check(after["ResolvingPlayerId"]==0 && after["Players"][0]["Graveyard"].size()==(definition==1102?2:1));
            resumed.Send("CloseMatch",{{"MatchId",id},{"GameNumber",1}});
            Check(manager.Replays().front().Verify());continue;
        }
        a.Send("CloseMatch",{{"MatchId",id},{"GameNumber",1}});Check(manager.Replays().front().Verify());
    }
    std::cout<<"Content choices: atomic private previews, opaque deck choices, session binding, reconnect, hidden exile and replay passed\n";
}
void PublicChoiceCancellationTests()
{
    auto root=std::filesystem::path(DefaultDataDirectory());
    auto directory=std::filesystem::temp_directory_path()/("ProjectCPublicChoice-"+Token());
    std::filesystem::create_directory(directory);
    struct Cleanup {std::filesystem::path Path;~Cleanup(){std::filesystem::remove(Path/"Cards.json");std::filesystem::remove(Path/"Decks.json");std::filesystem::remove(Path);}} cleanup{directory};
    std::filesystem::copy_file(root/"HyakkiYagyo"/"Cards.json",directory/"Cards.json");
    Json decks;{std::ifstream file(root/"HyakkiYagyo"/"Decks.json");file>>decks;}
    decks["Config"]["InitialHandSize"]=10;decks["Config"]["DrawPerTurn"]=0;
    decks["Config"]["MinDeckSize"]=decks["Config"]["MaxDeckSize"]=10;decks["Config"]["MaxCardCopies"]=10;
    decks["Decks"]=Json::array({Json{{"Id","Public"},{"Name","Public choice"},{"Cards",std::vector<int>{1001,1001,1001,1001,1001,2002,2002,2002,2002,2002}}}});
    {std::ofstream file(directory/"Decks.json");file<<decks.dump();}
    ServerConfig config;config.DataDirectory=directory.string();MatchManager manager(config);Peer a(manager),b(manager);
    a.Send("CreateMatch");auto id=a.Client->MatchId;b.Send("JoinMatch",{{"MatchId",id}});
    for(auto* peer:{&a,&b}) peer->Send("SetReady",{{"MatchId",id},{"GameNumber",0},{"Ready",true}});
    auto submit=[&](Peer& peer,Json command){peer.Send("SubmitCommand",{{"MatchId",id},{"GameNumber",1},{"SequenceNumber",peer.Last("StateSnapshot")["SequenceNumber"]},{"Command",command}});};
    submit(a,{{"Type","EndTurn"}});
    auto hand=b.Last("StateSnapshot")["Payload"]["Players"][1]["Hand"];
    int monster=0;for(auto const& card:hand) if(card["DefinitionId"]==1001) monster=card["Id"];
    Check(monster>0);submit(b,{{"Type","PlayCard"},{"CardId",monster}});submit(b,{{"Type","EndTurn"}});
    auto before=manager.Find(id)->Snapshot(a.Client->Player);int spell=0;
    for(auto const& card:before["Players"][0]["Hand"]) if(card["DefinitionId"]==2002) spell=card["Id"];
    Check(spell>0);submit(a,{{"Type","PlayerAction"},{"CardId",spell},{"EffectId",1}});
    auto choice=a.Last("ActionChoiceRequired")["Payload"];Check(choice["CanCancel"]==true && choice["IsCost"]==false && choice["Candidates"].size()==1);
    auto pending=a.Last("CommandPending")["RequestId"];
    submit(a,{{"Type","CancelAction"},{"ChoiceId",choice["ChoiceId"]}});
    Check(a.Last("CommandAccepted")["SequenceNumber"]==4);
    Check(manager.Find(id)->Snapshot(a.Client->Player)==before);
    bool rejected=false;for(auto const& message:a.Messages) if(message["Type"]=="CommandRejected" && message["RequestId"]==pending && message["Reason"]=="ActionCancelled") rejected=true;
    Check(rejected);
    a.Send("CloseMatch",{{"MatchId",id},{"GameNumber",1}});Check(manager.Replays().front().Verify());
    std::cout<<"Public choice cancellation: no state commit, original request rejected, replay passed\n";
}
int main()
{
try
{
    InteractionTests();
    RunHyakkiComboTests();
    ContentChoiceTests();
    PublicChoiceCancellationTests();
    MatchManager manager(LegacyConfig());
    Peer a(manager),b(manager),c(manager),d(manager),outsider(manager);
    a.Send("CreateMatch");auto id=a.Client->MatchId;auto token=a.Last("MatchCreated")["SessionToken"];
    b.Send("JoinMatch",{{"MatchId",id}});
    c.Send("CreateMatch");auto other=c.Client->MatchId;d.Send("JoinMatch",{{"MatchId",other}});
    Check(manager.ActiveCount()==2 && id!=other && a.Client->Player!=c.Client->Player);
    outsider.Send("JoinMatch",{{"MatchId",id}});Check(outsider.Last("ServerError")["Reason"]=="MatchFull");
    a.Send("SetReady",{{"MatchId",id},{"GameNumber",0},{"Ready",true},{"DeckId","Strikers"}});
    b.Send("SetReady",{{"MatchId",id},{"GameNumber",0},{"Ready",true},{"DeckId","Wardens"}});
    auto state=manager.Find(id)->Snapshot(a.Client->Player);
    Check(state["MatchStatus"]=="Playing" && manager.Find(other)->Snapshot(c.Client->Player)["Started"]==false);
    Check(state["Players"][1]["Hand"].empty() && !state["Players"][0].contains("Deck"));
    Check(state.dump().find("Test Warden")==std::string::npos);
    Check(state["FieldLimit"]==5);
    Check(state["Players"][0]["Hand"][0]["RulesText"]=="No card effects.");
    Check(state["Players"][0]["Hand"][0].contains("ManifestationText"));
    Check(state.dump().find("On summon")==std::string::npos); // No opponent definition text.
    int card=state["Players"][0]["Hand"][0]["Id"];
    Json command{{"MatchId",id},{"GameNumber",1},{"SequenceNumber",1},{"Command",{{"Type","PlayCard"},{"CardId",card}}}};
    a.Send("SubmitCommand",command);Check(a.Last("CommandAccepted")["SequenceNumber"]==2);
    Check(manager.Find(id)->Snapshot(a.Client->Player)["Players"][0]["Field"][0]["SummoningSick"]==true);
    auto request=a.Counter;command["Type"]="SubmitCommand";command["ProtocolVersion"]=2;command["RequestId"]=std::to_string(request);
    auto after=manager.Find(id)->Snapshot(a.Client->Player);manager.Receive(a.Client,command.dump());Check(manager.Find(id)->Snapshot(a.Client->Player)==after);
    command["Command"]["CardId"]=999;manager.Receive(a.Client,command.dump());Check(a.Last("CommandRejected")["Reason"]=="RequestIdConflict");
    c.Send("SubmitCommand",{{"MatchId",id},{"GameNumber",1},{"SequenceNumber",2},{"Command",{{"Type","EndTurn"}}}});Check(c.Last("CommandRejected")["Reason"]=="NotInMatch");
    a.Send("SubmitCommand",{{"MatchId",id},{"GameNumber",1},{"SequenceNumber",2},{"PlayerId",b.Client->Player},{"Command",{{"Type","EndTurn"}}}});Check(a.Last("CommandRejected")["Reason"]=="PlayerMismatch");
    for(auto type:{"Transform","ChangeLife","Draw","StartTurn"})
    {a.Send("SubmitCommand",{{"MatchId",id},{"GameNumber",1},{"SequenceNumber",2},{"Command",{{"Type",type}}}});Check(a.Last("CommandRejected")["Reason"]=="CommandNotAllowed");}
    manager.Disconnect(a.Client);Peer resumed(manager);
    // The pre-game token was rotated at start and is no longer valid.
    resumed.Send("JoinMatch",{{"MatchId",id},{"SessionToken",token}});Check(resumed.Last("ServerError")["Reason"]=="InvalidSession");
    auto current=a.Last("SessionUpdated")["SessionToken"];
    resumed.Send("JoinMatch",{{"MatchId",id},{"SessionToken",current}});Check(manager.Find(id)->Snapshot(resumed.Client->Player)==after);
    auto catalog=CardCatalog::Load(DefaultDataDirectory());
    auto first=catalog.Initialize({1,2},{"Strikers","Wardens"},12345,1);
    Check(first==catalog.Initialize({1,2},{"Strikers","Wardens"},12345,1));
    Check(!catalog.ValidateDeck({10,10,10,10,10}) && !catalog.ValidateDeck({999,10,11,10}));
    for(auto text:{"{","{}","[]","null","{\"Type\":2}","{\"Type\":\"x\",\"Type\":\"y\"}"})
    {manager.Receive(resumed.Client,text);Check(resumed.Last("ServerError")["Reason"]=="InvalidRequest");}
    resumed.Send("CloseMatch",{{"MatchId",id},{"GameNumber",1}});
    Check(!manager.Replays().empty() && manager.Replays().front().Verify());
    manager.Cleanup(Clock::now()+std::chrono::hours(2));Check(manager.ActiveCount()==0);
    manager.Cleanup(Clock::now()+std::chrono::hours(3));Check(manager.Replays().empty());
    auto limited=LegacyConfig();limited.MaxMatches=1;MatchManager single(limited);Peer x(single),y(single);
    x.Send("CreateMatch");y.Send("CreateMatch");Check(y.Last("ServerError")["Reason"]=="MatchLimit");single.Shutdown();Check(x.Client->MatchId.empty());
    auto expires=LegacyConfig();expires.FinishedTimeoutSeconds=1;MatchManager completed(expires);Peer p1(completed),p2(completed);
    p1.Send("CreateMatch",{{"DeckId","Strikers"}});auto finishedId=p1.Client->MatchId;
    p2.Send("JoinMatch",{{"MatchId",finishedId},{"DeckId","Wardens"}});
    for(auto* p:{&p1,&p2}) p->Send("SetReady",{{"MatchId",finishedId},{"GameNumber",0},{"Ready",true}});
    auto firstView=completed.Find(finishedId)->Snapshot(p1.Client->Player);
    int firstCard=firstView["Players"][0]["Hand"][0]["Id"];
    auto execute=[&](Peer& p, Json cmd) {
        auto seq=p.Last("StateSnapshot")["SequenceNumber"];
        p.Send("SubmitCommand",{{"MatchId",finishedId},{"GameNumber",1},{"SequenceNumber",seq},{"Command",cmd}});
    };
    execute(p1,{{"Type","PlayCard"},{"CardId",firstCard}});
    execute(p1,{{"Type","EndTurn"}});execute(p2,{{"Type","EndTurn"}});
    execute(p1,{{"Type","AdvancePhase"}});execute(p1,{{"Type","DirectAttack"},{"CardId",firstCard}});
    Check(completed.Find(finishedId)->Status()==MatchStatus::Finished);
    Check(completed.Replays().size()==1 && completed.Replays()[0].Verify());
    completed.Cleanup(Clock::now()+std::chrono::seconds(2));
    Check(completed.ActiveCount()==0 && completed.Replays().size()==1 && p1.Client->Player==0);
    std::cout << "Manager isolation, protocol, privacy, reconnect, deck, replay and cleanup tests passed\n";
}
catch(std::exception const& error) {std::cerr<<error.what()<<std::endl;return 1;}
}
