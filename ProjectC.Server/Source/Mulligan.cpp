#include "ProjectC/GameCommand.hpp"
#include "ProjectC/GameInitializer.hpp"
#include <algorithm>
#include <set>

namespace ProjectC::GameCore
{
namespace
{
// SplitMix64 + rejection sampling. The full state is journaled in InitialState.
// One stream per seat makes simultaneous submission order irrelevant to both decks.
std::uint64_t Next(std::uint64_t& state)
{
    auto value = (state += 0x9e3779b97f4a7c15ULL);
    value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9ULL;
    value = (value ^ (value >> 27)) * 0x94d049bb133111ebULL;
    return value ^ (value >> 31);
}
} // namespace
CommandResult ProcessCommand(GameState& state, SubmitMulliganCommand const& command)
{
    auto reject = [](CommandError code) { return CommandResult{.Code = code}; };
    if (!ValidateGameState(state))
    {
        return reject(CommandError::InvalidState);
    }
    if (state.Preparation != PreparationPhase::Mulligan)
    {
        return reject(CommandError::MulliganClosed);
    }
    auto owner = FindPlayer(state, command.Player);
    if (!owner)
    {
        return reject(CommandError::PlayerNotFound);
    }
    if (owner->MulliganComplete)
    {
        return reject(CommandError::MulliganAlreadyComplete);
    }
    if (command.Cards.size() > 5)
    {
        return reject(CommandError::InvalidMulliganSelection);
    }
    std::set<CardInstanceId> selected;
    for (auto id : command.Cards)
    {
        if (!selected.insert(id).second || std::none_of(owner->Hand.begin(),
                                                        owner->Hand.end(),
                                                        [&](auto const& c) { return c.Id == id; }))
        {
            return reject(CommandError::InvalidMulliganSelection);
        }
    }
    if (owner->Deck.size() < selected.size())
    {
        return reject(CommandError::InsufficientCards);
    }
    GameState candidate = state;
    auto& player = *FindPlayer(candidate, command.Player);
    std::vector<CardInstance> held;
    // Canonical hand order: client selection ordering cannot alter the random result.
    for (auto it = player.Hand.begin(); it != player.Hand.end();)
    {
        if (selected.contains(it->Id))
        {
            held.push_back(*it);
            it = player.Hand.erase(it);
        }
        else
        {
            ++it;
        }
    }
    player.Hand.insert(player.Hand.end(), player.Deck.begin(), player.Deck.begin() + held.size());
    player.Deck.erase(player.Deck.begin(), player.Deck.begin() + held.size());
    player.Deck.insert(player.Deck.end(), held.begin(), held.end());
    if (!held.empty())
    {
        auto& random = candidate.MulliganRandom[candidate.Players[0].Id == command.Player ? 0 : 1];
        for (std::size_t n = player.Deck.size(); n > 1; --n)
        {
            auto bound = static_cast<std::uint64_t>(n),
                 threshold = (std::uint64_t{0} - bound) % bound;
            std::uint64_t value;
            do
            {
                value = Next(random);
            } while (value < threshold);
            std::swap(player.Deck[n - 1], player.Deck[value % bound]);
        }
    }
    player.MulliganSelected.assign(selected.begin(), selected.end());
    player.MulliganComplete = true;
    CommandResult result;
    result.Events.push_back(
        GameEvent{.Type = GameEventType::MulliganCompleted, .TargetPlayerId = command.Player});
    if (candidate.Players[0].MulliganComplete && candidate.Players[1].MulliganComplete)
    {
        candidate.Preparation = PreparationPhase::Playing;
        auto start = ProcessCommand(candidate, StartTurnCommand{candidate.ActivePlayerId});
        if (start.Code != CommandError::Success)
        {
            return start;
        }
        result.Events.insert(result.Events.end(), start.Events.begin(), start.Events.end());
    }
    if (!ValidateGameState(candidate))
    {
        return reject(CommandError::InvalidState);
    }
    state = std::move(candidate);
    return result;
}
} // namespace ProjectC::GameCore
