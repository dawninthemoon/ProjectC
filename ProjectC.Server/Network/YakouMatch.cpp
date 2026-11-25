#include "YakouMatch.hpp"

#include <algorithm>
#include <map>
#include <limits>
#include <initializer_list>
#include <set>
#include <stdexcept>
#include <string>
#include <utility>

namespace ProjectC::Network
{
namespace
{
constexpr int MaximumRaidResponseLinks = 8;
constexpr int MaximumSpellResponseLinks = 8;
void Check(bool valid, std::string const& message)
{
    if (!valid) throw std::invalid_argument(message);
}
bool Contains(std::vector<int> const& cards, int id)
{
    return std::find(cards.begin(), cards.end(), id) != cards.end();
}
void Remove(std::vector<int>& cards, int id)
{
    auto it = std::find(cards.begin(), cards.end(), id);
    Check(it != cards.end(), "card missing from expected zone");
    cards.erase(it);
}
YakouUnit const* FindUnit(YakouPlayer const& player, int instance)
{
    for (auto const& slot : player.Field)
        if (slot && slot->InstanceId == instance) return &*slot;
    return nullptr;
}
int FindSlot(YakouPlayer const& player, int instance)
{
    for (int i = 0; i < 5; ++i)
        if (player.Field[i] && player.Field[i]->InstanceId == instance) return i;
    return -1;
}
bool HasOnlyFields(Json const& object, std::initializer_list<char const*> allowed)
{
    if (!object.is_object())
    {
        return false;
    }
    for (auto it = object.begin(); it != object.end(); ++it)
    {
        if (std::none_of(allowed.begin(), allowed.end(),
                         [&](char const* key) { return it.key() == key; }))
        {
            return false;
        }
    }
    return true;
}


bool CanPayResources(YakouPlayer const& player, YakouResourceAmounts const& costs)
{
    for (auto const& [resource, amount] : costs)
    {
        auto const found = player.Resources.find(resource);
        if (amount <= 0 || found == player.Resources.end() || found->second.Current < amount)
            return false;
    }
    return true;
}

void ChangeResource(YakouPlayer& player, std::string const& resource, int delta)
{
    auto found = player.Resources.find(resource);
    if (found == player.Resources.end())
    {
        // A free card may be splashed into a deck that did not select this resource.
        Check(delta >= 0, "Cannot spend inactive special resource");
        return;
    }
    auto& state = found->second;
    auto const next = static_cast<std::int64_t>(state.Current) + delta;
    Check(next >= 0, "Insufficient special resource");
    state.Current = static_cast<int>(std::min<std::int64_t>(state.Maximum, next));
    if (resource == "Ki") player.Ki = state.Current;
}

void PayResources(YakouPlayer& player, YakouResourceAmounts const& costs)
{
    Check(CanPayResources(player, costs), "Insufficient or inactive special resource");
    for (auto const& [resource, amount] : costs)
        ChangeResource(player, resource, -amount);
}

void SpendLegacyKi(YakouPlayer& player, int amount)
{
    Check(amount >= 0 && player.Ki >= amount, "Insufficient Ki");
    if (player.Resources.contains("Ki"))
        ChangeResource(player, "Ki", -amount);
    else
        player.Ki -= amount;
}

Json PublicResources(YakouPlayer const& player)
{
    Json result = Json::object();
    for (auto const& [id, state] : player.Resources)
        result[id] = {{"Current", state.Current}, {"Max", state.Maximum}};
    return result;
}

// Do not silently omit a newly authored cost, condition, duration or target rule.
void CheckSupportedAction(Json const& action)
{
    Check(action.is_object() && action.contains("Operation") && action.at("Operation").is_string(),
          "Malformed Yakou effect operation");
    auto const operation = action.at("Operation").get<std::string>();
    if (action.contains("Condition"))
    {
        auto const& condition = action.at("Condition");
        bool const legacyArchetype = HasOnlyFields(condition, {"GraveyardArchetypeCountAtLeast"}) &&
            condition.contains("GraveyardArchetypeCountAtLeast") &&
            action.contains("RequiredArchetype") && action.at("RequiredArchetype").is_string();
        bool const authoredArchetype = HasOnlyFields(condition, {"GraveyardArchetypeCountAtLeast", "Archetype"}) &&
            condition.contains("GraveyardArchetypeCountAtLeast") &&
            condition.contains("Archetype") && condition.at("Archetype").is_string();
        bool const authoredType = HasOnlyFields(condition, {"GraveyardCardTypeCountAtLeast", "CardType"}) &&
            condition.contains("GraveyardCardTypeCountAtLeast") &&
            condition.contains("CardType") && condition.at("CardType").is_string();
        Check(operation == "Salvage" && condition.is_object() &&
                  (legacyArchetype || authoredArchetype || authoredType),
              "Conditional Yakou effect not yet supported");
        auto const count = legacyArchetype || authoredArchetype
            ? condition.at("GraveyardArchetypeCountAtLeast")
            : condition.at("GraveyardCardTypeCountAtLeast");
        Check(count.is_number_integer() && count.get<int>() >= 0,
              "Invalid conditional Yakou threshold");
    }
    bool supported = false;
    if (operation == "ModifyResource")
    {
        supported = HasOnlyFields(action, {"Operation", "Target", "ResourceId", "Delta"}) &&
            action.value("Target", std::string()) == "FriendlyPlayer" &&
            action.contains("ResourceId") && action.at("ResourceId").is_string() &&
            action.contains("Delta") && action.at("Delta").is_number_integer() &&
            action.at("Delta").get<std::int64_t>() > 0 &&
            action.at("Delta").get<std::int64_t>() <= 100000;
    }
    else if (operation == "Mill")
    {
        supported = HasOnlyFields(action, {"Operation", "Count", "From", "To"});
    }
    else if (operation == "ModifyCountdown")
    {
        bool const distinct = action.value("Target", std::string()) == "DistinctFriendlyMonsters";
        supported = HasOnlyFields(action, {"Operation", "Target", "Delta", "Minimum", "Optional",
                                           "MinTargets", "MaxTargets"}) &&
            (!action.contains("Minimum") || action.at("Minimum") == 0) &&
            (distinct ? action.value("MinTargets", 0) == 1 && action.value("MaxTargets", 0) == 2
                      : !action.contains("MinTargets") && !action.contains("MaxTargets"));
    }
    else if (operation == "SetCountdown")
    {
        supported = HasOnlyFields(action, {"Operation", "Target", "Value", "Optional"});
    }
    else if (operation == "HealAccumulatedDamage")
    {
        supported = HasOnlyFields(action, {"Operation", "Target", "Amount", "Optional"});
    }
    else if (operation == "ReduceDamage")
    {
        supported = HasOnlyFields(action, {"Operation", "Target", "Amount", "Duration", "Minimum"}) &&
            action.value("Target", std::string()) == "FriendlyMonster" &&
            action.value("Duration", std::string()) == "NextDamageEvent" &&
            action.value("Amount", 0) == 2 && action.value("Minimum", -1) == 0;
    }
    else if (operation == "IgnoreOneManifestCondition")
    {
        supported = HasOnlyFields(action, {"Operation", "Target", "Duration", "KiCostUnaffected"}) &&
            action.value("Target", std::string()) == "FriendlyPlayer" &&
            action.value("Duration", std::string()) == "ThisTurn" &&
            action.value("KiCostUnaffected", false);
    }
    else if (operation == "Salvage")
    {
        supported = HasOnlyFields(action, {"Operation", "From", "To", "Count", "CardType",
                                           "RequiredArchetype", "Optional", "Condition", "ExcludePaidCostCards"}) &&
            (!action.contains("ExcludePaidCostCards") || action.at("ExcludePaidCostCards") == true);
    }
    else if (operation == "SpecialSummon")
    {
        supported = HasOnlyFields(action, {"Operation", "From", "To", "Count", "CardType",
                                           "RequiredArchetype", "MaxPrintedAttack", "ExcludePaidCostCards"}) &&
            action.value("From", std::string()) == "FriendlyGraveyard" &&
            action.value("To", std::string()) == "FriendlyField" &&
            action.value("Count", 0) == 1 &&
            action.value("CardType", std::string()) == "Monster" &&
            action.value("RequiredArchetype", std::string()) == "HyakkiYagyo" &&
            action.value("MaxPrintedAttack", -1) == 2 &&
            action.value("ExcludePaidCostCards", false);
    }
    else if (operation == "LookAndChoose")
    {
        supported = HasOnlyFields(action, {"Operation", "From", "LookCount", "ChooseCount", "ChosenDestination", "RestDestination"});
    }
    Check(supported, "Unsupported Yakou effect operation or action fields: " + operation);
}

bool IsMillThenCountdown(Json const& actions)
{
    return actions.is_array() && actions.size() == 2 &&
        actions[0].is_object() && actions[1].is_object() &&
        actions[0] == Json{{"Operation", "Mill"}, {"Count", 1},
                           {"From", "FriendlyDeck"}, {"To", "Graveyard"}} &&
        actions[1] == Json{{"Operation", "ModifyCountdown"},
                           {"Target", "FriendlyMonster"}, {"Delta", -1},
                           {"Minimum", 0}};
}

// Evaluate printed field-only passives before committing a summon or
// manifestation. Unrecognized continuous effects must not silently disappear.
int PrintedMaxBlocks(Json const& card)
{
    int maximum = 1;
    for (auto const& effect : card.at("Effects"))
    {
        if (effect.at("Type") != "Continuous")
        {
            continue;
        }
        Check(effect.at("Trigger") == "Passive" && effect.at("Actions").is_array() &&
                  !effect.at("Actions").empty(), "Unsupported continuous effect");
        for (auto const& action : effect.at("Actions"))
        {
            Check(action.is_object() && action.size() == 4 &&
                      action.at("Operation") == "IncreaseMaxBlocks" && action.at("Target") == "Self" &&
                      action.at("Duration") == "WhileOnField" && action.at("Value").is_number_integer() &&
                      !action.contains("Condition"), "Unsupported continuous action");
            int const value = action.at("Value").get<int>();
            Check(value > 0, "Invalid maximum blocker count");
            maximum = std::max(maximum, value);
        }
    }
    return maximum;
}
} // namespace

YakouMatch::YakouMatch(YakouPrototype const& catalog,
                       int firstPlayer,
                       std::array<std::vector<int>, 2> deckDefinitions)
    : catalog_(&catalog)
{
    Check(firstPlayer == 1 || firstPlayer == 2, "Invalid first player");
    Check(catalog.Config.at("ki.start") == 3 &&
              catalog.Config.at("ki.baseRegenPerTurn") == 0,
          "Unsupported Yakou configuration");
    bool const alpha02 = catalog.Config.contains("resource.definitions");
    Check(!alpha02 || catalog.Config.value("alpha02.experimentalRuntime", false),
          "Alpha02 experimental runtime is not enabled");
    auto const resourceDefinitions = YakouReadResourceDefinitions(catalog.Config);
    first_ = firstPlayer - 1;
    active_ = first_;
    std::set<int> legal;
    for (auto const& definition : catalog.Cards.at("Cards"))
    {
        if (definition.value("MainDeckEligible", true) &&
            definition.at("Type") != "Manifestation")
            legal.insert(definition.at("Id").get<int>());
    }
    for (int p = 0; p < 2; ++p)
    {
        auto& player = players_[p];
        player.Id = p + 1;
        Check(deckDefinitions[p].size() == 30, "Yakou requires 30 cards per deck");
        if (alpha02)
        {
            player.Ki = 0;
            for (auto const& type : YakouDeckResourceTypes(catalog.Cards, catalog.Config, deckDefinitions[p]))
            {
                auto const& definition = resourceDefinitions.at(type);
                player.Resources.emplace(type, YakouResourceState{definition.Initial, definition.Maximum,
                                                                    definition.RegenPerOwnTurn});
                if (type == "Ki") player.Ki = definition.Initial;
            }
        }
        std::map<int, int> copies;
        for (int definition : deckDefinitions[p])
        {
            Check(legal.contains(definition), "Unknown/ineligible card in deck");
            Check(++copies[definition] <= 3, "Too many copies in deck");
            int const instance = nextInstance_++;
            original_[instance] = definition;
            player.Deck.push_back(instance);
        }
        for (int i = 0; i < 5; ++i)
        {
            player.Hand.push_back(player.Deck.front());
            player.Deck.erase(player.Deck.begin());
        }
    }
}

Json YakouMatch::CardDefinition(int id) const
{
    for (auto const& card : catalog_->Cards.at("Cards"))
        if (card.at("Id") == id) return card;
    throw std::invalid_argument("Unknown card definition");
}
int YakouMatch::OriginalId(int instanceId) const
{
    auto it = original_.find(instanceId);
    Check(it != original_.end(), "Unknown instance");
    return it->second;
}
YakouPlayer const& YakouMatch::Player(int id) const
{
    Check(id == 1 || id == 2, "Unknown player");
    return players_[id - 1];
}

int YakouMatch::FilterEffectDamage(int target, int amount) const
{
    Check(amount >= 0, "Effect damage cannot be negative");
    if (chainEffectDamagePrevention_.contains(target))
    {
        return 0;
    }
    auto const found = chainEffectDamageReduction_.find(target);
    return found == chainEffectDamageReduction_.end()
        ? amount : static_cast<int>(std::max<std::int64_t>(0,
              static_cast<std::int64_t>(amount) - found->second));
}
Json YakouMatch::ViewerSnapshot(int viewer) const
{
    Check(viewer == 1 || viewer == 2, "Unknown snapshot viewer");
    auto visibleCard = [&](int instance)
    {
        int const definitionId = OriginalId(instance);
        auto const definition = CardDefinition(definitionId);
        return Json{{"Id", instance}, {"DefinitionId", definitionId},
                    {"NameKey", definition.at("NameKey")},
                    {"DescriptionKey", definition.at("DescriptionKey")},
                    {"CardType", definition.at("Type") == "Spell" ? 1 : 0},
                    {"Attack", definition.value("Attack", 0)},
                    {"Hp", definition.value("MaxHp", 0)},
                    {"MaxHp", definition.value("MaxHp", 0)}};
    };
    Json players = Json::array();
    for (auto const& player : players_)
    {
        Json hand = Json::array();
        if (player.Id == viewer)
        {
            for (int instance : player.Hand)
            {
                hand.push_back(visibleCard(instance));
            }
        }
        Json grave = Json::array();
        for (int instance : player.Graveyard)
        {
            grave.push_back(visibleCard(instance));
        }
        Json exile = Json::array();
        for (int instance : player.Exile)
        {
            exile.push_back(visibleCard(instance));
        }
        Json field = Json::array();
        for (auto const& entry : player.Field)
        {
            if (!entry)
            {
                field.push_back(nullptr);
                continue;
            }
            auto const& unit = *entry;
            auto definition = CardDefinition(unit.DefinitionId);
            field.push_back({{"Id", unit.InstanceId}, {"DefinitionId", unit.DefinitionId},
                             {"NameKey", definition.at("NameKey")},
                             {"DescriptionKey", definition.at("DescriptionKey")},
                             {"CardType", 0}, {"Attack", unit.Attack()}, {"Hp", unit.Hp()},
                             {"MaxHp", unit.MaxHp()}, {"AttackCounter", unit.Countdown},
                             {"MaxBlocks", unit.MaxBlocks}});
            if (auto const shield = nextDamageShields_.find(unit.InstanceId);
                shield != nextDamageShields_.end())
            {
                field.back()["NextDamageShield"] = shield->second;
            }
            if (piercingRaiders_.contains(unit.InstanceId))
            {
                field.back()["PiercingThisRaid"] = true;
            }
            if (auto const reduction = chainEffectDamageReduction_.find(unit.InstanceId);
                reduction != chainEffectDamageReduction_.end())
            {
                field.back()["EffectDamageReductionThisChain"] = reduction->second;
            }
            if (chainEffectDamagePrevention_.contains(unit.InstanceId))
            {
                field.back()["PreventEffectDamageThisChain"] = true;
            }
        }
        Json visiblePlayer = {{"PlayerId", player.Id}, {"Life", player.Life}, {"Ki", player.Ki},
                              {"HandCount", player.Hand.size()}, {"DeckCount", player.Deck.size()},
                              {"Hand", std::move(hand)}, {"FieldSlots", std::move(field)},
                              {"Graveyard", std::move(grave)}, {"Exile", std::move(exile)}};
        if (catalog_->Config.contains("resource.definitions"))
            visiblePlayer["Resources"] = PublicResources(player);
        if (player.Id == viewer)
        {
            bool canNormal = false;
            for (int instance : player.Hand)
            {
                auto const choices = SummonChoices(player.Id, instance);
                if (std::any_of(choices.begin(), choices.end(),
                                [](YakouSummonChoice const& option) { return option.Method == "Normal"; }))
                {
                    canNormal = true;
                    break;
                }
            }
            visiblePlayer["CanNormalSummon"] = canNormal;
            // Owner-only, server-derived summon choices. Do not mirror these
            // candidate instance IDs into the opponent's public snapshot.
            Json summonChoices = Json::array();
            if (phase_ == YakouPhase::Main && player.Id == ActivePlayer())
            {
                for (int instance : player.Hand)
                {
                    for (auto const& option : SummonChoices(player.Id, instance))
                    {
                        summonChoices.push_back({{"CardId", instance},
                                                  {"Method", option.Method},
                                                  {"SourceZone", option.SourceZone},
                                                  {"RequiredCount", option.RequiredCount},
                                                  {"Candidates", option.Candidates},
                                                  {"AvailableSlots", option.AvailableSlots}});
                        if (!option.ResourceCosts.empty())
                            summonChoices.back()["ResourceCosts"] = option.ResourceCosts;
                    }
                }
            }
            visiblePlayer["SummonChoices"] = std::move(summonChoices);
            Json paidSpells = Json::array();
            if (phase_ == YakouPhase::Main && player.Id == ActivePlayer() && pendingEffects_.empty())
            {
                for (int instance : player.Hand)
                {
                    int const definitionId = OriginalId(instance);
                    if (definitionId != 2010 && definitionId != 2012 && definitionId != 3012 && definitionId != 3013 && definitionId != 3015 && definitionId != 4006 &&
                        definitionId != 4012 && definitionId != 4013)
                    {
                        continue;
                    }
                    auto const spellDefinition = CardDefinition(definitionId);
                    auto const& activation = spellDefinition.at("SummonCostJson").at("Activation");
                    if (!activation.is_object() || activation.size() != 1 ||
                        (!activation.contains("Necromancy") && !activation.contains("Discard")))
                    {
                        continue;
                    }
                    bool const necromancy = activation.contains("Necromancy");
                    auto const& count = activation.at(necromancy ? "Necromancy" : "Discard");
                    if (!count.is_number_integer())
                    {
                        continue;
                    }
                    int const required = count.get<int>();
                    Json candidates = Json::array();
                    for (int cost : necromancy ? player.Graveyard : player.Hand)
                    {
                        if (cost != instance)
                        {
                            candidates.push_back(cost);
                        }
                    }
                    if (candidates.size() < static_cast<std::size_t>(required))
                    {
                        continue;
                    }
                    paidSpells.push_back({{"CardId", instance}, {"CostCount", required},
                                          {"CostCandidates", std::move(candidates)}});
                }
            }
            visiblePlayer["PaidSpellOptions"] = std::move(paidSpells);
            visiblePlayer["NormalSummonsUsed"] = player.NormalSummons;
            visiblePlayer["ManifestationsUsed"] = player.Manifestations;
            visiblePlayer["ManifestConditionWaivers"] = manifestConditionWaivers_[player.Id - 1];
            visiblePlayer["RedrawsUsed"] = player.Redraws;
        }
        players.push_back(std::move(visiblePlayer));
    }
    char const* phase = phase_ == YakouPhase::TurnStart ? "TurnStart"
        : phase_ == YakouPhase::Main ? "Main"
        : phase_ == YakouPhase::AssignBlockers ? "AssignBlockers"
        : phase_ == YakouPhase::RaidResponse ? "RaidResponse"
        : phase_ == YakouPhase::SpellResponse ? "SpellResponse" : "Finished";
    Json attacks = Json::array();
    for (auto const& attack : attacks_)
    {
        attacks.push_back({{"AttackerId", attack.Card}, {"Slot", attack.Slot}, {"BlockerId", attack.Blocker}});
    }
    Json result = {{"Phase", phase}, {"ActivePlayerId", ActivePlayer()},
                   {"TurnNumber", turn_}, {"PriorityPlayerId", PriorityPlayer()},
                   {"WinnerPlayerId", winner_}, {"Players", std::move(players)},
                   {"Attacks", std::move(attacks)}};
    // The next public action must not advance a raid past unresolved cost triggers.
    result["PendingEffectCount"] = pendingEffects_.size();
    result["PendingDeferredCostCount"] = std::count_if(
        pendingEffects_.begin(), pendingEffects_.end(),
        [](YakouPendingEffect const& effect) { return effect.DeferredCost; });
    // Both players can follow the public response depth and priority,
    // but private target selections never leave the authoritative server.
    if (phase_ == YakouPhase::RaidResponse)
    {
        result["RaidResponseDepth"] = response_.StackSize();
    }
    if (phase_ == YakouPhase::RaidResponse || phase_ == YakouPhase::SpellResponse)
    {
        Json stack = Json::array();
        for (auto const& link : response_.ServerState().Stack)
        {
            // Activated response cards have already moved to a public graveyard.
            // Targets can include private deck choices and are never projected.
            stack.push_back({{"Controller", link.Controller}, {"Card", visibleCard(link.SourceInstance)}});
        }
        result["ResponseStack"] = std::move(stack);
        Json options = Json::array();
        if (response_.IsOpen() && response_.PriorityPlayer() == viewer)
        {
            for (int instance : Player(viewer).Hand)
            {
                int const definition = OriginalId(instance);
                char const* type = nullptr;
                if (phase_ == YakouPhase::RaidResponse && viewer == ActivePlayer() &&
                    definition == 3017 && !chainTargetedByEnemySpell_.empty() &&
                    !Player(viewer).Graveyard.empty() &&
                    response_.StackSize() < MaximumRaidResponseLinks)
                {
                    type = "RespondWithEffectProtection";
                }
                else if (phase_ == YakouPhase::RaidResponse && viewer != ActivePlayer() &&
                    response_.StackSize() < MaximumRaidResponseLinks)
                {
                    type = (definition == 3016 || definition == 4018) ? "RespondWithRaidShield"
                        : (definition == 2011 || definition == 3018 || definition == 3019 ||
                           definition == 4017)
                            ? "RespondWithRaidTactic" : nullptr;
                }
                else if (phase_ == YakouPhase::SpellResponse &&
                         response_.StackSize() < MaximumSpellResponseLinks &&
                         (definition == 3020 || definition == 4020))
                {
                    type = "RespondWithNegation";
                }
                if (definition == 4018 &&
                    std::none_of(attacks_.begin(), attacks_.end(), [&](YakouAttack const& attack) {
                        return attack.Blocker > 0 &&
                            FindUnit(Player(viewer), attack.Blocker) != nullptr;
                    }))
                {
                    type = nullptr;
                }
                if (type != nullptr)
                {
                    Json targets = Json::array();
                    Json replacements = Json::array();
                    Json costs = Json::array();
                    if (definition == 3016 || definition == 3018 || definition == 4017)
                    {
                        for (auto const& unit : Player(viewer).Field)
                        {
                            if (!unit || unit->Hp() <= 0) continue;
                            if (definition == 3016)
                            {
                                targets.push_back(unit->InstanceId);
                                continue;
                            }
                            // A replacement must have spare block capacity and
                            // differ from the current blocker of at least one
                            // assigned raid. Keep the server's per-raid validation
                            // authoritative for the selected target/replacement pair.
                            int assigned = 0;
                            bool canReplace = false;
                            for (auto const& attack : attacks_)
                            {
                                if (attack.Blocker == unit->InstanceId) ++assigned;
                                if (attack.Blocker > 0 &&
                                    attack.Blocker != unit->InstanceId)
                                {
                                    canReplace = true;
                                }
                            }
                            if (canReplace && assigned < unit->MaxBlocks)
                            {
                                replacements.push_back(unit->InstanceId);
                            }
                        }
                    }
                    if (definition == 2011 || definition == 3018 || definition == 3019 ||
                        definition == 4017)
                    {
                        for (auto const& attack : attacks_)
                        {
                            // Blocker replacement only applies to raids that
                            // already have an assigned, still-present blocker.
                            if ((definition == 3018 || definition == 4017) &&
                                (attack.Blocker <= 0 ||
                                 FindUnit(Player(viewer), attack.Blocker) == nullptr))
                            {
                                continue;
                            }
                            targets.push_back(attack.Card);
                        }
                    }
                    if (definition == 3017)
                    {
                        for (int target : chainTargetedByEnemySpell_)
                        {
                            if (FindUnit(Player(viewer), target) != nullptr)
                            {
                                targets.push_back(target);
                            }
                        }
                    }
                    if (definition == 2011 || definition == 3018 || definition == 3020 ||
                        definition == 4017)
                    {
                        for (int cost : Player(viewer).Hand)
                        {
                            if (cost != instance) costs.push_back(cost);
                        }
                    }
                    else if (definition == 3019 || definition == 3017 || definition == 4020)
                    {
                        costs = Player(viewer).Graveyard;
                    }
                    int const count = definition == 3020 || definition == 3019 ||
                        definition == 4020 || definition == 4017 ? 2
                        : definition == 2011 || definition == 3018 || definition == 3017 ? 1 : 0;
                    if (costs.size() < static_cast<std::size_t>(count) ||
                        (definition == 3017 && targets.empty()) ||
                        (definition == 3016 && targets.empty()) ||
                        ((definition == 3018 || definition == 4017) &&
                         (targets.empty() || replacements.empty())))
                    {
                        continue;
                    }
                    // Candidates are filtered for presentation; the command still
                    // validates target combinations, costs and usage limits.
                    options.push_back({{"CardId", instance}, {"Type", type},
                                       {"TargetCandidates", std::move(targets)},
                                       {"ReplacementCandidates", std::move(replacements)},
                                       {"CostCandidates", std::move(costs)}, {"CostCount", count}});
                }
            }
            if (phase_ == YakouPhase::RaidResponse && viewer == ActivePlayer() &&
                response_.StackSize() < MaximumRaidResponseLinks)
            {
                for (auto const& attack : attacks_)
                {
                    auto const* unit = FindUnit(Player(viewer), attack.Card);
                    if (unit == nullptr || unit->DefinitionId != 2101 ||
                        piercingRaiders_.contains(attack.Card))
                    {
                        continue;
                    }
                    auto const stacked = response_.ServerState().Stack;
                    if (std::any_of(stacked.begin(), stacked.end(), [&](YakouChainLink const& link)
                        { return link.SourceInstance == attack.Card && link.Trigger == "OnRaid"; }))
                    {
                        continue;
                    }
                    Json costs = Json::array();
                    for (int candidate : Player(viewer).Graveyard)
                    {
                        if (HasArchetype(OriginalId(candidate), "HyakkiYagyo"))
                        {
                            costs.push_back(candidate);
                        }
                    }
                    if (!costs.empty())
                    {
                        options.push_back({{"CardId", attack.Card}, {"Type", "RespondWithPiercing"},
                                           {"TargetCandidates", Json::array()},
                                           {"ReplacementCandidates", Json::array()},
                                           {"CostCandidates", std::move(costs)}, {"CostCount", 1}});
                    }
                }
            }
        }
        result["ResponseOptions"] = std::move(options);
    }
    // The pending queue can include a hidden hand card or deck-top choice.
    // Never expose its source, candidate IDs or target requirements to the
    // opposing viewer. The controlling player may request the first choice.
    if (!pendingEffects_.empty() && pendingEffects_.front().Player == viewer)
    {
        auto const& pending = pendingEffects_.front();
        Json choice = {{"SourceCardId", pending.SourceCard},
                       {"EffectId", pending.EffectId},
                       {"Trigger", pending.Trigger}};
        try
        {
            auto const requested = PendingEffectChoices(viewer);
            choice["Status"] = "Ready";
            choice["Operation"] = requested.Operation;
            choice["SourceZone"] = requested.SourceZone;
            choice["MinCount"] = requested.MinCount;
            choice["MaxCount"] = requested.MaxCount;
            choice["Candidates"] = requested.Candidates;
            if (!requested.AvailableSlots.empty())
            {
                choice["AvailableSlots"] = requested.AvailableSlots;
            }
            // Only the choice owner may see the definitions of cards revealed
            // from a hidden zone. Never include these in the other projection.
            if (requested.SourceZone == "FriendlyDeckTop")
            {
                Json cards = Json::array();
                for (int instance : requested.Candidates)
                {
                    cards.push_back(visibleCard(instance));
                }
                choice["CandidateCards"] = std::move(cards);
            }
        }
        catch (std::invalid_argument const&)
        {
            // A future unsupported effect must remain explicitly unresolved,
            // without failing a viewer snapshot or claiming it is actionable.
            choice["Status"] = "Unavailable";
        }
        result["PendingSelection"] = std::move(choice);
    }
    return result;
}

Json YakouMatch::ServerCanonicalState() const
{
    Json players = Json::array();
    for (auto const& player : players_)
    {
        Json field = Json::array();
        for (auto const& entry : player.Field)
        {
            if (!entry)
            {
                field.push_back(nullptr);
                continue;
            }
            auto const& unit = *entry;
            field.push_back({{"Instance", unit.InstanceId}, {"Definition", unit.DefinitionId},
                             {"BaseAttack", unit.BaseAttack}, {"BaseMaxHp", unit.BaseMaxHp},
                             {"BonusAttack", unit.BonusAttack}, {"BonusMaxHp", unit.BonusMaxHp},
                             {"Damage", unit.Damage}, {"Countdown", unit.Countdown},
                             {"WasDevourSummoned", unit.WasDevourSummoned}, {"Manifested", unit.Manifested},
                             {"PostponedThisTurn", unit.PostponedThisTurn},
                             {"BaseMaxBlocks", unit.BaseMaxBlocks}, {"MaxBlocks", unit.MaxBlocks}});
        }
        players.push_back({{"Id", player.Id}, {"Life", player.Life}, {"Ki", player.Ki},
                           {"NormalSummons", player.NormalSummons}, {"Manifestations", player.Manifestations},
                           {"Redraws", player.Redraws}, {"Deck", player.Deck}, {"Hand", player.Hand},
                           {"Graveyard", player.Graveyard}, {"Exile", player.Exile}, {"Field", std::move(field)}});
        if (catalog_->Config.contains("resource.definitions"))
            players.back()["Resources"] = PublicResources(player);
    }
    Json original = Json::array();
    std::vector<std::pair<int, int>> definitions(original_.begin(), original_.end());
    std::sort(definitions.begin(), definitions.end());
    for (auto const& [instance, definition] : definitions)
    {
        original.push_back(Json::array({instance, definition}));
    }
    Json attacks = Json::array();
    for (auto const& attack : attacks_)
    {
        attacks.push_back(Json::array({attack.Card, attack.Slot, attack.Blocker}));
    }
    Json pending = Json::array();
    for (auto const& effect : pendingEffects_)
    {
        pending.push_back({{"Player", effect.Player}, {"SourceCard", effect.SourceCard},
                           {"Definition", effect.DefinitionId}, {"Effect", effect.EffectId},
                           {"Trigger", effect.Trigger}, {"RelatedCard", effect.RelatedCard},
                           {"PaidCostCards", effect.PaidCostCards}});
        if (effect.DeferredCost)
            pending.back()["DeferredCost"] = true;
    }
    Json deferredCosts = Json::array();
    for (auto const& effect : deferredCostEffects_)
    {
        deferredCosts.push_back({{"Player", effect.Player}, {"SourceCard", effect.SourceCard},
                                 {"Definition", effect.DefinitionId}, {"Effect", effect.EffectId},
                                 {"Trigger", effect.Trigger}, {"RelatedCard", effect.RelatedCard},
                                 {"PaidCostCards", effect.PaidCostCards}});
        if (effect.DeferredCost)
            deferredCosts.back()["DeferredCost"] = true;
    }
    Json uses = Json::array();
    for (auto const& [key, count] : effectUses_)
    {
        uses.push_back(Json::array({std::get<0>(key), std::get<1>(key), std::get<2>(key), std::get<3>(key), count}));
    }
    auto const chain = response_.ServerState();
    auto links = [](std::vector<YakouChainLink> const& values)
    {
        Json result = Json::array();
        for (auto const& link : values)
        {
            result.push_back({{"Controller", link.Controller}, {"SourceInstance", link.SourceInstance},
                              {"EffectId", link.EffectId}, {"Trigger", link.Trigger}, {"Targets", link.Targets}});
        }
        return result;
    };
    Json state = {{"Schema", 1}, {"CatalogCards", catalog_->Cards}, {"Config", catalog_->Config},
            {"Players", std::move(players)}, {"OriginalDefinitions", std::move(original)},
            {"NextInstance", nextInstance_}, {"Active", active_}, {"First", first_},
            {"Turn", turn_}, {"Phase", static_cast<int>(phase_)}, {"Winner", winner_},
            {"Attacks", std::move(attacks)}, {"PendingEffects", std::move(pending)},
            {"EffectUses", std::move(uses)}, {"Journal", journal_},
            {"Chain", {{"Open", chain.Open}, {"Priority", chain.Priority}, {"Passes", chain.ConsecutivePasses},
                       {"Stack", links(chain.Stack)}, {"Resolution", links(chain.Resolution)},
                       {"Deferred", links(chain.Deferred)}}}};
    if (!deferredCosts.empty())
        state["DeferredCostEffects"] = std::move(deferredCosts);
    // Preserve the existing canonical replay fixture whenever no raid shield
    // is pending; include live reductions in the private hash when present.
    if (!raidShields_.empty())
    {
        Json shields = Json::array();
        for (auto const& [instance, amount] : raidShields_)
            shields.push_back(Json::array({instance, amount}));
        state["RaidShields"] = std::move(shields);
    }
    if (!nextDamageShields_.empty())
    {
        Json shields = Json::array();
        for (auto const& [instance, amount] : nextDamageShields_)
        {
            shields.push_back(Json::array({instance, amount}));
        }
        state["NextDamageShields"] = std::move(shields);
    }
    if (manifestConditionWaivers_[0] != 0 || manifestConditionWaivers_[1] != 0)
    {
        state["ManifestConditionWaivers"] = manifestConditionWaivers_;
    }
    if (!raidAttackModifiers_.empty())
    {
        Json modifiers = Json::array();
        for (auto const& [instance, delta] : raidAttackModifiers_)
            modifiers.push_back(Json::array({instance, delta}));
        state["RaidAttackModifiers"] = std::move(modifiers);
    }
    if (!piercingRaiders_.empty())
    {
        state["PiercingRaiders"] = piercingRaiders_;
    }
    if (!chainTargetedByEnemySpell_.empty())
    {
        state["ChainTargetedByEnemySpell"] = chainTargetedByEnemySpell_;
    }
    if (!chainEffectDamageReduction_.empty())
    {
        state["ChainEffectDamageReduction"] = chainEffectDamageReduction_;
    }
    if (!chainEffectDamagePrevention_.empty())
    {
        state["ChainEffectDamagePrevention"] = chainEffectDamagePrevention_;
    }
    return state;
}

std::uint64_t YakouMatch::ServerStateHash() const
{
    // FNV-1a is a deterministic replay fingerprint, not an authentication hash.
    constexpr std::uint64_t offset = 14695981039346656037ULL;
    constexpr std::uint64_t prime = 1099511628211ULL;
    std::uint64_t hash = offset;
    for (unsigned char byte : ServerCanonicalState().dump())
    {
        hash ^= byte;
        hash *= prime;
    }
    return hash;
}

YakouPlayer& YakouMatch::MutablePlayer(int id)
{
    Check(id == 1 || id == 2, "Unknown player");
    return players_[id - 1];
}
YakouUnit& YakouMatch::FieldUnit(YakouPlayer& player, int instance)
{
    for (auto& slot : player.Field)
        if (slot && slot->InstanceId == instance) return *slot;
    throw std::invalid_argument("Monster not found on own field");
}
bool YakouMatch::HasArchetype(int definition, std::string const& archetype) const
{
    auto const data = CardDefinition(definition);
    auto const& archetypes = data.at("Archetypes");
    return std::any_of(archetypes.begin(), archetypes.end(),
                       [&](Json const& tag) { return tag == archetype; });
}
void YakouMatch::RequireMain(int player) const
{
    Check(phase_ == YakouPhase::Main, "Not in Yakou main phase");
    Check(player == ActivePlayer(), "Not active player");
}
void YakouMatch::Enqueue(int player, int instance, int definition, std::string const& trigger,
                         int relatedCard, std::vector<int> paidCostCards)
{
    auto const data = CardDefinition(definition);
    for (auto const& effect : data.at("Effects"))
    {
        if (effect.at("Trigger") != trigger)
        {
            continue;
        }
        auto const& actions = effect.at("Actions");
        if (actions.is_array() && actions.size() == 1 && actions[0].is_object() &&
            actions[0].value("Operation", std::string("")) == "Salvage" &&
            actions[0].contains("Condition"))
        {
            // This authored condition is checked at trigger registration. Other
            // condition shapes remain queued and fail closed at resolution.
            auto const& action = actions[0];
            auto const& condition = action.at("Condition");
            if (condition.is_object() && condition.contains("GraveyardArchetypeCountAtLeast"))
            {
                CheckSupportedAction(action);
                auto const archetype = condition.contains("Archetype")
                    ? condition.at("Archetype").get<std::string>()
                    : action.at("RequiredArchetype").get<std::string>();
                int tally = 0;
                for (int card : Player(player).Graveyard)
                {
                    if (HasArchetype(OriginalId(card), archetype))
                    {
                        ++tally;
                    }
                }
                if (tally < condition.at("GraveyardArchetypeCountAtLeast").get<int>())
                {
                    continue;
                }
            }
            else if (condition.is_object() && condition.contains("GraveyardCardTypeCountAtLeast"))
            {
                CheckSupportedAction(action);
                int tally = 0;
                for (int card : Player(player).Graveyard)
                {
                    if (CardDefinition(OriginalId(card)).at("Type") == condition.at("CardType"))
                    {
                        ++tally;
                    }
                }
                if (tally < condition.at("GraveyardCardTypeCountAtLeast").get<int>())
                {
                    continue;
                }
            }
        }
        pendingEffects_.push_back({player, instance, definition,
                                   effect.at("Id").get<int>(), trigger, relatedCard,
                                   paidCostCards});
    }
}

void YakouMatch::DeferPendingCostTriggers(std::size_t firstNewEffect)
{
    Check(firstNewEffect <= pendingEffects_.size(), "Invalid cost trigger boundary");
    // Ordinary pending effects may predate a raid response. Only the effects
    // enqueued by this specific payment are deferred to the end of the chain.
    for (std::size_t i = firstNewEffect; i < pendingEffects_.size(); ++i)
    {
        pendingEffects_[i].DeferredCost = true;
        deferredCostEffects_.push_back(std::move(pendingEffects_[i]));
    }
    pendingEffects_.resize(firstNewEffect);
}

void YakouMatch::ReleaseDeferredCostTriggers()
{
    for (auto& effect : deferredCostEffects_)
        pendingEffects_.push_back(std::move(effect));
    deferredCostEffects_.clear();
}

YakouEffectChoice YakouMatch::PendingEffectChoices(int player) const
{
    Check(player == 1 || player == 2, "Unknown effect controller");
    Check(!pendingEffects_.empty(), "No queued Yakou effect");
    auto const& queued = pendingEffects_.front();
    Check(queued.Player == player, "Only the effect controller may inspect its candidates");
    auto const card = CardDefinition(queued.DefinitionId);
    Json const* definition = nullptr;
    for (auto const& effect : card.at("Effects"))
    {
        if (effect.at("Id") == queued.EffectId && effect.at("Trigger") == queued.Trigger)
        {
            definition = &effect;
            break;
        }
    }
    Check(definition != nullptr && definition->at("Type") == "Triggered",
          "Queued Yakou effect definition missing or unsupported");
    int const limit = definition->value("PerTurnLimit", 0);
    if (limit > 0)
    {
        Check(definition->value("LimitScope", std::string("")) == "CardName",
              "Unsupported Yakou trigger limit scope");
        auto const usage = std::make_tuple(turn_, player, card.at("NameKey").get<std::string>(), queued.EffectId);
        auto const found = effectUses_.find(usage);
        Check(found == effectUses_.end() || found->second < limit,
              "Card-name effect turn limit reached");
    }
    auto const& actions = definition->at("Actions");
    Check(actions.is_array() && (actions.size() == 1 || IsMillThenCountdown(actions)),
          "Unsupported multi-action or empty Yakou effect");
    bool const millThenCountdown = IsMillThenCountdown(actions);
    auto const& action = actions.at(millThenCountdown ? 1 : 0);
    CheckSupportedAction(action);
    YakouEffectChoice choice;
    choice.Operation = action.at("Operation").get<std::string>();
    auto const& owner = Player(player);
    if (millThenCountdown)
    {
        Check(!owner.Deck.empty(), "Insufficient cards for authored mill");
    }
    if (choice.Operation == "Mill")
    {
        Check(action.at("From") == "FriendlyDeck" && action.at("To") == "Graveyard" &&
                  action.at("Count").get<int>() > 0 && action.at("Count").get<int>() <= 30,
              "Unsupported mill shape");
        Check(owner.Deck.size() >= static_cast<std::size_t>(action.at("Count").get<int>()),
              "Insufficient cards for authored mill");
        return choice;
    }
    if (choice.Operation == "ModifyResource")
    {
        auto const definitions = YakouReadResourceDefinitions(catalog_->Config);
        Check(definitions.contains(action.at("ResourceId").get<std::string>()), "Unknown resource gain");
        return choice;
    }
    if (choice.Operation == "IgnoreOneManifestCondition")
    {
        return choice;
    }
    if (choice.Operation == "LookAndChoose")
    {
        Check(action.at("From") == "FriendlyDeckTop" && action.at("ChooseCount") == 1 &&
                  action.at("ChosenDestination") == "Hand" && action.at("RestDestination") == "DeckBottom",
              "Unsupported look-and-choose shape");
        int const count = action.at("LookCount").get<int>();
        Check(count > 0 && count <= 30 && owner.Deck.size() >= static_cast<std::size_t>(count),
              "Insufficient cards for look-and-choose");
        choice.SourceZone = "FriendlyDeckTop";
        choice.MinCount = choice.MaxCount = 1;
        choice.Candidates.assign(owner.Deck.begin(), owner.Deck.begin() + count);
        return choice;
    }
    if (choice.Operation == "Salvage")
    {
        Check(action.at("From") == "FriendlyGraveyard" && action.at("To") == "Hand" &&
                  action.at("Count") == 1,
              "Unsupported salvage shape");
        choice.SourceZone = "FriendlyGraveyard";
        for (int instance : owner.Graveyard)
        {
            if (action.value("ExcludePaidCostCards", false) &&
                Contains(queued.PaidCostCards, instance))
            {
                continue;
            }
            auto const definitionId = OriginalId(instance);
            auto const candidate = CardDefinition(definitionId);
            if (action.contains("CardType") && candidate.at("Type") != action.at("CardType"))
            {
                continue;
            }
            if (action.contains("RequiredArchetype") &&
                !HasArchetype(definitionId, action.at("RequiredArchetype").get<std::string>()))
            {
                continue;
            }
            choice.Candidates.push_back(instance);
        }
    }
    else if (choice.Operation == "SpecialSummon")
    {
        choice.SourceZone = "FriendlyGraveyard";
        for (int instance : owner.Graveyard)
        {
            if (action.value("ExcludePaidCostCards", false) &&
                Contains(queued.PaidCostCards, instance))
            {
                continue;
            }
            int const definitionId = OriginalId(instance);
            auto const candidate = CardDefinition(definitionId);
            if (candidate.at("Type") == action.at("CardType") &&
                candidate.at("Attack").get<int>() <= action.at("MaxPrintedAttack").get<int>() &&
                HasArchetype(definitionId, action.at("RequiredArchetype").get<std::string>()))
            {
                choice.Candidates.push_back(instance);
            }
        }
        for (int slot = 0; slot < static_cast<int>(owner.Field.size()); ++slot)
        {
            if (!owner.Field[slot])
            {
                choice.AvailableSlots.push_back(slot);
            }
        }
        Check(!choice.AvailableSlots.empty(), "No open slot for authored special summon");
    }
    else
    {
        Check(choice.Operation == "ModifyCountdown" || choice.Operation == "SetCountdown" ||
                  choice.Operation == "HealAccumulatedDamage" || choice.Operation == "ReduceDamage",
              "Unsupported Yakou effect operation: " + choice.Operation);
        auto const targetMode = action.at("Target").get<std::string>();
        Check(targetMode == "Self" || targetMode == "FriendlyMonster" || targetMode == "OtherFriendlyMonster" ||
                  targetMode == "DevouringMonster" ||
                  (choice.Operation == "ModifyCountdown" && targetMode == "DistinctFriendlyMonsters"),
              "Unsupported friendly monster target mode");
        if (targetMode == "Self" || targetMode == "DevouringMonster")
        {
            int const automaticTarget = targetMode == "Self" ? queued.SourceCard : queued.RelatedCard;
            Check(automaticTarget > 0 && FindUnit(owner, automaticTarget) != nullptr,
                  "Automatic target must remain on the friendly field");
            return choice;
        }
        choice.SourceZone = "FriendlyField";
        for (auto const& field : owner.Field)
        {
            if (field && (targetMode != "OtherFriendlyMonster" || field->InstanceId != queued.SourceCard) &&
                (choice.Operation != "HealAccumulatedDamage" || queued.DefinitionId != 4015 ||
                 field->Damage > 0))
            {
                choice.Candidates.push_back(field->InstanceId);
            }
        }
    }
    choice.MaxCount = action.value("MaxTargets", 1);
    choice.MinCount = action.value("MinTargets", action.value("Optional", false) ? 0 : 1);
    return choice;
}

void YakouMatch::ResolvePendingEffect(int player, int selectedTarget)
{
    ResolvePendingEffect(player, selectedTarget == 0 ? std::vector<int>{}
                                                     : std::vector<int>{selectedTarget});
}

void YakouMatch::ResolvePendingEffect(int player, std::vector<int> const& selectedTargets)
{
    ResolvePendingEffect(player, selectedTargets, -1);
}

void YakouMatch::ResolvePendingEffect(int player, std::vector<int> const& selectedTargets, int slot)
{
    Check(player == 1 || player == 2, "Unknown effect controller");
    Check(!pendingEffects_.empty(), "No queued Yakou effect");
    auto const queued = pendingEffects_.front();
    Check(queued.Player == player, "Only the effect controller may choose its target");

    auto const card = CardDefinition(queued.DefinitionId);
    Json const* definition = nullptr;
    for (auto const& effect : card.at("Effects"))
    {
        if (effect.at("Id") == queued.EffectId && effect.at("Trigger") == queued.Trigger)
        {
            definition = &effect;
            break;
        }
    }
    Check(definition != nullptr, "Queued Yakou effect definition missing");
    Check(definition->at("Type") == "Triggered", "Only triggered effects can resolve");
    auto const& actions = definition->at("Actions");
    Check(actions.is_array() && (actions.size() == 1 || IsMillThenCountdown(actions)),
          "Unsupported multi-action or empty Yakou effect");
    bool const millThenCountdown = IsMillThenCountdown(actions);
    auto const& action = actions.at(millThenCountdown ? 1 : 0);
    // Validate the complete authored shape before changing any state.
    CheckSupportedAction(action);
    auto const operation = action.at("Operation").get<std::string>();
    bool const distinct = operation == "ModifyCountdown" &&
        action.value("Target", std::string()) == "DistinctFriendlyMonsters";
    Check(distinct || selectedTargets.size() <= 1, "Effect accepts only one target");
    int const selectedTarget = selectedTargets.empty() ? 0 : selectedTargets.front();
    Check(operation == "SpecialSummon" || slot == -1,
          "Only a special summon may choose a field slot");

    auto const usage = std::make_tuple(turn_, player, card.at("NameKey").get<std::string>(), queued.EffectId);
    int const limit = definition->value("PerTurnLimit", 0);
    if (limit > 0)
    {
        Check(definition->value("LimitScope", std::string("")) == "CardName",
              "Unsupported Yakou trigger limit scope");
        auto const it = effectUses_.find(usage);
        Check(it == effectUses_.end() || it->second < limit,
              "Card-name effect turn limit reached");
    }

    // Compute the complete mutation on a candidate, including its journal
    // and queue, then commit only after all validation succeeds.
    YakouMatch next = *this;
    auto& owner = next.MutablePlayer(player);
    bool declined = false;
    if (operation == "ModifyResource")
    {
        Check(selectedTarget == 0, "Resource gain is targetless");
        auto const resource = action.at("ResourceId").get<std::string>();
        Check(YakouReadResourceDefinitions(catalog_->Config).contains(resource), "Unknown resource gain");
        ChangeResource(owner, resource, action.at("Delta").get<int>());
        next.journal_.push_back("EffectResource:" + resource + ":" + std::to_string(player));
    }
    else if (operation == "IgnoreOneManifestCondition")
    {
        Check(selectedTarget == 0, "Manifest condition waiver has no card target");
        Check(next.manifestConditionWaivers_[player - 1] < std::numeric_limits<int>::max(),
              "Too many manifest condition waivers");
        ++next.manifestConditionWaivers_[player - 1];
        next.journal_.push_back("ManifestConditionWaiver:" + std::to_string(player));
    }
    else if (operation == "Mill")
    {
        Check(action.at("From") == "FriendlyDeck" &&
                  action.at("To") == "Graveyard", "Unsupported mill zones");
        Check(selectedTarget == 0, "Mill does not accept a target");
        int const count = action.at("Count").get<int>();
        Check(count > 0 && count <= 30, "Invalid mill count");
        Check(owner.Deck.size() >= static_cast<std::size_t>(count),
              "Insufficient cards for authored mill");
        for (int i = 0; i < count; ++i)
        {
            owner.Graveyard.push_back(owner.Deck.front());
            owner.Deck.erase(owner.Deck.begin());
        }
        next.journal_.push_back("EffectMill:" + std::to_string(queued.SourceCard) +
                                ":" + std::to_string(count));
    }
    else if (operation == "LookAndChoose")
    {
        Check(action.at("From") == "FriendlyDeckTop" && action.at("ChooseCount") == 1 &&
                  action.at("ChosenDestination") == "Hand" && action.at("RestDestination") == "DeckBottom",
              "Unsupported look-and-choose shape");
        int const count = action.at("LookCount").get<int>();
        Check(count > 0 && count <= 30 && owner.Deck.size() >= static_cast<std::size_t>(count),
              "Insufficient cards for look-and-choose");
        auto const end = owner.Deck.begin() + count;
        Check(std::find(owner.Deck.begin(), end, selectedTarget) != end,
              "Selected card must be among the authorized deck-top cards");
        std::vector<int> viewed(owner.Deck.begin(), end);
        owner.Deck.erase(owner.Deck.begin(), end);
        for (int instance : viewed)
        {
            if (instance == selectedTarget)
            {
                owner.Hand.push_back(instance);
            }
            else
            {
                owner.Deck.push_back(instance);
            }
        }
        // The journal contains a public count, never the identities of looked-at cards.
        next.journal_.push_back("EffectLookAndChoose:" + std::to_string(queued.SourceCard) +
                                ":" + std::to_string(count));
    }
    else if (operation == "Salvage")
    {
        Check(action.at("From") == "FriendlyGraveyard" && action.at("To") == "Hand" &&
                  action.at("Count") == 1,
              "Unsupported salvage shape");
        bool const optional = action.value("Optional", false);
        if (selectedTarget == 0 && optional)
        {
            declined = true;
            next.journal_.push_back("EffectDeclined:" + std::to_string(queued.SourceCard));
        }
        else
        {
            Check(selectedTarget > 0 && Contains(owner.Graveyard, selectedTarget),
                  "Salvage target must be in the controller's graveyard");
            Check(!action.value("ExcludePaidCostCards", false) ||
                      !Contains(queued.PaidCostCards, selectedTarget),
                  "Paid cost card cannot be recovered by this effect");
            auto const recovered = next.CardDefinition(next.OriginalId(selectedTarget));
            if (action.contains("CardType"))
            {
                Check(recovered.at("Type") == action.at("CardType"),
                      "Salvage target has the wrong card type");
            }
            if (action.contains("RequiredArchetype"))
            {
                Check(next.HasArchetype(next.OriginalId(selectedTarget),
                                        action.at("RequiredArchetype").get<std::string>()),
                      "Salvage target has the wrong archetype");
            }
            Remove(owner.Graveyard, selectedTarget);
            owner.Hand.push_back(selectedTarget);
            next.journal_.push_back("EffectSalvage:" + std::to_string(selectedTarget));
        }
    }
    else if (operation == "SpecialSummon")
    {
        Check(selectedTarget > 0 && Contains(owner.Graveyard, selectedTarget) &&
                  slot >= 0 && slot < static_cast<int>(owner.Field.size()) &&
                  !owner.Field[slot],
              "Special summon needs an authorized graveyard monster and empty slot");
        Check(!action.value("ExcludePaidCostCards", false) ||
                  !Contains(queued.PaidCostCards, selectedTarget),
              "Paid cost card cannot be special summoned");
        int const definitionId = next.OriginalId(selectedTarget);
        auto const summoned = next.CardDefinition(definitionId);
        Check(summoned.at("Type") == action.at("CardType") &&
                  summoned.at("Attack").get<int>() <= action.at("MaxPrintedAttack").get<int>() &&
                  next.HasArchetype(definitionId, action.at("RequiredArchetype").get<std::string>()),
              "Special summon target does not satisfy the authored filter");
        int const maxBlocks = PrintedMaxBlocks(summoned);
        Remove(owner.Graveyard, selectedTarget);
        owner.Field[slot] = YakouUnit{.InstanceId = selectedTarget,
                                     .DefinitionId = definitionId,
                                     .BaseAttack = summoned.at("Attack").get<int>(),
                                     .BaseMaxHp = summoned.at("MaxHp").get<int>(),
                                     .Countdown = summoned.at("AttackInterval").get<int>(),
                                     .BaseMaxBlocks = maxBlocks,
                                     .MaxBlocks = maxBlocks};
        next.Enqueue(player, selectedTarget, definitionId, "OnSummoned");
        next.journal_.push_back("EffectSpecialSummon:" + std::to_string(selectedTarget) +
                                ":" + std::to_string(slot));
    }
    else
    {
        auto const targetMode = action.at("Target").get<std::string>();
        if (distinct)
        {
            Check(selectedTargets.size() >= 1 && selectedTargets.size() <= 2 &&
                      selectedTargets[0] != selectedTargets.back() || selectedTargets.size() == 1,
                  "Countdown effect needs one or two distinct targets");
            int const delta = action.at("Delta").get<int>();
            for (int target : selectedTargets)
            {
                int const slot = FindSlot(owner, target);
                Check(slot >= 0, "Countdown target must be a friendly field monster");
                auto& unit = *owner.Field[slot];
                auto const adjusted = static_cast<std::int64_t>(unit.Countdown) + delta;
                Check(adjusted <= std::numeric_limits<int>::max(), "Countdown overflow");
                unit.Countdown = static_cast<int>(std::max<std::int64_t>(0, adjusted));
                next.journal_.push_back("EffectCountdown:" + std::to_string(target) +
                                        ":" + std::to_string(delta));
            }
        }
        else
        {
        Check(targetMode == "Self" || targetMode == "FriendlyMonster" ||
                  targetMode == "OtherFriendlyMonster" || targetMode == "DevouringMonster",
              "Unsupported friendly monster target mode");
        bool const automatic = targetMode == "Self" || targetMode == "DevouringMonster";
        int const target = targetMode == "Self" ? queued.SourceCard
            : targetMode == "DevouringMonster" ? queued.RelatedCard : selectedTarget;
        if (automatic)
        {
            Check(target > 0 && (selectedTarget == 0 || selectedTarget == target),
                  "Automatic effect cannot select a different monster");
        }
        bool const optional = action.value("Optional", false);
        if (target == 0 && optional && targetMode != "Self")
        {
            declined = true;
            next.journal_.push_back("EffectDeclined:" +
                                    std::to_string(queued.SourceCard));
        }
        else
        {
            Check(target > 0 && (targetMode != "OtherFriendlyMonster" ||
                                  target != queued.SourceCard),
                  "Missing or prohibited countdown target");
            int const slot = FindSlot(owner, target);
            Check(slot >= 0, "Countdown target must be a friendly field monster");
            if (millThenCountdown)
            {
                Check(!owner.Deck.empty(), "Insufficient cards for authored mill");
                owner.Graveyard.push_back(owner.Deck.front());
                owner.Deck.erase(owner.Deck.begin());
                next.journal_.push_back("EffectMill:" + std::to_string(queued.SourceCard) + ":1");
            }
            auto& unit = *owner.Field[slot];
            if (operation == "ReduceDamage")
            {
                int const amount = action.at("Amount").get<int>();
                int& shield = next.nextDamageShields_[target];
                Check(amount > 0 && shield <= std::numeric_limits<int>::max() - amount,
                      "Next damage shield overflow");
                shield += amount;
                next.journal_.push_back("EffectNextDamageShield:" +
                                        std::to_string(target) + ":" + std::to_string(amount));
            }
            else if (operation == "HealAccumulatedDamage")
            {
                Check(queued.DefinitionId != 4015 || unit.Damage > 0,
                      "Healing target has no accumulated damage");
                int const amount = action.at("Amount").get<int>();
                Check(amount > 0, "Invalid healing amount");
                unit.Damage = std::max(0, unit.Damage - amount);
                next.journal_.push_back("EffectHeal:" + std::to_string(target) +
                                        ":" + std::to_string(amount));
            }
            else if (operation == "SetCountdown")
            {
                int const value = action.at("Value").get<int>();
                Check(value >= 0, "Invalid countdown value");
                unit.Countdown = value;
                next.journal_.push_back("EffectSetCountdown:" + std::to_string(target) +
                                        ":" + std::to_string(value));
            }
            else
            {
                int const delta = action.at("Delta").get<int>();
                auto const adjusted = static_cast<std::int64_t>(unit.Countdown) + delta;
                Check(adjusted <= std::numeric_limits<int>::max(), "Countdown overflow");
                unit.Countdown = static_cast<int>(std::max<std::int64_t>(0, adjusted));
                next.journal_.push_back("EffectCountdown:" + std::to_string(target) +
                                        ":" + std::to_string(delta));
            }
        }
        }
    }
    if (limit > 0 && !declined)
    {
        ++next.effectUses_[usage];
    }
    next.pendingEffects_.erase(next.pendingEffects_.begin());
    *this = std::move(next);
}

int YakouMatch::ResolveAutomaticTriggers(int maxEffects)
{
    Check(maxEffects > 0 && maxEffects <= 128,
          "Automatic trigger drain must have a bounded positive budget");
    Check(!response_.IsOpen(), "Cannot drain triggers during an open response window");
    // The existing interpreter already applies each effect transactionally.
    // Use an outer copy too: a failure in any later effect rolls back the
    // entire batch, including its journal and per-turn usage counters.
    YakouMatch next = *this;
    int completed = 0;
    while (completed < maxEffects && !next.pendingEffects_.empty())
    {
        auto const& pending = next.pendingEffects_.front();
        // A pending effect's controller determines its private choices. The
        // scheduler may inspect them server-side but cannot choose a target.
        auto const choice = next.PendingEffectChoices(pending.Player);
        if (choice.MinCount != 0 || choice.MaxCount != 0)
        {
            // Optional target selections are still player decisions. No
            // auto-decline and no 'first candidate' shortcuts.
            break;
        }
        next.ResolvePendingEffect(pending.Player);
        ++completed;
    }
    *this = std::move(next);
    return completed;
}

int YakouMatch::SummonAndRunAutomaticTriggers(
    int player, int handInstance, std::string const& method,
    std::vector<int> const& materialIds, int slot, int maxEffects)
{
    YakouMatch next = *this;
    next.Summon(player, handInstance, method, materialIds, slot);
    int const processed = next.ResolveAutomaticTriggers(maxEffects);
    *this = std::move(next);
    return processed;
}

int YakouMatch::ActivateMainSpellAndRunAutomaticTriggers(
    int player, int handInstance, int maxEffects, std::vector<int> const& costs)
{
    YakouMatch next = *this;
    next.ActivateMainSpell(player, handInstance, costs);
    int const processed = next.ResolveAutomaticTriggers(maxEffects);
    *this = std::move(next);
    return processed;
}

int YakouMatch::ResolveSelectedEffectAndRunAutomaticTriggers(
    int player, int selectedTarget, int maxEffects)
{
    return ResolveSelectedEffectsAndRunAutomaticTriggers(
        player, selectedTarget == 0 ? std::vector<int>{} : std::vector<int>{selectedTarget},
        maxEffects);
}

int YakouMatch::ResolveSelectedEffectsAndRunAutomaticTriggers(
    int player, std::vector<int> const& selectedTargets, int maxEffects)
{
    return ResolveSelectedEffectsAndRunAutomaticTriggers(player, selectedTargets, -1, maxEffects);
}

int YakouMatch::ResolveSelectedEffectsAndRunAutomaticTriggers(
    int player, std::vector<int> const& selectedTargets, int slot, int maxEffects)
{
    YakouMatch next = *this;
    next.ResolvePendingEffect(player, selectedTargets, slot);
    int const processed = next.ResolveAutomaticTriggers(maxEffects);
    *this = std::move(next);
    return processed;
}

int YakouMatch::ManifestAndRunAutomaticTriggers(
    int player, int fieldInstance, int manifestationDefinition, int maxEffects)
{
    YakouMatch next = *this;
    next.Manifest(player, fieldInstance, manifestationDefinition);
    int const processed = next.ResolveAutomaticTriggers(maxEffects);
    *this = std::move(next);
    return processed;
}

void YakouMatch::ActivateMainSpell(int player, int handInstance, std::vector<int> const& costs)
{
    YakouMatch next = *this;
    next.ActivateMainSpellImpl(player, handInstance, costs);
    next.ReleaseDeferredCostTriggers();
    *this = std::move(next);
}

void YakouMatch::ActivateMainSpellImpl(int player, int handInstance, std::vector<int> const& costs)
{
    RequireMain(player);
    Check(pendingEffects_.empty(), "Resolve pending Yakou effects before casting a spell");
    auto& owner = MutablePlayer(player);
    Check(Contains(owner.Hand, handInstance), "Spell must be in the controller's hand");
    int const definitionId = OriginalId(handInstance);
    auto const definition = CardDefinition(definitionId);
    Check(definition.at("Type") == "Spell", "Only spells can be activated");
    Check(definition.contains("SummonCostJson") &&
              definition.at("SummonCostJson").is_object() &&
              definition.at("SummonCostJson").contains("Activation") &&
              definition.at("SummonCostJson").at("Activation").is_object(),
          "Spell activation cost is missing");
    auto const& activation = definition.at("SummonCostJson").at("Activation");
    std::string costMethod;
    YakouResourceAmounts resourceCosts;
    if (activation.contains("Costs"))
    {
        auto const typed = YakouParseTypedCost(activation.at("Costs"));
        costMethod = typed.OrdinaryType;
        resourceCosts = typed.Resources;
        Check(costMethod != "Devour" && costs.size() == typed.OrdinaryCount &&
                  CanPayResources(owner, resourceCosts), "Invalid typed spell payment");
        std::set<int> unique;
        for (int cardId : costs)
            Check(cardId != handInstance && unique.insert(cardId).second &&
                      Contains(costMethod == "Discard" ? owner.Hand : owner.Graveyard, cardId),
                  "Invalid or repeated typed spell cost card");
    }
    else if (activation.empty())
    {
        Check(costs.empty(), "This spell does not accept a cost");
    }
    else
    {
        Check(activation.size() == 1, "Unsupported spell activation cost");
        costMethod = activation.begin().key();
        Check((costMethod == "Discard" || costMethod == "Necromancy") &&
                  activation.begin().value().is_number_integer(),
              "Unsupported spell activation cost");
        int const required = activation.begin().value().get<int>();
        Check(required > 0 && required <= 30 && costs.size() == static_cast<std::size_t>(required),
              "Incorrect spell activation cost count");
        std::set<int> unique;
        for (int cardId : costs)
        {
            Check(cardId != handInstance && unique.insert(cardId).second &&
                      Contains(costMethod == "Discard" ? owner.Hand : owner.Graveyard, cardId),
                  "Invalid or repeated spell activation cost card");
        }
    }
    auto const& effects = definition.at("Effects");
    Check(effects.is_array() && effects.size() == 1,
          "Only one supported spell effect may be activated");
    auto const& effect = effects.at(0);
    Check(effect.value("Timing", std::string("Main")) == "Main", "Spell is not a main-phase spell");
    Check(effect.at("Type") == "Triggered" &&
              effect.at("Trigger") == "OnResolve" &&
              effect.at("Actions").is_array() &&
              (effect.at("Actions").size() == 1 || IsMillThenCountdown(effect.at("Actions"))),
          "Unsupported main-phase spell effect");
    if (IsMillThenCountdown(effect.at("Actions")))
    {
        CheckSupportedAction(effect.at("Actions")[0]);
        CheckSupportedAction(effect.at("Actions")[1]);
    }
    else
    {
        CheckSupportedAction(effect.at("Actions")[0]);
    }

    std::size_t const previousPendingCount = pendingEffects_.size();
    for (int paid : costs)
    {
        if (costMethod == "Discard")
        {
            Remove(owner.Hand, paid);
            owner.Graveyard.push_back(paid);
            Enqueue(player, paid, OriginalId(paid), "OnDiscarded");
        }
        else
        {
            Remove(owner.Graveyard, paid);
            owner.Exile.push_back(paid);
            Enqueue(player, paid, OriginalId(paid), "OnBanishedForNecromancyCost");
        }
    }
    DeferPendingCostTriggers(previousPendingCount);

    PayResources(owner, resourceCosts);
    // Move the authored spell into its public graveyard, then queue precisely
    // its OnResolve operation. The outer candidate copy commits only if
    // target availability, the per-turn limit and effect shape are valid.
    Remove(owner.Hand, handInstance);
    owner.Graveyard.push_back(handInstance);
    Enqueue(player, handInstance, definitionId, "OnResolve", 0, costs);
    Check(pendingEffects_.size() == 1 &&
              pendingEffects_.front().SourceCard == handInstance,
          "Spell did not enqueue exactly one effect");
    auto const choices = PendingEffectChoices(player);
    Check(choices.MinCount == 0 || !choices.Candidates.empty() ||
              (choices.MinCount == 0 && choices.MaxCount == 0),
          "Spell has no valid effect target");
    journal_.push_back("SpellActivated:" + std::to_string(handInstance));
}


void YakouMatch::BeginSpellResponse(int player, int handInstance, int targetInstance,
                                    std::vector<int> const& costs)
{
    YakouMatch next = *this;
    next.BeginSpellResponseImpl(player, handInstance, targetInstance, costs);
    *this = std::move(next);
}

void YakouMatch::BeginSpellResponseImpl(int player, int handInstance, int targetInstance,
                                        std::vector<int> const& costs)
{
    // Reuse all authored spell and cost checks; do not expose the provisional
    // effect to the normal resolver before the priority window closes.
    ActivateMainSpellImpl(player, handInstance, costs);
    auto const choice = PendingEffectChoices(player);
    if (choice.MaxCount == 0)
    {
        Check(targetInstance == 0, "Targetless spell cannot select a target");
    }
    else
    {
        Check(targetInstance == 0 || Contains(choice.Candidates, targetInstance),
              "Selected spell target is not an authorized candidate");
    }
    auto const pending = pendingEffects_.front();
    Check(pending.Player == player && pending.SourceCard == handInstance,
          "Unexpected spell effect while opening chain");
    pendingEffects_.erase(pendingEffects_.begin());
    response_.Open(player, YakouChainLink{player, handInstance,
                                          pending.EffectId, "OnResolve",
                                          targetInstance ? std::vector<int>{targetInstance}
                                                         : std::vector<int>{}});
    phase_ = YakouPhase::SpellResponse;
    journal_.push_back("SpellResponseOpened:" + std::to_string(handInstance));
}

void YakouMatch::RespondWithNegation(int player, int handInstance,
                                    std::vector<int> const& discarded)
{
    YakouMatch next = *this;
    next.RespondWithNegationImpl(player, handInstance, discarded);
    *this = std::move(next);
}

void YakouMatch::RespondWithNegationImpl(int player, int handInstance,
                                        std::vector<int> const& discarded)
{
    Check(phase_ == YakouPhase::SpellResponse &&
              response_.IsOpen() && response_.PriorityPlayer() == player,
          "No spell response priority");
    Check((player == 1 || player == 2) &&
              response_.ServerState().Stack.back().Controller != player,
          "Only the opposing priority holder may negate the top spell");
    Check(response_.StackSize() < MaximumSpellResponseLinks,
          "Spell response chain limit reached");
    auto& owner = MutablePlayer(player);
    Check(Contains(owner.Hand, handInstance), "Counterspell is not in owner's hand");
    auto const card = CardDefinition(OriginalId(handInstance));
    int const definitionId = card.at("Id").get<int>();
    Check((definitionId == 3020 || definitionId == 4020) && card.at("Type") == "Spell" &&
              card.at("SummonCostJson").at("Activation").is_object() &&
              card.at("SummonCostJson").at("Activation").size() == 1 &&
              card.at("SummonCostJson").at("Activation").at(
                  definitionId == 4020 ? "Necromancy" : "Discard") == 2,
          "Unsupported authored counterspell cost");
    auto const& effects = card.at("Effects");
    Check(effects.is_array() && effects.size() == 1 &&
              effects[0].at("Trigger") == "OnResolve" &&
              effects[0].at("Type") == "Triggered" &&
              effects[0].at("Actions").is_array() &&
              effects[0].at("Actions").size() == 1,
          "Invalid counterspell effect");
    auto const& action = effects[0].at("Actions")[0];
    Check(action.is_object() &&
              HasOnlyFields(action, {"Operation", "Target"}) &&
              action.at("Operation") == "NegateSpellEffect" &&
              action.at("Target") == "RespondedEnemySpell",
          "Unsupported counterspell operation");
    int const effectId = effects[0].at("Id").get<int>();
    int const limit = effects[0].value("PerTurnLimit", 0);
    Check(limit > 0 && effects[0].value("LimitScope", std::string("")) == "CardName",
          "Counterspell requires supported card-name limit");
    auto const usage = std::make_tuple(turn_, player,
                                       card.at("NameKey").get<std::string>(), effectId);
    Check(effectUses_[usage] < limit, "Counterspell per-turn limit reached");
    Check(discarded.size() == 2 && discarded[0] != discarded[1] &&
              discarded[0] != handInstance && discarded[1] != handInstance,
          "Counterspell needs two distinct cost cards");
    for (int cardId : discarded)
    {
        Check(Contains(definitionId == 4020 ? owner.Graveyard : owner.Hand, cardId),
              "Invalid counterspell cost card");
    }

    // The top link is the spell being answered. Its identity is server-derived;
    // incoming clients never supply the target spell ID.
    std::size_t const previousPendingCount = pendingEffects_.size();
    for (int cardId : discarded)
    {
        if (definitionId == 4020)
        {
            Remove(owner.Graveyard, cardId);
            owner.Exile.push_back(cardId);
            Enqueue(player, cardId, OriginalId(cardId), "OnBanishedForNecromancyCost");
        }
        else
        {
            Remove(owner.Hand, cardId);
            owner.Graveyard.push_back(cardId);
            Enqueue(player, cardId, OriginalId(cardId), "OnDiscarded");
        }
    }
    DeferPendingCostTriggers(previousPendingCount);
    Remove(owner.Hand, handInstance);
    owner.Graveyard.push_back(handInstance);
    response_.Respond(player, YakouChainLink{player, handInstance, effectId,
                                              "OnResolve", {}});
    ++effectUses_[usage];
    journal_.push_back("CounterspellActivated:" + std::to_string(handInstance));
}

void YakouMatch::PassSpellResponse(int player)
{
    YakouMatch next = *this;
    next.PassSpellResponseImpl(player);
    *this = std::move(next);
}

void YakouMatch::PassSpellResponseImpl(int player)
{
    Check(phase_ == YakouPhase::SpellResponse, "No spell response window");
    bool const closed = response_.Pass(player);
    journal_.push_back("SpellResponsePass:" + std::to_string(player));
    if (!closed) return;
    auto const resolution = response_.TakeResolution(); // LIFO: counter before original.
    Check(!resolution.empty() && resolution.size() <= MaximumSpellResponseLinks,
          "Unsupported resolved spell chain length");
    bool negated = false;
    for (std::size_t index = 0; index < resolution.size(); ++index)
    {
        auto const& link = resolution[index];
        if (index + 1 < resolution.size())
        {
            Check(link.Trigger == "OnResolve" && link.EffectId > 0 &&
                      link.Controller == 3 - resolution[index + 1].Controller,
                  "Unexpected chain counterspell");
            if (!negated)
            {
                journal_.push_back("SpellNegated:" +
                                   std::to_string(resolution[index + 1].SourceInstance));
            }
            negated = !negated;
            continue;
        }
        Check(link.Trigger == "OnResolve" &&
                  link.Controller == ActivePlayer() && link.EffectId > 0,
              "Unexpected original spell link");
        if (negated)
        {
            // Negated effects still consume their authored per-turn allowance.
            auto const card = CardDefinition(OriginalId(link.SourceInstance));
            auto const key = std::make_tuple(turn_, link.Controller,
                                              card.at("NameKey").get<std::string>(),
                                              link.EffectId);
            for (auto const& effect : card.at("Effects"))
            {
                if (effect.at("Id") != link.EffectId) continue;
                int const limit = effect.value("PerTurnLimit", 0);
                if (limit > 0)
                {
                    Check(effect.value("LimitScope", std::string("")) == "CardName" &&
                              effectUses_[key] < limit,
                          "Negated spell already exhausted its per-turn use");
                    ++effectUses_[key];
                }
            }
        }
        else
        {
            int const definitionId = OriginalId(link.SourceInstance);
            Enqueue(link.Controller, link.SourceInstance, definitionId, "OnResolve");
            Check(pendingEffects_.size() == 1,
                  "Expected one original spell effect after response");
            auto const choice = PendingEffectChoices(link.Controller);
            if (!link.Targets.empty())
            {
                ResolvePendingEffect(link.Controller, link.Targets.front());
            }
            else if (choice.MinCount == 0 && choice.MaxCount == 0)
            {
                ResolvePendingEffect(link.Controller);
            }
        }
    }
    phase_ = YakouPhase::Main;
    ReleaseDeferredCostTriggers();
    journal_.push_back("SpellResponseClosed");
}

std::vector<YakouSummonChoice> YakouMatch::SummonChoices(int player,
                                                               int handInstance) const
{
    Check(player == 1 || player == 2, "Unknown player");
    auto const& owner = Player(player);
    // Never expose costs or candidates for cards that are not in the
    // requesting player's own hand.
    Check(Contains(owner.Hand, handInstance), "Card is not in own hand");
    if (phase_ != YakouPhase::Main || player != ActivePlayer())
    {
        return {};
    }
    auto const card = CardDefinition(OriginalId(handInstance));
    if (card.at("Type") != "Monster")
    {
        return {};
    }

    std::vector<YakouSummonChoice> choices;
    for (auto const& methodValue : card.at("SummonMethods"))
    {
        auto const method = methodValue.get<std::string>();
        auto const requirement = YakouRequiredCost(card, method);
        if (!CanPayResources(owner, requirement.Resources)) continue;
        if (method == "Normal" && owner.NormalSummons != 0)
        {
            continue;
        }
        YakouSummonChoice choice;
        choice.Method = method;
        choice.SourceZone = requirement.SourceZone;
        choice.RequiredCount = requirement.ExactCount;
        choice.ResourceCosts = requirement.Resources;
        if (requirement.SourceZone == "FriendlyField")
        {
            for (auto const& unit : owner.Field)
            {
                if (unit && unit->Hp() > 0)
                {
                    choice.Candidates.push_back(unit->InstanceId);
                }
            }
        }
        else if (requirement.SourceZone == "FriendlyGraveyard")
        {
            choice.Candidates = owner.Graveyard;
        }
        else if (requirement.SourceZone == "FriendlyHand")
        {
            for (int instance : owner.Hand)
            {
                if (instance != handInstance)
                {
                    choice.Candidates.push_back(instance);
                }
            }
        }
        else
        {
            Check(requirement.ExactCount == 0 && (method == "Normal" || method == "Resource"),
                  "Unsupported summon cost method");
        }
        if (choice.Candidates.size() < choice.RequiredCount)
        {
            continue;
        }
        for (int slot = 0; slot < 5; ++slot)
        {
            if (!owner.Field[slot] ||
                (requirement.SourceZone == "FriendlyField" &&
                 Contains(choice.Candidates, owner.Field[slot]->InstanceId)))
            {
                choice.AvailableSlots.push_back(slot);
            }
        }
        if (!choice.AvailableSlots.empty())
        {
            choices.push_back(std::move(choice));
        }
    }
    return choices;
}

void YakouMatch::StartTurn(int player)
{
    YakouMatch next = *this;
    next.StartTurnImpl(player);
    *this = std::move(next);
}

void YakouMatch::StartTurnImpl(int player)
{
    Check(phase_ == YakouPhase::TurnStart && player == ActivePlayer(),
          "Cannot start this turn");
    auto& owner = MutablePlayer(player);
    int const drawn = (turn_ == 1 && active_ == first_) ? 0
            : owner.Hand.size() >= 5 ? 1
            : std::min(3, 5 - static_cast<int>(owner.Hand.size()));
    // Never silently draw an incorrect number. Drawing shortage is a loss,
    // consistent with the existing GameCore deck-exhaustion rule.
    if (owner.Deck.size() < static_cast<std::size_t>(drawn))
    {
        phase_ = YakouPhase::Finished;
        winner_ = 3 - player;
        journal_.push_back("DeckExhausted:" + std::to_string(player));
        return;
    }
    owner.NormalSummons = 0;
    owner.Manifestations = 0;
    owner.Redraws = 0;
    for (auto& unit : owner.Field)
        if (unit)
        {
            unit->Countdown = std::max(0, unit->Countdown - 1);
            unit->PostponedThisTurn = false;
        }
    for (int i = 0; i < drawn; ++i)
    {
        owner.Hand.push_back(owner.Deck.front());
        owner.Deck.erase(owner.Deck.begin());
    }
    // Alpha01 has no Ki regeneration; configured Alpha02 resources may opt in.
    for (auto& [resource, state] : owner.Resources)
        ChangeResource(owner, resource, state.RegenPerOwnTurn);
    phase_ = YakouPhase::Main;
    journal_.push_back("TurnStart:" + std::to_string(player));
}

void YakouMatch::Summon(int player, int handInstance, std::string const& method,
                        std::vector<int> const& materialIds, int slot)
{
    YakouMatch next = *this;
    next.SummonImpl(player, handInstance, method, materialIds, slot);
    *this = std::move(next);
}

void YakouMatch::SummonImpl(int player, int handInstance, std::string const& method,
                        std::vector<int> const& materialIds, int slot)
{
    RequireMain(player);
    auto& owner = MutablePlayer(player);
    Check(slot >= 0 && slot < 5, "Invalid fixed field slot");
    Check(Contains(owner.Hand, handInstance), "Card is not in own hand");
    auto const card = CardDefinition(OriginalId(handInstance));
    Check(card.at("Type") == "Monster", "Only monsters can be summoned");
    int const maxBlocks = PrintedMaxBlocks(card);
    YakouCostRequirement requirement;
    try
    {
        requirement = YakouRequiredCost(card, method);
    }
    catch (std::exception const& error)
    {
        throw std::invalid_argument("Card " + std::to_string(OriginalId(handInstance)) +
                                    " cannot use " + method + ": " + error.what());
    }
    auto const count = requirement.ExactCount;
    Check(CanPayResources(owner, requirement.Resources), "Insufficient summon resource");
    Check(materialIds.size() == count, "Incorrect number of summon materials");
    if (method == "Normal")
        Check(owner.NormalSummons == 0, "Normal summon already used");
    std::set<int> unique;
    int bonusAttack = 0, bonusMaxHp = 0;
    int occupied = owner.Field[slot] ? owner.Field[slot]->InstanceId : 0;
    for (int instance : materialIds)
    {
        Check(instance > 0 && unique.insert(instance).second, "Duplicate/invalid material");
        if (requirement.SourceZone == "FriendlyField")
        {
            auto const* unit = FindUnit(owner, instance);
            Check(unit != nullptr, "Devour requires allied field monster");
            auto const printed = CardDefinition(OriginalId(instance));
            bonusAttack += printed.at("Attack").get<int>();
            bonusMaxHp += printed.at("MaxHp").get<int>();
        }
        else if (requirement.SourceZone == "FriendlyGraveyard")
            Check(Contains(owner.Graveyard, instance), "Necromancy requires own graveyard");
        else if (requirement.SourceZone == "FriendlyHand")
            Check(instance != handInstance && Contains(owner.Hand, instance),
                  "Discard requires other cards in own hand");
        else
            Check(false, "Unexpected normal summon material");
    }
    Check(!occupied || (requirement.SourceZone == "FriendlyField" && unique.contains(occupied)),
          "Destination slot must be empty or devoured");
    if (requirement.SourceZone == "FriendlyField")
    {
        for (int instance : materialIds)
        {
            int const index = FindSlot(owner, instance);
            Check(index >= 0, "Missing devour material");
            int const sourceDefinition = owner.Field[index]->DefinitionId;
            owner.Field[index].reset();
            nextDamageShields_.erase(instance);
            owner.Graveyard.push_back(instance);
            Enqueue(player, instance, sourceDefinition, "OnDevoured", handInstance);
        }
    }
    else if (requirement.SourceZone == "FriendlyGraveyard")
    {
        for (int instance : materialIds)
        {
            Remove(owner.Graveyard, instance);
            owner.Exile.push_back(instance);
            Enqueue(player, instance, OriginalId(instance), "OnBanishedForNecromancyCost");
        }
    }
    else if (requirement.SourceZone == "FriendlyHand")
    {
        for (int instance : materialIds)
        {
            Remove(owner.Hand, instance);
            owner.Graveyard.push_back(instance);
            Enqueue(player, instance, OriginalId(instance), "OnDiscarded");
        }
    }
    PayResources(owner, requirement.Resources);
    Remove(owner.Hand, handInstance);
    owner.Field[slot] = YakouUnit{.InstanceId = handInstance,
                                   .DefinitionId = OriginalId(handInstance),
                                   .BaseAttack = card.at("Attack").get<int>(),
                                   .BaseMaxHp = card.at("MaxHp").get<int>(),
                                   .BonusAttack = bonusAttack,
                                   .BonusMaxHp = bonusMaxHp,
                                   .Countdown = card.at("AttackInterval").get<int>(),
                                   .WasDevourSummoned = requirement.SourceZone == "FriendlyField",
                                   .BaseMaxBlocks = maxBlocks,
                                   .MaxBlocks = maxBlocks};
    if (method == "Normal") ++owner.NormalSummons;
    Enqueue(player, handInstance, OriginalId(handInstance), "OnSummoned", 0, materialIds);
    if (requirement.SourceZone == "FriendlyField") Enqueue(player, handInstance, OriginalId(handInstance), "OnDevourSummoned");
    if (requirement.SourceZone == "FriendlyGraveyard") Enqueue(player, handInstance, OriginalId(handInstance), "OnNecromancySummoned");
    journal_.push_back("Summon:" + method + ":" + std::to_string(handInstance));
}

void YakouMatch::Manifest(int player, int fieldInstance, int manifestationDefinition)
{
    YakouMatch next = *this;
    next.ManifestImpl(player, fieldInstance, manifestationDefinition);
    *this = std::move(next);
}

void YakouMatch::ManifestImpl(int player, int fieldInstance, int manifestationDefinition)
{
    RequireMain(player);
    auto& owner = MutablePlayer(player);
    Check(owner.Manifestations == 0, "Already manifested this turn");
    auto& unit = FieldUnit(owner, fieldInstance);
    Check(!unit.Manifested, "Already a manifestation");
    auto const source = CardDefinition(unit.DefinitionId);
    auto const target = CardDefinition(manifestationDefinition);
    Check(target.at("Type") == "Manifestation", "Invalid manifestation definition");
    int const printedBlocks = PrintedMaxBlocks(target);
    auto const effectiveBlocks = static_cast<std::int64_t>(printedBlocks) + unit.MaxBlocks - unit.BaseMaxBlocks;
    Check(effectiveBlocks > 0 && effectiveBlocks <= std::numeric_limits<int>::max(),
          "Invalid manifestation blocker limit");
    bool allowed = false;
    for (auto const& entry : source.at("AllowedManifestations"))
        if (entry == manifestationDefinition) allowed = true;
    Check(allowed, "Not a listed manifestation for this monster");
    auto const& data = target.at("SummonCostJson").at("Manifestation");
    YakouResourceAmounts manifestationCosts;
    int ki = 0;
    if (data.contains("Costs"))
    {
        auto const typed = YakouParseTypedCost(data.at("Costs"));
        Check(typed.OrdinaryCount == 0 && CanPayResources(owner, typed.Resources),
              "Invalid manifestation special resource payment");
        manifestationCosts = typed.Resources;
    }
    else
    {
        ki = data.at("Ki").get<int>();
        Check(ki > 0 && (catalog_->Config.contains("resource.definitions") || ki == 2) &&
                  owner.Ki >= ki, "Insufficient Ki");
    }
    auto const& conditions = data.at("Condition");
    Check(conditions.is_object() &&
              HasOnlyFields(conditions, {"WasDevourSummoned", "FriendlyGraveyardArchetypeCountAtLeast",
                                         "Archetype", "ArchetypeMonsterTotalAcrossZones"}),
          "Unsupported manifestation condition");
    int unmetConditions = 0;
    if (conditions.contains("WasDevourSummoned"))
    {
        if (unit.WasDevourSummoned != conditions.at("WasDevourSummoned").get<bool>())
        {
            ++unmetConditions;
        }
    }
    if (conditions.contains("FriendlyGraveyardArchetypeCountAtLeast"))
    {
        auto const archetype = conditions.at("Archetype").get<std::string>();
        int tally = 0;
        for (int instance : owner.Graveyard)
            if (HasArchetype(OriginalId(instance), archetype)) ++tally;
        if (tally < conditions.at("FriendlyGraveyardArchetypeCountAtLeast").get<int>())
        {
            ++unmetConditions;
        }
    }
    if (conditions.contains("ArchetypeMonsterTotalAcrossZones"))
    {
        auto const& requirement = conditions.at("ArchetypeMonsterTotalAcrossZones");
        auto const archetype = requirement.at("Archetype").get<std::string>();
        int tally = 0;
        for (auto const* zone : {&owner.Graveyard, &owner.Exile})
        {
            for (int instance : *zone)
            {
                auto const original = CardDefinition(OriginalId(instance));
                if (original.at("Type") == "Monster" &&
                    HasArchetype(OriginalId(instance), archetype)) ++tally;
            }
        }
        if (tally < requirement.at("AtLeast").get<int>())
        {
            ++unmetConditions;
        }
    }
    Check(unmetConditions == 0 ||
              (unmetConditions == 1 && manifestConditionWaivers_[player - 1] > 0),
          "Manifest conditions not met");
    if (unmetConditions == 1)
    {
        --manifestConditionWaivers_[player - 1];
        journal_.push_back("ManifestConditionIgnored:" + std::to_string(fieldInstance));
    }
    PayResources(owner, manifestationCosts);
    SpendLegacyKi(owner, ki);
    ++owner.Manifestations;
    unit.BaseAttack = target.at("Attack").get<int>();
    unit.BaseMaxHp = target.at("MaxHp").get<int>();
    unit.BaseMaxBlocks = printedBlocks;
    unit.MaxBlocks = static_cast<int>(effectiveBlocks);
    unit.DefinitionId = manifestationDefinition;
    unit.Manifested = true;
    unit.Countdown = std::max(0, unit.Countdown - 1);
    // Damage and devour bonus are both preserved on the same instance/slot.
    Enqueue(player, fieldInstance, manifestationDefinition, "OnManifested");
    journal_.push_back("Manifest:" + std::to_string(fieldInstance));
    if (unit.Hp() <= 0)
    {
        int const index = FindSlot(owner, fieldInstance);
        owner.Field[index].reset();
        nextDamageShields_.erase(fieldInstance);
        owner.Graveyard.push_back(fieldInstance);
    }
}

void YakouMatch::Postpone(int player, int fieldInstance)
{
    YakouMatch next = *this;
    next.PostponeImpl(player, fieldInstance);
    *this = std::move(next);
}

void YakouMatch::PostponeImpl(int player, int fieldInstance)
{
    RequireMain(player);
    auto& owner = MutablePlayer(player);
    auto& unit = FieldUnit(owner, fieldInstance);
    Check(unit.Countdown == 0 && !unit.PostponedThisTurn && owner.Ki >= 1,
          "Cannot postpone this raid");
    SpendLegacyKi(owner, 1);
    unit.Countdown = 1;
    unit.PostponedThisTurn = true;
    journal_.push_back("Postpone:" + std::to_string(fieldInstance));
}

void YakouMatch::Redraw(int player, std::vector<int> const& cards)
{
    YakouMatch next = *this;
    next.RedrawImpl(player, cards);
    *this = std::move(next);
}

void YakouMatch::RedrawImpl(int player, std::vector<int> const& cards)
{
    RequireMain(player);
    auto& owner = MutablePlayer(player);
    Check(owner.Redraws == 0 && owner.Ki >= 1 && !cards.empty() && cards.size() <= 3,
          "Cannot redraw");
    Check(owner.Deck.size() >= cards.size(), "Insufficient deck for redraw");
    std::set<int> unique;
    for (int instance : cards)
        Check(unique.insert(instance).second && Contains(owner.Hand, instance),
              "Redraw cards must be distinct cards in own hand");
    SpendLegacyKi(owner, 1);
    ++owner.Redraws;
    for (int instance : cards)
    {
        Remove(owner.Hand, instance);
        owner.Deck.push_back(instance);
    }
    for (std::size_t i = 0; i < cards.size(); ++i)
    {
        owner.Hand.push_back(owner.Deck.front());
        owner.Deck.erase(owner.Deck.begin());
    }
    journal_.push_back("Redraw:" + std::to_string(cards.size()));
}

void YakouMatch::BeginRaid(int player)
{
    YakouMatch next = *this;
    next.BeginRaidImpl(player);
    *this = std::move(next);
}

void YakouMatch::BeginRaidImpl(int player)
{
    RequireMain(player);
    attacks_.clear();
    auto const& owner = Player(player);
    for (int slot = 0; slot < 5; ++slot)
        if (owner.Field[slot] && owner.Field[slot]->Countdown == 0)
            attacks_.push_back({owner.Field[slot]->InstanceId, slot, 0});
    if (attacks_.empty())
    {
        TurnOver();
        return;
    }
    phase_ = YakouPhase::AssignBlockers;
    journal_.push_back("RaidDeclared:" + std::to_string(attacks_.size()));
}

void YakouMatch::AssignBlockers(int defendingPlayer,
                                std::vector<std::pair<int, int>> const& assignments)
{
    YakouMatch next = *this;
    next.AssignBlockersImpl(defendingPlayer, assignments);
    *this = std::move(next);
}

void YakouMatch::AssignBlockersImpl(int defendingPlayer,
                                std::vector<std::pair<int, int>> const& assignments)
{
    Check(phase_ == YakouPhase::AssignBlockers, "No raid awaiting blockers");
    Check(defendingPlayer != ActivePlayer() && (defendingPlayer == 1 || defendingPlayer == 2),
          "Not defending player");
    auto const& owner = Player(defendingPlayer);
    std::set<int> assignedAttackers;
    std::map<int, int> blockerUses;
    std::vector<YakouAttack> proposed = attacks_;
    for (auto const& [attacker, blocker] : assignments)
    {
        Check(blocker > 0 && assignedAttackers.insert(attacker).second,
              "Duplicate attacker or missing blocker");
        auto attack = std::find_if(proposed.begin(), proposed.end(),
                                   [&](auto const& x) { return x.Card == attacker; });
        Check(attack != proposed.end(), "Unknown attacker");
        auto const* defender = FindUnit(owner, blocker);
        Check(defender != nullptr && defender->Hp() > 0, "Invalid blocker");
        Check(++blockerUses[blocker] <= defender->MaxBlocks,
              "Blocker used more than permitted");
        attack->Blocker = blocker;
    }
    attacks_ = std::move(proposed);
    phase_ = YakouPhase::RaidResponse;
    response_.OpenWindow(ActivePlayer());
    journal_.push_back("BlockersAssigned:" + std::to_string(assignments.size()));
    journal_.push_back("RaidResponseOpened");
}

void YakouMatch::RespondWithRaidShield(int player, int handInstance, int friendlyUnit)
{
    YakouMatch next = *this;
    next.RespondWithRaidShieldImpl(player, handInstance, friendlyUnit);
    *this = std::move(next);
}

void YakouMatch::RespondWithRaidShieldImpl(int player, int handInstance, int friendlyUnit)
{
    Check(phase_ == YakouPhase::RaidResponse && response_.IsOpen() &&
              response_.PriorityPlayer() == player &&
              player == 3 - ActivePlayer(),
          "Only the defending player with priority may activate a raid shield");
    Check(response_.StackSize() < MaximumRaidResponseLinks,
          "Maximum of eight defensive raid responses per window");
    auto& owner = MutablePlayer(player);
    Check(Contains(owner.Hand, handInstance), "Raid response must be in owner's hand");
    auto const card = CardDefinition(OriginalId(handInstance));
    int const definitionId = card.at("Id").get<int>();
    Check((definitionId == 3016 || definitionId == 4018) && card.at("Type") == "Spell",
          "Unsupported raid shield card");
    Check(definitionId == 4018 ? friendlyUnit == 0 : FindUnit(owner, friendlyUnit) != nullptr,
          "Invalid raid shield target");
    if (definitionId == 4018)
    {
        Check(std::any_of(attacks_.begin(), attacks_.end(), [&](YakouAttack const& attack) {
            return attack.Blocker > 0 && FindUnit(owner, attack.Blocker) != nullptr;
        }), "All-blocker response requires an assigned friendly blocker");
    }
    Check(card.at("SummonCostJson").at("Activation").is_object() &&
              card.at("SummonCostJson").at("Activation").empty(),
          "Raid shield must have no activation costs");
    auto const& effects = card.at("Effects");
    Check(effects.is_array() && effects.size() == 1 &&
              effects[0].at("Type") == "Triggered" &&
              effects[0].at("Trigger") == "OnResolve" &&
              effects[0].at("Actions").is_array() &&
              effects[0].at("Actions").size() == 1,
          "Unsupported raid shield effect shape");
    auto const& action = effects[0].at("Actions")[0];
    Check(action.is_object() &&
              HasOnlyFields(action, {"Operation", "Target", "Amount", "Duration"}) &&
              action.at("Operation") == "ReduceDamage" &&
              action.at("Target") == (definitionId == 4018 ? "AllFriendlyAssignedBlockers" : "FriendlyMonster") &&
              action.at("Duration") == "FirstDamageThisRaid" &&
              action.at("Amount").is_number_integer(),
          "Unsupported raid shield operation");
    Check(action.at("Amount").get<int>() > 0, "Raid shield must reduce positive damage");
    int const limit = effects[0].value("PerTurnLimit", 0);
    Check(limit > 0 && effects[0].value("LimitScope", std::string("")) == "CardName",
          "Unsupported raid shield use limit");
    auto const key = std::make_tuple(turn_, player,
                                     card.at("NameKey").get<std::string>(),
                                     effects[0].at("Id").get<int>());
    Check(effectUses_[key] < limit, "Raid shield card-name limit reached");

    Remove(owner.Hand, handInstance);
    owner.Graveyard.push_back(handInstance);
    response_.Respond(player, YakouChainLink{player, handInstance,
                        effects[0].at("Id").get<int>(), "OnResolve",
                        definitionId == 4018 ? std::vector<int>{} : std::vector<int>{friendlyUnit}});
    ++effectUses_[key];
    journal_.push_back("RaidShieldActivated:" + std::to_string(handInstance));
}


void YakouMatch::RespondWithRaidTactic(int player, int spell, int raider,
                                       int replacement, std::vector<int> const& costs)
{
    YakouMatch next = *this;
    next.RespondWithRaidTacticImpl(player, spell, raider, replacement, costs);
    *this = std::move(next);
}

void YakouMatch::RespondWithRaidTacticImpl(int player, int spell, int raider,
                                           int replacement, std::vector<int> const& costs)
{
    Check(phase_ == YakouPhase::RaidResponse && response_.IsOpen() &&
          response_.PriorityPlayer() == player && player == 3 - ActivePlayer() &&
          response_.StackSize() < MaximumRaidResponseLinks, "No legal raid tactic priority");
    auto& owner = MutablePlayer(player);
    Check(Contains(owner.Hand, spell), "Tactic must be in controller's hand");
    auto const card = CardDefinition(OriginalId(spell));
    const int id = card.at("Id").get<int>();
    Check((id == 2011 || id == 3018 || id == 3019 || id == 4017) &&
          card.at("Type") == "Spell",
          "Unsupported raid tactic");
    auto attacked = std::find_if(attacks_.begin(), attacks_.end(),
        [&](YakouAttack const& attack) { return attack.Card == raider; });
    Check(attacked != attacks_.end() && FindUnit(Player(ActivePlayer()), raider) != nullptr,
          "Unknown raiding monster");
    auto const& effects = card.at("Effects");
    Check(effects.is_array() && effects.size() == 1 &&
          effects[0].at("Type") == "Triggered" && effects[0].at("Trigger") == "OnResolve" &&
          effects[0].at("Actions").is_array() &&
          effects[0].at("Actions").size() == (id == 4017 ? 2 : 1),
          "Unsupported tactic effect definition");
    auto const& effect = effects[0];
    auto const& action = effect.at("Actions")[0];
    if (id == 3018 || id == 4017)
    {
        Check(action.is_object() &&
              HasOnlyFields(action, {"Operation", "Target", "NewBlocker", "RespectBlockLimit"}) &&
              action.at("Operation") == "ReassignBlocker" &&
              action.at("Target") == "OneBlockedRaid" &&
              action.at("NewBlocker") == "OtherEligibleFriendlyMonster" &&
              action.at("RespectBlockLimit") == true,
              "Unsupported blocker-reassignment action");
        auto const* unit = FindUnit(owner, replacement);
        Check(attacked->Blocker > 0 && replacement > 0 &&
              attacked->Blocker != replacement && unit != nullptr && unit->Hp() > 0,
              "Cannot assign this replacement blocker");
        int inUse = 0;
        for (auto const& attack : attacks_)
            if (attack.Blocker == replacement) ++inUse;
        Check(inUse < unit->MaxBlocks, "Replacement blocker has no capacity");
        if (id == 4017)
        {
            auto const& reduction = effect.at("Actions")[1];
            Check(reduction.is_object() &&
                  HasOnlyFields(reduction, {"Operation", "Target", "Amount", "Duration"}) &&
                  reduction.at("Operation") == "ReduceDamage" &&
                  reduction.at("Target") == "NewlyAssignedBlocker" &&
                  reduction.at("Amount") == 2 &&
                  reduction.at("Duration") == "FirstDamageThisRaid",
                  "Unsupported authored blocker replacement shield");
        }
    }
    else
    {
        Check(replacement == 0 && action.is_object() &&
              HasOnlyFields(action, {"Operation", "Target", "Delta", "Duration", "Minimum"}) &&
              action.at("Operation") == "ModifyAttack" &&
              action.at("Target") == "EnemyRaidingMonster" &&
              action.at("Delta").is_number_integer() &&
              action.at("Delta").get<int>() <= 0 &&
              action.at("Minimum") == 0 &&
              action.at("Duration") == (id == 2011 ? "CurrentCombat" : "CurrentRaidPhase"),
              "Unsupported raid attack modifier");
    }
    auto const& activation = card.at("SummonCostJson").at("Activation");
    const bool necromancy = id == 3019;
    const char* key = necromancy ? "Necromancy" : "Discard";
    const int expected = necromancy || id == 4017 ? 2 : 1;
    Check(activation.is_object() && activation.size() == 1 &&
          activation.contains(key) && activation.at(key) == expected &&
          costs.size() == static_cast<std::size_t>(expected), "Invalid raid tactic cost");
    std::set<int> unique;
    for (int instance : costs)
        Check(instance > 0 && unique.insert(instance).second &&
              (necromancy ? Contains(owner.Graveyard, instance)
                          : (instance != spell && Contains(owner.Hand, instance))),
              "Spoofed or duplicate raid tactic payment");
    const int effectId = effect.at("Id").get<int>();
    const int limit = effect.value("PerTurnLimit", 0);
    Check(limit > 0 && effect.value("LimitScope", std::string("")) == "CardName",
          "Unsupported tactic use limit");
    auto const usage = std::make_tuple(turn_, player,
                                      card.at("NameKey").get<std::string>(), effectId);
    Check(effectUses_[usage] < limit, "Raid tactic per-turn limit reached");
    std::size_t const previousPendingCount = pendingEffects_.size();
    for (int instance : costs)
    {
        if (necromancy)
        {
            Remove(owner.Graveyard, instance);
            owner.Exile.push_back(instance);
            Enqueue(player, instance, OriginalId(instance), "OnBanishedForNecromancyCost");
        }
        else
        {
            Remove(owner.Hand, instance);
            owner.Graveyard.push_back(instance);
            Enqueue(player, instance, OriginalId(instance), "OnDiscarded");
        }
    }
    DeferPendingCostTriggers(previousPendingCount);
    Remove(owner.Hand, spell);
    owner.Graveyard.push_back(spell);
    response_.Respond(player, YakouChainLink{player, spell, effectId, "OnResolve",
              (id == 3018 || id == 4017) ? std::vector<int>{raider, replacement}
                                         : std::vector<int>{raider}});
    ++effectUses_[usage];
    if (id == 2011 || id == 3019)
    {
        chainTargetedByEnemySpell_.insert(raider);
        journal_.push_back("EnemySpellTargetConfirmed:" + std::to_string(raider));
        auto const* target = FindUnit(Player(ActivePlayer()), raider);
        if (target != nullptr && target->DefinitionId == 3009)
        {
            auto const targetCard = CardDefinition(3009);
            auto const& trigger = targetCard.at("Effects")[0];
            auto const& reduction = trigger.at("Actions")[0];
            Check(trigger.at("Trigger") == "OnTargetedByEnemySpell" &&
                      HasOnlyFields(reduction,
                          {"Operation", "Target", "Amount", "Duration", "DamageType"}) &&
                      reduction.at("Operation") == "ReduceDamage" &&
                      reduction.at("Target") == "Self" &&
                      reduction.at("Duration") == "CurrentChain" &&
                      reduction.at("DamageType") == "Effect" &&
                      reduction.at("Amount") == 1,
                  "Unsupported 3009 targeting trigger");
            int& amount = chainEffectDamageReduction_[raider];
            Check(amount < std::numeric_limits<int>::max(),
                  "Chain effect damage reduction overflow");
            ++amount;
            journal_.push_back("EffectDamageReductionGranted:" +
                               std::to_string(raider));
        }
    }
    journal_.push_back("RaidTacticActivated:" + std::to_string(spell));
}

void YakouMatch::RespondWithPiercing(int player, int raider, int graveyardCost)
{
    YakouMatch next = *this;
    next.RespondWithPiercingImpl(player, raider, graveyardCost);
    *this = std::move(next);
}

void YakouMatch::RespondWithEffectProtection(int player, int spell,
                                             int target, int graveyardCost)
{
    YakouMatch next = *this;
    next.RespondWithEffectProtectionImpl(player, spell, target, graveyardCost);
    *this = std::move(next);
}

void YakouMatch::RespondWithEffectProtectionImpl(int player, int spell,
                                                 int target, int graveyardCost)
{
    Check(phase_ == YakouPhase::RaidResponse && response_.IsOpen() &&
              response_.PriorityPlayer() == player && player == ActivePlayer() &&
              response_.StackSize() < MaximumRaidResponseLinks,
          "No effect-protection response priority");
    auto& owner = MutablePlayer(player);
    Check(Contains(owner.Hand, spell) && OriginalId(spell) == 3017 &&
              chainTargetedByEnemySpell_.contains(target) &&
              FindUnit(owner, target) != nullptr,
          "3017 needs an own monster confirmed as an enemy spell target");
    auto const card = CardDefinition(3017);
    auto const& effect = card.at("Effects")[0];
    auto const& action = effect.at("Actions")[0];
    Check(card.at("SummonCostJson").at("Activation") == Json{{"Necromancy", 1}} &&
              effect.at("Trigger") == "OnResolve" &&
              HasOnlyFields(action, {"Operation", "Target", "Duration"}) &&
              action.at("Operation") == "PreventEffectDamage" &&
              action.at("Target") == "FriendlyMonsterTargetedBySpell" &&
              action.at("Duration") == "CurrentChain",
          "Unsupported 3017 protection definition");
    auto const usage = std::make_tuple(turn_, player,
        card.at("NameKey").get<std::string>(), effect.at("Id").get<int>());
    Check(effect.value("PerTurnLimit", 0) == 1 &&
              effect.value("LimitScope", std::string()) == "CardName" &&
              effectUses_[usage] < 1,
          "3017 card-name response limit reached");
    Check(Contains(owner.Graveyard, graveyardCost),
          "3017 needs one own graveyard cost card");
    std::size_t const previousPendingCount = pendingEffects_.size();
    Remove(owner.Graveyard, graveyardCost);
    owner.Exile.push_back(graveyardCost);
    Enqueue(player, graveyardCost, OriginalId(graveyardCost),
            "OnBanishedForNecromancyCost");
    DeferPendingCostTriggers(previousPendingCount);
    Remove(owner.Hand, spell);
    owner.Graveyard.push_back(spell);
    response_.Respond(player, YakouChainLink{player, spell,
        effect.at("Id").get<int>(), "OnResolve", {target}});
    ++effectUses_[usage];
    journal_.push_back("EffectProtectionActivated:" + std::to_string(target));
}

void YakouMatch::RespondWithPiercingImpl(int player, int raider, int graveyardCost)
{
    Check(phase_ == YakouPhase::RaidResponse && response_.IsOpen() &&
              response_.PriorityPlayer() == player && player == ActivePlayer() &&
              response_.StackSize() < MaximumRaidResponseLinks,
          "No piercing response priority");
    Check(std::any_of(attacks_.begin(), attacks_.end(), [&](YakouAttack const& attack)
          { return attack.Card == raider; }) &&
          FindUnit(Player(player), raider) != nullptr &&
          FindUnit(Player(player), raider)->DefinitionId == 2101 &&
          !piercingRaiders_.contains(raider),
          "Piercing source must be a raiding 2101");
    auto const stacked = response_.ServerState().Stack;
    Check(std::none_of(stacked.begin(), stacked.end(), [&](YakouChainLink const& link)
          { return link.SourceInstance == raider && link.Trigger == "OnRaid"; }),
          "Piercing already activated for this raid");
    auto const card = CardDefinition(2101);
    auto const& effect = card.at("Effects")[1];
    auto const& action = effect.at("Actions")[0];
    Check(effect.at("Trigger") == "OnRaid" && action.at("Operation") == "GrantPiercing" &&
              action.at("Target") == "Self" && action.at("Duration") == "CurrentCombat" &&
              action.at("OptionalCost") == Json{{"Necromancy", 1},
                                                {"RequiredArchetype", "HyakkiYagyo"}},
          "Unsupported authored piercing effect");
    auto& owner = MutablePlayer(player);
    Check(Contains(owner.Graveyard, graveyardCost) &&
              HasArchetype(OriginalId(graveyardCost), "HyakkiYagyo"),
          "Piercing needs one owned HyakkiYagyo graveyard card");
    std::size_t const previousPendingCount = pendingEffects_.size();
    Remove(owner.Graveyard, graveyardCost);
    owner.Exile.push_back(graveyardCost);
    Enqueue(player, graveyardCost, OriginalId(graveyardCost),
            "OnBanishedForNecromancyCost");
    DeferPendingCostTriggers(previousPendingCount);
    response_.Respond(player, YakouChainLink{player, raider,
        effect.at("Id").get<int>(), "OnRaid", {}});
    journal_.push_back("PiercingActivated:" + std::to_string(raider));
}

void YakouMatch::PassRaidResponse(int player)
{
    YakouMatch next = *this;
    next.PassRaidResponseImpl(player);
    *this = std::move(next);
}

void YakouMatch::PassRaidResponseImpl(int player)
{
    Check(phase_ == YakouPhase::RaidResponse, "No raid response window");
    bool const closed = response_.Pass(player);
    journal_.push_back("RaidResponsePass:" + std::to_string(player));
    if (closed)
    {
        // Resolve supported defensive links in strict reverse activation
        // order. Every link is checked against the state produced by the
        // preceding link; any illegal intermediate result rolls back the
        // entire closing pass and leaves the live response window open.
        auto const links = response_.TakeResolution();
        Check(links.size() <= static_cast<std::size_t>(MaximumRaidResponseLinks), "Unsupported raid chain length");
        for (auto const& link : links)
        {
            if (link.Controller == ActivePlayer() && link.Trigger == "OnRaid")
            {
                Check(FindUnit(Player(link.Controller), link.SourceInstance) != nullptr &&
                          FindUnit(Player(link.Controller), link.SourceInstance)->DefinitionId == 2101 &&
                          std::any_of(attacks_.begin(), attacks_.end(), [&](YakouAttack const& attack)
                              { return attack.Card == link.SourceInstance; }) &&
                          !piercingRaiders_.contains(link.SourceInstance),
                      "Piercing raider left combat before resolution");
                piercingRaiders_.insert(link.SourceInstance);
                journal_.push_back("PiercingGranted:" + std::to_string(link.SourceInstance));
                continue;
            }
            if (link.Controller == ActivePlayer() && link.Trigger == "OnResolve" &&
                OriginalId(link.SourceInstance) == 3017)
            {
                Check(link.Targets.size() == 1 &&
                          chainTargetedByEnemySpell_.contains(link.Targets[0]) &&
                          FindUnit(Player(link.Controller), link.Targets[0]) != nullptr,
                      "3017 protected target left the chain");
                chainEffectDamagePrevention_.insert(link.Targets[0]);
                journal_.push_back("EffectDamagePreventionGranted:" +
                                   std::to_string(link.Targets[0]));
                continue;
            }
            Check(link.Controller == 3 - ActivePlayer() &&
                      link.Trigger == "OnResolve", "Unsupported raid response link");
            auto const card = CardDefinition(OriginalId(link.SourceInstance));
            const int id = card.at("Id").get<int>();
            Check(link.EffectId == card.at("Effects")[0].at("Id").get<int>(),
                  "Unknown raid response effect");
            auto const& action = card.at("Effects")[0].at("Actions")[0];
            if (id == 3016)
            {
                Check(link.Targets.size() == 1, "Invalid raid shield targets");
                int const target = link.Targets[0];
                Check(FindUnit(Player(link.Controller), target) != nullptr,
                      "Raid shield target left its owner's field");
                int const amount = action.at("Amount").get<int>();
                int& reduction = raidShields_[target];
                Check(amount > 0 && reduction <= std::numeric_limits<int>::max() - amount,
                      "Raid shield overflow");
                reduction += amount;
                journal_.push_back("RaidShieldResolved:" + std::to_string(target));
            }
            else if (id == 4018)
            {
                Check(link.Targets.empty(), "All-blocker response cannot select a target");
                int const amount = action.at("Amount").get<int>();
                Check(amount > 0, "All-blocker response must reduce positive damage");
                for (auto const& attack : attacks_)
                {
                    if (attack.Blocker == 0 ||
                        FindUnit(Player(link.Controller), attack.Blocker) == nullptr)
                    {
                        continue;
                    }
                    auto& reduction = raidShields_[attack.Blocker];
                    Check(reduction <= std::numeric_limits<int>::max() - amount,
                          "Raid shield overflow");
                    reduction += amount;
                }
                journal_.push_back("RaidAllBlockersShielded:" + std::to_string(amount));
            }
            else if (id == 2011 || id == 3019)
            {
                Check(link.Targets.size() == 1, "Invalid raid attack target");
                int const target = link.Targets.front();
                Check(std::any_of(attacks_.begin(), attacks_.end(),
                          [&](YakouAttack const& attack) { return attack.Card == target; }) &&
                      FindUnit(Player(ActivePlayer()), target) != nullptr,
                      "Raid attack target no longer exists");
                int const delta = action.at("Delta").get<int>();
                int const previous = raidAttackModifiers_.contains(target) ?
                                     raidAttackModifiers_.at(target) : 0;
                auto const sum = static_cast<std::int64_t>(previous) + delta;
                Check(sum >= std::numeric_limits<int>::min() &&
                      sum <= std::numeric_limits<int>::max(), "Attack modifier overflow");
                raidAttackModifiers_[target] = static_cast<int>(sum);
                journal_.push_back("RaidAttackReduced:" + std::to_string(target));
            }
            else if (id == 3018 || id == 4017)
            {
                Check(link.Targets.size() == 2, "Invalid reassignment targets");
                int const raider = link.Targets[0], newBlocker = link.Targets[1];
                auto it = std::find_if(attacks_.begin(), attacks_.end(),
                    [&](YakouAttack const& attack) { return attack.Card == raider; });
                auto const* unit = FindUnit(Player(link.Controller), newBlocker);
                Check(it != attacks_.end() && it->Blocker > 0 &&
                      it->Blocker != newBlocker && unit != nullptr && unit->Hp() > 0,
                      "Invalid replacement blocker at resolution");
                int used = 0;
                for (auto const& attack : attacks_)
                    if (attack.Blocker == newBlocker) ++used;
                Check(used < unit->MaxBlocks, "Blocker limit exceeded at resolution");
                it->Blocker = newBlocker;
                if (id == 4017)
                {
                    int const amount = card.at("Effects")[0].at("Actions")[1].at("Amount").get<int>();
                    auto& reduction = raidShields_[newBlocker];
                    Check(reduction <= std::numeric_limits<int>::max() - amount,
                          "Replacement shield overflow");
                    reduction += amount;
                }
                journal_.push_back("RaidBlockerReassigned:" + std::to_string(raider) +
                                   ":" + std::to_string(newBlocker));
            }
            else Check(false, "Unsupported raid response card");
        }
        ReleaseDeferredCostTriggers();
        journal_.push_back("RaidResponseClosed");
    }
}

void YakouMatch::ResolveRaid(int defendingPlayer)
{
    YakouMatch next = *this;
    next.ResolveRaidImpl(defendingPlayer);
    *this = std::move(next);
}

void YakouMatch::ResolveRaidImpl(int defendingPlayer)
{
    Check(phase_ == YakouPhase::RaidResponse && !response_.IsOpen() &&
              std::none_of(pendingEffects_.begin(), pendingEffects_.end(),
                  [](YakouPendingEffect const& effect) { return effect.DeferredCost; }) &&
              defendingPlayer != ActivePlayer() &&
              (defendingPlayer == 1 || defendingPlayer == 2),
          "Cannot resolve raid");
    auto& attacker = players_[active_];
    auto& defender = MutablePlayer(defendingPlayer);

    // Resolve the narrow set of authored combat-only triggers before damage.
    // No persistent attack/damage stat layer is introduced: CurrentCombat
    // expires after this individual combat, including when the unit survives.
    // Other authored combat shapes must fail closed instead of applying damage
    // first and leaving an unresolvable trigger behind.
    auto combatAttackBonus = [&](YakouUnit const& unit) -> int
    {
        int bonus = 0;
        auto const card = CardDefinition(unit.DefinitionId);
        for (auto const& effect : card.at("Effects"))
        {
            if (effect.at("Trigger") != "OnRaid") continue;
            Check(effect.at("Type") == "Triggered" &&
                      effect.at("Actions").is_array() && effect.at("Actions").size() == 1,
                  "Unsupported raid trigger");
            auto const& action = effect.at("Actions")[0];
            if (unit.DefinitionId == 2101 &&
                action.value("Operation", std::string()) == "GrantPiercing")
            {
                // The optional cost is paid through an authorized raid
                // response link; merely raiding grants no piercing.
                continue;
            }
            Check(action.is_object() &&
                      HasOnlyFields(action, {"Operation", "Target", "Delta", "Duration", "Condition"}) &&
                      action.at("Operation") == "ModifyAttack" &&
                      action.at("Target") == "Self" &&
                      action.at("Duration") == "CurrentCombat" &&
                      action.at("Delta").is_number_integer(),
                  "Unsupported raid combat effect");
            if (action.contains("Condition"))
            {
                auto const& condition = action.at("Condition");
                Check(condition.is_object() &&
                          HasOnlyFields(condition, {"FriendlyGraveyardArchetypeCountAtLeast", "Archetype"}) &&
                          condition.at("FriendlyGraveyardArchetypeCountAtLeast").is_number_integer() &&
                          condition.at("Archetype").is_string(),
                      "Unsupported raid effect condition");
                int tally = 0;
                for (int instance : attacker.Graveyard)
                    if (HasArchetype(OriginalId(instance),
                                     condition.at("Archetype").get<std::string>()))
                        ++tally;
                if (tally < condition.at("FriendlyGraveyardArchetypeCountAtLeast").get<int>())
                    continue;
            }
            auto const sum = static_cast<std::int64_t>(bonus) + action.at("Delta").get<int>();
            Check(sum >= std::numeric_limits<int>::min() &&
                      sum <= std::numeric_limits<int>::max(), "Raid attack modifier overflow");
            bonus = static_cast<int>(sum);
        }
        return bonus;
    };
    auto blockerCombatModifiers = [&](YakouUnit const& unit) -> std::pair<int, int>
    {
        int attackDelta = 0;
        int reduction = 0;
        auto const card = CardDefinition(unit.DefinitionId);
        for (auto const& effect : card.at("Effects"))
        {
            if (effect.at("Trigger") != "OnAssignedAsBlocker") continue;
            Check(effect.at("Type") == "Triggered" &&
                      effect.at("Actions").is_array() && effect.at("Actions").size() == 1,
                  "Unsupported blocker trigger");
            auto const& action = effect.at("Actions")[0];
            bool const reducesAttack = action.value("Operation", std::string()) == "ModifyAttack";
            if (reducesAttack)
            {
                Check(unit.DefinitionId == 4019 &&
                          HasOnlyFields(action, {"Operation", "Target", "Delta", "Minimum", "Duration"}) &&
                          action.at("Target") == "RaidMonsterBlockedBySelf" &&
                          action.at("Delta") == -1 && action.at("Minimum") == 0 &&
                          action.at("Duration") == "CurrentCombat",
                      "Unsupported blocker attack modifier");
            }
            else
            {
                Check(action.is_object() &&
                          HasOnlyFields(action, {"Operation", "Target", "Amount", "Duration"}) &&
                          action.at("Operation") == "ReduceDamage" &&
                          action.at("Target") == "Self" &&
                          action.at("Duration") == "CurrentCombat" &&
                          action.at("Amount").is_number_integer(),
                      "Unsupported blocker combat effect");
            }
            int const limit = effect.value("PerTurnLimit", 0);
            if (limit > 0)
            {
                Check(effect.value("LimitScope", std::string("")) == "CardName",
                      "Unsupported blocker effect limit scope");
                auto const key = std::make_tuple(turn_, defender.Id,
                                                  card.at("NameKey").get<std::string>(),
                                                  effect.at("Id").get<int>());
                if (effectUses_[key] >= limit) continue;
                ++effectUses_[key];
            }
            if (reducesAttack)
            {
                --attackDelta;
            }
            else
            {
                int const amount = action.at("Amount").get<int>();
                Check(amount >= 0 && reduction <= std::numeric_limits<int>::max() - amount,
                      "Invalid blocker damage reduction");
                reduction += amount;
            }
        }
        return {attackDelta, reduction};
    };
    // The only currently authored post-combat kill trigger resets the
    // surviving attacker's countdown. Resolve it after simultaneous damage
    // and the ordinary countdown reset, without leaking it into the manual
    // pending-effect queue or allowing it to act on a destroyed source.
    auto resolveCombatKill = [&](YakouUnit& source)
    {
        auto const card = CardDefinition(source.DefinitionId);
        for (auto const& effect : card.at("Effects"))
        {
            if (effect.at("Trigger") != "OnDestroyedEnemyByCombat") continue;
            Check(effect.at("Type") == "Triggered" &&
                      effect.at("Actions").is_array() && effect.at("Actions").size() == 1,
                  "Unsupported combat-kill trigger");
            auto const& action = effect.at("Actions")[0];
            Check(action.is_object() &&
                      HasOnlyFields(action, {"Operation", "Target", "Value"}) &&
                      action.at("Operation") == "SetCountdown" &&
                      action.at("Target") == "Self" &&
                      action.at("Value").is_number_integer(),
                  "Unsupported combat-kill action");
            int const value = action.at("Value").get<int>();
            Check(value >= 0, "Invalid combat-kill countdown");
            int const limit = effect.value("PerTurnLimit", 0);
            if (limit > 0)
            {
                Check(effect.value("LimitScope", std::string("")) == "CardName",
                      "Unsupported combat-kill limit scope");
                auto const key = std::make_tuple(turn_, attacker.Id,
                                                  card.at("NameKey").get<std::string>(),
                                                  effect.at("Id").get<int>());
                if (effectUses_[key] >= limit) continue;
                ++effectUses_[key];
            }
            source.Countdown = value;
            journal_.push_back("CombatKillCountdown:" +
                               std::to_string(source.InstanceId) + ":" +
                               std::to_string(value));
        }
    };
    auto applyNextDamageShield = [&](int instance, int incoming) -> int
    {
        if (incoming <= 0)
        {
            return incoming;
        }
        auto const shield = nextDamageShields_.find(instance);
        if (shield == nextDamageShields_.end())
        {
            return incoming;
        }
        int const reduced = std::max(0, incoming - shield->second);
        journal_.push_back("NextDamageShieldUsed:" + std::to_string(instance) +
                           ":" + std::to_string(incoming - reduced));
        nextDamageShields_.erase(shield);
        return reduced;
    };
    for (auto const& attack : attacks_)
    {
        int const aSlot = FindSlot(attacker, attack.Card);
        if (aSlot < 0) continue;
        auto& source = *attacker.Field[aSlot];
        int const bonus = combatAttackBonus(source);
        int const dSlot = attack.Blocker ? FindSlot(defender, attack.Blocker) : -1;
        auto const [blockerAttackDelta, blockerReduction] = dSlot >= 0
            ? blockerCombatModifiers(*defender.Field[dSlot]) : std::pair<int, int>{0, 0};
        int const debuff = raidAttackModifiers_.contains(source.InstanceId) ?
                           raidAttackModifiers_.at(source.InstanceId) : 0;
        auto const modifiedAttack = static_cast<std::int64_t>(source.Attack()) +
            bonus + debuff + blockerAttackDelta;
        Check(modifiedAttack <= std::numeric_limits<int>::max() &&
                  modifiedAttack >= std::numeric_limits<int>::min(),
              "Combat attack overflow");
        int const aDamage = static_cast<int>(std::max<std::int64_t>(0, modifiedAttack));
        bool destroyedEnemyByCombat = false;
        if (!attack.Blocker)
        {
            defender.Life = std::max(0, defender.Life - aDamage);
            journal_.push_back("HeroDamage:" + std::to_string(aDamage));
        }
        else
        {
            // A missing blocker still blocks an assigned attacker. Piercing
            // requires a surviving blocker that takes lethal positive damage.
            if (dSlot >= 0)
            {
                auto& target = *defender.Field[dSlot];
                int const hpBefore = target.Hp();
                int const dDamage = applyNextDamageShield(source.InstanceId,
                                                           std::max(0, target.Attack()));
                source.Damage += dDamage;
                int remainingDamage = std::max(0, aDamage - blockerReduction);
                auto shield = raidShields_.find(target.InstanceId);
                if (shield != raidShields_.end() && remainingDamage > 0)
                {
                    remainingDamage = std::max(0, remainingDamage - shield->second);
                    raidShields_.erase(shield); // first actual damage event
                }
                int const actualDamage = applyNextDamageShield(target.InstanceId, remainingDamage);
                target.Damage += actualDamage;
                if (piercingRaiders_.contains(source.InstanceId) && actualDamage >= hpBefore)
                {
                    int const excess = std::max(0, actualDamage - hpBefore);
                    defender.Life = std::max(0, defender.Life - excess);
                    journal_.push_back("PiercingDamage:" + std::to_string(excess));
                }
                if (target.Hp() <= 0)
                {
                    destroyedEnemyByCombat = true;
                    int const id = target.InstanceId;
                    defender.Field[dSlot].reset();
                    nextDamageShields_.erase(id);
                    defender.Graveyard.push_back(id);
                    Enqueue(defender.Id, id, OriginalId(id), "OnDestroyed");
                }
                journal_.push_back("Combat:" + std::to_string(attack.Card) + ":" +
                                   std::to_string(attack.Blocker));
            }
        }
        if (aSlot >= 0 && attacker.Field[aSlot])
        {
            if (attacker.Field[aSlot]->Hp() <= 0)
            {
                int const id = attacker.Field[aSlot]->InstanceId;
                attacker.Field[aSlot].reset();
                nextDamageShields_.erase(id);
                attacker.Graveyard.push_back(id);
                Enqueue(attacker.Id, id, OriginalId(id), "OnDestroyed");
            }
            else
            {
                attacker.Field[aSlot]->Countdown =
                    CardDefinition(attacker.Field[aSlot]->DefinitionId)
                        .at("AttackInterval").get<int>();
                if (destroyedEnemyByCombat)
                {
                    resolveCombatKill(*attacker.Field[aSlot]);
                }
            }
        }
        if (defender.Life == 0)
        {
            raidAttackModifiers_.clear();
            raidShields_.clear();
            piercingRaiders_.clear();
            chainTargetedByEnemySpell_.clear();
            chainEffectDamageReduction_.clear();
            chainEffectDamagePrevention_.clear();
            phase_ = YakouPhase::Finished;
            winner_ = attacker.Id;
            journal_.push_back("GameOver:" + std::to_string(attacker.Id));
            return;
        }
    }
    TurnOver();
}

void YakouMatch::TurnOver()
{
    manifestConditionWaivers_[players_[active_].Id - 1] = 0;
    raidAttackModifiers_.clear();
    raidShields_.clear();
    piercingRaiders_.clear();
    chainTargetedByEnemySpell_.clear();
    chainEffectDamageReduction_.clear();
    chainEffectDamagePrevention_.clear();
    attacks_.clear();
    active_ = 1 - active_;
    ++turn_;
    phase_ = YakouPhase::TurnStart;
    journal_.push_back("TurnEnded");
}
} // namespace ProjectC::Network
