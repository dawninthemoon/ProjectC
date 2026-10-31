#include "CardCatalog.hpp"
#include "ProjectC/GameReplay.hpp"
#include <algorithm>
#include <filesystem>
#include <iostream>
#include <stdexcept>

using namespace ProjectC::Network;
using namespace ProjectC::GameCore;

void RunHyakkiComboTests()
{
    auto catalog=CardCatalog::Load((std::filesystem::path(DefaultDataDirectory())/"HyakkiYagyo").string());
    auto check=[](bool value,char const* message){if(!value) throw std::runtime_error(message);};
    check(catalog.Definitions.size()==17 && catalog.Decks.front().Cards.size()==30,"Content catalog");
    auto invalid=catalog.Decks.front().Cards;invalid[0]=1007;check(!catalog.ValidateDeck(invalid),"Manifest form excluded from main deck");
    auto config=catalog.Config;config.DrawPerTurn=0;
    auto deck=catalog.Decks.front().Cards;
    std::vector<int> ordered{1001,1002,1003,1101,1102,1103};
    for(auto id:ordered) deck.erase(std::find(deck.begin(),deck.end(),id));
    ordered.insert(ordered.end(),deck.begin(),deck.end());
    GameState state;
    check(InitializeGame(state,config,catalog.Definitions,{PlayerSetup{1,ordered},PlayerSetup{2,ordered}},1)==InitializationResult::Success,"Combo initialization");
    auto initial=state;std::vector<GameCommand> commands;std::vector<CommandResult> results;
    auto apply=[&](GameCommand command){auto result=ProcessCommand(state,command);check(result.Code==CommandError::Success,"Combo command failed");commands.push_back(command);results.push_back(result);};
    auto card=[&](Zone zone,int definition){for(auto const& c:*GetZone(state.Players[0],zone)) if(c.CurrentDefinitionId==definition) return c.Id;throw std::runtime_error("Combo card not found");};
    auto action=[&](int source,int effect,std::vector<int> choices)
    {
        PlayerActionCommand command{1,source,effect,{}};auto before=state;
        for(int definition:choices)
        {
            auto result=ProcessCommand(state,command);
            check(result.Code==CommandError::ChoiceRequired && state==before,"Combo choice must be atomic");
            auto candidate=std::find_if(result.Choice.Candidates.begin(),result.Choice.Candidates.end(),[&](auto const& c){return c.Card.CurrentDefinitionId==definition;});
            check(candidate!=result.Choice.Candidates.end(),"Combo requested candidate missing");
            command.Choices.push_back({candidate->Target});
        }
        apply(command);
    };
    auto round=[&]()
    {
        for(int player:{1,2})
        {
            while(state.Phase!=TurnPhase::End) apply(AdvancePhaseCommand{player});
            apply(EndTurnCommand{player});apply(StartTurnCommand{player==1?2:1});
        }
    };
    apply(StartTurnCommand{1});
    action(card(Zone::Hand,1001),0,{1104}); // Starter searches an archetype spell.
    action(card(Zone::Hand,1102),1,{1002}); // Draw, discard collector, gain one power.
    check(state.Players[0].YokaiPower==3,"Discard reward");
    action(card(Zone::Hand,1003),1,{}); // Extension from the same-turn graveyard entry.
    action(card(Zone::Hand,1101),1,{1002,1004}); // Revive collector; send monk from deck.
    check(state.Players[0].Field.size()==3 && state.Players[0].YokaiPower==2,"Starter chain field");
    round();
    action(card(Zone::Hand,1104),1,{1006});
    action(card(Zone::Hand,1006),0,{});
    action(card(Zone::Hand,1103),1,{1001,1004,1101}); // Tribute, revive monk, salvage spell.
    check(state.Players[0].Field.size()==4 && state.Players[0].YokaiPower==1,"Tribute revival chain");
    check(card(Zone::Hand,1101)>0,"Monk salvaged spell");
    auto monk=FindCard(state.Players[0],card(Zone::Field,1004));
    check(monk->DirectAttackRestrictedTurn==state.TurnNumber,"Revival direct-attack restriction");
    round();
    auto fox=card(Zone::Field,1006);action(fox,1,{});
    check(FindCard(state.Players[0],fox)->CurrentDefinitionId==1007 && state.Players[0].YokaiPower==0,"Two-power manifestation");
    auto replay=ReplayCommands(initial,commands);
    check(replay.FinalState==state && replay.Results==results,"Whole content combo replay");
    std::cout<<"Hyakki JSON combo: search, discard, special summon, revive, tribute, salvage, manifestation and replay passed\n";
}
