
#pragma once

namespace ProjectC::GameCore
{
    struct Monster
    {
        int Attack = 0;
        int Hp = 0;

        [[nodiscard]] bool IsDestroyed() const
        {
            return Hp <= 0;
        }
    };

    void ApplyDamage(Monster& target, int damage);
}
