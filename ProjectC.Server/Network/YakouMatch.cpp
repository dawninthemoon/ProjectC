#include "YakouMatch.hpp"

#include <algorithm>
#include <map>
#include <set>
#include <stdexcept>
#include <string>
#include <utility>

namespace ProjectC::Network
{
namespace
{
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
void YakouMatch::Enqueue(int player, int instance, int definition, std::string const& trigger)
{
    auto const data = CardDefinition(definition);
    for (auto const& effect : data.at("Effects"))
    {
        if (effect.at("Trigger") == trigger)
            pendingEffects_.push_back({player, instance, definition,
                                       effect.at("Id").get<int>(), trigger});
    }
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
        if (method == "Normal" && owner.NormalSummons != 0)
        {
            continue;
        }
        YakouSummonChoice choice;
        choice.Method = method;
        choice.SourceZone = requirement.SourceZone;
        choice.RequiredCount = requirement.ExactCount;
        if (method == "Devour")
        {
            for (auto const& unit : owner.Field)
            {
                if (unit && unit->Hp() > 0)
                {
                    choice.Candidates.push_back(unit->InstanceId);
                }
            }
        }
        else if (method == "Necromancy")
        {
            choice.Candidates = owner.Graveyard;
        }
        else if (method == "Discard")
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
            Check(method == "Normal", "Unsupported summon cost method");
        }
        if (choice.Candidates.size() < choice.RequiredCount)
        {
            continue;
        }
        for (int slot = 0; slot < 5; ++slot)
        {
            if (!owner.Field[slot] ||
                (method == "Devour" &&
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
    // No Ki regeneration at any point.
    phase_ = YakouPhase::Main;
    journal_.push_back("TurnStart:" + std::to_string(player));
}

void YakouMatch::Summon(int player, int handInstance, std::string const& method,
                        std::vector<int> const& materialIds, int slot)
{
    RequireMain(player);
    auto& owner = MutablePlayer(player);
    Check(slot >= 0 && slot < 5, "Invalid fixed field slot");
    Check(Contains(owner.Hand, handInstance), "Card is not in own hand");
    auto const card = CardDefinition(OriginalId(handInstance));
    Check(card.at("Type") == "Monster", "Only monsters can be summoned");
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
    Check(materialIds.size() == count, "Incorrect number of summon materials");
    if (method == "Normal")
        Check(owner.NormalSummons == 0, "Normal summon already used");
    std::set<int> unique;
    int bonusAttack = 0, bonusMaxHp = 0;
    int occupied = owner.Field[slot] ? owner.Field[slot]->InstanceId : 0;
    for (int instance : materialIds)
    {
        Check(instance > 0 && unique.insert(instance).second, "Duplicate/invalid material");
        if (method == "Devour")
        {
            auto const* unit = FindUnit(owner, instance);
            Check(unit != nullptr, "Devour requires allied field monster");
            auto const printed = CardDefinition(OriginalId(instance));
            bonusAttack += printed.at("Attack").get<int>();
            bonusMaxHp += printed.at("MaxHp").get<int>();
        }
        else if (method == "Necromancy")
            Check(Contains(owner.Graveyard, instance), "Necromancy requires own graveyard");
        else if (method == "Discard")
            Check(instance != handInstance && Contains(owner.Hand, instance),
                  "Discard requires other cards in own hand");
        else
            Check(false, "Unexpected normal summon material");
    }
    Check(!occupied || (method == "Devour" && unique.contains(occupied)),
          "Destination slot must be empty or devoured");
    if (method == "Devour")
    {
        for (int instance : materialIds)
        {
            int const index = FindSlot(owner, instance);
            Check(index >= 0, "Missing devour material");
            owner.Field[index].reset();
            owner.Graveyard.push_back(instance);
            Enqueue(player, instance, OriginalId(instance), "OnDevoured");
        }
    }
    else if (method == "Necromancy")
    {
        for (int instance : materialIds)
        {
            Remove(owner.Graveyard, instance);
            owner.Exile.push_back(instance);
            Enqueue(player, instance, OriginalId(instance), "OnBanishedForNecromancyCost");
        }
    }
    else if (method == "Discard")
    {
        for (int instance : materialIds)
        {
            Remove(owner.Hand, instance);
            owner.Graveyard.push_back(instance);
            Enqueue(player, instance, OriginalId(instance), "OnDiscarded");
        }
    }
    Remove(owner.Hand, handInstance);
    owner.Field[slot] = YakouUnit{.InstanceId = handInstance,
                                   .DefinitionId = OriginalId(handInstance),
                                   .BaseAttack = card.at("Attack").get<int>(),
                                   .BaseMaxHp = card.at("MaxHp").get<int>(),
                                   .BonusAttack = bonusAttack,
                                   .BonusMaxHp = bonusMaxHp,
                                   .Countdown = card.at("AttackInterval").get<int>(),
                                   .WasDevourSummoned = method == "Devour"};
    if (method == "Normal") ++owner.NormalSummons;
    Enqueue(player, handInstance, OriginalId(handInstance), "OnSummoned");
    if (method == "Devour") Enqueue(player, handInstance, OriginalId(handInstance), "OnDevourSummoned");
    if (method == "Necromancy") Enqueue(player, handInstance, OriginalId(handInstance), "OnNecromancySummoned");
    journal_.push_back("Summon:" + method + ":" + std::to_string(handInstance));
}

void YakouMatch::Manifest(int player, int fieldInstance, int manifestationDefinition)
{
    RequireMain(player);
    auto& owner = MutablePlayer(player);
    Check(owner.Manifestations == 0, "Already manifested this turn");
    auto& unit = FieldUnit(owner, fieldInstance);
    Check(!unit.Manifested, "Already a manifestation");
    auto const source = CardDefinition(unit.DefinitionId);
    auto const target = CardDefinition(manifestationDefinition);
    Check(target.at("Type") == "Manifestation", "Invalid manifestation definition");
    bool allowed = false;
    for (auto const& entry : source.at("AllowedManifestations"))
        if (entry == manifestationDefinition) allowed = true;
    Check(allowed, "Not a listed manifestation for this monster");
    auto const& data = target.at("SummonCostJson").at("Manifestation");
    auto const ki = data.at("Ki").get<int>();
    Check(ki == 2 && owner.Ki >= ki, "Insufficient Ki");
    auto const& conditions = data.at("Condition");
    if (conditions.contains("WasDevourSummoned"))
        Check(unit.WasDevourSummoned == conditions.at("WasDevourSummoned").get<bool>(),
              "Devour condition not met");
    if (conditions.contains("FriendlyGraveyardArchetypeCountAtLeast"))
    {
        auto const archetype = conditions.at("Archetype").get<std::string>();
        int tally = 0;
        for (int instance : owner.Graveyard)
            if (HasArchetype(OriginalId(instance), archetype)) ++tally;
        Check(tally >= conditions.at("FriendlyGraveyardArchetypeCountAtLeast").get<int>(),
              "Graveyard condition not met");
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
        Check(tally >= requirement.at("AtLeast").get<int>(), "Zone condition not met");
    }
    owner.Ki -= ki;
    ++owner.Manifestations;
    unit.BaseAttack = target.at("Attack").get<int>();
    unit.BaseMaxHp = target.at("MaxHp").get<int>();
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
        owner.Graveyard.push_back(fieldInstance);
    }
}

void YakouMatch::Postpone(int player, int fieldInstance)
{
    RequireMain(player);
    auto& owner = MutablePlayer(player);
    auto& unit = FieldUnit(owner, fieldInstance);
    Check(unit.Countdown == 0 && !unit.PostponedThisTurn && owner.Ki >= 1,
          "Cannot postpone this raid");
    --owner.Ki;
    unit.Countdown = 1;
    unit.PostponedThisTurn = true;
    journal_.push_back("Postpone:" + std::to_string(fieldInstance));
}

void YakouMatch::Redraw(int player, std::vector<int> const& cards)
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
    --owner.Ki;
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

void YakouMatch::PassRaidResponse(int player)
{
    Check(phase_ == YakouPhase::RaidResponse, "No raid response window");
    bool const closed = response_.Pass(player);
    journal_.push_back("RaidResponsePass:" + std::to_string(player));
    if (closed)
    {
        // Empty chain resolution is intentional: no response effects are supported
        // yet, and the raid must not resolve while a player has priority.
        Check(response_.TakeResolution().empty(), "Unresolved response effects");
        journal_.push_back("RaidResponseClosed");
    }
}

void YakouMatch::ResolveRaid(int defendingPlayer)
{
    Check(phase_ == YakouPhase::RaidResponse && !response_.IsOpen() &&
              defendingPlayer != ActivePlayer() &&
              (defendingPlayer == 1 || defendingPlayer == 2),
          "Cannot resolve raid");
    auto& attacker = players_[active_];
    auto& defender = MutablePlayer(defendingPlayer);
    for (auto const& attack : attacks_)
    {
        int const aSlot = FindSlot(attacker, attack.Card);
        if (aSlot < 0) continue;
        auto& source = *attacker.Field[aSlot];
        Enqueue(attacker.Id, source.InstanceId, source.DefinitionId, "OnRaid");
        int const aDamage = std::max(0, source.Attack());
        if (!attack.Blocker)
        {
            defender.Life = std::max(0, defender.Life - aDamage);
            journal_.push_back("HeroDamage:" + std::to_string(aDamage));
        }
        else
        {
            int const dSlot = FindSlot(defender, attack.Blocker);
            // A missing blocker still blocks an assigned attacker; no direct
            // hero damage unless a separate piercing effect is later implemented.
            if (dSlot >= 0)
            {
                auto& target = *defender.Field[dSlot];
                int const dDamage = std::max(0, target.Attack());
                source.Damage += dDamage;
                target.Damage += aDamage; // simultaneous combat
                if (target.Hp() <= 0)
                {
                    int const id = target.InstanceId;
                    defender.Field[dSlot].reset();
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
                attacker.Graveyard.push_back(id);
                Enqueue(attacker.Id, id, OriginalId(id), "OnDestroyed");
            }
            else
            {
                attacker.Field[aSlot]->Countdown =
                    CardDefinition(attacker.Field[aSlot]->DefinitionId)
                        .at("AttackInterval").get<int>();
            }
        }
        if (defender.Life == 0)
        {
            phase_ = YakouPhase::Finished;
            journal_.push_back("GameOver:" + std::to_string(attacker.Id));
            return;
        }
    }
    TurnOver();
}

void YakouMatch::TurnOver()
{
    attacks_.clear();
    active_ = 1 - active_;
    ++turn_;
    phase_ = YakouPhase::TurnStart;
    journal_.push_back("TurnEnded");
}
} // namespace ProjectC::Network
