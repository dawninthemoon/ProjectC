using System;
using Game.Utils;
using TMPro;
using UnityEngine;

namespace ProjectC.Client
{
    public sealed class PlayerPanelView : MonoBehaviour
    {
        public TMP_Text Life;
        private bool _bound;

        public void Render(PlayerView player, bool own, bool active, int maxYokai = 0)
        {
            if (!_bound)
            {
                if (!this.Bind("LifeText", out Life))
                {
                    throw new InvalidOperationException("Player panel requires its live health text.");
                }
                BattleText.StyleValue(Life);
                _bound = true;
            }
            Life.text = player.Life.ToString();
        }
    }
}
