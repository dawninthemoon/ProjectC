#include "ProjectC/GameState.hpp"

#include <iostream>
#include <limits>
#include <type_traits>

using namespace ProjectC::GameCore;

namespace
{
    template <typename T>
    void ExpectEqual(T actual, T expected, char const* testName, char const* valueName, int& failedTests)
    {
        if (actual == expected)
        {
            return;
        }
        std::cerr << "FAILED: " << testName << " / " << valueName << ": actual=";
        if constexpr (std::is_enum_v<T>)
        {
            std::cerr << static_cast<int>(actual) << ", expected=" << static_cast<int>(expected);
        }
        else
        {
            std::cerr << actual << ", expected=" << expected;
        }
        std::cerr << '\n';
        ++failedTests;
    }

    GameState MakeGame()
    {
        GameState gameState;
        gameState.Players = {
            PlayerState{ .Id = 1, .Life = 20, .Field = {
                CardInstance{ .Id = 101, .OwnerId = 1, .Attack = 2, .Hp = 5, .MaxHp = 5 }
            } },
            PlayerState{ .Id = 2, .Life = 17, .Field = {
                CardInstance{ .Id = 201, .OwnerId = 2, .Attack = 3, .Hp = 5, .MaxHp = 5 }
            } }
        };
        StartTurn(gameState, 1);
        AdvancePhase(gameState);
        return gameState;
    }

    // Compare every public state field so failed commands cannot mutate hidden details.
    bool SameState(GameState const& actual, GameState const& expected)
    {
        if (actual.ActivePlayerId != expected.ActivePlayerId
            || actual.TurnNumber != expected.TurnNumber || actual.Phase != expected.Phase)
        {
            return false;
        }
        for (std::size_t playerIndex = 0; playerIndex < actual.Players.size(); ++playerIndex)
        {
            PlayerState const& player = actual.Players[playerIndex];
            PlayerState const& expectedPlayer = expected.Players[playerIndex];
            if (player.Id != expectedPlayer.Id || player.Life != expectedPlayer.Life
                || player.Field.size() != expectedPlayer.Field.size())
            {
                return false;
            }
            for (std::size_t cardIndex = 0; cardIndex < player.Field.size(); ++cardIndex)
            {
                CardInstance const& card = player.Field[cardIndex];
                CardInstance const& expectedCard = expectedPlayer.Field[cardIndex];
                if (card.Id != expectedCard.Id || card.OwnerId != expectedCard.OwnerId
                    || card.Attack != expectedCard.Attack || card.Hp != expectedCard.Hp
                    || card.MaxHp != expectedCard.MaxHp
                    || card.HasAttackedThisTurn != expectedCard.HasAttackedThisTurn)
                {
                    return false;
                }
            }
        }
        return true;
    }
}

