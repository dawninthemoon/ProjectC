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

// Do not silently omit a newly authored cost, condition, duration or target rule.
void CheckSupportedAction(Json const& action)
{
    Check(action.is_object() && action.contains("Operation") && action.at("Operation").is_string(),
          "Malformed Yakou effect operation");
    auto const operation = action.at("Operation").get<std::string>();
    if (action.contains("Condition"))
    {
        auto const& condition = action.at("Condition");
        Check(operation == "Salvage" && condition.is_object() && condition.size() == 1 &&
                  HasOnlyFields(condition, {"GraveyardArchetypeCountAtLeast"}) &&
                  condition.at("GraveyardArchetypeCountAtLeast").is_number_integer() &&
                  condition.at("GraveyardArchetypeCountAtLeast").get<int>() >= 0 &&
                  action.contains("RequiredArchetype") && action.at("RequiredArchetype").is_string(),
              "Conditional Yakou effect not yet supported");
    }
    bool supported = false;
    if (operation == "Mill")
    {
        supported = HasOnlyFields(action, {"Operation", "Count", "From", "To"});
    }
    else if (operation == "ModifyCountdown")
    {
        supported = HasOnlyFields(action, {"Operation", "Target", "Delta", "Optional"});
    }
    else if (operation == "SetCountdown")
    {
        supported = HasOnlyFields(action, {"Operation", "Target", "Value", "Optional"});
    }
    else if (operation == "HealAccumulatedDamage")
    {
        supported = HasOnlyFields(action, {"Operation", "Target", "Amount", "Optional"});
    }
    else if (operation == "Salvage")
    {
        supported = HasOnlyFields(action, {"Operation", "From", "To", "Count", "CardType", "RequiredArchetype", "Optional", "Condition"});
    }
    else if (operation == "LookAndChoose")
    {
        supported = HasOnlyFields(action, {"Operation", "From", "LookCount", "ChooseCount", "ChosenDestination", "RestDestination"});
    }
    Check(supported, "Unsupported Yakou effect operation or action fields: " + operation);
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
        }
        Json visiblePlayer = {{"PlayerId", player.Id}, {"Life", player.Life}, {"Ki", player.Ki},
                              {"HandCount", player.Hand.size()}, {"DeckCount", player.Deck.size()},
                              {"Hand", std::move(hand)}, {"FieldSlots", std::move(field)},
                              {"Graveyard", std::move(grave)}, {"Exile", std::move(exile)}};
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
                    }
                }
            }
            visiblePlayer["SummonChoices"] = std::move(summonChoices);
            visiblePlayer["NormalSummonsUsed"] = player.NormalSummons;
            visiblePlayer["ManifestationsUsed"] = player.Manifestations;
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
                           {"Trigger", effect.Trigger}, {"RelatedCard", effect.RelatedCard}});
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
    // Preserve the existing canonical replay fixture whenever no raid shield
    // is pending; include live reductions in the private hash when present.
    if (!raidShields_.empty())
    {
        Json shields = Json::array();
        for (auto const& [instance, amount] : raidShields_)
            shields.push_back(Json::array({instance, amount}));
        state["RaidShields"] = std::move(shields);
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
                         int relatedCard)
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
                auto const archetype = action.at("RequiredArchetype").get<std::string>();
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
        }
        pendingEffects_.push_back({player, instance, definition,
                                   effect.at("Id").get<int>(), trigger, relatedCard});
    }
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
    Check(actions.is_array() && actions.size() == 1,
          "Unsupported multi-action or empty Yakou effect");
    auto const& action = actions.at(0);
    CheckSupportedAction(action);
    YakouEffectChoice choice;
    choice.Operation = action.at("Operation").get<std::string>();
    auto const& owner = Player(player);
    if (choice.Operation == "Mill")
    {
        Check(action.at("From") == "FriendlyDeck" && action.at("To") == "Graveyard" &&
                  action.at("Count").get<int>() > 0 && action.at("Count").get<int>() <= 30,
              "Unsupported mill shape");
        Check(owner.Deck.size() >= static_cast<std::size_t>(action.at("Count").get<int>()),
              "Insufficient cards for authored mill");
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
                  action.at("Count") == 1 && !action.contains("ExcludePaidCostCards"),
              "Unsupported salvage shape");
        choice.SourceZone = "FriendlyGraveyard";
        for (int instance : owner.Graveyard)
        {
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
    else
    {
        Check(choice.Operation == "ModifyCountdown" || choice.Operation == "SetCountdown" ||
                  choice.Operation == "HealAccumulatedDamage", "Unsupported Yakou effect operation: " + choice.Operation);
        auto const targetMode = action.at("Target").get<std::string>();
        Check(targetMode == "Self" || targetMode == "FriendlyMonster" || targetMode == "OtherFriendlyMonster" ||
                  targetMode == "DevouringMonster", "Unsupported friendly monster target mode");
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
            if (field && (targetMode != "OtherFriendlyMonster" || field->InstanceId != queued.SourceCard))
            {
                choice.Candidates.push_back(field->InstanceId);
            }
        }
    }
    choice.MaxCount = 1;
    choice.MinCount = action.value("Optional", false) ? 0 : 1;
    return choice;
}

