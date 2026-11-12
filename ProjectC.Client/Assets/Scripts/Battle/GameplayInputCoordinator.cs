using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectC.Client
{
    public enum GameplayInputAction
    {
        PlayCard, ActivateEffect, Attack, AdvancePhase, EndTurn,
        SelectHandCard, SelectFieldCard, SelectDeckCard, SelectGraveyardCard, SelectExileCard,
        SelectPlayer, ConfirmSelection, CancelSelection, OpenCardDetails, OpenPublicZone, SubmitMulligan,
        OpenBattleMenu, ToggleBattleLog,
    }

    public enum SelectionInputPhase
    {
        None, Selecting, Submitting, WaitingForServer, Resolved,
    }

    // Presentation/transport permissions only. Card costs and rule validity remain server-owned.
    public sealed class GameplayInputCoordinator
    {
        public sealed class InputScope : IDisposable
        {
            private GameplayInputCoordinator _coordinator;
            public Guid Id { get; } = Guid.NewGuid();
            public string Owner { get; }
            internal readonly HashSet<GameplayInputAction> Allowed;
            internal InputScope(GameplayInputCoordinator coordinator, string owner, IEnumerable<GameplayInputAction> allowed)
            {
                _coordinator = coordinator;
                Owner = owner;
                Allowed = new HashSet<GameplayInputAction>(allowed);
            }
            public void Dispose()
            {
                var coordinator = _coordinator;
                _coordinator = null;
                if (coordinator != null && coordinator._scopes.Remove(Id))
                {
                    coordinator.Changed?.Invoke();
                }
            }
        }

        private readonly Dictionary<Guid, InputScope> _scopes = new Dictionary<Guid, InputScope>();
        private InputScope _selectionScope, _waitingScope;
        private ActionChoiceView _serverChoice;
        private EffectSelectionView _localChoice;
        private StateView _snapshot;
        private int _player;
        public event Action Changed;
        public int ScopeCount => _scopes.Count;
        public SelectionInputPhase SelectionPhase { get; private set; }
        public bool HasSelection => _serverChoice != null || _localChoice != null;
        public bool HasLocalSelection => _localChoice != null;

        public InputScope PushScope(string owner, IEnumerable<GameplayInputAction> allowedActions)
        {
            if (string.IsNullOrWhiteSpace(owner) || allowedActions == null)
            {
                throw new ArgumentException("An input scope requires an owner and an allowlist.");
            }
            var scope = new InputScope(this, owner, allowedActions);
            _scopes.Add(scope.Id, scope);
            Changed?.Invoke();
            return scope;
        }

        public bool CanExecute(GameplayInputAction action)
        {
            if (IsGameAction(action) && _snapshot != null
                && (!_snapshot.Started || _snapshot.Finished || _snapshot.MulliganActive
                    || _snapshot.ResolvingPlayerId != 0 || _snapshot.ActivePlayerId != _player))
            {
                return false;
            }
            return _scopes.Values.All(scope => scope.Allowed.Contains(action));
        }

        private static bool IsGameAction(GameplayInputAction action)
        {
            return action == GameplayInputAction.PlayCard || action == GameplayInputAction.ActivateEffect
                || action == GameplayInputAction.Attack || action == GameplayInputAction.EndTurn
                || action == GameplayInputAction.AdvancePhase;
        }

        public static GameplayInputAction CardSelectionAction(int zone)
        {
            switch ((PresentationZone)zone)
            {
                case PresentationZone.Deck: return GameplayInputAction.SelectDeckCard;
                case PresentationZone.Hand: return GameplayInputAction.SelectHandCard;
                case PresentationZone.Field: return GameplayInputAction.SelectFieldCard;
                case PresentationZone.Graveyard: return GameplayInputAction.SelectGraveyardCard;
                case PresentationZone.Exile:
                case PresentationZone.HiddenExile: return GameplayInputAction.SelectExileCard;
                default: throw new ArgumentOutOfRangeException(nameof(zone));
            }
        }

        private void SelectionScope(IEnumerable<int> zones, bool canCancel)
        {
            RemoveInternal(_selectionScope);
            var allowed = zones.Select(CardSelectionAction).ToList();
            allowed.Add(GameplayInputAction.SelectPlayer);
            allowed.Add(GameplayInputAction.ConfirmSelection);
            allowed.Add(GameplayInputAction.OpenCardDetails);
            if (canCancel)
            {
                allowed.Add(GameplayInputAction.CancelSelection);
            }
            _selectionScope = PushScope("EffectSelection", allowed);
        }

        public void SetServerChoice(ActionChoiceView choice)
        {
            if (choice == null)
            {
                throw new ArgumentNullException(nameof(choice));
            }
            if (_serverChoice?.ChoiceId == choice.ChoiceId)
            {
                // Candidate updates must not discard the draft or release an outstanding submission lock.
                _serverChoice = choice;
                SelectionScope((choice.Candidates ?? Array.Empty<ActionChoiceCandidateView>()).Select(value => value.Zone), choice.CanCancel);
                return;
            }
            _serverChoice = choice;
            _localChoice = null;
            RemoveInternal(_waitingScope);
            _waitingScope = null;
            SelectionPhase = SelectionInputPhase.Selecting;
            SelectionScope((choice.Candidates ?? Array.Empty<ActionChoiceCandidateView>()).Select(value => value.Zone), choice.CanCancel);
        }

        public void SetLocalChoice(EffectSelectionView choice)
        {
            if (_serverChoice != null)
            {
                throw new InvalidOperationException("A local picker cannot replace an authoritative choice.");
            }
            _localChoice = choice ?? throw new ArgumentNullException(nameof(choice));
            _serverChoice = null;
            SelectionPhase = SelectionInputPhase.Selecting;
            SelectionScope((choice.Candidates ?? Array.Empty<EffectTargetView>()).Select(value => value.Zone), true);
        }

        public bool CanSelectCard(int zone, int cardId)
        {
            if (!CanExecute(CardSelectionAction(zone)))
            {
                return false;
            }
            if (_serverChoice != null)
            {
                return (_serverChoice.Candidates ?? Array.Empty<ActionChoiceCandidateView>()).Any(value => value.Zone == zone && value.CardId == cardId);
            }
            return _localChoice == null || (_localChoice.Candidates ?? Array.Empty<EffectTargetView>()).Any(value => value.Zone == zone && value.CardId == cardId);
        }

        public bool CanSelectPlayer(int player)
        {
            return CanExecute(GameplayInputAction.SelectPlayer)
                && (_serverChoice != null
                    ? (_serverChoice.Candidates ?? Array.Empty<ActionChoiceCandidateView>()).Any(value => value.PlayerId == player && value.CardId == 0 && value.Zone == (int)PresentationZone.Field)
                    : _localChoice != null && (_localChoice.Candidates ?? Array.Empty<EffectTargetView>()).Any(value => value.PlayerId == player && value.CardId == 0));
        }

        public void CommandSent(bool selection)
        {
            if (selection)
            {
                SelectionPhase = SelectionInputPhase.Submitting;
            }
            RemoveInternal(_waitingScope);
            _waitingScope = PushScope("ServerResponse", new[] { GameplayInputAction.OpenCardDetails });
            if (selection)
            {
                SelectionPhase = SelectionInputPhase.WaitingForServer;
                Changed?.Invoke();
            }
        }

        public void CommandRejected()
        {
            RemoveInternal(_waitingScope);
            _waitingScope = null;
            if (HasSelection)
            {
                SelectionPhase = SelectionInputPhase.Selecting;
            }
            Changed?.Invoke();
        }

        public void ApplySnapshot(StateView snapshot, int player, bool commandSettled)
        {
            _snapshot = snapshot;
            _player = player;
            if (snapshot != null && snapshot.Finished)
            {
                Reset();
                _snapshot = snapshot;
                _player = player;
            }
            else if (commandSettled && (!HasSelection || snapshot?.ResolvingPlayerId == 0))
            {
                RemoveInternal(_waitingScope);
                _waitingScope = null;
                if (snapshot?.ResolvingPlayerId == 0)
                {
                    ResolveSelection();
                }
            }
            Changed?.Invoke();
        }

        public void CancelLocalSelection()
        {
            if (_localChoice != null && SelectionPhase == SelectionInputPhase.Selecting)
            {
                ResolveSelection();
                Changed?.Invoke();
            }
        }

        private void ResolveSelection()
        {
            bool selected = HasSelection;
            _serverChoice = null;
            _localChoice = null;
            RemoveInternal(_selectionScope);
            _selectionScope = null;
            if (selected)
            {
                SelectionPhase = SelectionInputPhase.Resolved;
            }
        }

        public void Reset()
        {
            _scopes.Clear();
            _selectionScope = _waitingScope = null;
            _serverChoice = null;
            _localChoice = null;
            _snapshot = null;
            SelectionPhase = SelectionInputPhase.None;
            Changed?.Invoke();
        }

        private void RemoveInternal(InputScope scope)
        {
            if (scope != null)
            {
                _scopes.Remove(scope.Id);
            }
        }
    }
}
