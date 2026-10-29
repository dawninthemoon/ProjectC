
#include "ProjectC/GameState.hpp"

#include <iostream>

using namespace ProjectC::GameCore;

int main()
{
    int failedTests = 0;

    // Test 1: Damage reduces HP.
    {
        Monster monster{ 3, 5 };

        ApplyDamage(monster, 2);

        if (monster.Hp != 3)
        {
            std::cerr << "FAILED: DamageReducesHp\n";
            ++failedTests;
        }
    }

    // Test 2: HP reaches zero when destroyed.
    {
        Monster monster{ 3, 5 };

        ApplyDamage(monster, 10);

        if (monster.Hp != 0 || !monster.IsDestroyed())
        {
            std::cerr << "FAILED: MonsterDestroyedAtZeroHp\n";
            ++failedTests;
        }
    }

    // Test 3: Negative damage is ignored.
    {
        Monster monster{ 3, 5 };

        ApplyDamage(monster, -2);

        if (monster.Hp != 5)
        {
            std::cerr << "FAILED: NegativeDamageIgnored\n";
            ++failedTests;
        }
    }

    if (failedTests > 0)
    {
        std::cerr << failedTests << " test(s) failed.\n";
        return 1;
    }

    std::cout << "All GameCore tests passed!\n";
    return 0;
}
