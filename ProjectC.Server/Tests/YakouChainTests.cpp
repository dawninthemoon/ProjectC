#include "YakouChain.hpp"
#include <iostream>
#include <stdexcept>

using namespace ProjectC::Network;
namespace
{
void Check(bool b, char const* why)
{
    if (!b) throw std::runtime_error(why);
}
template<class F> void Reject(F f)
{
    bool rejected = false;
    try { f(); } catch (std::invalid_argument const&) { rejected = true; }
    Check(rejected, "Invalid priority operation accepted");
}
YakouChainLink Link(int owner, int id)
{
    return YakouChainLink{owner, id, id, "OnResolve", {}};
}
}
int main()
{
    try
    {
        YakouChain chain;
        chain.Open(1, Link(1, 1));
        Check(chain.StackSize() == 1 && chain.PriorityPlayer() == 2,
              "First effect is chain 1 and grants opponent priority");
        Reject([&] { chain.Respond(1, Link(1, 999)); });
        chain.Respond(2, Link(2, 2));
        chain.Respond(1, Link(1, 3));
        Check(chain.StackSize() == 3 && chain.PriorityPlayer() == 2,
              "Later effects are chain 2 and chain 3");
        Reject([&] { (void)chain.TakeResolution(); });
        Check(!chain.Pass(2), "First pass does not resolve the chain");
        Check(chain.Pass(1) && !chain.IsOpen(), "Two passes close the chain");
        auto order = chain.TakeResolution();
        Check(order.size() == 3 && order[0].EffectId == 3 &&
                  order[1].EffectId == 2 && order[2].EffectId == 1,
              "Chain resolves LIFO (3 -> 2 -> 1)");
        chain.Defer(Link(2, 4));
        chain.Defer(Link(1, 5));
        chain.Defer(Link(2, 6));
        chain.Defer(Link(1, 7));
        Check(chain.OpenDeferred(1) && chain.StackSize() == 4,
              "Deferred triggers open fresh chain");
        Check(!chain.Pass(1) && chain.Pass(2), "Deferred chain needs two passes");
        order = chain.TakeResolution();
        Check(order.size() == 4 && order[0].EffectId == 6 &&
                  order[1].EffectId == 4 && order[2].EffectId == 7 &&
                  order[3].EffectId == 5,
              "AP registered first, NAP resolves first; preserve intra-owner order");
        Check(!chain.OpenDeferred(1), "No pending triggers should remain");
        // Source removal cannot cancel committed links; the source ID is an
        // immutable reference rather than a live pointer.
        chain.Open(1, Link(1, 42));
        Check(!chain.Pass(2) && chain.Pass(1), "Empty response passes");
        order = chain.TakeResolution();
        Check(order.size() == 1 && order[0].SourceInstance == 42,
              "Committed effect survives unrelated source-zone movement");
        std::cout << "Yakou response priority/LIFO/deferred triggers passed\n";
        return 0;
    }
    catch (std::exception const& ex)
    {
        std::cerr << ex.what() << '\n';
        return 1;
    }
}
