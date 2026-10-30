#include "ProjectC/GameInitializer.hpp"
#include "ProjectC/GameReplay.hpp"

#include <iostream>
#include <utility>

int main()
{
    using namespace ProjectC::GameCore;

    GameConfig config;
    config.InitialLife = 3;
    config.InitialHandSize = 1;
    GameState state;
    std::vector<CardDefinition> const definitions{
        { .Id = 10, .Name = "Attacker", .Attack = 3, .MaxHp = 5 },
        { .Id = 20, .Name = "Defender", .Attack = 4, .MaxHp = 3 }
    };
    std::array<PlayerSetup, 2> const players{
        PlayerSetup{ 1, { 10, 10 } }, PlayerSetup{ 2, { 20, 20 } }
    };
    if (InitializeGame(state, config, definitions, players, 1) != InitializationResult::Success)
    {
        std::cerr << "Initialization failed\n";
        return 1;
    }
    GameState const initial = state;
    std::vector<GameCommand> const commands{
        StartTurnCommand{ 1 }, PlayCardCommand{ 1, 1 },
        AdvancePhaseCommand{ 1 }, AdvancePhaseCommand{ 1 }, EndTurnCommand{ 1 },
        StartTurnCommand{ 2 }, PlayCardCommand{ 2, 3 },
        AdvancePhaseCommand{ 2 }, AdvancePhaseCommand{ 2 }, EndTurnCommand{ 2 },
        StartTurnCommand{ 1 }, AdvancePhaseCommand{ 1 }, AttackCommand{ 1, 1, 2, 3 },
        AdvancePhaseCommand{ 1 }, EndTurnCommand{ 1 },
        StartTurnCommand{ 2 }, AdvancePhaseCommand{ 2 }, AdvancePhaseCommand{ 2 }, EndTurnCommand{ 2 },
        StartTurnCommand{ 1 }, AdvancePhaseCommand{ 1 }, DirectAttackCommand{ 1, 1, 2 }
    };
    std::vector<CommandResult> results;
    for (GameCommand const& command : commands)
    {
        CommandResult result = ProcessCommand(state, command);
        if (result.Code != CommandError::Success)
        {
            std::cerr << "Command failed: " << static_cast<int>(result.Code) << '\n';
            return 1;
        }
        results.push_back(std::move(result));
    }
    ReplayResult const replay = ReplayCommands(initial, commands);
    bool const matches = replay.FinalState == state && replay.Results == results;
    std::cout << "GameCore scenario complete\n"
        << "Attacker HP: " << state.Players[0].Field[0].Hp << '\n'
        << "Defender graveyard cards: " << state.Players[1].Graveyard.size() << '\n'
        << "Player 2 Life (direct attack): " << state.Players[1].Life << '\n'
        << "Winner: " << state.WinnerPlayerId << '\n'
        << "Replay matches: " << std::boolalpha << matches << '\n';
    return matches ? 0 : 1;
}
