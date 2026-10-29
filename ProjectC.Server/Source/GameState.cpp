
#include "ProjectC/GameState.hpp"

#include <algorithm>

namespace ProjectC::GameCore
{
    void ApplyDamage(Monster& target, int damage)
    {
        target.Hp = std::max(0, target.Hp - std::max(0, damage));
    }
}