void YakouMatch::ResolvePendingEffect(int player, int selectedTarget)
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
    Check(actions.is_array() && actions.size() == 1,
          "Unsupported multi-action or empty Yakou effect");
    auto const& action = actions.at(0);
    // Validate the complete authored shape before changing any state.
    CheckSupportedAction(action);
    auto const operation = action.at("Operation").get<std::string>();

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
    if (operation == "Mill")
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
                  action.at("Count") == 1 && !action.contains("ExcludePaidCostCards"),
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
    else
    {
        auto const targetMode = action.at("Target").get<std::string>();
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
            auto& unit = *owner.Field[slot];
            if (operation == "HealAccumulatedDamage")
            {
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
    if (limit > 0 && !declined)
    {
        ++next.effectUses_[usage];
    }
    next.pendingEffects_.erase(next.pendingEffects_.begin());
    *this = std::move(next);
}

void YakouMatch::ActivateMainSpell(int player, int handInstance)
{
    YakouMatch next = *this;
    next.ActivateMainSpellImpl(player, handInstance);
    *this = std::move(next);
}

void YakouMatch::ActivateMainSpellImpl(int player, int handInstance)
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
              definition.at("SummonCostJson").at("Activation").is_object() &&
              definition.at("SummonCostJson").at("Activation").empty(),
          "Paid spell costs are not yet supported by this activation API");
    auto const& effects = definition.at("Effects");
    Check(effects.is_array() && effects.size() == 1,
          "Only one supported spell effect may be activated");
    auto const& effect = effects.at(0);
    Check(effect.at("Type") == "Triggered" &&
              effect.at("Trigger") == "OnResolve" &&
              effect.at("Actions").is_array() &&
              effect.at("Actions").size() == 1,
          "Unsupported main-phase spell effect");
    CheckSupportedAction(effect.at("Actions")[0]);

    // Move the authored spell into its public graveyard, then queue precisely
    // its OnResolve operation. The outer candidate copy commits only if
    // target availability, the per-turn limit and effect shape are valid.
    Remove(owner.Hand, handInstance);
    owner.Graveyard.push_back(handInstance);
    Enqueue(player, handInstance, definitionId, "OnResolve");
    Check(pendingEffects_.size() == 1 &&
              pendingEffects_.front().SourceCard == handInstance,
          "Spell did not enqueue exactly one effect");
    auto const choices = PendingEffectChoices(player);
    Check(choices.MinCount == 0 || !choices.Candidates.empty() ||
              (choices.MinCount == 0 && choices.MaxCount == 0),
          "Spell has no valid effect target");
    journal_.push_back("SpellActivated:" + std::to_string(handInstance));
}


void YakouMatch::BeginSpellResponse(int player, int handInstance, int targetInstance)
{
    YakouMatch next = *this;
    next.BeginSpellResponseImpl(player, handInstance, targetInstance);
    *this = std::move(next);
}

