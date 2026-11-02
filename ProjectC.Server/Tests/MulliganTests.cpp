#include "CardCatalog.hpp"
#include "ProjectC/GameReplay.hpp"
#include <algorithm>
#include <filesystem>
#include <iostream>
#include <source_location>
#include <stdexcept>
using namespace ProjectC::GameCore;
using namespace ProjectC::Network;
namespace
{
void Require(bool value,std::source_location at=std::source_location::current())
{if(!value) throw std::runtime_error("Mulligan assertion at line "+std::to_string(at.line()));}
}
void RunMulliganCoreTests()
{
    auto catalog=CardCatalog::Load((std::filesystem::path(DefaultDataDirectory())/"HyakkiYagyo").string());
    catalog.Config.EnableMulligan=true;
    auto initial=catalog.Initialize({1,2},{"HyakkiYagyo","HyakkiYagyo"},12345,1);
    Require(ValidateGameState(initial) && !initial.TurnStarted && initial.Players[0].Hand.size()==5);
    for(int count:{0,1,5})
    {
        auto state=initial;std::vector<int> ids;
        for(int i=0;i<count;++i) ids.push_back(state.Players[0].Hand[i].Id);
        GameCommand first=SubmitMulliganCommand{1,ids},second=SubmitMulliganCommand{2,{}};
        auto result=ProcessCommand(state,first);Require(result.Code==CommandError::Success);
        Require(state.Players[0].Hand.size()==5 && state.Players[0].Deck.size()==25 && !state.TurnStarted);
        for(auto id:ids) Require(std::none_of(state.Players[0].Hand.begin(),state.Players[0].Hand.end(),[&](auto const& c){return c.Id==id;}));
        if(count==0) Require(state.Players[0].Hand==initial.Players[0].Hand && state.Players[0].Deck==initial.Players[0].Deck);
        auto before=state;Require(ProcessCommand(state,first).Code==CommandError::MulliganAlreadyComplete && state==before);
        auto result2=ProcessCommand(state,second);Require(result2.Code==CommandError::Success);
        Require(state.TurnStarted && state.Preparation==PreparationPhase::Playing && state.Players[0].Hand.size()==6 && state.Players[0].TurnsStarted==1);
        Require(state.Players[0].YokaiPower==2 && state.Players[1].TurnsStarted==0);
        auto replay=ReplayCommands(initial,{first,second});Require(replay.FinalState==state && replay.Results==std::vector<CommandResult>{result,result2});
        auto reversed=initial;Require(ProcessCommand(reversed,second).Code==CommandError::Success);Require(ProcessCommand(reversed,first).Code==CommandError::Success);Require(reversed==state);
        std::reverse(ids.begin(),ids.end());auto unordered=initial;
        Require(ProcessCommand(unordered,SubmitMulliganCommand{1,ids}).Code==CommandError::Success);
        Require(ProcessCommand(unordered,second).Code==CommandError::Success && unordered==state);
    }
    auto own=initial.Players[0].Hand[0].Id,enemy=initial.Players[1].Hand[0].Id;
    for(auto ids:{std::vector<int>{own,own},std::vector<int>{enemy},std::vector<int>{-1},std::vector<int>{999999},std::vector<int>(6,own)})
    {auto state=initial;Require(ProcessCommand(state,SubmitMulliganCommand{1,ids}).Code==CommandError::InvalidMulliganSelection && state==initial);}
    std::vector<GameCommand> prohibited{StartTurnCommand{1},DrawCommand{1,1},PlayCardCommand{1,own},EndTurnCommand{1},AdvancePhaseCommand{1},ChangeLifeCommand{1,-1},AttackCommand{1,own,2,enemy},DirectAttackCommand{1,own,2},ActivateEffectCommand{1,own,1,{}},TransformCommand{1,own,1007},PlayerActionCommand{1,own,0,{}}};
    for(auto const& command:prohibited)
    {auto state=initial;Require(ProcessCommand(state,command).Code==CommandError::MulliganPending && state==initial);}
    auto disabled=catalog;disabled.Config.EnableMulligan=false;
    auto state=disabled.Initialize({1,2},{"HyakkiYagyo","HyakkiYagyo"},12345,1);
    Require(ProcessCommand(state,SubmitMulliganCommand{1,{}}).Code==CommandError::MulliganClosed);
    Require(ProcessCommand(state,StartTurnCommand{1}).Code==CommandError::Success && state.Players[0].Hand.size()==6);
    auto bad=catalog.Config;bad.InitialHandSize=4;Require(!ValidateConfig(bad));
    std::cout<<"Mulligan core: 0/1/5, no immediate redraw, validation, all command gates, order-independent RNG and replay passed\n";
}
