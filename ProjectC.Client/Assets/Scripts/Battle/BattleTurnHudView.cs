using System;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class BattleTurnHudView : MonoBehaviour
    {
        private Image _ring;
        private TMP_Text _turnUnit, _timeUnit;
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
            if (!this.Bind("TimeRing", out _ring) || !this.Bind("TurnUnit", out _turnUnit) || !this.Bind("TimeUnit", out _timeUnit))
            {
                throw new InvalidOperationException("Turn HUD requires its radial ring and unit labels.");
            }
        }

        public void Render(int turn, int activePlayer, bool running, double remaining)
        {
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
            _ring.color = remaining <= 10 ? new Color(1f, .62f, .25f) : new Color(.12f, .9f, .88f);
            _turnUnit.text = BattleText.Get("ui_battle_turn");
            _timeUnit.text = running ? BattleText.Get("ui_battle_seconds") : "";
        }
    }
}
