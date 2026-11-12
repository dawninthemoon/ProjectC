#pragma once
#include "YakouPrototype.hpp"
#include "YakouChain.hpp"

#include <array>
#include <cstdint>
#include <map>
#include <optional>
#include <string>
#include <tuple>
#include <unordered_map>
#include <vector>

namespace ProjectC::Network
{
// Separate, opt-in Yakou prototype state machine. Never used by legacy
// MatchSession until the remaining authoring effects and response protocol work.
enum class YakouPhase { TurnStart, Main, AssignBlockers, RaidResponse, Finished };

struct YakouUnit
{
    int InstanceId = 0;
    int DefinitionId = 0;
    int BaseAttack = 0;
    int BaseMaxHp = 0;
    int BonusAttack = 0; // Printed stats inherited from devoured materials.
    int BonusMaxHp = 0;
    int Damage = 0;      // Persistent damage, not current HP.
    int Countdown = 0;
    bool WasDevourSummoned = false;
    bool Manifested = false;
    bool PostponedThisTurn = false;
    int BaseMaxBlocks = 1;
    int MaxBlocks = 1;
    [[nodiscard]] int Attack() const { return BaseAttack + BonusAttack; }
    [[nodiscard]] int MaxHp() const { return BaseMaxHp + BonusMaxHp; }
    [[nodiscard]] int Hp() const { return MaxHp() - Damage; }
};

struct YakouPlayer
{
    int Id = 0;
    int Life = 20;
    int Ki = 3;
    int NormalSummons = 0;
    int Manifestations = 0;
    int Redraws = 0;
    std::vector<int> Deck;
    std::vector<int> Hand;
    std::vector<int> Graveyard;
    std::vector<int> Exile;
    std::array<std::optional<YakouUnit>, 5> Field{};
};

struct YakouAttack
{
    int Card = 0;
    int Slot = 0;
    int Blocker = 0; // 0: unblocked
};
struct YakouPendingEffect
{
    int Player = 0;
    int SourceCard = 0;
    int DefinitionId = 0;
    int EffectId = 0;
    std::string Trigger;
    // Immutable event context; the source may have left the field already.
    int RelatedCard = 0;
};

// Server-derived selection metadata. Candidates are instance IDs, never client-authored.
struct YakouSummonChoice
{
    std::string Method;
    std::string SourceZone;
    std::size_t RequiredCount = 0;
    std::vector<int> Candidates;
    std::vector<int> AvailableSlots;
};

// Only the effect controller may receive these server-derived instance IDs.
struct YakouEffectChoice
{
    std::string Operation;
    std::string SourceZone;
    int MinCount = 0;
    int MaxCount = 0;
    std::vector<int> Candidates;
};

class YakouMatch
{
public:
    // The deck order is authoritative and pre-shuffled by the caller.
    // No random generator or network state is implicitly consulted.
    YakouMatch(YakouPrototype const& catalog,
               int firstPlayer,
               std::array<std::vector<int>, 2> deckDefinitions);

    [[nodiscard]] YakouPhase Phase() const { return phase_; }
    [[nodiscard]] int ActivePlayer() const { return players_[active_].Id; }
    [[nodiscard]] int TurnNumber() const { return turn_; }
    [[nodiscard]] YakouPlayer const& Player(int id) const;
    [[nodiscard]] std::vector<YakouAttack> const& Attacks() const { return attacks_; }
    [[nodiscard]] std::vector<YakouPendingEffect> const& PendingEffects() const
    {
        return pendingEffects_;
    }
    [[nodiscard]] std::vector<std::string> const& Journal() const { return journal_; }
    [[nodiscard]] int PriorityPlayer() const { return response_.PriorityPlayer(); }
    [[nodiscard]] int WinnerPlayer() const { return winner_; }
    // Viewer projection excludes both deck orders and every opponent hand identity.
    // This is a server prototype API, not yet a public WebSocket schema.
    [[nodiscard]] Json ViewerSnapshot(int viewer) const;
    // Internal replay verification only: includes private hands and deck order.
    // Do not expose this hash or its source state to clients.
    [[nodiscard]] std::uint64_t ServerStateHash() const;

    // Every failure throws std::invalid_argument *before* any mutation.
    // These gameplay operations are not yet a public online protocol.
    // Public to the controlling player only; transport must not reveal hand IDs
    // to spectators or the opponent. Empty result means no legal summon methods.
    [[nodiscard]] std::vector<YakouSummonChoice> SummonChoices(int player,
                                                               int handInstance) const;
    // Resolve exactly one queued authored trigger, using a server-validated
    // target instance (or zero for targetless/declined optional effects).
    // Unsupported operations throw without changing match state or the queue.
    [[nodiscard]] YakouEffectChoice PendingEffectChoices(int player) const;
    void ResolvePendingEffect(int player, int selectedTarget = 0);
    void StartTurn(int player);
    void Summon(int player, int handInstance, std::string const& method,
                std::vector<int> const& materialIds, int slot);
    void Manifest(int player, int fieldInstance, int manifestationDefinition);
    void Postpone(int player, int fieldInstance);
    void Redraw(int player, std::vector<int> const& handInstances);
    void BeginRaid(int player);
    void AssignBlockers(int defendingPlayer, std::vector<std::pair<int, int>> const& assignments);
    // Two authorized consecutive passes close the raid preparation window.
    // Card response activation is deliberately unavailable until effect execution exists.
    void PassRaidResponse(int player);
    void ResolveRaid(int defendingPlayer);

private:
    void StartTurnImpl(int player);
    void SummonImpl(int player, int handInstance, std::string const& method,
                    std::vector<int> const& materialIds, int slot);
    void ManifestImpl(int player, int fieldInstance, int manifestationDefinition);
    void PostponeImpl(int player, int fieldInstance);
    void RedrawImpl(int player, std::vector<int> const& cards);
    void BeginRaidImpl(int player);
    void AssignBlockersImpl(int defendingPlayer, std::vector<std::pair<int, int>> const& assignments);
    void PassRaidResponseImpl(int player);
    void ResolveRaidImpl(int defendingPlayer);
    [[nodiscard]] Json ServerCanonicalState() const;
    Json CardDefinition(int id) const;
    YakouPlayer& MutablePlayer(int id);
    YakouUnit& FieldUnit(YakouPlayer& player, int instanceId);
    void RequireMain(int player) const;
    void TurnOver();
    void Enqueue(int player, int instanceId, int definitionId, std::string const& trigger,
                 int relatedCard = 0);
    int OriginalId(int instanceId) const;
    bool HasArchetype(int definitionId, std::string const& archetype) const;

    YakouPrototype const* catalog_;
    std::array<YakouPlayer, 2> players_;
    std::unordered_map<int, int> original_;
    int nextInstance_ = 1;
    int active_ = 0;
    int first_ = 0;
    int turn_ = 1;
    int winner_ = 0;
    YakouPhase phase_ = YakouPhase::TurnStart;
    YakouChain response_;
    std::vector<YakouAttack> attacks_;
    std::vector<YakouPendingEffect> pendingEffects_;
    // Turn, controller, authored card name key and authored effect identity.
    std::map<std::tuple<int, int, std::string, int>, int> effectUses_;
    std::vector<std::string> journal_;
};
} // namespace ProjectC::Network
