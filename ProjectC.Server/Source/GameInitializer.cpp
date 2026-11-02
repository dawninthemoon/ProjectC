#include "ProjectC/GameInitializer.hpp"
#include "ProjectC/Zone.hpp"

#include <algorithm>
#include <cstdint>
#include <limits>
#include <utility>

namespace ProjectC::GameCore
{
    bool ValidateConfig(GameConfig const& config)
    {
        return config.InitialLife > 0 && config.InitialHandSize <= config.HandLimit
            && config.TriggerResolutionLimit > 0
            && (!config.EnableMulligan || (config.InitialHandSize==5 && config.DrawPerTurn<=config.HandLimit-config.InitialHandSize))
            && config.InitialYokaiPower>=0 && config.YokaiPowerPerTurn>=0 && config.MaxYokaiPower>=config.InitialYokaiPower;
    }

    bool ValidateGameState(GameState const& gameState)
    {
        if (!ValidateConfig(gameState.Config) || !ValidateCardIntegrity(gameState)
            || FindPlayer(gameState, gameState.ActivePlayerId) == nullptr || gameState.TurnNumber <= 0)
            return false;
        if (gameState.Phase != TurnPhase::Main && gameState.Phase != TurnPhase::Battle
            && gameState.Phase != TurnPhase::End) return false;
        if (!gameState.TurnStarted && gameState.Phase != TurnPhase::Main) return false;
        if(gameState.Preparation!=PreparationPhase::Playing && gameState.Preparation!=PreparationPhase::Mulligan) return false;
        if(gameState.Preparation==PreparationPhase::Mulligan && (!gameState.Config.EnableMulligan || gameState.TurnStarted
            || gameState.TurnNumber!=1 || gameState.Phase!=TurnPhase::Main || gameState.Status!=GameStatus::Running
            || (gameState.Players[0].MulliganComplete && gameState.Players[1].MulliganComplete))) return false;
        if(gameState.Preparation==PreparationPhase::Playing && (!gameState.Players[0].MulliganComplete || !gameState.Players[1].MulliganComplete)) return false;
        int livingPlayers = 0;
        PlayerId survivor = 0;
        for (PlayerState const& player : gameState.Players)
        {
            if (player.Field.size() > gameState.Config.FieldLimit
                || player.Hand.size() > gameState.Config.HandLimit || player.Life < 0
                || player.YokaiPower<0 || player.YokaiPower>gameState.Config.MaxYokaiPower
                || player.NormalSummonsThisTurn < 0 || player.NormalSummonsThisTurn > 1) return false;
            if (player.Life > 0) { ++livingPlayers; survivor = player.Id; }
            if (!gameState.Definitions.empty())
                for (Zone const zone : { Zone::Deck, Zone::Hand, Zone::Field, Zone::Graveyard, Zone::Exile, Zone::HiddenExile })
                    for (CardInstance const& card : *GetZone(player, zone))
                        if (card.MaxHp < 0 || card.Hp < 0 || card.Hp > card.MaxHp) return false;
        }
        if (gameState.Status == GameStatus::Running)
            return livingPlayers == 2 && gameState.WinnerPlayerId == 0 && gameState.EndReason == GameEndReason::None;
        if (gameState.Status == GameStatus::Finished)
        {
            if (gameState.EndReason == GameEndReason::DeckExhausted)
            {
                if (livingPlayers != 2 || FindPlayer(gameState, gameState.WinnerPlayerId) == nullptr) return false;
                for (PlayerState const& player : gameState.Players)
                    if (player.Id != gameState.WinnerPlayerId) return player.Deck.empty();
                return false;
            }
            return gameState.EndReason == GameEndReason::LifeDepleted && livingPlayers < 2
                && gameState.WinnerPlayerId == (livingPlayers == 1 ? survivor : 0);
        }
        return false;
    }

    CardDefinition const* FindDefinition(GameState const& gameState, CardDefinitionId id)
    {
        for (CardDefinition const& definition : gameState.Definitions)
        {
            if (definition.Id == id) return &definition;
        }
        return nullptr;
    }

    InitializationResult InitializeGame(GameState& output, GameConfig const& config,
        std::vector<CardDefinition> const& definitions,
        std::array<PlayerSetup, 2> const& players, PlayerId firstPlayerId, std::uint64_t randomSeed)
    {
        if (!ValidateConfig(config)) return InitializationResult::InvalidConfig;
        if (players[0].Id <= 0 || players[1].Id <= 0 || players[0].Id == players[1].Id
            || (firstPlayerId != players[0].Id && firstPlayerId != players[1].Id))
            return InitializationResult::InvalidPlayers;
        GameState candidate;
        candidate.Config = config;
        candidate.Preparation=config.EnableMulligan?PreparationPhase::Mulligan:PreparationPhase::Playing;
        if(config.EnableMulligan) candidate.MulliganRandom={randomSeed ^ 0x243f6a8885a308d3ULL, randomSeed ^ 0x13198a2e03707344ULL};
        candidate.Definitions = definitions;
        candidate.ActivePlayerId = firstPlayerId;
        for (std::size_t index = 0; index < players.size(); ++index)
        {
            candidate.Players[index].MulliganComplete=!config.EnableMulligan;
            candidate.Players[index].Id = players[index].Id;
            candidate.Players[index].Life = config.InitialLife;
            candidate.Players[index].YokaiPower = config.InitialYokaiPower;
        }
        if (!ValidateCardIntegrity(candidate)) return InitializationResult::InvalidDefinitions;
        std::int64_t nextId = 1;
        for (std::size_t index = 0; index < players.size(); ++index)
        {
            PlayerState& player = candidate.Players[index];
            if (players[index].Deck.size() < config.InitialHandSize || (config.EnableMulligan && players[index].Deck.size()<config.InitialHandSize*2))
                return InitializationResult::InsufficientCards;
            for (CardDefinitionId const id : players[index].Deck)
            {
                CardDefinition const* definition = FindDefinition(candidate, id);
                if (definition == nullptr) return InitializationResult::UnknownDefinition;
                if (nextId > std::numeric_limits<CardInstanceId>::max())
                    return InitializationResult::TooManyCards;
                player.Deck.push_back(CardInstance{
                    .Id = static_cast<CardInstanceId>(nextId++), .OwnerId = player.Id,
                    .Attack = definition->Attack, .Hp = definition->MaxHp, .MaxHp = definition->MaxHp,
                    .OriginalDefinitionId = id, .CurrentDefinitionId = id });
            }
            player.Hand.assign(player.Deck.begin(), player.Deck.begin() + config.InitialHandSize);
            player.Deck.erase(player.Deck.begin(), player.Deck.begin() + config.InitialHandSize);
        }
        output = std::move(candidate);
        return InitializationResult::Success;
    }
}
