using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectC.Client
{
    public sealed class DefenseAssignmentPair
    {
        public int AttackerId;
        public int DefenderId;
    }

    public sealed class DefenseAssignmentRequest
    {
        // The adapter must provide the legal pairs. This UI does not deduce eligibility or counters.
        public DefenseAssignmentPair[] AllowedPairs = Array.Empty<DefenseAssignmentPair>();
        public DefenseAssignmentPair[] Assignments = Array.Empty<DefenseAssignmentPair>();
        public Dictionary<int, int> AttackCounters = new Dictionary<int, int>();
        public bool AllowSharedDefender;
    }

    public sealed class DefenseAssignmentDraft
    {
        private DefenseAssignmentRequest _request;
        private readonly Dictionary<int, int> _assignments = new Dictionary<int, int>();
        public IReadOnlyDictionary<int, int> Assignments => _assignments;
        public bool CanAssign(int attacker, int defender) => _request != null
            && _request.AllowedPairs.Any(pair => pair.AttackerId == attacker && pair.DefenderId == defender);

        public void Refresh(DefenseAssignmentRequest request)
        {
            if (request == null || request.AllowedPairs == null || request.Assignments == null || request.AttackCounters == null
                || request.Assignments.Any(pair => pair == null) || request.AttackCounters.Values.Any(value => value < 0)
                || request.AllowedPairs.Any(pair => pair == null || pair.AttackerId == pair.DefenderId
                    || pair.AttackerId <= 0 || pair.DefenderId <= 0))
            {
                throw new ArgumentException("Defense requests require valid explicit eligible pairs.", nameof(request));
            }
            _request = request;
            foreach (var pair in _assignments.ToArray())
            {
                if (!CanAssign(pair.Key, pair.Value))
                {
                    _assignments.Remove(pair.Key);
                }
            }
            if (!request.AllowSharedDefender)
            {
                var used = new HashSet<int>();
                foreach (var pair in _assignments.ToArray())
                {
                    if (!used.Add(pair.Value))
                    {
                        _assignments.Remove(pair.Key);
                    }
                }
            }
        }

        public void Restore(IEnumerable<DefenseAssignmentPair> assignments)
        {
            _assignments.Clear();
            foreach (var pair in assignments)
            {
                Assign(pair.AttackerId, pair.DefenderId, false);
            }
        }

        public bool Assign(int attacker, int defender, bool toggle = true)
        {
            if (!CanAssign(attacker, defender))
            {
                return false;
            }
            if (toggle && _assignments.TryGetValue(attacker, out int current) && current == defender)
            {
                _assignments.Remove(attacker);
                return true;
            }
            if (!_request.AllowSharedDefender)
            {
                foreach (var pair in _assignments.Where(pair => pair.Value == defender).ToArray())
                {
                    _assignments.Remove(pair.Key);
                }
            }
            _assignments[attacker] = defender;
            return true;
        }

        public bool Remove(int attacker) => _assignments.Remove(attacker);
        public DefenseAssignmentPair[] Export() => _assignments.Select(pair => new DefenseAssignmentPair
        {
            AttackerId = pair.Key, DefenderId = pair.Value,
        }).ToArray();
    }
}