int main()
{
    int failedTests = 0;
    int testCount = 0;

    struct DamageCase
    {
        char const* Name;
        int Hp;
        int Damage;
        int ExpectedHp;
    };
    DamageCase const damageCases[] = {
        { "DamageReducesHp", 5, 2, 3 },
        { "NegativeDamageIgnored", 5, -2, 5 },
        { "MinimumNegativeDamageIgnored", 5, std::numeric_limits<int>::min(), 5 },
        { "DamageClampsAtZero", 5, 10, 0 },
        { "ExactLethalDamage", 5, 5, 0 },
        { "ZeroDamageIgnored", 5, 0, 5 },
        { "DestroyedHpStaysAtZero", 0, 1, 0 },
        { "NegativeHpNormalizesWithoutOverflow", std::numeric_limits<int>::min(),
            std::numeric_limits<int>::max(), 0 }
    };
    for (DamageCase const& testCase : damageCases)
    {
        ++testCount;
        CardInstance card{ .Attack = 3, .Hp = testCase.Hp, .MaxHp = 5 };
        ApplyDamage(card, testCase.Damage);
        ExpectEqual(card.Hp, testCase.ExpectedHp, testCase.Name, "HP", failedTests);
        ExpectEqual(card.IsDestroyed(), testCase.ExpectedHp == 0, testCase.Name, "destroyed", failedTests);
        ExpectEqual(card.MaxHp, 5, testCase.Name, "maximum HP", failedTests);
    }
    {
        ++testCount;
        CardInstance card{ .Hp = 1 };
        ExpectEqual(card.IsDestroyed(), false, "CardAliveAboveZeroHp", "destroyed", failedTests);
        card.Hp = -1;
        ExpectEqual(card.IsDestroyed(), true, "NegativeHpIsDestroyed", "destroyed", failedTests);
    }

    struct BattleCase
    {
        char const* Name;
        int AttackerAttack;
        int AttackerHp;
        int DefenderAttack;
        int DefenderHp;
        int ExpectedAttackerHp;
        int ExpectedDefenderHp;
    };
    BattleCase const battleCases[] = {
        { "BothCardsSurviveBattle", 2, 5, 3, 5, 2, 3 },
        { "DefenderDestroyed", 3, 5, 4, 3, 1, 0 },
        { "AttackerDestroyed", 2, 3, 4, 5, 0, 3 },
        { "BothCardsDestroyed", 5, 4, 4, 5, 0, 0 },
        { "BattleIsSimultaneous", 10, 8, 6, 1, 2, 0 },
        { "NegativeAttackDoesNotHeal", -2, 5, -3, 5, 5, 5 }
    };
    for (BattleCase const& testCase : battleCases)
    {
        ++testCount;
        GameState gameState = MakeGame();
        CardInstance& attacker = gameState.Players[0].Field[0];
        CardInstance& defender = gameState.Players[1].Field[0];
        attacker.Attack = testCase.AttackerAttack;
        attacker.Hp = testCase.AttackerHp;
        attacker.MaxHp = testCase.AttackerHp;
        defender.Attack = testCase.DefenderAttack;
        defender.Hp = testCase.DefenderHp;
        defender.MaxHp = testCase.DefenderHp;
        GameState expectedState = gameState;
        expectedState.Players[0].Field[0].Hp = testCase.ExpectedAttackerHp;
        expectedState.Players[0].Field[0].HasAttackedThisTurn = true;
        expectedState.Players[1].Field[0].Hp = testCase.ExpectedDefenderHp;

        ExpectEqual(ResolveBattle(gameState, 1, 101, 2, 201), BattleResult::Success,
            testCase.Name, "result", failedTests);
        ExpectEqual(attacker.Hp, testCase.ExpectedAttackerHp, testCase.Name, "attacker HP", failedTests);
        ExpectEqual(defender.Hp, testCase.ExpectedDefenderHp, testCase.Name, "defender HP", failedTests);
        ExpectEqual(attacker.IsDestroyed(), testCase.ExpectedAttackerHp == 0,
            testCase.Name, "attacker destroyed", failedTests);
        ExpectEqual(defender.IsDestroyed(), testCase.ExpectedDefenderHp == 0,
            testCase.Name, "defender destroyed", failedTests);
        ExpectEqual(SameState(gameState, expectedState), true,
            testCase.Name, "only HP and attacker flag changed; cards retained", failedTests);
    }

    struct RejectedBattleCase
    {
        char const* Name;
        BattleResult Expected;
        TurnPhase Phase = TurnPhase::Battle;
        PlayerId ActivePlayerId = 1;
        PlayerId AttackerPlayerId = 1;
        CardInstanceId AttackerCardId = 101;
        PlayerId DefenderPlayerId = 2;
        CardInstanceId DefenderCardId = 201;
        PlayerId AttackerOwnerId = 1;
        PlayerId DefenderOwnerId = 2;
        int AttackerHp = 5;
        int DefenderHp = 5;
        bool AlreadyAttacked = false;
    };
    RejectedBattleCase const rejectedCases[] = {
        { .Name = "CannotAttackOutsideBattlePhase", .Expected = BattleResult::NotBattlePhase,
            .Phase = TurnPhase::Main },
        { .Name = "CannotAttackInEndPhase", .Expected = BattleResult::NotBattlePhase,
            .Phase = TurnPhase::End },
        { .Name = "InactivePlayerCannotAttack", .Expected = BattleResult::NotActivePlayer,
            .AttackerPlayerId = 2, .AttackerCardId = 201, .DefenderPlayerId = 1, .DefenderCardId = 101 },
        { .Name = "MissingAttackerPlayer", .Expected = BattleResult::AttackerNotFound,
            .ActivePlayerId = 99, .AttackerPlayerId = 99 },
        { .Name = "MissingAttackerCard", .Expected = BattleResult::AttackerNotFound,
            .AttackerCardId = 999 },
        { .Name = "MissingDefenderPlayer", .Expected = BattleResult::DefenderNotFound,
            .DefenderPlayerId = 99 },
        { .Name = "MissingDefenderCard", .Expected = BattleResult::DefenderNotFound,
            .DefenderCardId = 999 },
        { .Name = "AttackerOwnershipMismatch", .Expected = BattleResult::AttackerNotFound,
            .AttackerOwnerId = 2 },
        { .Name = "DefenderOwnershipMismatch", .Expected = BattleResult::DefenderNotFound,
            .DefenderOwnerId = 1 },
        { .Name = "CannotAttackOwnCard", .Expected = BattleResult::DefenderNotFound,
            .DefenderPlayerId = 1, .DefenderCardId = 101 },
        { .Name = "DestroyedAttackerCannotAttack", .Expected = BattleResult::AttackerDestroyed,
            .AttackerHp = 0 },
        { .Name = "DestroyedDefenderCannotBeAttacked", .Expected = BattleResult::DefenderDestroyed,
            .DefenderHp = 0 },
        { .Name = "NegativeHpAttackerCannotAttack", .Expected = BattleResult::AttackerDestroyed,
            .AttackerHp = -1 },
        { .Name = "AlreadyAttackedRejected", .Expected = BattleResult::AlreadyAttacked,
            .AlreadyAttacked = true },
        { .Name = "PhaseValidationComesFirst", .Expected = BattleResult::NotBattlePhase,
            .Phase = TurnPhase::Main, .AttackerPlayerId = 99 },
        { .Name = "ExistenceValidationBeforeDestroyed", .Expected = BattleResult::DefenderNotFound,
            .DefenderCardId = 999, .AttackerHp = 0 },
        { .Name = "DestroyedValidationBeforeAlreadyAttacked", .Expected = BattleResult::DefenderDestroyed,
            .DefenderHp = 0, .AlreadyAttacked = true }
    };
    for (RejectedBattleCase const& testCase : rejectedCases)
    {
        ++testCount;
        GameState gameState = MakeGame();
        gameState.Phase = testCase.Phase;
        gameState.ActivePlayerId = testCase.ActivePlayerId;
        gameState.Players[0].Field[0].OwnerId = testCase.AttackerOwnerId;
        gameState.Players[1].Field[0].OwnerId = testCase.DefenderOwnerId;
        gameState.Players[0].Field[0].Hp = testCase.AttackerHp;
        gameState.Players[1].Field[0].Hp = testCase.DefenderHp;
        gameState.Players[0].Field[0].HasAttackedThisTurn = testCase.AlreadyAttacked;
        GameState const before = gameState;
        ExpectEqual(ResolveBattle(gameState, testCase.AttackerPlayerId, testCase.AttackerCardId,
            testCase.DefenderPlayerId, testCase.DefenderCardId), testCase.Expected,
            testCase.Name, "result", failedTests);
        ExpectEqual(SameState(gameState, before), true, testCase.Name, "state unchanged", failedTests);
    }
    {
        ++testCount;
        GameState gameState = MakeGame();
        ExpectEqual(ResolveBattle(gameState, 1, 101, 2, 201), BattleResult::Success,
            "CardCannotAttackTwiceInTurn", "first result", failedTests);
        GameState const before = gameState;
        ExpectEqual(ResolveBattle(gameState, 1, 101, 2, 201), BattleResult::AlreadyAttacked,
            "CardCannotAttackTwiceInTurn", "second result", failedTests);
        ExpectEqual(SameState(gameState, before), true,
            "CardCannotAttackTwiceInTurn", "second attack leaves state unchanged", failedTests);
    }
    {
        ++testCount;
        GameState gameState = MakeGame();
        gameState.Players[0].Field[0].HasAttackedThisTurn = true;
        gameState.Players[1].Field[0].HasAttackedThisTurn = true;
        gameState.Players[0].Field.push_back(CardInstance{
            .Id = 102, .OwnerId = 1, .Hp = 0, .HasAttackedThisTurn = true });
        GameState expectedState = gameState;
        expectedState.Phase = TurnPhase::Main;
        expectedState.Players[0].Field[0].HasAttackedThisTurn = false;
        StartTurn(gameState, 1);
        ExpectEqual(SameState(gameState, expectedState), true, "StartTurnResetsAttackState",
            "only active living card flags and phase reset", failedTests);
    }
    {
        ++testCount;
        GameState gameState = MakeGame();
        StartTurn(gameState, 1);
        ExpectEqual(gameState.Phase, TurnPhase::Main, "AdvancePhaseOrder", "initial phase", failedTests);
        AdvancePhase(gameState);
        ExpectEqual(gameState.Phase, TurnPhase::Battle, "AdvancePhaseOrder", "first phase", failedTests);
        AdvancePhase(gameState);
        ExpectEqual(gameState.Phase, TurnPhase::End, "AdvancePhaseOrder", "second phase", failedTests);
        GameState const before = gameState;
        AdvancePhase(gameState);
        ExpectEqual(SameState(gameState, before), true, "AdvancePhaseOrder", "End is stable", failedTests);
    }
    {
        ++testCount;
        GameState gameState = MakeGame();
        gameState.Players[0].Field[0].HasAttackedThisTurn = true;
        gameState.Players[1].Field[0].HasAttackedThisTurn = true;
        AdvancePhase(gameState);
        EndTurn(gameState);
        ExpectEqual(gameState.ActivePlayerId, 2, "EndTurnSwitchesPlayer", "active player", failedTests);
        ExpectEqual(gameState.TurnNumber, 2, "EndTurnIncrementsTurnNumber", "turn number", failedTests);
        ExpectEqual(gameState.Phase, TurnPhase::Main, "EndTurnStartsMain", "phase", failedTests);
        ExpectEqual(gameState.Players[1].Field[0].HasAttackedThisTurn, false,
            "EndTurnResetsNextPlayer", "next player attack flag", failedTests);
        ExpectEqual(gameState.Players[0].Field[0].HasAttackedThisTurn, true,
            "EndTurnPreservesOtherPlayer", "previous player attack flag", failedTests);
        AdvancePhase(gameState);
        AdvancePhase(gameState);
        EndTurn(gameState);
        ExpectEqual(gameState.ActivePlayerId, 1, "EndTurnSwitchesBack", "active player", failedTests);
        ExpectEqual(gameState.TurnNumber, 3, "EndTurnSwitchesBack", "turn number", failedTests);
        AdvancePhase(gameState);
        ExpectEqual(ResolveBattle(gameState, 1, 101, 2, 201), BattleResult::Success,
            "CardCanAttackOnNextOwnTurn", "result", failedTests);
    }
    {
        ++testCount;
        GameState gameState = MakeGame();
        GameState const before = gameState;
        StartTurn(gameState, 99);
        ExpectEqual(SameState(gameState, before), true, "UnknownStartTurnPlayer",
            "state unchanged", failedTests);
        gameState.ActivePlayerId = 99;
        GameState const invalidBefore = gameState;
        EndTurn(gameState);
        ExpectEqual(SameState(gameState, invalidBefore), true, "UnknownEndTurnPlayer",
            "state unchanged", failedTests);
    }
    {
        ++testCount;
        GameState gameState = MakeGame();
        gameState.Players[0].Field.push_back(CardInstance{ .Id = 102, .OwnerId = 1, .Hp = 4 });
        ExpectEqual(FindCard(gameState.Players[0], 102) == &gameState.Players[0].Field[1], true,
            "FindCardReturnsCorrectCard", "mutable pointer", failedTests);
        CardInstance* card = FindCard(gameState.Players[0], 102);
        if (card != nullptr)
        {
            ApplyDamage(*card, 1);
        }
        ExpectEqual(gameState.Players[0].Field[1].Hp, 3,
            "FindCardReturnsCorrectCard", "mutation affects stored card", failedTests);
        ExpectEqual(FindCard(gameState.Players[0], 999) == nullptr, true,
            "FindCardReturnsNullForMissingId", "missing mutable pointer", failedTests);
        ExpectEqual(FindPlayer(gameState, 2) == &gameState.Players[1], true,
            "FindPlayerReturnsCorrectPlayer", "mutable pointer", failedTests);
        ExpectEqual(FindPlayer(gameState, 99) == nullptr, true,
            "FindPlayerReturnsNullForMissingId", "missing mutable pointer", failedTests);
        GameState const& constGameState = gameState;
        ExpectEqual(FindCard(constGameState.Players[0], 102) == &constGameState.Players[0].Field[1], true,
            "ConstFindCardReturnsCorrectCard", "const pointer", failedTests);
        ExpectEqual(FindCard(constGameState.Players[0], 999) == nullptr, true,
            "ConstFindCardReturnsNullForMissingId", "missing const pointer", failedTests);
        ExpectEqual(FindPlayer(constGameState, 2) == &constGameState.Players[1], true,
            "ConstFindPlayerReturnsCorrectPlayer", "const pointer", failedTests);
        ExpectEqual(FindPlayer(constGameState, 99) == nullptr, true,
            "ConstFindPlayerReturnsNullForMissingId", "missing const pointer", failedTests);
        PlayerState emptyPlayer;
        ExpectEqual(FindCard(emptyPlayer, 101) == nullptr, true,
            "FindCardOnEmptyField", "pointer", failedTests);
    }

    if (failedTests > 0)
    {
        std::cerr << failedTests << " assertion(s) failed in " << testCount << " test groups.\n";
        return 1;
    }
    std::cout << "All " << testCount << " GameCore test groups passed!\n";
    return 0;
}
