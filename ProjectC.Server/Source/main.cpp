
#include "ProjectC/GameState.hpp"

#include <iostream>

int main()
{
    ProjectC::GameCore::Monster monster
    {
        .Attack = 3,
        .Hp = 5
    };

    ProjectC::GameCore::ApplyDamage(monster, 2);

    std::cout << "Project C GameCore\n";
    std::cout << "ATK: " << monster.Attack << '\n';
    std::cout << "HP: " << monster.Hp << '\n';

    return 0;
}
