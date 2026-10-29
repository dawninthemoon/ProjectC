#include "ProjectC/CardInstance.hpp"

#include <algorithm>

namespace ProjectC::GameCore
{
    bool CardInstance::IsDestroyed() const
    {
        return Hp <= 0;
    }

    void ApplyDamage(CardInstance& target, int damage)
    {
        int const damageAmount = std::max(0, damage);
        target.Hp = std::max(0, std::max(0, target.Hp) - damageAmount);
    }
}
