#include "YakouMatch.hpp"
#include "YakouSession.hpp"
#include "CardCatalog.hpp"
#include <algorithm>
#include <array>
#include <filesystem>
#include <iostream>
#include <map>
#include <set>
#include <stdexcept>
#include <vector>

using namespace ProjectC::Network;
namespace
{
void Check(bool ok, char const* message)
{
    if (!ok) throw std::runtime_error(message);
}
bool Has(std::vector<int> const& v,int id)
{
    return std::find(v.begin(),v.end(),id) != v.end();
}
template<class F> void RejectUnchanged(YakouMatch const& match,F f)
{
    auto before = match.ServerStateHash();
    bool rejected = false;
    try { f(); } catch (std::invalid_argument const&) { rejected = true; }
    Check(rejected && before == match.ServerStateHash(),"Illegal command mutated match or was accepted");
}
// Only test setup is synthetic: real catalog cards, legal 30-card decks, and
// resource starting values adjustable so an isolated effect can be tested.
std::vector<int> Deck(std::vector<int> front,bool spirit,bool blood)
{
    std::map<int,int> counts;
    for (int id : front) Check(++counts[id] <= 3,"Fixture exceeds copies");
    auto add = [&](int id) { if (counts[id] < 3 && front.size() < 30) { front.push_back(id); ++counts[id]; } };
    if (spirit) add(5116);
    if (blood) add(5213);
    for (int id : {5102,5103,5104,5201,5202,5203,5207,3014,3004,3009,5108,5204})
        while (counts[id] < 3 && front.size() < 30) add(id);
    Check(front.size() == 30,"Fixture deck must have 30 cards");
    return front;
}
struct Game
{
    YakouPrototype catalog;
    YakouMatch match;
    static YakouPrototype Config(YakouPrototype const& source,int spirit,int blood)
    {
        auto copy = source;
        copy.Config["resource.definitions"]["Spirit"]["InitialValue"] = spirit;
        copy.Config["resource.definitions"]["Blood"]["InitialValue"] = blood;
        return copy;
    }
    Game(YakouPrototype const& source,std::vector<int> a,std::vector<int> b,
         int spirit=8,int blood=6)
        : catalog(Config(source,spirit,blood)),
          match(catalog,1,{Deck(a,true,true),Deck(b,true,true)},1234567)
    {
        match.StartTurn(1);
    }
    int Id(int p,int definition) const
    {
        auto snapshot = match.ViewerSnapshot(p);
        for (auto const& card : snapshot.at("Players").at(p-1).at("Hand"))
            if (card.at("DefinitionId") == definition) return card.at("Id").get<int>();
        throw std::runtime_error("Missing fixture card "+std::to_string(definition));
    }
    int Resource(int p,char const* key) const { return match.Player(p).Resources.at(key).Current; }
    YakouUnit const& Unit(int p,int slot) const
    {
        Check(match.Player(p).Field[slot].has_value(),"Fixture unit absent");
        return *match.Player(p).Field[slot];
    }
    int Summon(int p,int definition,char const* method,int slot,std::vector<int> costs={})
    {
        int id=Id(p,definition);
        (void)match.SummonAndRunAutomaticTriggers(p,id,method,costs,slot);
        return id;
    }
    void Pick(int p,int id)
    {
        (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(p,id);
    }
    void ClearChoices()
    {
        for (int budget=0; !match.PendingEffects().empty() && budget<32; ++budget)
        {
            int p=match.PendingEffects().front().Player;
            auto choice=match.PendingEffectChoices(p);
            if (choice.MinCount == 0) Pick(p,0);
            else
            {
                Check(choice.MinCount==1 && !choice.Candidates.empty(),"Fixture needs explicit target");
                if (!choice.AvailableSlots.empty())
                    (void)match.ResolveSelectedEffectsAndRunAutomaticTriggers(p,{choice.Candidates.front()},choice.AvailableSlots.front(),64);
                else Pick(p,choice.Candidates.front());
            }
        }
        Check(match.PendingEffects().empty(),"Fixture effect queue did not drain");
    }
    int Cast(int p,int definition,int target=0,std::vector<int> costs={},int second=0)
    {
        int id=Id(p,definition);
        match.BeginSpellResponse(p,id,target,costs,second);
        match.PassSpellResponse(3-p);
        match.PassSpellResponse(p);
        return id;
    }
    void OpenRaid(int p,std::vector<std::pair<int,int>> blockers={})
    {
        ClearChoices();
        match.BeginRaid(p);
        Check(match.Phase()==YakouPhase::AssignBlockers,"Expected a ready attacker");
        match.AssignBlockers(3-p,blockers);
    }
    void CloseRaid(int p)
    {
        while (match.PriorityPlayer()) match.PassRaidResponse(match.PriorityPlayer());
        ClearChoices();
        match.ResolveRaid(3-p);
        ClearChoices();
    }
    void Next(std::vector<std::pair<int,int>> blockers={})
    {
        int p=match.ActivePlayer();
        ClearChoices();
        match.BeginRaid(p);
        if (match.Phase()==YakouPhase::AssignBlockers)
        {
            match.AssignBlockers(3-p,blockers);
            CloseRaid(p);
        }
        Check(match.Phase()==YakouPhase::TurnStart,"Expected next turn");
        match.StartTurn(3-p);
    }
};
}
int main()
{
    try
    {
        auto catalog=YakouPrototype::LoadAuthoringSnapshot(
            std::filesystem::path(DefaultDataDirectory())/"Alpha02"/"AuthoringSnapshot.json",true);
        // 5102: actual block, not a printed stats assertion.
        {
            Game g(catalog,{5102},{5201,5212});
            int block=g.Summon(1,5102,"Normal",0);
            g.Next();
            int raid=g.Summon(2,5201,"Normal",0);
            g.Cast(2,5212,raid);
            g.Next(); g.Next();
            g.Next({{raid,block}});
            Check(g.Unit(1,0).Damage==1,"5102 must reduce incoming combat damage");
        }
        // 5103, 5114: recover actual spell instances, not exile/payment cards.
        {
            Game g(catalog,{5102,5111,5115,5103,5114},{5202});
            int unit=g.Summon(1,5102,"Normal",0);
            int first=g.Cast(1,5111);
            int second=g.Cast(1,5115,unit);
            g.Next(); g.Next();
            g.Summon(1,5103,"Normal",1);
            auto choice=g.match.PendingEffectChoices(1);
            Check(Has(choice.Candidates,first)&&Has(choice.Candidates,second)&&choice.MinCount==0,
                  "5103 must offer two authored graveyard spells");
            g.Pick(1,first);
            Check(Has(g.match.Player(1).Hand,first),"5103 must recover selected instance");
            g.Cast(1,5111);
            g.Cast(1,5114,0,{second});
            choice=g.match.PendingEffectChoices(1);
            Check(Has(choice.Candidates,first)&&!Has(choice.Candidates,second),
                  "5114 must exclude the necromancy payment");
            g.Pick(1,first);
            Check(Has(g.match.Player(1).Exile,second)&&Has(g.match.Player(1).Hand,first),
                  "5114 must pay and recover actual instances");
        }
        // 5104: choose only another wounded friendly monster.
        {
            Game g(catalog,{5104,5105,5212,5111},{5202});
            g.Summon(1,5104,"Normal",0);
            int target=g.Summon(1,5105,"Resource",1);
            g.ClearChoices();
            g.Cast(1,5212,target);
            auto choice=g.match.PendingEffectChoices(1);
            Check(choice.Candidates==std::vector<int>{target}&&choice.MinCount==0,
                  "5104 must offer wounded other friendly only");
            g.Pick(1,target);
            Check(g.Unit(1,1).Damage==0,"5104 heal");
            g.Cast(1,5111);
            Check(g.match.PendingEffects().empty(),"5104 card-name use limit");
        }
        // 5106: spell-dependent combat bonus.
        {
            Game g(catalog,{5106,5111},{5202});
            g.Summon(1,5106,"Resource",0);
            g.Next(); g.Next(); g.Next(); g.Next();
            g.Cast(1,5111);
            g.Next();
            Check(g.match.Player(2).Life==16,"5106 must deal 3+1 after a spell this turn");
        }
        // 5107: block two separate attacks using actual assignment capacity.
        {
            Game g(catalog,{5107},{5201,5212,5205});
            int block=g.Summon(1,5107,"Resource",0);
            Check(g.Unit(1,0).MaxBlocks==2,"5107 block capacity");
            g.Next();
            int a=g.Summon(2,5201,"Normal",0);
            g.Cast(2,5212,a);
            int b=g.Summon(2,5205,"Resource",1);
            g.Next(); g.Next();
            g.Next({{a,block},{b,block}});
            Check(g.Unit(1,0).Damage==5&&g.match.Player(1).Life==20,"5107 must block both raids");
        }
        // 5108, 5116, 5110: delay enemy countdown, then mixed payment + lethal effect damage.
        {
            Game g(catalog,{5108,5116,5110,5102,5103},{5201});
            g.Next();
            int enemy=g.Summon(2,5201,"Normal",0);
            g.Next();
            int discard=g.Id(1,5102);
            g.Summon(1,5108,"Discard",0,{discard});
            g.Pick(1,enemy);
            Check(g.Unit(2,0).Countdown==3&&Has(g.match.Player(1).Graveyard,discard),"5108 delay and cost");
            g.Cast(1,5116,enemy);
            Check(g.Unit(2,0).Countdown==4&&g.Resource(1,"Spirit")==6,"5116 delay and cost");
            int material=g.Id(1,5103);
            g.Summon(1,5110,"Resource",1,{material});
            g.Pick(1,enemy);
            Check(!g.match.Player(2).Field[0]&&Has(g.match.Player(2).Graveyard,enemy)&&
                  Has(g.match.Player(1).Graveyard,material)&&g.Resource(1,"Spirit")==1,
                  "5110 lethal effect damage and mixed cost");
        }
        // 5109, 5113: response reduces actual damage and refunds one Spirit.
        {
            Game g(catalog,{5109,5113},{5201,5212},4);
            g.Summon(1,5109,"Normal",0);
            g.Next();
            int enemy=g.Summon(2,5201,"Normal",0);
            g.Cast(2,5212,enemy);
            g.Next(); g.Next();
            g.OpenRaid(2);
            int spell=g.Id(1,5113);
            RejectUnchanged(g.match,[&]{g.match.RespondWithRaidTactic(1,spell,g.Unit(1,0).InstanceId,0,{});});
            g.match.RespondWithRaidTactic(1,spell,enemy,0,{});
            Check(g.Resource(1,"Spirit")==3,"5113 cost");
            int life=g.match.Player(1).Life;
            g.CloseRaid(2);
            Check(g.match.Player(1).Life==life&&g.Resource(1,"Spirit")==4,"5113/5109 response synergy");
        }
        // 5118: actual counterspell prevents damage and gain, costs Spirit.
        {
            Game g(catalog,{5109,5118},{5211},4,0);
            g.Summon(1,5109,"Normal",0);
            g.Next();
            int original=g.Id(2,5211);
            g.match.BeginSpellResponse(2,original);
            g.match.RespondWithNegation(1,g.Id(1,5118),{});
            g.match.PassSpellResponse(2);
            g.match.PassSpellResponse(1);
            auto const resolution = g.match.ViewerSnapshot(1).at("ChainResolution");
            Check(resolution.size() == 2 &&
                  resolution[0].at("Card").at("DefinitionId") == 5118 &&
                  resolution[0].at("Result") == "Resolved" &&
                  resolution[1].at("Card").at("DefinitionId") == 5211 &&
                  resolution[1].at("Result") == "Negated",
                  "Public chain results must follow actual LIFO negation without private targets");
            Check(g.match.Player(2).Life==20&&g.Resource(2,"Blood")==0&&
                  g.Resource(1,"Spirit")==2,"5118 negate and 5109 refund");
        }
        // 5115: healing and next-positive-hit shield; blocked damage must not produce Blood.
        {
            Game g(catalog,{5201,5212,5212,5115},{5202},0,0);
            int unit=g.Summon(1,5201,"Normal",0);
            g.Cast(1,5212,unit);
            Check(g.Resource(1,"Blood")==1&&g.Unit(1,0).Damage==1,"5201 positive surviving damage");
            g.Cast(1,5115,unit);
            Check(g.Unit(1,0).Damage==0&&
                  g.match.ViewerSnapshot(1).at("Players")[0].at("FieldSlots")[0].at("NextDamageShield")==1,
                  "5115 must heal and grant shield");
            g.Next(); g.Next();
            g.Cast(1,5212,unit);
            Check(g.Unit(1,0).Damage==0&&g.Resource(1,"Blood")==1&&
                  !g.match.ViewerSnapshot(1).at("Players")[0].at("FieldSlots")[0].contains("NextDamageShield"),
                  "5115 consumes shield without creating Blood");
        }
        // 5202: effect damage must not trigger combat-only gain.
        {
            Game g(catalog,{5202,5212},{5201,5212},0,0);
            int block=g.Summon(1,5202,"Normal",0);
            g.Cast(1,5212,block);
            Check(g.Resource(1,"Blood")==0,"5202 must ignore effect damage");
            g.Next();
            int raid=g.Summon(2,5201,"Normal",0);
            g.Cast(2,5212,raid);
            g.Next(); g.Next();
            g.Next({{raid,block}});
            Check(g.Unit(1,0).Damage==3&&g.Resource(1,"Blood")==1,"5202 combat-only surviving damage");
        }
        // 5203, 5204, 5207: real discard and damage event synergy.
        {
            Game g(catalog,{5207,5204,5204,5203},{5202},0,0);
            g.Summon(1,5207,"Normal",0);
            int discard=g.Id(1,5204);
            // Pick a different copy as summon source; the cost is explicitly selected.
            auto own=g.match.ViewerSnapshot(1);
            int second=0;
            for (auto const& card : own.at("Players")[0].at("Hand"))
                if(card.at("DefinitionId")==5204&&card.at("Id")!=discard)second=card.at("Id").get<int>();
            Check(second!=0,"5204 duplicate fixture");
            (void)g.match.SummonAndRunAutomaticTriggers(1,second,"Discard",{discard},1);
            Check(g.Resource(1,"Blood")==1,"5204 discard trigger");
            g.Next(); g.Next();
            g.Summon(1,5203,"Normal",2);
            g.Pick(1,second);
            Check(g.Unit(1,1).Damage==1&&g.Unit(1,1).Countdown==0&&g.Resource(1,"Blood")==2,
                  "5203 damage/acceleration and 5207 other-friendly damage trigger");
        }
        // 5208: devour uses prior material damage, not consumer damage.
        {
            Game g(catalog,{5201,5212,5208},{5202},0,0);
            int material=g.Summon(1,5201,"Normal",0);
            g.Cast(1,5212,material);
            g.Summon(1,5208,"Devour",0,{material});
            Check(g.Unit(1,0).Attack()==4&&g.Unit(1,0).MaxHp()==6&&
                  g.Unit(1,0).Damage==0&&g.Unit(1,0).Countdown==2&&
                  Has(g.match.Player(1).Graveyard,material),"5208 wounded Devour and stats");
        }
        // 5206, 5214, 5218: actual raid attack and piercing with both paid responses.
        {
            Game g(catalog,{5206,5212,5214,5218},{5102});
            int raid=g.Summon(1,5206,"Resource",0);
            g.Cast(1,5212,raid);
            g.Next();
            int block=g.Summon(2,5102,"Normal",0);
            g.Next();
            g.OpenRaid(1,{{raid,block}});
            g.match.PassRaidResponse(2);
            g.match.RespondWithRaidTactic(1,g.Id(1,5214),raid,0,{});
            g.match.PassRaidResponse(2);
            g.match.RespondWithRaidTactic(1,g.Id(1,5218),raid,0,{});
            g.CloseRaid(1);
            Check(g.match.Player(2).Life==18&&!g.match.Player(2).Field[0]&&g.Resource(1,"Blood")==0,
                  "5206 + 5214 + 5218 combat and piercing damage");
        }
        // 5209, 5215: wound reduction + shield; discard-trigger gain is deferred.
        {
            Game g(catalog,{5209,5212,5215,5204},{5106});
            int block=g.Summon(1,5209,"Resource",0);
            g.Cast(1,5212,block);
            g.Next();
            int raid=g.Summon(2,5106,"Resource",0);
            g.Next(); g.Next(); g.Next(); g.Next();
            g.OpenRaid(2,{{raid,block}});
            int before=g.Resource(1,"Blood");
            g.match.RespondWithRaidTactic(1,g.Id(1,5215),block,0,{g.Id(1,5204)});
            Check(g.Resource(1,"Blood")==before,"5215 discard gain must wait until chain closes");
            g.CloseRaid(2);
            Check(g.Unit(1,0).Damage==1&&g.Resource(1,"Blood")==before+1,
                  "5209 reduction + 5215 shield + deferred 5204");
        }
        // 5210: mixed cost, self wound and intrinsic piercing.
        {
            Game g(catalog,{5210,5102},{5201});
            int raid=g.Summon(1,5210,"Resource",0,{g.Id(1,5102)});
            Check(g.Resource(1,"Blood")==1&&g.Unit(1,0).Damage==1,"5210 mixed cost and self wound");
            g.Next();
            int block=g.Summon(2,5201,"Normal",0);
            g.Next(); g.Next(); g.Next();
            g.Next({{raid,block}});
            Check(g.match.Player(2).Life==19&&!g.match.Player(2).Field[0],"5210 intrinsic piercing");
        }
        // 5213/5216: heal and two-target damage; invalid cross-owner target is atomic.
        {
            Game g(catalog,{5202,5212,5212,5216,5213},{5102});
            int friendly=g.Summon(1,5202,"Normal",0);
            g.Cast(1,5212,friendly);
            g.Next();
            int enemy=g.Summon(2,5102,"Normal",0);
            g.Next();
            g.Cast(1,5212,friendly);
            int curse=g.Id(1,5216);
            RejectUnchanged(g.match,[&]{g.match.BeginSpellResponse(1,curse,enemy,{},enemy);});
            g.Cast(1,5216,enemy,{},friendly);
            Check(g.Unit(2,0).Damage==2&&g.Resource(1,"Blood")==4,"5216 must read friendly wound");
            g.Cast(1,5213,friendly);
            Check(g.Unit(1,0).Damage==0&&g.Resource(1,"Blood")==3,"5213 must heal two");
        }
        // 5217: paid discard is excluded from the recovery candidate set.
        {
            Game g(catalog,{5204,5201,5202,5217},{5102},0,0);
            int recover=g.Id(1,5201),pay=g.Id(1,5202);
            g.Summon(1,5204,"Discard",0,{recover});
            g.Cast(1,5217,0,{pay});
            auto choice=g.match.PendingEffectChoices(1);
            Check(Has(choice.Candidates,recover)&&!Has(choice.Candidates,pay),"5217 must exclude its paid card");
            g.Pick(1,recover);
            Check(Has(g.match.Player(1).Hand,recover)&&Has(g.match.Player(1).Graveyard,pay),"5217 recovery");
        }
        // Same command sequence + private seed produces exact same canonical state.
        {
            auto a=Game::Config(catalog,8,6);
            auto left=Deck({5117,5101},true,true),right=Deck({5201},true,true);
            auto run=[&](std::uint64_t seed){
                YakouSession s(a,1,{left,right},seed);
                s.Bind(1,"one"); s.Bind(2,"two");
                auto exec=[&](int seat,char const* req,Json cmd){
                    auto result=s.Dispatch(seat==1?"one":"two",req,cmd);
                    Check(result.at("Accepted")==true,"Replay fixture command");
                };
                exec(1,"start",{{"Type","StartTurn"}});
                int spell=s.Match().Player(1).Hand[0];
                exec(1,"search",{{"Type","BeginSpellResponse"},{"CardId",spell}});
                exec(2,"pass2",{{"Type","PassSpellResponse"}});
                exec(1,"pass1",{{"Type","PassSpellResponse"}});
                auto choice=s.Match().PendingEffectChoices(1);
                Check(!choice.Candidates.empty(),"Search fixture candidates");
                exec(1,"pick",{{"Type","ChooseEffect"},{"TargetId",choice.Candidates.front()}});
                Check(s.VerifyReplay(),"Session must replay exact accepted and rejected commands");
                auto snapshot=s.Snapshot("one");
                Check(!snapshot.contains("RandomSeed")&&!snapshot.contains("RandomDraws"),"RNG state leaked");
                return s.Match().ServerStateHash();
            };
            Check(run(42)==run(42)&&run(42)!=run(43),"Seeded session replay must be reproducible and seed-sensitive");
        }
        std::cout<<"Alpha02 actual card behavior regression cases completed\n";
        return 0;
    }
    catch(std::exception const& e)
    {
        std::cerr<<"Alpha02CardTests: "<<e.what()<<'\n';
        return 1;
    }
}
