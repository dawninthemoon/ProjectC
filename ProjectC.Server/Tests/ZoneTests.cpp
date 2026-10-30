#include "ProjectC/GameCommand.hpp"

#include <iostream>

using namespace ProjectC::GameCore;

void RunZoneTests(int& testCount, int& failedTests)
{
    GameState initial;
    initial.Players[0].Id = 1;
    initial.Players[1].Id = 2;
    initial.Players[0].Deck.push_back(CardInstance{ .Id = 1, .OwnerId = 1, .Hp = 3 });
    auto check = [&](bool passed, char const* name)
    {
        ++testCount;
        if (!passed) { ++failedTests; std::cerr << "FAILED: " << name << '\n'; }
    };
    GameState state = initial;
    check(ValidateCardIntegrity(state), "ZoneInitialIntegrity");
    check(MoveCard(state, 1, 1, Zone::Deck, Zone::Hand) == ZoneResult::Success
        && state.Players[0].Deck.empty() && state.Players[0].Hand[0].Id == 1, "DeckToHand");
    check(MoveCard(state, 1, 1, Zone::Hand, Zone::Field) == ZoneResult::Success
        && FindCard(state.Players[0], 1) != nullptr, "HandToField");
    check(MoveCard(state, 1, 1, Zone::Field, Zone::Graveyard) == ZoneResult::Success
        && state.Players[0].Field.empty() && state.Players[0].Graveyard.size() == 1, "FieldToGraveyard");
    struct FailureCase { Zone From; Zone To; PlayerId Player; CardInstanceId Card; ZoneResult Expected; };
    for (FailureCase const& test : {
        FailureCase{ Zone::Deck, Zone::Deck, 1, 1, ZoneResult::SameZone },
        FailureCase{ Zone::Deck, Zone::Hand, 99, 1, ZoneResult::PlayerNotFound },
        FailureCase{ Zone::Deck, Zone::Hand, 2, 1, ZoneResult::CardNotFound },
        FailureCase{ Zone::Hand, Zone::Field, 1, 1, ZoneResult::CardNotFound },
        FailureCase{ Zone::Deck, static_cast<Zone>(99), 1, 1, ZoneResult::InvalidZone } })
    {
        state = initial;
        check(MoveCard(state, test.Player, test.Card, test.From, test.To) == test.Expected
            && state == initial, "InvalidMoveAtomic");
    }
    state = initial;
    state.Players[1].Hand.push_back(state.Players[0].Deck[0]);
    GameState before = state;
    check(!ValidateCardIntegrity(state)
        && MoveCard(state, 1, 1, Zone::Deck, Zone::Hand) == ZoneResult::InvalidState
        && state == before, "DuplicateIdRejected");
    state = initial;
    state.Players[0].Deck[0].OwnerId = 2;
    check(!ValidateCardIntegrity(state), "OwnershipIntegrity");
    state = initial;
    state.Config.HandLimit = 0;
    before = state;
    check(MoveCard(state, 1, 1, Zone::Deck, Zone::Hand) == ZoneResult::DestinationFull
        && state == before, "HandLimitAtomic");

    for (Zone const zone : { Zone::Deck, Zone::Hand, Zone::Field, Zone::Graveyard })
    {
        for (std::size_t playerIndex = 0; playerIndex < 2; ++playerIndex)
        {
            state = initial;
            CardInstance duplicate = state.Players[0].Deck[0];
            duplicate.OwnerId = state.Players[playerIndex].Id;
            GetZone(state.Players[playerIndex], zone)->push_back(duplicate);
            before = state;
            check(!ValidateCardIntegrity(state)
                && MoveCard(state, 1, 1, Zone::Deck, Zone::Hand) == ZoneResult::InvalidState
                && state == before, "DuplicateAcrossEveryPlayerAndZone");
        }
    }
    state = initial;
    state.Config.FieldLimit = 0;
    before = state;
    check(MoveCard(state, 1, 1, Zone::Deck, Zone::Field) == ZoneResult::DestinationFull
        && state == before, "MoveToFullField");
    state = initial;
    state.Players[0].Deck[0].Hp = 0;
    before = state;
    check(MoveCard(state, 1, 1, Zone::Deck, Zone::Field) == ZoneResult::DestroyedCard
        && state == before, "MoveDestroyedCardToFieldRejected");
    state = initial;
    state.Players[0].Deck[0].Id = 0;
    check(!ValidateCardIntegrity(state), "ZeroInstanceIdRejected");

    for (Zone const zone : { Zone::Deck, Zone::Hand, Zone::Graveyard })
    {
        for (std::size_t playerIndex = 0; playerIndex < 2; ++playerIndex)
        {
            state = initial;
            state.Players[0].Deck.clear();
            state.ActivePlayerId = 1;
            state.TurnStarted = true;
            state.Phase = TurnPhase::Battle;
            state.Players[0].Life = state.Players[1].Life = 20;
            state.Players[0].Field.push_back(CardInstance{ .Id = 1, .OwnerId = 1, .Attack = 2, .Hp = 5 });
            state.Players[1].Field.push_back(CardInstance{ .Id = 2, .OwnerId = 2, .Attack = 2, .Hp = 5 });
            PlayerState& player = state.Players[playerIndex];
            GetZone(player, zone)->push_back(player.Field[0]);
            player.Field.clear();
            before = state;
            CommandResult const result = ProcessCommand(state, AttackCommand{ 1, 1, 2, 2 });
            check(result.Result == (playerIndex == 0
                ? BattleResult::AttackerNotFound : BattleResult::DefenderNotFound)
                && result.Code == CommandError::BattleRejected && result.Events.empty()
                && state == before, "AttackOnlyTargetsField");
        }
    }
    state = initial;
    state.Players[0].Deck.clear();
    state.Players[0].Life = state.Players[1].Life = 20;
    state.ActivePlayerId = 1;
    state.TurnStarted = true;
    state.Phase = TurnPhase::Battle;
    state.Players[0].Field.push_back(CardInstance{ .Id = 1, .OwnerId = 1, .Attack = 9, .Hp = 1 });
    state.Players[1].Field.push_back(CardInstance{ .Id = 2, .OwnerId = 2, .Attack = 9, .Hp = 1 });
    state.Players[1].Deck.push_back(state.Players[1].Field[0]);
    before = state;
    CommandResult const result = ProcessCommand(state, AttackCommand{ 1, 1, 2, 2 });
    check(result.Code == CommandError::InvalidState && result.Events.empty() && state == before,
        "InvalidGlobalIntegrityRollsBackLethalBattle");
}
