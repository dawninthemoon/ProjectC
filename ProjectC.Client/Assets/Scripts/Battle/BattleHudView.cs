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
        private TMP_Text _current, _maximum, _separator;
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
                || !this.Bind("YokaiFill", out _gauge)
                || !this.Bind("YokaiCurrent", out _current)
                || !this.Bind("YokaiMaximum", out _maximum)
                || !this.Bind("YokaiSeparator", out _separator))
            {
                throw new InvalidOperationException("Battle HUD requires both resource rows and Yokai gauge bindings.");
            }
            foreach (var value in new[] { _ownDeck, _ownGrave, _ownExile, _enemyDeck, _enemyGrave, _enemyExile })
            {
                BattleText.StyleValue(value);
            }
            BattleText.StyleBody(_yokai);
            BattleText.StyleValue(_current);
            BattleText.StyleValue(_maximum);
            BattleText.StyleBody(_separator);
        }

        public void Render(PlayerView own, PlayerView enemy, int maximum)
        {
            _ownDeck.text = own.DeckCount.ToString();
            _ownGrave.text = (own.Graveyard?.Length ?? 0).ToString();
            _ownExile.text = ((own.Exile?.Length ?? 0) + own.HiddenExileCount).ToString();
            _enemyDeck.text = enemy.DeckCount.ToString();
            _enemyGrave.text = (enemy.Graveyard?.Length ?? 0).ToString();
            _enemyExile.text = ((enemy.Exile?.Length ?? 0) + enemy.HiddenExileCount).ToString();
            _yokai.transform.parent.gameObject.SetActive(maximum > 0);
            _yokai.text = BattleText.Get("ui_battle_yokai_power");
            _current.text = own.YokaiPower.ToString();
            _maximum.text = maximum.ToString();
            _separator.text = BattleText.Get("ui_battle_resource_amount", "", "");
            _gauge.fillAmount = maximum > 0 ? Mathf.Clamp01((float)own.YokaiPower / maximum) : 0;
        }
    }
}
