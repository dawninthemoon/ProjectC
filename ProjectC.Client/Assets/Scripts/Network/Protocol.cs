using System;
namespace ProjectC.Client
{
    [Serializable] public class CardView
    {
        public int Id, DefinitionId, Attack, Hp, MaxHp;
        public string Name;
        public bool HasAttacked;
    }
    [Serializable] public class PlayerView
    {
        public int PlayerId, Life, HandCount, DeckCount;
        public CardView[] Hand, Field, Graveyard;
    }
    [Serializable] public class StateView
    {
        public bool Started, TurnStarted, Finished;
        public int ActivePlayerId, TurnNumber, Phase, WinnerPlayerId;
        public PlayerView[] Players;
    }
    [Serializable] public class MessageView
    {
        public string Type, MatchId, RequestId, SessionToken, Reason;
        public int ProtocolVersion, PlayerId, WinnerPlayerId;
        public long SequenceNumber;
    }
    [Serializable] public class SnapshotMessage : MessageView { public StateView Payload; }
    [Serializable] public class EventView
    {
        public int Type, SourcePlayerId, SourceCardId, TargetPlayerId, TargetCardId, DefinitionId, Damage, HpAfter, LifeAfter;
    }
    [Serializable] public class EventsMessage : MessageView { public EventView[] Payload; }
    [Serializable] public class CommandView
    {
        public string Type;
        public int CardId, TargetCardId;
    }
}
