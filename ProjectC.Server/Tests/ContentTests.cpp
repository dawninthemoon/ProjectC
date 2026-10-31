#include "ProjectC/GameInitializer.hpp"
#include "ProjectC/GameReplay.hpp"
#include "ProjectC/CardEffects.hpp"
#include <iostream>
#include <stdexcept>
using namespace ProjectC::GameCore;
namespace
{
    EffectAction Action(EffectOperation operation,TargetCategory category,Zone zone=Zone::Field,int amount=1)
    { return {.Operation=operation,.Targets={.Category=category,.Zones={zone}},.Amount=amount}; }
    CardDefinition Spell(int id,std::vector<EffectAction> actions,std::vector<CostDefinition> costs={})
    { return {.Id=id,.Name="Spell fixture",.Type=CardType::Spell,.Effects={{.Id=1,.Actions=actions,.ActivationZone=Zone::Hand,.Costs=costs}}}; }
    GameState Fixture()
    {
        auto damage=Spell(10,{Action(EffectOperation::DealDamage,TargetCategory::EnemyMonster)},{{CostKind::YokaiPower,1,{}}});
        damage.Effects[0].PerTurnLimit=1;
        auto discard=Action(EffectOperation::Discard,TargetCategory::FriendlyHand,Zone::Hand);discard.Targets.Type=CardType::Any;
        auto revive=Action(EffectOperation::SummonFromGraveyard,TargetCategory::FriendlyGraveyard,Zone::Graveyard);revive.Targets.MaxAttack=3;revive.PreventDirectAttackThisTurn=true;
        auto tribute=CostDefinition{CostKind::Tribute,0,{.Category=TargetCategory::FriendlyMonster}};
        std::vector<CardDefinition> definitions{
            {.Id=1,.Name="Yokai",.Attack=2,.MaxHp=4,.AllowedManifestations={2},.Archetypes={"HyakkiYagyo"}},
            {.Id=2,.Name="Form",.Attack=3,.MaxHp=6,.MainDeckEligible=false},damage,
            Spell(11,{Action(EffectOperation::Draw,TargetCategory::FriendlyPlayer)}),
            Spell(12,{Action(EffectOperation::Draw,TargetCategory::FriendlyPlayer),discard}),
            Spell(13,{revive},{{CostKind::YokaiPower,1,{}}}),Spell(14,{revive},{{CostKind::YokaiPower,2,{}},tribute}),
            Spell(15,{Action(EffectOperation::Draw,TargetCategory::FriendlyPlayer,Zone::Field,2)},{{CostKind::YokaiPower,1,{}},{CostKind::HiddenExileTop,5,{}}}),
            Spell(16,{Action(EffectOperation::Draw,TargetCategory::FriendlyPlayer)},{{CostKind::Life,20,{}}})};
        GameConfig config;config.InitialHandSize=9;config.HandLimit=30;config.InitialYokaiPower=2;config.YokaiPowerPerTurn=1;config.MaxYokaiPower=3;config.SummoningSickness=false;
        std::vector<int> deck{1,1,10,11,12,13,14,15,16,1,11,10,1,1,1,1,1,1};
        GameState state;
        if(InitializeGame(state,config,definitions,{PlayerSetup{1,deck},PlayerSetup{2,deck}},1)!=InitializationResult::Success) throw std::runtime_error("Content fixture initialization");
        if(ProcessCommand(state,StartTurnCommand{1}).Code!=CommandError::Success) throw std::runtime_error("Content fixture start");
        return state;
    }
    int Id(GameState const& state,int player,Zone zone,int definition)
    { for(auto const& card:*GetZone(*FindPlayer(state,player),zone)) if(card.CurrentDefinitionId==definition) return card.Id;return 0; }
    bool Replay(GameState const& before,GameCommand const& command,GameState const& after,CommandResult const& result)
    {auto replay=ReplayCommands(before,{command});return replay.FinalState==after && replay.Results==std::vector<CommandResult>{result};}
}
void RunContentTests(int& count,int& failed)
{
    auto check=[&](bool value,char const* name){++count;if(!value){++failed;std::cerr<<"FAILED: "<<name<<'\n';}};
    {
        auto state=Fixture();int source=Id(state,1,Zone::Hand,1);
        auto result=ProcessCommand(state,PlayCardCommand{1,source});check(result.Code==CommandError::Success,"ContentNormalSummon");
        check(!IsSummoningSick(state,*FindCard(state.Players[0],source)),"ExperimentalImmediateAttack");
        check(ProcessCommand(state,PlayCardCommand{1,Id(state,1,Zone::Hand,1)}).Code==CommandError::NormalSummonLimit,"NormalSummonStillOnce");
        (void)ProcessCommand(state,AdvancePhaseCommand{1});(void)ProcessCommand(state,DirectAttackCommand{1,source,2});
        (void)ProcessCommand(state,TransformCommand{1,source,2});check(FindCard(state.Players[0],source)->HasAttackedThisTurn,"ManifestKeepsAttackFlag");
    }
    {
        auto state=Fixture();int source=Id(state,1,Zone::Hand,12);auto before=state;
        PlayerActionCommand command{1,source,1,{}};auto result=ProcessCommand(state,command);
        check(result.Code==CommandError::ChoiceRequired && state==before && result.Events.empty(),"DrawDiscardChoiceAtomicPreview");
        int drawn=before.Players[0].Deck.front().Id;bool offered=false,sourceOffered=false;
        for(auto const& c:result.Choice.Candidates){offered|=c.Target.Card==drawn;sourceOffered|=c.Target.Card==source;}
        check(offered && !sourceOffered,"DrawnCardSelectableButResolvingSpellExcluded");
        command.Choices={{{1,drawn,Zone::Hand}}};result=ProcessCommand(state,command);
        check(result.Code==CommandError::Success && state.Players[0].Graveyard.size()==2,"DrawDiscardThenSpellGraveyard");
        check(Replay(before,command,state,result),"InteractiveCommandReplay");
    }
    {
        auto state=Fixture();int source=Id(state,1,Zone::Hand,10),enemy=Id(state,2,Zone::Hand,1);
        (void)MoveCard(state,2,enemy,Zone::Hand,Zone::Field);auto before=state;
        PlayerActionCommand command{1,source,1,{{{2,enemy,Zone::Field}}}};
        auto result=ProcessCommand(state,command);
        check(result.Code==CommandError::Success && state.Players[0].YokaiPower==1 && state.Players[1].Field[0].Hp==3,"SpellCostAndDamage");
        check(Replay(before,command,state,result),"SpellCostReplay");
        (void)MoveCard(state,1,Id(state,1,Zone::Deck,10),Zone::Deck,Zone::Hand);auto second=Id(state,1,Zone::Hand,10);
        check(ProcessCommand(state,PlayerActionCommand{1,second,1,{{{2,enemy,Zone::Field}}}}).Code==CommandError::UsageLimitReached,"DefinitionSharedHopt");
        state.Players[0].EffectUses.clear();state.Players[0].YokaiPower=0;before=state;
        result=ProcessCommand(state,PlayerActionCommand{1,second,1,{{{2,enemy,Zone::Field}}}});
        check(result.Code==CommandError::InsufficientResource && state==before,"InsufficientPowerRollsBack");
    }
    {
        auto state=Fixture();int source=Id(state,1,Zone::Hand,16);auto before=state;
        check(ProcessCommand(state,PlayerActionCommand{1,source,1,{}}).Code==CommandError::InvalidCost && state==before,"LifeCostCannotKillActor");
        auto free=Id(state,1,Zone::Hand,11);check(ProcessCommand(state,PlayerActionCommand{1,free,1,{}}).Code==CommandError::Success,"FreeSpellFirst");
        (void)MoveCard(state,1,Id(state,1,Zone::Deck,11),Zone::Deck,Zone::Hand);
        check(ProcessCommand(state,PlayerActionCommand{1,Id(state,1,Zone::Hand,11),1,{}}).Code==CommandError::Success,"FreeSpellsMultiplePerTurn");
    }
    {
        auto state=Fixture();int source=Id(state,1,Zone::Hand,14),tribute=Id(state,1,Zone::Hand,1);
        (void)MoveCard(state,1,tribute,Zone::Hand,Zone::Field);int dead=Id(state,1,Zone::Hand,1);(void)MoveCard(state,1,dead,Zone::Hand,Zone::Graveyard);
        state.Config.FieldLimit=1;state.Players[0].Graveyard[0].Hp=0;auto before=state;
        PlayerActionCommand command{1,source,1,{{{1,tribute,Zone::Field}},{{1,tribute,Zone::Graveyard}}}};
        check(ProcessCommand(state,command).Code==CommandError::InvalidTarget && state==before,"TributeCannotReviveAsSameSpellTarget");
        command.Choices[1][0].Card=dead;auto result=ProcessCommand(state,command);
        check(result.Code==CommandError::Success && state.Players[0].Field.size()==1 && state.Players[0].Field[0].Hp==4,"TributeFreesFieldAndReviveRestoresPrintedHp");
        check(!IsSummoningSick(state,state.Players[0].Field[0]),"SpecialSummonImmediateAttack");
        (void)ProcessCommand(state,AdvancePhaseCommand{1});check(ProcessCommand(state,DirectAttackCommand{1,dead,2}).Code==CommandError::DirectAttackRestricted,"RevivalDirectAttackRestriction");
    }
    {
        auto state=Fixture();int source=Id(state,1,Zone::Hand,15);state.Players[0].Deck.resize(6);auto before=state;
        PlayerActionCommand command{1,source,1,{}};auto result=ProcessCommand(state,command);
        check(result.Code==CommandError::Success && state.Status==GameStatus::Finished && state.WinnerPlayerId==2,"SpellDrawDeckOutIsSuccessfulGameLoss");
        check(state.Players[0].HiddenExile.size()==5 && Id(state,1,Zone::Graveyard,15)==source,"HiddenCostAndSpellCommittedOnDeckOut");
        bool hidden=true;for(auto const& event:EventsForPlayer(result.Events,2)) if(event.ToZone==Zone::HiddenExile) hidden &= event.TargetCardId==0 && event.CurrentDefinitionId==0;
        check(hidden && Replay(before,command,state,result),"HiddenExileRedactionAndReplay");
    }
    {
        auto state=Fixture();auto turn=[&](int id){while(state.Phase!=TurnPhase::End)(void)ProcessCommand(state,AdvancePhaseCommand{id});(void)ProcessCommand(state,EndTurnCommand{id});(void)ProcessCommand(state,StartTurnCommand{state.ActivePlayerId});};
        turn(1);turn(2);check(state.Players[0].YokaiPower==3,"YokaiCarryAndIncrease");turn(1);turn(2);check(state.Players[0].YokaiPower==3,"YokaiCap");
    }
}
