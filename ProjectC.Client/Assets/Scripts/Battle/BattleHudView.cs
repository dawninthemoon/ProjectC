using System;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class BattleHudView : MonoBehaviour
    {
        private TMP_Text _ownDeck, _ownGrave, _ownExile, _enemyDeck, _enemyGrave, _enemyExile, _yokai;
        private Image _gauge;

        private void Awake()
        {
            if (!this.Bind("OwnDeckCount", out _ownDeck)
                || !this.Bind("OwnGraveCount", out _ownGrave)
                || !this.Bind("OwnExileCount", out _ownExile)
                || !this.Bind("EnemyDeckCount", out _enemyDeck)
                || !this.Bind("EnemyGraveCount", out _enemyGrave)
                || !this.Bind("EnemyExileCount", out _enemyExile)
                || !this.Bind("YokaiValue", out _yokai)
                || !this.Bind("YokaiFill", out _gauge))
            {
                throw new InvalidOperationException("Battle HUD requires both resource rows and Yokai gauge bindings.");
            }
        }

        public void Render(PlayerView own, PlayerView enemy, int maximum)
        {
            _ownDeck.text = own.DeckCount.ToString();
            _ownGrave.text = (own.Graveyard?.Length ?? 0).ToString();
            _ownExile.text = ((own.Exile?.Length ?? 0) + own.HiddenExileCount).ToString();
            _enemyDeck.text = enemy.DeckCount.ToString();
            _enemyGrave.text = (enemy.Graveyard?.Length ?? 0).ToString();
            _enemyExile.text = ((enemy.Exile?.Length ?? 0) + enemy.HiddenExileCount).ToString();
            _yokai.text = BattleText.Get("ui.hud.yokai", own.YokaiPower, maximum);
            _gauge.fillAmount = maximum > 0 ? Mathf.Clamp01((float)own.YokaiPower / maximum) : 0;
        }
    }
}
