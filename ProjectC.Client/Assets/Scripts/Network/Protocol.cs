using System;
namespace ProjectC.Client
{
    [Serializable] public class CardView
    {
        public int Id, DefinitionId, Attack, Hp, MaxHp, EffectCount;
        public string Name, RulesText, ManifestationText;
        public bool HasAttacked, CanSummon, CanAttack, SummoningSick;
    }
    [Serializable] public class PlayerView
    {
        public int PlayerId, Life, HandCount, DeckCount;
        public CardView[] Hand, Field, Graveyard;
    }
    [Serializable] public class StateView
    {
        public bool Started, TurnStarted, Finished;
        public string MatchStatus;
        public int ActivePlayerId, TurnNumber, Phase, WinnerPlayerId, FieldLimit;
        public PlayerView[] Players;
    }
    [Serializable] public class LobbyPlayerView { public int PlayerId; public bool Occupied, Connected, Ready, Rematch; }
    [Serializable] public class LobbyView { public string Status; public int HostPlayerId; public LobbyPlayerView[] Players; }
    [Serializable] public class DeckView { public string Id, Name; public int CardCount; }
    [Serializable] public class MessageView
    {
        public string Type, MatchId, RequestId, SessionToken, Reason, DeckId;
        public int ProtocolVersion, PlayerId, WinnerPlayerId, GameNumber;
        public long SequenceNumber;
    }
    [Serializable] public class SnapshotMessage : MessageView { public StateView Payload; }
    [Serializable] public class LobbyMessage : MessageView { public LobbyView Payload; }
    [Serializable] public class DeckCatalogMessage : MessageView { public DeckView[] Payload; }
    [Serializable] public class EventView
    {
        public int Type, SourcePlayerId, SourceCardId, TargetPlayerId, TargetCardId, DefinitionId, Damage, HpAfter, LifeAfter, AttackAfter, Phase, TurnNumber, FromZone, ToZone;
    }
    [Serializable] public class EventsMessage : MessageView { public EventView[] Payload; }
}
