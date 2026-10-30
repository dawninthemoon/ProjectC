#include "CardCatalog.hpp"
#include "ProjectC/Zone.hpp"
#include "ProjectC/GameCommand.hpp"
#include <fstream>
#include <filesystem>
#include <map>
#include <random>
#include <stdexcept>
namespace ProjectC::Network
{
using namespace GameCore;
namespace
{
Json File(std::filesystem::path const& path)
{
    if (std::filesystem::file_size(path) > 1024 * 1024) throw std::runtime_error("Data file too large");
    std::ifstream stream(path);
    if (!stream) throw std::runtime_error("Cannot open data file");
    return ParseJson(std::string(std::istreambuf_iterator<char>(stream), {}), 1024 * 1024);
}
template<typename T> T Enum(Json const& value, char const* key, std::initializer_list<std::pair<char const*, T>> names)
{
    auto name = String(value, key);
    for (auto const& [text, item] : names) if (name == text) return item;
    throw std::runtime_error("Invalid enum in card data");
}
Zone Location(Json const& value)
{
    return Enum<Zone>(Json{{"Zone",value}},"Zone",{{"Deck",Zone::Deck},{"Hand",Zone::Hand},{"Field",Zone::Field},{"Graveyard",Zone::Graveyard}});
}
std::string Text(Json const& value, char const* key)
{
    auto const& field=value.at(key);
    if(!field.is_string()) throw std::runtime_error("Expected card data text");
    auto text=field.get<std::string>();
    if(text.empty() || text.size()>256) throw std::runtime_error("Invalid card data text length");
    for(unsigned char c:text) if(c<32) throw std::runtime_error("Control character in card data text");
    return text;
}
void Array(Json const& value, std::size_t maximum)
{
    if (!value.is_array() || value.size() > maximum) throw std::runtime_error("Invalid data array");
}
TargetRule Rule(Json const& value)
{
    Fields(value,{"Category","Zones","MinCount","MaxCount","RequiredTrait"});
    TargetRule rule;
    rule.Category = Enum<TargetCategory>(value,"Category",{
        {"Self",TargetCategory::Self},{"FriendlyMonster",TargetCategory::FriendlyMonster},{"EnemyMonster",TargetCategory::EnemyMonster},
        {"AnyMonster",TargetCategory::AnyMonster},{"FriendlyPlayer",TargetCategory::FriendlyPlayer},{"EnemyPlayer",TargetCategory::EnemyPlayer},
        {"FriendlyDeck",TargetCategory::FriendlyDeck},{"FriendlyHand",TargetCategory::FriendlyHand},{"FriendlyGraveyard",TargetCategory::FriendlyGraveyard},
        {"EnemyGraveyard",TargetCategory::EnemyGraveyard}});
    if (value.contains("Zones")) { rule.Zones.clear(); Array(value["Zones"],4); for (auto const& zone:value["Zones"]) rule.Zones.push_back(Location(zone)); }
    if (value.contains("MinCount")) rule.MinCount=Number(value,"MinCount",128);
    if (value.contains("MaxCount")) rule.MaxCount=Number(value,"MaxCount",128);
    if (value.contains("RequiredTrait")) rule.RequiredTrait=Text(value,"RequiredTrait");
    return rule;
}
EffectDefinition Effect(Json const& value)
{
    Fields(value,{"Id","Type","Trigger","Actions"});
    EffectDefinition effect;
    effect.Id=static_cast<int>(Number(value,"Id",1000000));
    effect.Type=Enum<EffectType>(value,"Type",{{"Activated",EffectType::Activated},{"Triggered",EffectType::Triggered}});
    if (value.contains("Trigger")) effect.Trigger=Enum<TriggerCondition>(value,"Trigger",{
        {"None",TriggerCondition::None},{"OnSummoned",TriggerCondition::OnSummoned},{"OnDestroyed",TriggerCondition::OnDestroyed},
        {"OnDamaged",TriggerCondition::OnDamaged},{"OnTurnStarted",TriggerCondition::OnTurnStarted},{"OnTurnEnded",TriggerCondition::OnTurnEnded}});
    Array(value.at("Actions"),16);
    for(auto const& entry:value["Actions"])
    {
        Fields(entry,{"Operation","Targets","Amount","AttackDelta","MaxHpDelta","TransformDefinitionId"});
        EffectAction action;
        action.Operation=Enum<EffectOperation>(entry,"Operation",{
            {"Draw",EffectOperation::Draw},{"Mill",EffectOperation::Mill},{"Salvage",EffectOperation::Salvage},{"Search",EffectOperation::Search},
            {"SummonFromDeck",EffectOperation::SummonFromDeck},{"SummonFromGraveyard",EffectOperation::SummonFromGraveyard},
            {"ReturnToHand",EffectOperation::ReturnToHand},{"Destroy",EffectOperation::Destroy},{"DealDamage",EffectOperation::DealDamage},
            {"Heal",EffectOperation::Heal},{"ModifyStats",EffectOperation::ModifyStats},{"Transform",EffectOperation::Transform}});
        action.Targets=Rule(entry.at("Targets"));
        if (entry.contains("Amount")) action.Amount=static_cast<int>(Number(entry,"Amount",100000));
        for (auto key:{"AttackDelta","MaxHpDelta"}) if(entry.contains(key))
        {
            if (!entry[key].is_number_integer() || (entry[key].is_number_unsigned() && entry[key].get<std::uint64_t>()>100000))
                throw std::runtime_error("Invalid stat delta");
            auto delta=entry[key].get<std::int64_t>();
            if (delta < -100000 || delta > 100000) throw std::runtime_error("Invalid stat delta");
            if(std::string(key)=="AttackDelta") action.AttackDelta=static_cast<int>(delta); else action.MaxHpDelta=static_cast<int>(delta);
        }
        if(entry.contains("TransformDefinitionId")) action.TransformDefinitionId=static_cast<int>(Number(entry,"TransformDefinitionId",1000000));
        effect.Actions.push_back(action);
    }
    return effect;
}
}
std::string DefaultDataDirectory() { return PROJECTC_DATA_DIR; }
CardCatalog CardCatalog::Load(std::string const& directory)
{
    return FromJson(File(std::filesystem::path(directory)/"Cards.json"),File(std::filesystem::path(directory)/"Decks.json"));
}
CardCatalog CardCatalog::FromJson(Json const& cards, Json const& decks)
{
    Fields(cards,{"Cards"}); Fields(decks,{"Config","Decks"});
    CardCatalog catalog;
    auto const& config=decks.at("Config");
    Fields(config,{"InitialLife","InitialHandSize","DrawPerTurn","FieldLimit","HandLimit","MinDeckSize","MaxDeckSize","MaxCardCopies"});
    catalog.Config.InitialLife=static_cast<int>(Number(config,"InitialLife",100000));
    catalog.Config.InitialHandSize=Number(config,"InitialHandSize",128);
    catalog.Config.DrawPerTurn=Number(config,"DrawPerTurn",128);
    catalog.Config.FieldLimit=Number(config,"FieldLimit",32);
    catalog.Config.HandLimit=Number(config,"HandLimit",128);
    catalog.Config.MinDeckSize=Number(config,"MinDeckSize",128);
    catalog.Config.MaxDeckSize=Number(config,"MaxDeckSize",128);
    catalog.Config.MaxCardCopies=Number(config,"MaxCardCopies",128);
    catalog.Config.AllowManualDraw=false;
    if(!ValidateConfig(catalog.Config) || catalog.Config.MinDeckSize==0 || catalog.Config.MinDeckSize>catalog.Config.MaxDeckSize
        || catalog.Config.MaxCardCopies==0 || catalog.Config.FieldLimit==0) throw std::runtime_error("Invalid deck configuration");
    Array(cards.at("Cards"),1024);
    for(auto const& entry:cards["Cards"])
    {
        Fields(entry,{"Id","Name","Attack","MaxHp","Traits","AllowedManifestations","Effects"});
        CardDefinition card;
        card.Id=static_cast<int>(Number(entry,"Id",1000000)); card.Name=Text(entry,"Name");
        card.Attack=static_cast<int>(Number(entry,"Attack",100000)); card.MaxHp=static_cast<int>(Number(entry,"MaxHp",100000));
        if(entry.contains("Traits")) { Array(entry["Traits"],16); for(auto const& trait:entry["Traits"]) card.Traits.push_back(Text(Json{{"Value",trait}},"Value")); }
        if(entry.contains("AllowedManifestations")) { Array(entry["AllowedManifestations"],16); for(auto const& id:entry["AllowedManifestations"]) card.AllowedManifestations.push_back(static_cast<int>(Number(Json{{"Id",id}},"Id",1000000))); }
        if(entry.contains("Effects")) { Array(entry["Effects"],16); for(auto const& effect:entry["Effects"]) card.Effects.push_back(Effect(effect)); }
        catalog.Definitions.push_back(card);
    }
    Array(decks.at("Decks"),32);
    for(auto const& entry:decks["Decks"])
    {
        Fields(entry,{"Id","Name","Cards"});
        DeckDefinition deck{String(entry,"Id",64),Text(entry,"Name"),{}};
        if(catalog.FindDeck(deck.Id)) throw std::runtime_error("Duplicate deck ID");
        Array(entry.at("Cards"),128);
        for(auto const& id:entry["Cards"]) deck.Cards.push_back(static_cast<int>(Number(Json{{"Id",id}},"Id",1000000)));
        if(!catalog.ValidateDeck(deck.Cards)) throw std::runtime_error("Invalid deck composition");
        catalog.Decks.push_back(deck);
    }
    if(catalog.Decks.size()<2) throw std::runtime_error("At least two playtest decks required");
    // Core validates IDs, stats, effects, target rules and transformation links before serving clients.
    for(auto const& deck:catalog.Decks)
    {
        auto sample=catalog.Initialize({1,2},{deck.Id,deck.Id},1,1);
        if(!ValidateGameState(sample) || ProcessCommand(sample,StartTurnCommand{1}).Code!=CommandError::Success)
            throw std::runtime_error("Invalid card definitions or initial draw configuration");
    }
    return catalog;
}
DeckDefinition const* CardCatalog::FindDeck(std::string const& id) const
{
    for(auto const& deck:Decks) if(deck.Id==id) return &deck;
    return nullptr;
}
bool CardCatalog::ValidateDeck(std::vector<CardDefinitionId> const& cards) const
{
    if(cards.size()<Config.MinDeckSize || cards.size()>Config.MaxDeckSize || cards.size()<Config.InitialHandSize) return false;
    std::map<int,std::size_t> counts;
    for(auto id:cards)
    {
        bool known=false; for(auto const& definition:Definitions) if(definition.Id==id) known=true;
        if(!known || ++counts[id]>Config.MaxCardCopies) return false;
    }
    return true;
}
Json CardCatalog::PublicDecks() const
{
    Json result=Json::array();
    for(auto const& deck:Decks) result.push_back({{"Id",deck.Id},{"Name",deck.Name},{"CardCount",deck.Cards.size()}});
    return result;
}
GameState CardCatalog::Initialize(std::array<int,2> players, std::array<std::string,2> const& decks, std::uint64_t seed, int firstCardId) const
{
    std::mt19937_64 random(seed);
    std::array<PlayerSetup,2> setup;
    for(int i=0;i<2;++i)
    {
        auto deck=FindDeck(decks[i]);
        if(!deck || !ValidateDeck(deck->Cards)) throw std::runtime_error("Invalid selected deck");
        setup[i]={players[i],deck->Cards};
        for(std::size_t n=setup[i].Deck.size();n>1;--n)
        {
            auto const bound=static_cast<std::uint64_t>(n);
            auto const threshold=(std::uint64_t{0}-bound)%bound;
            std::uint64_t value;
            do {value=random();} while(value<threshold);
            std::swap(setup[i].Deck[n-1],setup[i].Deck[value%bound]);
        }
    }
    GameState state;
    if(InitializeGame(state,Config,Definitions,setup,players[0])!=InitializationResult::Success) throw std::runtime_error("Invalid card data");
    for(auto& player:state.Players) for(auto zone:{Zone::Deck,Zone::Hand,Zone::Field,Zone::Graveyard})
        for(auto& card:*GetZone(player,zone)) card.Id+=firstCardId-1;
    return state;
}
}
