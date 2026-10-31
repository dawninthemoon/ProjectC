using System;
namespace ProjectC.Client
{
    [Serializable] public class CardView
    {
        public int Id, DefinitionId, Attack, Hp, MaxHp, EffectCount, CardType;
        public string Name, RulesText, ManifestationText;
        public bool HasAttacked, CanSummon, CanAttack, SummoningSick;
        public ActivationView[] Activations;
    }
    [Serializable] public class EffectTargetView { public int PlayerId, CardId, Zone; }
    [Serializable] public class EffectSelectionView { public int MinCount, MaxCount; public EffectTargetView[] Candidates; }
    [Serializable] public class ActivationView { public int EffectId, YokaiCost; public bool CanActivate, IsManifest, Interactive; public EffectSelectionView[] Selections; }
    [Serializable] public class ActionChoiceCandidateView { public string Token; public int PlayerId, CardId, Zone; public CardView Card; }
    [Serializable] public class ActionChoiceView { public string ChoiceId; public int SourceCardId, MinCount, MaxCount; public bool IsCost, CanCancel; public ActionChoiceCandidateView[] Candidates; }
    [Serializable] public class ActionChoiceMessage : MessageView { public ActionChoiceView Payload; }
    [Serializable] public class PlayerView
    {
        public int PlayerId, Life, HandCount, DeckCount, YokaiPower, HiddenExileCount;
        public CardView[] Hand, Field, Graveyard, Exile;
    }
    [Serializable] public class StateView
    {
        public bool Started, TurnStarted, Finished, SummoningSickness;
        public string MatchStatus;
        public int ActivePlayerId, TurnNumber, Phase, WinnerPlayerId, FieldLimit, MaxYokaiPower, ResolvingPlayerId;
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
        public int Type, SourcePlayerId, SourceCardId, TargetPlayerId, TargetCardId, DefinitionId, Damage, HpAfter, LifeAfter, AttackAfter, Phase, TurnNumber, FromZone, ToZone, YokaiPowerAfter;
    }
    [Serializable] public class EventsMessage : MessageView { public EventView[] Payload; }
}
