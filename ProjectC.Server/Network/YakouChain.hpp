#pragma once
#include <cstddef>
#include <string>
#include <vector>

namespace ProjectC::Network
{
// A resolution record holds immutable action data; source disappearance alone
// cannot cancel an already committed link. Target validity is checked by the
// future effect interpreter at resolution time.
struct YakouChainLink
{
    int Controller = 0;
    int SourceInstance = 0;
    int EffectId = 0;
    std::string Trigger;
    std::vector<int> Targets;
    bool operator==(YakouChainLink const&) const = default;
};

// Server-private replay material. Never include this in a viewer snapshot:
// chain targets may contain private card identities.
struct YakouChainState
{
    bool Open = false;
    int Priority = 0;
    int ConsecutivePasses = 0;
    std::vector<YakouChainLink> Stack;
    std::vector<YakouChainLink> Resolution;
    std::vector<YakouChainLink> Deferred;
};

class YakouChain
{
public:
    // Empty response window for raid preparation. Defender receives priority first.
    void OpenWindow(int activePlayer);
    void Open(int activePlayer, YakouChainLink const& initial);
    void Respond(int player, YakouChainLink const& link);
    // Returns true only after two consecutive passes close the current window.
    bool Pass(int player);
    void Defer(YakouChainLink const& link);
    // Caller must execute these records in returned order; no mid-chain responses.
    [[nodiscard]] std::vector<YakouChainLink> TakeResolution();
    // After resolving the active chain, start another window for deferred triggers.
    // Active player triggers are registered first, then opponent triggers (LIFO
    // resolution consequently runs opponent triggers first).
    bool OpenDeferred(int activePlayer);

    [[nodiscard]] YakouChainState ServerState() const;
    [[nodiscard]] bool IsOpen() const { return open_; }
    [[nodiscard]] int PriorityPlayer() const { return priority_; }
    [[nodiscard]] int StackSize() const { return static_cast<int>(stack_.size()); }
    [[nodiscard]] int PendingTriggerCount() const { return static_cast<int>(deferred_.size()); }

private:
    static constexpr std::size_t MaximumLinks = 128;
    static int Other(int player) { return 3 - player; }
    static void CheckPlayer(int player);
    bool open_ = false;
    int priority_ = 0;
    int consecutivePasses_ = 0;
    std::vector<YakouChainLink> stack_;
    std::vector<YakouChainLink> resolution_;
    std::vector<YakouChainLink> deferred_;
};
} // namespace ProjectC::Network
