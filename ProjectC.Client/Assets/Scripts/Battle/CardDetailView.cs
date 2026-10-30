using UnityEngine;
using UnityEngine.UI;
namespace ProjectC.Client
{
    public sealed class CardDetailView : MonoBehaviour
    {
        public Text Title, Stats, Rules, Forms;
        public void Show(CardView card)
        {
            Title.text = card?.Name ?? "CARD INSPECTOR";
            Stats.text = card == null ? "Hover a visible card" : $"ATK {card.Attack}     HP {card.Hp}/{card.MaxHp}";
            Rules.text = card == null ? "Select a hand card to summon.\nSelect a ready field card, then an opponent to attack." : card.RulesText ?? "No effect description available.";
            Forms.text = card?.ManifestationText ?? "No resource cost is used by the current playtest rules.";
        }
    }
}
