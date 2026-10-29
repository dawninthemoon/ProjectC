
#include "ProjectC/GameState.hpp"

#include <iostream>

int main()
{
    using namespace ProjectC::GameCore;

    GameState gameState;
    gameState.Players = {
        PlayerState{ .Id = 1, .Life = 20, .Field = {
            CardInstance{ .Id = 101, .OwnerId = 1, .Attack = 3, .Hp = 5, .MaxHp = 5 }
        } },
        PlayerState{ .Id = 2, .Life = 20, .Field = {
            CardInstance{ .Id = 201, .OwnerId = 2, .Attack = 4, .Hp = 3, .MaxHp = 3 }
        } }
    };

    StartTurn(gameState, 1);
    AdvancePhase(gameState);
    BattleResult const result = ResolveBattle(gameState, 1, 101, 2, 201);
    if (result != BattleResult::Success)
    {
        std::cerr << "Battle failed: " << static_cast<int>(result) << '\n';
        return 1;
    }

    CardInstance const& attacker = gameState.Players[0].Field[0];
    CardInstance const& defender = gameState.Players[1].Field[0];

    std::cout << "== Result ==\n" << std::boolalpha;
    std::cout << "Attacker HP: " << attacker.Hp << '\n';
    std::cout << "Defender HP: " << defender.Hp << '\n';

    std::cout << "Attacker Destroyed: "
        << attacker.IsDestroyed() << '\n';

    std::cout << "Defender Destroyed: "
        << defender.IsDestroyed() << '\n';

    std::cout << "Attacker HasAttackedThisTurn: " << attacker.HasAttackedThisTurn << '\n';

    AdvancePhase(gameState);
    EndTurn(gameState);
    std::cout << "Next active player: " << gameState.ActivePlayerId << '\n';
    std::cout << "Turn number: " << gameState.TurnNumber << '\n';

    return 0;
}
