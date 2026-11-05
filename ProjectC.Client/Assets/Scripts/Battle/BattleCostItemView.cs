using System;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class BattleCostItemView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Image _icon, _frame;
        private TMP_Text _value;
        private BattleSpecialCostsView _owner;
        private BattleCostDisplay _data;
        private bool _bound;
        public BattleCostDisplay Data => _data;
        public string Amount => _value.text;

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
            _frame = GetComponent<Image>();
            if (!this.Bind("CostIcon", out _icon) || !this.Bind("CostValue", out _value))
            {
                throw new InvalidOperationException("Cost item requires icon and value.");
            }
            _bound = true;
        }

        public void Bind(BattleSpecialCostsView owner, BattleCostDisplay data)
        {
            Initialize();
            _owner = owner;
            _data = data;
            _icon.sprite = data.Definition.Icon;
            _value.text = data.Amount;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _frame.color = new Color(1f, .94f, .72f);
            _owner.ShowTooltip(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _frame.color = Color.white;
            _owner.HideTooltip();
        }
    }
}
