#pragma once

namespace ProjectC::GameCore
{
    using PlayerId = int;
    using CardInstanceId = int;

    struct CardInstance
    {
        CardInstanceId Id = 0;
        PlayerId OwnerId = 0;
        int Attack = 0;
        int Hp = 0;
        int MaxHp = 0;
        bool HasAttackedThisTurn = false;

        [[nodiscard]] bool IsDestroyed() const;
    };

    void ApplyDamage(CardInstance& target, int damage);
}
