using Game.Utils;
using UnityEngine;
using UnityEngine.UI;
namespace ProjectC.Client
{
    public sealed class CardDetailView : MonoBehaviour
    {
        public Text Title, Stats, Rules, Forms;
        bool bound;
        public void Show(CardView card)
        {
            if(!bound) { this.Bind("Name",out Title);this.Bind("Stats",out Stats);this.Bind("Rules",out Rules);this.Bind("Forms",out Forms);bound=true; }
            gameObject.SetActive(card!=null);if(card==null) return;
            Title.text=card.Name;Stats.text=card.CardType==1?"NORMAL SPELL":$"ATK {card.Attack}   HP {card.Hp}/{card.MaxHp}";
            Rules.text=card.RulesText??"";Forms.text=card.ManifestationText??"";
        }
        public void Place(Vector3 world) { PopupPosition.Place((RectTransform)transform,world+Vector3.right*110+Vector3.up*100); }
    }
}
