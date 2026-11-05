using System;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class BattleTurnHudView : MonoBehaviour
    {
        private Image _ring, _ownerAccent;
        private TMP_Text _turnUnit, _timeUnit, _status;
        [SerializeField] private bool _showCenterStatus;
        private TMP_Text _centerStatus, _centerTurn;
        private Shadow _statusGlow;
        private bool _bound;
        private int _turn = -1, _activePlayer = -1;
        private double _observedDuration;
        public float FillAmount => _ring.fillAmount;

        public void ResetTimer()
        {
            _turn = -1;
            _activePlayer = -1;
            _observedDuration = 0;
        }

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_bound)
            {
                return;
            }
            if (!this.Bind("TimeRing", out _ring) || !this.Bind("TurnUnit", out _turnUnit)
                || !this.Bind("TimeUnit", out _timeUnit) || !this.Bind("TurnOwner", out _status)
                || !this.Bind("TurnOwnerAccent", out _ownerAccent))
            {
                throw new InvalidOperationException("Turn HUD requires its radial ring and unit labels.");
            }
            _statusGlow = _status.GetComponent<Shadow>();
            if (_showCenterStatus)
            {
                var presenter = GetComponentInParent<BattlePresenter>();
                if (presenter == null || !presenter.Bind("CenterTurnOwner", out _centerStatus)
                    || !presenter.Bind("CenterTurnNumber", out _centerTurn))
                {
                    throw new InvalidOperationException("Centered turn presentation requires both live labels.");
                }
            }
            _bound = true;
        }

        public void Render(int turn, int activePlayer, bool running, double remaining,
            int ownPlayer = 0, bool started = true, bool finished = false, bool choosing = false)
        {
            Initialize();
            remaining = Math.Max(0, remaining);
            if (_turn != turn || _activePlayer != activePlayer || !running)
            {
                _observedDuration = remaining;
                _turn = turn;
                _activePlayer = activePlayer;
            }
            // The protocol supplies a deadline, not a configured duration. Start from the first observed
            // remaining time after a turn change/reconnect and accept later server clock corrections.
            _observedDuration = Math.Max(_observedDuration, remaining);
            _ring.fillAmount = running && _observedDuration > 0 ? (float)(remaining / _observedDuration) : 0;
            // Timer art already contains the intended red ticks; only their exposure changes.
            _ring.color = Color.white;
            _turnUnit.text = BattleText.Get("ui_battle_turn");
            _timeUnit.text = running ? BattleText.Get("ui_battle_seconds") : "";
            bool ownTurn = activePlayer != 0 && activePlayer == ownPlayer;
            _status.text = BattleText.Get(finished ? "ui.turn.finished" : !started ? "ui.turn.waiting"
                : choosing ? "ui.turn.choose" : ownTurn ? "ui.turn.yours" : "ui.turn.theirs");
            if (_showCenterStatus)
            {
                _centerStatus.gameObject.SetActive(started && !finished);
                _centerTurn.gameObject.SetActive(started && !finished);
                _centerStatus.text = _status.text;
                _centerTurn.text = BattleText.Get("ui_battle_turn") + " " + turn;
            }
            _status.color = choosing ? new Color(1, .78f, .4f)
                : ownTurn ? new Color(1, .94f, .72f) : new Color(.65f, .79f, 1);
            _ownerAccent.color = choosing ? new Color(1, .6f, .1f, .65f)
                : ownTurn ? new Color(.08f, .9f, .7f, .45f) : new Color(.2f, .3f, 1, .65f);
            if (_statusGlow != null)
            {
                _statusGlow.effectColor = ownTurn ? new Color(.05f, .8f, .65f, .8f) : new Color(.16f, .25f, .6f, .7f);
            }
        }
    }
}
