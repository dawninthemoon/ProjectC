#include "MatchSession.hpp"
#include "ProjectC/CardEffects.hpp"
#include <stdexcept>
#include <limits>
#include <iostream>
#include <algorithm>
namespace ProjectC::Network
{
using namespace GameCore;
namespace
{
bool VisibleCard(GameState const& state, int viewer, CardInstanceId id)
{
    if (id == 0) return false;
    for (auto const& player : state.Players)
    {
        for (auto const& card : player.Field) if (card.Id == id) return true;
        for (auto const& card : player.Graveyard) if (card.Id == id) return true;
        for (auto const& card : player.Exile) if(card.Id==id) return true;
        if (player.Id == viewer) for (auto const& card : player.Hand) if (card.Id == id) return true;
    }
    return false;
}
std::string RuleReason(CommandError error)
{
    static char const* const names[] = {
        "Success", "BattleRejected", "GameFinished", "InvalidState", "PlayerNotFound",
        "NotActivePlayer", "WrongPhase", "TurnNotStarted", "TurnAlreadyStarted",
        "CardNotFound", "WrongZone", "NotOwner", "InvalidCount", "DeckEmpty",
        "InsufficientCards", "HandFull", "FieldFull", "DrawDisabled", "InvalidDefinition",
        "InvalidLifeChange", "TurnOverflow", "NormalSummonLimit", "DefenderHasMonsters",
        "EffectNotFound", "InvalidTarget", "InvalidTargetCount", "UnsupportedEffect",
        "InvalidEffect", "InvalidTransformation", "TriggerLimitReached", "InsufficientResource", "InvalidCost", "UsageLimitReached", "ConditionNotMet", "ChoiceRequired", "DirectAttackRestricted"};
    return names[static_cast<int>(error)];
}
char const* StatusName(MatchStatus status)
{
    switch(status) {
    case MatchStatus::WaitingForPlayers:return "WaitingForPlayers";
    case MatchStatus::Ready:return "Ready";
    case MatchStatus::Playing:return "Playing";
    case MatchStatus::Finished:return "Finished";
    default:return "Closed";
    }
}
// Descriptions come from the visible definition, never from an opponent's deck.
std::string RulesText(CardDefinition const& definition)
{
    static char const* const triggers[]={"Activated", "On summon", "On destruction", "On damage", "On turn start", "On turn end", "On normal summon", "On revival", "On manifestation"};
    static char const* const operations[]={"Draw", "Mill", "Salvage", "Search", "Summon from deck", "Summon from graveyard", "Return to hand", "Destroy", "Deal damage", "Heal", "Modify stats", "Transform", "Discard", "Send to graveyard", "Special summon from hand", "Exile", "Gain Yokai Power"};
    static char const* const targets[]={"self", "friendly monster", "enemy monster", "any monster", "friendly player", "enemy player", "friendly deck", "friendly hand", "friendly graveyard", "enemy graveyard"};
    std::string text;
    for(auto const& effect:definition.Effects)
    {
        if(!text.empty()) text+="\n";
        text+=triggers[static_cast<int>(effect.Trigger)]; text+=": ";
        for(auto const& cost:effect.Costs)
        {
            int amount=cost.Kind==CostKind::Tribute || cost.Kind==CostKind::Discard?static_cast<int>(cost.Targets.MinCount):cost.Amount;
            text+="Cost "+std::to_string(amount)+" "+(cost.Kind==CostKind::YokaiPower?"Yokai":cost.Kind==CostKind::Life?"Life":cost.Kind==CostKind::HiddenExileTop?"hidden deck exile":cost.Kind==CostKind::Tribute?"tribute":"discard")+". ";
        }
        if(effect.PerTurnLimit>0) text+=std::to_string(effect.PerTurnLimit)+" use(s) per turn across copies. ";
        if(effect.MinGraveyardCount>0) text+="Requires "+std::to_string(effect.MinGraveyardCount)+" in graveyard. ";
        if(!effect.RequireGraveyardEntryThisTurn.empty()) text+="Requires "+effect.RequireGraveyardEntryThisTurn+" sent to graveyard this turn. ";
        for(auto const& action:effect.Actions)
        {
            text+=operations[static_cast<int>(action.Operation)]; text+=" (";
            text+=targets[static_cast<int>(action.Targets.Category)]; text+=")";
            if(!action.Targets.RequiredArchetype.empty()) text+=" / "+action.Targets.RequiredArchetype;
            if(action.Targets.MaxAttack>=0) text+=" / ATK <= "+std::to_string(action.Targets.MaxAttack);
            if(action.Targets.Type==CardType::Monster) text+=" / monster";
            if(action.Targets.Type==CardType::Spell) text+=" / spell";
            if(action.Operation==EffectOperation::ModifyStats)
                text+=" ATK "+std::to_string(action.AttackDelta)+", max HP "+std::to_string(action.MaxHpDelta);
            else if(action.Operation==EffectOperation::Transform) text+=" definition #"+std::to_string(action.TransformDefinitionId);
            else text+=" x"+std::to_string(action.Amount);
            if(action.PreventDirectAttackThisTurn) text+=" / no direct attack this turn";
            if(action.TemporaryStats) text+=" / until turn end";
            text+=". ";
        }
    }
    return text.empty()?"No card effects.":text;
}
std::string Manifestations(GameState const& state, CardDefinition const& definition)
{
    std::string text;
    for(auto id:definition.AllowedManifestations) if(auto form=FindDefinition(state,id))
    {
        if(!text.empty()) text+=", ";
        text+=form->Name+" ("+std::to_string(form->Attack)+"/"+std::to_string(form->MaxHp)+")";
    }
    return text.empty()?"No manifestation forms.":"Forms: "+text+". Effect-driven only.";
}
// Public action metadata is viewer-specific. Deck identities never become target candidates.
Json Activations(GameState const& state, CardInstance const& card, int viewer, Zone zone)
{
    Json actions=Json::array();
    auto definition=FindDefinition(state,card.CurrentDefinitionId);
    if(!definition || (zone!=Zone::Field && zone!=Zone::Hand) || card.OwnerId!=viewer) return actions;
    for(auto const& effect:definition->Effects)
    {
        if(effect.Type!=EffectType::Activated || effect.ActivationZone!=zone) continue;
        bool interactive=zone==Zone::Hand || !effect.Costs.empty() || !effect.RequireGraveyardEntryThisTurn.empty();
        for(auto const& action:effect.Actions) interactive |= action.Targets.Category==TargetCategory::FriendlyDeck;
        if(interactive)
        {
            auto candidate=state;auto result=ProcessCommand(candidate,PlayerActionCommand{viewer,card.Id,effect.Id,{}});
            bool enabled=result.Code==CommandError::Success || result.Code==CommandError::ChoiceRequired;
            bool manifest=false;int yokai=0;for(auto const& action:effect.Actions) manifest |= action.Operation==EffectOperation::Transform;
            for(auto const& cost:effect.Costs) if(cost.Kind==CostKind::YokaiPower) yokai+=cost.Amount;
            actions.push_back({{"EffectId",effect.Id},{"CanActivate",enabled},{"IsManifest",manifest},{"Interactive",true},{"YokaiCost",yokai},{"Selections",Json::array()}});
            continue;
        }
        Json selections=Json::array();bool manifest=false,supported=true;
        bool allowed=state.Status==GameStatus::Running && state.ActivePlayerId==viewer
            && state.TurnStarted && state.Phase==TurnPhase::Main && !card.IsDestroyed();
        for(auto const& action:effect.Actions)
        {
            manifest|=action.Operation==EffectOperation::Transform;
            auto single=action.Targets;single.MinCount=0;single.MaxCount=1;
            if(action.Targets.Category==TargetCategory::FriendlyDeck) supported=false;
            Json candidates=Json::array();
            auto add=[&](EffectTarget const& target) {
                if(ValidateTargets(state,viewer,card.Id,single,{target})==CommandError::Success)
                    candidates.push_back({{"PlayerId",target.Player},{"CardId",target.Card},{"Zone",static_cast<int>(target.Location)}});
            };
            for(auto const& player:state.Players)
            {
                add({player.Id,0,Zone::Field});
                for(auto location:{Zone::Hand,Zone::Field,Zone::Graveyard})
                {
                    if(location==Zone::Hand && player.Id!=viewer) continue;
                    for(auto const& target:*GetZone(player,location)) add({player.Id,target.Id,location});
                }
            }
            allowed &= candidates.size()>=action.Targets.MinCount;
            selections.push_back({{"MinCount",action.Targets.MinCount},{"MaxCount",action.Targets.MaxCount},{"Candidates",candidates}});
        }
        if(supported) actions.push_back({{"EffectId",effect.Id},{"IsManifest",manifest},{"CanActivate",allowed},{"Selections",selections}});
    }
    return actions;
}
Json Cards(GameState const& state, std::vector<CardInstance> const& cards, int viewer, Zone zone)
{
    Json result=Json::array();
    auto owner=FindPlayer(state,viewer);
    for(auto const& card:cards)
    {
        auto definition=FindDefinition(state,card.CurrentDefinitionId);
        bool summon=definition && definition->Type==CardType::Monster && zone==Zone::Hand && card.OwnerId==viewer && state.ActivePlayerId==viewer
            && state.TurnStarted && state.Phase==TurnPhase::Main && state.Status==GameStatus::Running
            && owner && owner->NormalSummonsThisTurn<1 && owner->Field.size()<state.Config.FieldLimit;
        bool attack=zone==Zone::Field && card.OwnerId==viewer && state.TurnStarted
            && ValidateAttackSource(state,viewer,card.Id)==BattleResult::Success
            && ValidateAttackReadiness(state,card)==BattleResult::Success;
        result.push_back({{"Id",card.Id},{"DefinitionId",card.CurrentDefinitionId},{"Name",definition?definition->Name:""},
            {"CardType",definition?static_cast<int>(definition->Type):0},{"Attack",card.Attack},{"Hp",card.Hp},{"MaxHp",card.MaxHp},{"HasAttacked",card.HasAttackedThisTurn},
            {"RulesText",definition?RulesText(*definition):""},{"ManifestationText",definition?Manifestations(state,*definition):""},
            {"SummoningSick",zone==Zone::Field && IsSummoningSick(state,card)},
            {"CanSummon",summon},{"CanAttack",attack},{"EffectCount",definition?definition->Effects.size():0},
            {"Activations",Activations(state,card,viewer,zone)}});
    }
    return result;
}
}
std::array<int,2> IdentityAllocator::Players()
{
    if(NextPlayer>std::numeric_limits<int>::max()-2) throw std::runtime_error("Player IDs exhausted");
    std::array<int,2> result{NextPlayer,NextPlayer+1}; NextPlayer+=2;return result;
}
int IdentityAllocator::Cards(std::size_t count)
{
    if(count>static_cast<std::size_t>(std::numeric_limits<int>::max()-NextCard)) throw std::runtime_error("Card IDs exhausted");
    int first=NextCard;NextCard+=static_cast<int>(count);return first;
}
bool ReplayRecord::Verify() const
{
    auto replay=ReplayCommands(InitialState,Commands);
    return replay.FinalState==FinalState && replay.Results==Results;
}
MatchSession::MatchSession(std::string id, std::shared_ptr<CardCatalog const> catalog,
    std::shared_ptr<IdentityAllocator> identities, std::function<void(ReplayRecord)> archive)
    : id(std::move(id)),catalog(std::move(catalog)),identities(std::move(identities)),archive(std::move(archive))
{
    auto players=this->identities->Players();
    for(int i=0;i<2;++i) {seats[i].Player=players[i];seats[i].DeckId=this->catalog->Decks[i%this->catalog->Decks.size()].Id;}
    hostPlayer=players[0];
}
int MatchSession::SeatIndex(int player) const
{
    for(int i=0;i<2;++i) if(seats[i].Player==player) return i;
    return -1;
}
Json MatchSession::Message(std::string const& type, std::string const& request) const
{
    return Envelope(type,request,id,sequence,gameNumber);
}
Json MatchSession::Snapshot(int viewer) const
{
    Json players=Json::array();
    if(gameNumber>0) for(auto const& player:state.Players)
        players.push_back({{"PlayerId",player.Id},{"Life",player.Life},{"HandCount",player.Hand.size()},
            {"DeckCount",player.Deck.size()},{"YokaiPower",player.YokaiPower},{"HiddenExileCount",player.HiddenExile.size()},{"Exile",Cards(state,player.Exile,viewer,Zone::Exile)},{"Hand",player.Id==viewer?Cards(state,player.Hand,viewer,Zone::Hand):Json::array()},
            {"Field",Cards(state,player.Field,viewer,Zone::Field)},{"Graveyard",Cards(state,player.Graveyard,viewer,Zone::Graveyard)}});
    return {{"ResolvingPlayerId",pendingAction?pendingAction->Choice.Player:0},{"MaxYokaiPower",state.Config.MaxYokaiPower},{"SummoningSickness",state.Config.SummoningSickness},{"FieldLimit",state.Config.FieldLimit},{"Started",gameNumber>0},{"MatchStatus",StatusName(status)},{"Players",players},
        {"ActivePlayerId",state.ActivePlayerId},{"TurnNumber",state.TurnNumber},{"TurnStarted",state.TurnStarted},
        {"Phase",static_cast<int>(state.Phase)},{"Finished",status==MatchStatus::Finished},{"WinnerPlayerId",state.WinnerPlayerId}};
}
void MatchSession::PrepareChoice(PlayerActionCommand const& command,EffectChoiceRequest const& choice)
{
    if(!pendingAction) pendingAction.emplace();
    auto& pending=*pendingAction;pending.Command=command;pending.Choice=choice;pending.ChoiceId=Token();pending.Options.clear();
    Json candidates=Json::array();auto options=choice.Candidates;
    std::stable_sort(options.begin(),options.end(),[](auto const& a,auto const& b){return a.Card.CurrentDefinitionId<b.Card.CurrentDefinitionId;});
    for(auto const& option:options)
    {
        auto token=Token();pending.Options.emplace(token,option.Target);
        bool hidden=option.Target.Location==Zone::Deck || option.Target.Location==Zone::HiddenExile;
        auto owner=FindPlayer(state,option.Target.Player);
        bool newHandCard=option.Target.Location==Zone::Hand && owner && std::none_of(owner->Hand.begin(),owner->Hand.end(),[&](auto const& card){return card.Id==option.Target.Card;});
        if(hidden || newHandCard) pending.CanCancel=false;
        auto definition=FindDefinition(state,option.Card.CurrentDefinitionId);
        Json card={{"Id",hidden?0:option.Card.Id},{"DefinitionId",option.Card.CurrentDefinitionId},{"Name",definition?definition->Name:"Player"},
            {"CardType",definition?static_cast<int>(definition->Type):0},{"Attack",option.Card.Attack},{"Hp",option.Card.Hp},{"MaxHp",option.Card.MaxHp},
            {"RulesText",definition?RulesText(*definition):""}};
        candidates.push_back({{"Token",token},{"PlayerId",option.Target.Player},{"CardId",hidden?0:option.Target.Card},{"Zone",static_cast<int>(option.Target.Location)},{"Card",card}});
    }
    pending.Payload={{"ChoiceId",pending.ChoiceId},{"CanCancel",pending.CanCancel},{"SourceCardId",choice.Source},{"IsCost",choice.IsCost},{"MinCount",choice.Rule.MinCount},{"MaxCount",choice.Rule.MaxCount},{"Candidates",candidates}};
}
void MatchSession::SendChoice(std::shared_ptr<ClientSession> const& client) const
{
    if(pendingAction && pendingAction->Choice.Player==client->Player)
    {auto message=Message("ActionChoiceRequired");message["Payload"]=pendingAction->Payload;client->Send(message);}
}
void MatchSession::SendSnapshot(std::shared_ptr<ClientSession> const& client,std::string const& request) const
{
    auto message=Message("StateSnapshot",request); message["Payload"]=Snapshot(client->Player);client->Send(message);SendChoice(client);
}
void MatchSession::Lobby() const
{
    auto message=Message("LobbyState");
    Json players=Json::array();
    for(auto const& seat:seats) players.push_back({{"PlayerId",seat.Player},{"Occupied",!seat.Token.empty() || !seat.Client.expired()},
        {"Connected",!seat.Client.expired()},{"Ready",seat.Ready},{"Rematch",seat.Rematch}});
    // Opponents see readiness, never the selected deck ID/composition.
    message["Payload"]={{"Status",StatusName(status)},{"HostPlayerId",hostPlayer},{"Players",players}};
    for(auto const& seat:seats) if(auto client=seat.Client.lock())
    {
        auto personal=message;personal["DeckId"]=seat.DeckId;client->Send(personal);
    }
}
void MatchSession::Broadcast(std::vector<GameEvent> const& events)
{
    for(auto const& seat:seats) if(auto client=seat.Client.lock())
    {
        auto message=Message("GameEvents");Json visible=Json::array();
        for(auto e:EventsForPlayer(events,seat.Player))
        {
            if(!VisibleCard(state,seat.Player,e.SourceCardId)) e.SourceCardId=0;
            if(!VisibleCard(state,seat.Player,e.TargetCardId)) e.TargetCardId=0;
            if(e.SourceCardId==0 && e.TargetCardId==0) e.CurrentDefinitionId=0;
            visible.push_back({{"Type",static_cast<int>(e.Type)},{"SourcePlayerId",e.SourcePlayerId},{"SourceCardId",e.SourceCardId},
                {"TargetPlayerId",e.TargetPlayerId},{"TargetCardId",e.TargetCardId},{"DefinitionId",e.CurrentDefinitionId},
                {"FromZone",static_cast<int>(e.FromZone)},{"ToZone",static_cast<int>(e.ToZone)},
                {"Damage",e.Damage},{"HpAfter",e.HpAfter},{"LifeAfter",e.LifeAfter},{"AttackAfter",e.AttackAfter},
                {"Phase",static_cast<int>(e.Phase)},{"TurnNumber",e.TurnNumber},{"YokaiPowerAfter",e.YokaiPowerAfter}});
        }
        message["Payload"]=visible;client->Send(message);SendSnapshot(client);
        if(status==MatchStatus::Finished)
        {
            auto ended=Message("MatchEnded");ended["WinnerPlayerId"]=state.WinnerPlayerId;
            ended["EndReason"]=static_cast<int>(state.EndReason);ended["SessionTokenRevoked"]=true;client->Send(ended);
        }
    }
}
void MatchSession::Archive()
{
    if(archived) return;
    journal.FinalState=state;journal.StoredAt=Clock::now();
    archive(journal);archived=true;
}
void MatchSession::StartGame()
{
    if(seats[0].Client.expired() || seats[1].Client.expired() || !seats[0].Ready || !seats[1].Ready)
        throw std::runtime_error("Players not ready");
    if(gameNumber>=1000000) throw std::runtime_error("Game limit");
    std::uint64_t seed=std::stoull(Token().substr(0,16),nullptr,16);
    auto count=catalog->FindDeck(seats[0].DeckId)->Cards.size()+catalog->FindDeck(seats[1].DeckId)->Cards.size();
    auto fresh=catalog->Initialize({seats[0].Player,seats[1].Player},{seats[0].DeckId,seats[1].DeckId},seed,identities->Cards(count));
    std::array<std::string,2> tokens{Token(),Token()};
    auto initial=fresh;
    GameCommand command=StartTurnCommand{fresh.ActivePlayerId};
    auto result=ProcessCommand(fresh,command);
    if(result.Code!=CommandError::Success) throw std::runtime_error("Start game failed");
    pendingAction.reset();state=std::move(fresh);sequence=0;++gameNumber;status=MatchStatus::Playing;lastActivity=Clock::now();
    journal={};journal.MatchId=id;journal.GameNumber=gameNumber;journal.Seed=seed;journal.InitialState=std::move(initial);archived=false;
    journal.Commands.push_back(command);journal.Results.push_back(result);++sequence;
    if(state.Status==GameStatus::Finished) {status=MatchStatus::Finished;Archive();}
    for(int i=0;i<2;++i)
    {
        seats[i].Token=status==MatchStatus::Finished?"":tokens[i];seats[i].Ready=status==MatchStatus::Playing;seats[i].Requests.clear();seats[i].Rematch=false;
        if(auto client=seats[i].Client.lock())
        {
            auto updated=Message("SessionUpdated");updated["SessionToken"]=seats[i].Token;updated["PlayerId"]=client->Player;client->Send(updated);
            client->Send(Message("MatchStarted"));
        }
    }
    std::cout << "Match " << id << " started game=" << gameNumber << std::endl;
    Lobby();Broadcast(result.Events);
}
void MatchSession::Disconnect(std::shared_ptr<ClientSession> const& client)
{
    auto index=SeatIndex(client->Player);
    if(index>=0 && seats[index].Client.lock()==client)
    {
        seats[index].Client.reset();
        if(status==MatchStatus::WaitingForPlayers || status==MatchStatus::Ready) {seats[index].Ready=false;status=MatchStatus::WaitingForPlayers;}
        seats[index].Rematch=false;
    }
    client->Player=0;client->MatchId.clear();Lobby();
}
void MatchSession::Close(std::string const& reason)
{
    if(status==MatchStatus::Closed) return;
    pendingAction.reset();Archive();status=MatchStatus::Closed;
    for(auto& seat:seats)
    {
        if(auto client=seat.Client.lock())
        {
            auto closed=Message("MatchClosed");closed["Reason"]=reason;client->Send(closed);
            client->Player=0;client->MatchId.clear();
        }
        seat.Token.clear();seat.Client.reset();seat.Requests.clear();
    }
    std::cout << "Match " << id << " closed reason=" << reason << std::endl;
}
void MatchSession::Handle(std::shared_ptr<ClientSession> const& client,Json const& input)
{
    auto type=String(input,"Type");auto request=String(input,"RequestId",64);
    auto reject=[&](std::string const& reason){auto response=Message(type=="SubmitCommand"?"CommandRejected":"ServerError",request);response["Reason"]=reason;client->Send(response);};
    if(status==MatchStatus::Closed) {reject("MatchClosed");return;}
    if(type=="JoinMatch" || type=="CreateMatch")
    {
        if(type=="JoinMatch") Fields(input,{"Type","ProtocolVersion","RequestId","MatchId","SessionToken","DeckId"});
        else Fields(input,{"Type","ProtocolVersion","RequestId","DeckId"});
        if(!client->MatchId.empty()) {reject("AlreadyJoined");return;}
        std::string deck=input.contains("DeckId")?String(input,"DeckId",64):"";
        if(!deck.empty() && !catalog->FindDeck(deck)) {reject("InvalidDeck");return;}
        int index=-1;
        if(input.contains("SessionToken"))
        {
            auto token=String(input,"SessionToken",64);
            for(int i=0;i<2;++i) if(!seats[i].Token.empty() && seats[i].Token==token) index=i;
            if(index<0) {reject("InvalidSession");return;}
            if(!seats[index].Client.expired()) {reject("SessionConnected");return;}
        }
        else
        {
            if(status!=MatchStatus::WaitingForPlayers) {reject("MatchNotWaiting");return;}
            for(int i=0;i<2;++i) if(seats[i].Token.empty() && seats[i].Client.expired()) {index=i;break;}
            if(index<0) {reject("MatchFull");return;}
            seats[index].Token=Token();
        }
        client->Player=seats[index].Player;client->MatchId=id;seats[index].Client=client;
        if(!deck.empty() && status==MatchStatus::WaitingForPlayers) {seats[index].DeckId=deck;seats[index].Ready=false;}
        auto joined=Message(type=="CreateMatch"?"MatchCreated":"MatchJoined",request);
        joined["PlayerId"]=client->Player;joined["SessionToken"]=seats[index].Token;joined["DeckId"]=seats[index].DeckId;client->Send(joined);
        lastActivity=Clock::now();Lobby();SendSnapshot(client,request);return;
    }
    int index=SeatIndex(client->Player);
    if(index<0 || seats[index].Client.lock()!=client || client->MatchId!=id) {reject("NotJoined");return;}
    if(input.contains("PlayerId") && Number(input,"PlayerId",2147483647)!=static_cast<unsigned>(client->Player)) {reject("PlayerMismatch");return;}
    if(type=="StateResyncRequest")
    {
        Fields(input,{"Type","ProtocolVersion","RequestId","MatchId","PlayerId"});Lobby();SendSnapshot(client,request);return;
    }
    if(type=="SubmitCommand") {Submit(client,input);return;}
    if(type=="SetReady") Fields(input,{"Type","ProtocolVersion","RequestId","MatchId","GameNumber","Ready","DeckId"});
    else if(type=="Rematch") Fields(input,{"Type","ProtocolVersion","RequestId","MatchId","GameNumber","DeckId"});
    else Fields(input,{"Type","ProtocolVersion","RequestId","MatchId","GameNumber"});
    if(Number(input,"GameNumber",1000000)!=static_cast<unsigned>(gameNumber)) {reject("StaleGame");return;}
    auto accepted=[&]{client->Send(Message("LobbyAccepted",request));};
    auto& seat=seats[index];
    if(type=="SetReady")
    {
        if(status!=MatchStatus::WaitingForPlayers && status!=MatchStatus::Ready) {reject("MatchNotWaiting");return;}
        auto ready=Boolean(input,"Ready");auto deck=input.contains("DeckId")?String(input,"DeckId",64):seat.DeckId;
        if(!catalog->FindDeck(deck)) {reject("InvalidDeck");return;}
        seat.DeckId=deck;seat.Ready=ready;lastActivity=Clock::now();accepted();
        status=MatchStatus::WaitingForPlayers;
        if(seats[0].Ready && seats[1].Ready && !seats[0].Client.expired() && !seats[1].Client.expired()) status=MatchStatus::Ready;
        Lobby();if(status==MatchStatus::Ready) StartGame();return;
    }
    if(type=="StartMatch")
    {
        if(client->Player!=hostPlayer) {reject("HostOnly");return;}
        if(status!=MatchStatus::Ready) {reject("NotReady");return;}
        accepted();StartGame();return;
    }
    if(type=="Rematch")
    {
        if(status!=MatchStatus::Finished) {reject("MatchNotFinished");return;}
        if(seats[0].Client.expired() || seats[1].Client.expired()) {reject("PlayersDisconnected");return;}
        auto deck=input.contains("DeckId")?String(input,"DeckId",64):seat.DeckId;
        if(!catalog->FindDeck(deck)) {reject("InvalidDeck");return;}
        seat.DeckId=deck;seat.Rematch=true;lastActivity=Clock::now();accepted();Lobby();
        if(seats[0].Rematch && seats[1].Rematch) {for(auto& entry:seats) entry.Ready=true;StartGame();}
        return;
    }
    if(type=="CloseMatch")
    {
        if(client->Player!=hostPlayer) {reject("HostOnly");return;}
        accepted();Close("HostClosed");return;
    }
    if(type=="LeaveMatch")
    {
        auto left=Message("MatchLeft",request);client->Send(left);
        if(status==MatchStatus::Playing || status==MatchStatus::Finished) {Close("PlayerLeft");return;}
        client->Player=0;client->MatchId.clear();seat.Client.reset();seat.Token.clear();seat.Ready=false;seat.Rematch=false;seat.Requests.clear();
        auto const& other=seats[1-index];
        if(other.Token.empty()) Close("EmptyLobby");
        else {if(hostPlayer==seat.Player) hostPlayer=other.Player;lastActivity=Clock::now();status=MatchStatus::WaitingForPlayers;Lobby();}
        return;
    }
    reject("UnknownMessage");
}
void MatchSession::Submit(std::shared_ptr<ClientSession> const& client,Json const& input)
{
    Fields(input,{"Type","ProtocolVersion","RequestId","MatchId","PlayerId","GameNumber","SequenceNumber","Command"});
    auto request=String(input,"RequestId",64);
    auto reject=[&](std::string const& reason){auto response=Message("CommandRejected",request);response["Reason"]=reason;client->Send(response);};
    if(Number(input,"GameNumber",1000000)!=static_cast<unsigned>(gameNumber)) {reject("StaleGame");return;}
    auto& requests=seats[SeatIndex(client->Player)].Requests;auto fingerprint=input.dump();
    if(auto previous=requests.find(request);previous!=requests.end())
    {
        if(previous->second.first!=fingerprint) {reject("RequestIdConflict");return;}
        client->Send(previous->second.second);SendSnapshot(client);return;
    }
    if(requests.size()>=4096) {reject("RequestLimit");return;}
    auto cacheReject=[&](std::string const& reason){auto response=Message("CommandRejected",request);response["Reason"]=reason;requests.emplace(request,std::make_pair(fingerprint,response));client->Send(response);};
    if(status!=MatchStatus::Playing) {cacheReject("MatchNotPlaying");return;}
    if(Number(input,"SequenceNumber",9007199254740991ULL)!=sequence) {cacheReject("StaleSequence");return;}
    auto const& command=input.at("Command");auto name=String(command,"Type");
    int player=client->Player;int opponent=seats[1-SeatIndex(player)].Player;
    GameCommand parsed;
    if(pendingAction)
    {
        if(name!="ResolveChoice" && name!="CancelAction") {cacheReject("ActionPending");return;}
        if(name=="CancelAction") Fields(command,{"Type","ChoiceId"});else Fields(command,{"Type","ChoiceId","Choices"});
        if(client->Player!=pendingAction->Choice.Player || String(command,"ChoiceId",64)!=pendingAction->ChoiceId) {cacheReject("InvalidChoiceSession");SendSnapshot(client);return;}
        if(name=="CancelAction")
        {
            if(!pendingAction->CanCancel) {cacheReject("ChoiceMustResolve");SendSnapshot(client);return;}
            for(auto const& [owner,key]:pendingAction->Requests)
            {
                auto& seat=seats[SeatIndex(owner)];auto response=Message("CommandRejected",key);response["Reason"]="ActionCancelled";
                if(auto cached=seat.Requests.find(key);cached!=seat.Requests.end()) cached->second.second=response;
                if(auto peer=seat.Client.lock()) peer->Send(response);
            }
            pendingAction.reset();lastActivity=Clock::now();
            auto response=Message("CommandAccepted",request);requests.emplace(request,std::make_pair(fingerprint,response));client->Send(response);
            for(auto const& seat:seats) if(auto peer=seat.Client.lock()) SendSnapshot(peer);
            return;
        }
        auto const& choices=command.at("Choices");
        if(!choices.is_array() || choices.size()>128) {cacheReject("InvalidChoice");SendSnapshot(client);return;}
        std::vector<EffectTarget> targets;
        for(auto const& token:choices)
        {
            auto key=String(Json{{"Token",token}},"Token",64);auto found=pendingAction->Options.find(key);
            if(found==pendingAction->Options.end()) {cacheReject("InvalidChoice");SendSnapshot(client);return;}
            targets.push_back(found->second);
        }
        auto action=pendingAction->Command;action.Choices.push_back(std::move(targets));parsed=std::move(action);
    }
    else if(name=="PlayerAction")
    {Fields(command,{"Type","CardId","EffectId"});parsed=PlayerActionCommand{player,static_cast<int>(Number(command,"CardId",2147483647)),static_cast<int>(Number(command,"EffectId",2147483647)),{}};}
    else if(name=="PlayCard") {Fields(command,{"Type","CardId"});parsed=PlayerActionCommand{player,static_cast<int>(Number(command,"CardId",2147483647)),0,{}};}
    else if(name=="EndTurn") {Fields(command,{"Type"});parsed=EndTurnCommand{player};}
    else if(name=="AdvancePhase") {Fields(command,{"Type"});parsed=AdvancePhaseCommand{player};}
    else if(name=="Attack" || name=="DirectAttack")
    {
        Fields(command,name=="Attack"?std::initializer_list<char const*>{"Type","CardId","TargetCardId"}:std::initializer_list<char const*>{"Type","CardId"});
        auto card=static_cast<int>(Number(command,"CardId",2147483647));
        if(name=="Attack") parsed=AttackCommand{player,card,opponent,static_cast<int>(Number(command,"TargetCardId",2147483647))};
        else parsed=DirectAttackCommand{player,card,opponent};
    }
    else if(name=="ActivateEffect")
    {
        Fields(command,{"Type","CardId","EffectId","Targets"});
        ActivateEffectCommand activation{player,static_cast<int>(Number(command,"CardId",2147483647)),
            static_cast<int>(Number(command,"EffectId",2147483647)),{}};
        auto own=FindPlayer(state,player);auto source=own?FindCard(*own,activation.SourceCard):nullptr;
        auto definition=source?FindDefinition(state,source->CurrentDefinitionId):nullptr;
        if(!definition) {cacheReject("GameRule:CardNotFound");return;}
        for(auto const& effect:definition->Effects) if(effect.Id==activation.EffectId && !effect.Costs.empty()) {cacheReject("UsePlayerAction");return;}
        auto const& groups=command.at("Targets");
        if(!groups.is_array() || groups.size()>16) {cacheReject("InvalidTargets");return;}
        for(auto const& group:groups)
        {
            if(!group.is_array() || group.size()>128) {cacheReject("InvalidTargets");return;}
            std::vector<EffectTarget> targets;
            for(auto const& target:group)
            {
                Fields(target,{"PlayerId","CardId","Zone"});
                int owner=static_cast<int>(Number(target,"PlayerId",2147483647));
                int card=static_cast<int>(Number(target,"CardId",2147483647));
                auto zone=static_cast<Zone>(Number(target,"Zone",3));
                if(zone==Zone::Deck || (card!=0 && !VisibleCard(state,player,card))) {cacheReject("InvalidTarget");return;}
                targets.push_back({owner,card,zone});
            }
            activation.Targets.push_back(std::move(targets));
        }
        parsed=std::move(activation);
    }
    else {cacheReject("CommandNotAllowed");return;}
    GameState candidate=state;
    std::vector<GameCommand> commands;std::vector<CommandResult> results;std::vector<GameEvent> events;
    auto apply=[&](GameCommand const& cmd){
        auto result=ProcessCommand(candidate,cmd);commands.push_back(cmd);results.push_back(result);
        events.insert(events.end(),result.Events.begin(),result.Events.end());return result.Code==CommandError::Success;
    };
    bool success=true;
    if(name=="EndTurn")
    {
        while(success && candidate.Status==GameStatus::Running && candidate.Phase!=TurnPhase::End) success=apply(AdvancePhaseCommand{player});
        if(success && candidate.Status==GameStatus::Running) success=apply(parsed);
        if(success && candidate.Status==GameStatus::Running) success=apply(StartTurnCommand{candidate.ActivePlayerId});
    }
    else success=apply(parsed);
    if(!success && results.back().Code==CommandError::ChoiceRequired && std::holds_alternative<PlayerActionCommand>(parsed))
    {
        PrepareChoice(std::get<PlayerActionCommand>(parsed),results.back().Choice);
        pendingAction->Requests.push_back({player,request});
        auto response=Message("CommandPending",request);requests.emplace(request,std::make_pair(fingerprint,response));client->Send(response);
        lastActivity=Clock::now();for(auto const& seat:seats) if(auto peer=seat.Client.lock()) SendSnapshot(peer);
        return;
    }
    if(!success)
    {
        if(commands.size()==1) {journal.Commands.push_back(commands[0]);journal.Results.push_back(results[0]);}
        auto const& result=results.back();cacheReject("GameRule:"+RuleReason(result.Code)+":Battle:"+std::to_string(static_cast<int>(result.Result)));if(pendingAction) SendSnapshot(client);return;
    }
    auto pendingRequests=pendingAction?pendingAction->Requests:std::vector<std::pair<int,std::string>>{};
    pendingAction.reset();state=std::move(candidate);
    journal.Commands.insert(journal.Commands.end(),commands.begin(),commands.end());journal.Results.insert(journal.Results.end(),results.begin(),results.end());
    ++sequence;lastActivity=Clock::now();
    if(state.Status==GameStatus::Finished)
    {
        status=MatchStatus::Finished;Archive();
        for(auto& seat:seats) {seat.Token.clear();seat.Ready=false;seat.Rematch=false;}
        std::cout << "Match " << id << " finished game=" << gameNumber << " winner=" << state.WinnerPlayerId << std::endl;
    }
    auto response=Message("CommandAccepted",request);requests.emplace(request,std::make_pair(fingerprint,response));client->Send(response);
    for(auto const& [owner,key]:pendingRequests)
    {
        auto& seat=seats[SeatIndex(owner)];auto accepted=Message("CommandAccepted",key);
        if(auto cached=seat.Requests.find(key);cached!=seat.Requests.end()) cached->second.second=accepted;
        if(auto peer=seat.Client.lock()) peer->Send(accepted);
    }
    Broadcast(events);if(status==MatchStatus::Finished) Lobby();
}
}
