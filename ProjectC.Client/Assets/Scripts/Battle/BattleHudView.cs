using System;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class BattleHudView : MonoBehaviour
    {
        [SerializeField] private BattleCostDefinition _yokaiCost;
        private BattleSpecialCostsView _specialCosts;
        private readonly BattleCostDisplay[] _costs = new BattleCostDisplay[1];
        private TMP_Text _ownDeck, _ownGrave, _ownExile, _enemyDeck, _enemyGrave, _enemyExile, _yokai, _enemyYokai;
        private Image _gauge;
        private RectTransform _yokaiPanel, _enemyYokaiPanel;
        private bool _bound;

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
            if (!this.Bind("OwnDeckCount", out _ownDeck)
                || !this.Bind("OwnGraveCount", out _ownGrave)
                || !this.Bind("OwnExileCount", out _ownExile)
                || !this.Bind("EnemyDeckCount", out _enemyDeck)
                || !this.Bind("EnemyGraveCount", out _enemyGrave)
                || !this.Bind("EnemyExileCount", out _enemyExile)
                || !this.Bind("YokaiValue", out _yokai)
                || !this.Bind("YokaiFill", out _gauge)
                || !this.Bind("YokaiPanel", out _yokaiPanel)
                || !this.Bind("EnemyYokaiPanel", out _enemyYokaiPanel)
                || !this.Bind("EnemyYokaiValue", out _enemyYokai)
                || !this.Bind("SpecialCosts", out _specialCosts) || _yokaiCost == null)
            {
                throw new InvalidOperationException("Battle HUD requires both resource rows and Yokai gauge bindings.");
            }
            foreach (var value in new[] { _ownDeck, _ownGrave, _ownExile, _enemyDeck, _enemyGrave, _enemyExile })
            {
                BattleText.StyleValue(value);
            }
            BattleText.StyleValue(_yokai);
            BattleText.StyleValue(_enemyYokai);
            _bound = true;
        }

        public void Render(PlayerView own, PlayerView enemy, int maximum)
        {
            Initialize();
            _yokaiPanel.gameObject.SetActive(false);
            _enemyYokaiPanel.gameObject.SetActive(maximum > 0);
            _costs[0] = new BattleCostDisplay(_yokaiCost, own.YokaiPower, maximum);
            _specialCosts.Render(maximum > 0 ? _costs : Array.Empty<BattleCostDisplay>());
            _ownDeck.text = own.DeckCount.ToString();
            _ownGrave.text = (own.Graveyard?.Length ?? 0).ToString();
            _ownExile.text = ((own.Exile?.Length ?? 0) + own.HiddenExileCount).ToString();
            _enemyDeck.text = enemy.DeckCount.ToString();
            _enemyGrave.text = (enemy.Graveyard?.Length ?? 0).ToString();
            _enemyExile.text = ((enemy.Exile?.Length ?? 0) + enemy.HiddenExileCount).ToString();
            _yokai.text = $"{own.YokaiPower}/{maximum}";
            _enemyYokai.text = $"{enemy.YokaiPower}/{maximum}";
            _gauge.fillAmount = maximum > 0 ? Mathf.Clamp01((float)own.YokaiPower / maximum) : 0;
        }
    }
}