void YakouMatch::BeginSpellResponseImpl(int player, int handInstance, int targetInstance)
{
    // Reuse all authored spell and cost checks; do not expose the provisional
    // effect to the normal resolver before the priority window closes.
    ActivateMainSpellImpl(player, handInstance);
    auto const choice = PendingEffectChoices(player);
    if (choice.MaxCount == 0)
    {
        Check(targetInstance == 0, "Targetless spell cannot select a target");
    }
    else
    {
        Check(targetInstance == 0 ? choice.MinCount == 0 :
                  Contains(choice.Candidates, targetInstance),
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
    Check(player != ActivePlayer() && (player == 1 || player == 2),
          "Only the opponent may negate this opening spell");
    Check(response_.StackSize() == 1,
          "This prototype supports one counterspell per response chain");
    auto& owner = MutablePlayer(player);
    Check(Contains(owner.Hand, handInstance), "Counterspell is not in owner's hand");
    auto const card = CardDefinition(OriginalId(handInstance));
    Check(card.at("Id") == 3020 && card.at("Type") == "Spell" &&
              card.at("SummonCostJson").at("Activation").is_object() &&
              card.at("SummonCostJson").at("Activation").size() == 1 &&
              card.at("SummonCostJson").at("Activation").at("Discard") == 2,
          "Only authored discard-two counterspell is supported");
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
          "Counterspell needs two distinct other hand cards");
    for (int cardId : discarded)
        Check(Contains(owner.Hand, cardId), "Invalid counterspell discard material");

    // The chain's first link is the spell being answered. Its identity is
    // server-derived; incoming clients never supply the target spell ID.
    for (int cardId : discarded)
    {
        Remove(owner.Hand, cardId);
        owner.Graveyard.push_back(cardId);
    }
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
    Check(!resolution.empty() && resolution.size() <= 2,
          "Unsupported resolved spell chain length");
    bool negated = false;
    for (auto const& link : resolution)
    {
        if (link.Controller != ActivePlayer())
        {
            Check(resolution.size() == 2 && !negated &&
                      link.Controller == 3 - ActivePlayer(),
                  "Unexpected chain counterspell");
            negated = true;
            journal_.push_back("SpellNegated:" +
                               std::to_string(resolution.back().SourceInstance));
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
            ResolvePendingEffect(link.Controller,
                                 link.Targets.empty() ? 0 : link.Targets.front());
        }
    }
    phase_ = YakouPhase::Main;
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
    // No Ki regeneration at any point.
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
            int const sourceDefinition = owner.Field[index]->DefinitionId;
            owner.Field[index].reset();
            owner.Graveyard.push_back(instance);
            Enqueue(player, instance, sourceDefinition, "OnDevoured", handInstance);
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
                                   .WasDevourSummoned = method == "Devour",
                                   .BaseMaxBlocks = maxBlocks,
                                   .MaxBlocks = maxBlocks};
    if (method == "Normal") ++owner.NormalSummons;
    Enqueue(player, handInstance, OriginalId(handInstance), "OnSummoned");
    if (method == "Devour") Enqueue(player, handInstance, OriginalId(handInstance), "OnDevourSummoned");
    if (method == "Necromancy") Enqueue(player, handInstance, OriginalId(handInstance), "OnNecromancySummoned");
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
    --owner.Ki;
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
    Check(response_.StackSize() == 0,
          "This prototype supports only one defensive raid response");
    auto& owner = MutablePlayer(player);
    Check(Contains(owner.Hand, handInstance), "Raid response must be in owner's hand");
    Check(FindUnit(owner, friendlyUnit) != nullptr,
          "Raid shield target must be a controlled field monster");
    auto const card = CardDefinition(OriginalId(handInstance));
    Check(card.at("Id") == 3016 && card.at("Type") == "Spell",
          "Only authored raid shield 3016 is supported");
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
              action.at("Target") == "FriendlyMonster" &&
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
                        effects[0].at("Id").get<int>(), "OnResolve", {friendlyUnit}});
    ++effectUses_[key];
    journal_.push_back("RaidShieldActivated:" + std::to_string(handInstance));
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
        // Resolve supported one-link defensive responses after double-pass.
        // Unsupported chains fail atomically in the caller's candidate copy.
        auto const links = response_.TakeResolution();
        Check(links.size() <= 1, "Unsupported raid chain length");
        for (auto const& link : links)
        {
            Check(link.Controller == 3 - ActivePlayer() &&
                      link.Trigger == "OnResolve" && link.Targets.size() == 1,
                  "Unsupported raid response link");
            auto const card = CardDefinition(OriginalId(link.SourceInstance));
            Check(card.at("Id") == 3016 &&
                      link.EffectId == card.at("Effects")[0].at("Id").get<int>(),
                  "Unknown raid response effect");
            int const target = link.Targets.front();
            Check(FindUnit(Player(link.Controller), target) != nullptr,
                  "Raid shield target left its owner's field");
            auto const& action = card.at("Effects")[0].at("Actions")[0];
            int const amount = action.at("Amount").get<int>();
            Check(amount > 0 &&
                      (raidShields_.find(target) == raidShields_.end()),
                  "Invalid or duplicate raid shield");
            raidShields_[target] = amount;
            journal_.push_back("RaidShieldResolved:" + std::to_string(target));
        }
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
    auto combatDamageReduction = [&](YakouUnit const& unit) -> int
    {
        int reduction = 0;
        auto const card = CardDefinition(unit.DefinitionId);
        for (auto const& effect : card.at("Effects"))
        {
            if (effect.at("Trigger") != "OnAssignedAsBlocker") continue;
            Check(effect.at("Type") == "Triggered" &&
                      effect.at("Actions").is_array() && effect.at("Actions").size() == 1,
                  "Unsupported blocker trigger");
            auto const& action = effect.at("Actions")[0];
            Check(action.is_object() &&
                      HasOnlyFields(action, {"Operation", "Target", "Amount", "Duration"}) &&
                      action.at("Operation") == "ReduceDamage" &&
                      action.at("Target") == "Self" &&
                      action.at("Duration") == "CurrentCombat" &&
                      action.at("Amount").is_number_integer(),
                  "Unsupported blocker combat effect");
            int const amount = action.at("Amount").get<int>();
            Check(amount >= 0, "Invalid blocker damage reduction");
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
            Check(reduction <= std::numeric_limits<int>::max() - amount,
                  "Blocker damage reduction overflow");
            reduction += amount;
        }
        return reduction;
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
    for (auto const& attack : attacks_)
    {
        int const aSlot = FindSlot(attacker, attack.Card);
        if (aSlot < 0) continue;
        auto& source = *attacker.Field[aSlot];
        int const bonus = combatAttackBonus(source);
        auto const modifiedAttack = static_cast<std::int64_t>(source.Attack()) + bonus;
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
            int const dSlot = FindSlot(defender, attack.Blocker);
            // A missing blocker still blocks an assigned attacker; no direct
            // hero damage unless a separate piercing effect is later implemented.
            if (dSlot >= 0)
            {
                auto& target = *defender.Field[dSlot];
                int const dDamage = std::max(0, target.Attack());
                source.Damage += dDamage;
                int const reduction = combatDamageReduction(target);
                int remainingDamage = std::max(0, aDamage - reduction);
                auto shield = raidShields_.find(target.InstanceId);
                if (shield != raidShields_.end() && remainingDamage > 0)
                {
                    remainingDamage = std::max(0, remainingDamage - shield->second);
                    raidShields_.erase(shield); // first actual damage event
                }
                target.Damage += remainingDamage; // simultaneous combat
                if (target.Hp() <= 0)
                {
                    destroyedEnemyByCombat = true;
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
                if (destroyedEnemyByCombat)
                {
                    resolveCombatKill(*attacker.Field[aSlot]);
                }
            }
        }
        if (defender.Life == 0)
        {
            raidShields_.clear();
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
    raidShields_.clear();
    attacks_.clear();
    active_ = 1 - active_;
    ++turn_;
    phase_ = YakouPhase::TurnStart;
    journal_.push_back("TurnEnded");
}
} // namespace ProjectC::Network
