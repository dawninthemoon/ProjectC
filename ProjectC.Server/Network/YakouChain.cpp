#include "YakouChain.hpp"
#include <algorithm>
#include <stdexcept>

namespace ProjectC::Network
{
void YakouChain::CheckPlayer(int player)
{
    if (player != 1 && player != 2)
        throw std::invalid_argument("Invalid Yakou chain player");
}
void YakouChain::Open(int activePlayer, YakouChainLink const& initial)
{
    CheckPlayer(activePlayer);
    CheckPlayer(initial.Controller);
    if (open_ || !resolution_.empty() || initial.Controller != activePlayer)
        throw std::invalid_argument("Cannot open Yakou chain");
    open_ = true;
    priority_ = Other(activePlayer);
    consecutivePasses_ = 0;
    stack_ = {initial};
}
void YakouChain::Respond(int player, YakouChainLink const& link)
{
    CheckPlayer(player);
    if (!open_ || priority_ != player || link.Controller != player ||
        stack_.size() >= MaximumLinks || !resolution_.empty())
        throw std::invalid_argument("Not authorized to respond");
    stack_.push_back(link);
    priority_ = Other(player);
    consecutivePasses_ = 0;
}
bool YakouChain::Pass(int player)
{
    CheckPlayer(player);
    if (!open_ || player != priority_)
        throw std::invalid_argument("No priority to pass");
    if (++consecutivePasses_ == 1)
    {
        priority_ = Other(player);
        return false;
    }
    open_ = false;
    priority_ = 0;
    resolution_.assign(stack_.rbegin(), stack_.rend());
    stack_.clear();
    consecutivePasses_ = 0;
    return true;
}
void YakouChain::Defer(YakouChainLink const& link)
{
    CheckPlayer(link.Controller);
    if (deferred_.size() >= MaximumLinks)
        throw std::invalid_argument("Too many deferred triggers");
    deferred_.push_back(link);
}
std::vector<YakouChainLink> YakouChain::TakeResolution()
{
    if (open_)
        throw std::invalid_argument("Cannot resolve open chain");
    auto result = std::move(resolution_);
    resolution_.clear();
    return result;
}
bool YakouChain::OpenDeferred(int activePlayer)
{
    CheckPlayer(activePlayer);
    if (open_ || !resolution_.empty())
        throw std::invalid_argument("Finish current chain before deferred triggers");
    if (deferred_.empty()) return false;
    // Stable ordering preserves each player's chosen order as registered.
    std::stable_sort(deferred_.begin(), deferred_.end(),
        [activePlayer](YakouChainLink const& a, YakouChainLink const& b) {
            return a.Controller == activePlayer && b.Controller != activePlayer;
        });
    for (auto const& link : deferred_)
    {
        CheckPlayer(link.Controller);
        if (stack_.size() >= MaximumLinks)
            throw std::invalid_argument("Too many deferred chain links");
        stack_.push_back(link);
    }
    deferred_.clear();
    open_ = true;
    // AP/NAP ordered automatic effects are registered before response priority.
    priority_ = activePlayer;
    consecutivePasses_ = 0;
    return true;
}
} // namespace ProjectC::Network
