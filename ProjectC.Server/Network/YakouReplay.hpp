#pragma once

#include "YakouMatch.hpp"
#include <array>
#include <cstdint>
#include <string>
#include <utility>
#include <vector>

namespace ProjectC::Network
{
// Server-private command journal. Initial deck orders and command targets may
// contain hidden information; this structure must never be sent to clients.
enum class YakouReplayOperation
{
    StartTurn,
    Summon,
    Manifest,
    Postpone,
    Redraw,
    BeginRaid,
    AssignBlockers,
    PassRaidResponse,
    ResolveRaid,
    ResolvePendingEffect,
};

struct YakouReplayCommand
{
    YakouReplayOperation Operation = YakouReplayOperation::StartTurn;
    int Player = 0;
    int CardId = 0;
    int TargetId = 0;
    int DefinitionId = 0;
    int Slot = 0;
    std::string Method;
    std::vector<int> Cards;
    std::vector<std::pair<int, int>> Blockers;
};

struct YakouReplayStep
{
    bool Accepted = false;
    std::string Reason;
    std::uint64_t BeforeHash = 0;
    std::uint64_t AfterHash = 0;
};

struct YakouReplayResult
{
    YakouMatch FinalState;
    std::vector<YakouReplayStep> Steps;
};

// Replays accepted and rejected commands in order against a fresh authoritative
// match. Invalid commands must leave the full server state unchanged. Catalog
// lifetime must exceed the returned match's lifetime, just as for YakouMatch.
[[nodiscard]] YakouReplayResult ReplayYakou(
    YakouPrototype const& catalog,
    int firstPlayer,
    std::array<std::vector<int>, 2> const& initialDecks,
    std::vector<YakouReplayCommand> const& commands);
} // namespace ProjectC::Network
