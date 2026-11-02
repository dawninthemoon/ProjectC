using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class CardDetailView : MonoBehaviour
    {
        public TMP_Text Title,
            Stats,
            Rules,
            Forms;
        bool bound;
        int _shownCardId, _shownDefinitionId;
        KeywordInfo _keyword;

        public void Show(CardView card)
        {
            if (!bound)
            {
                this.Bind("Name", out Title);
                this.Bind("Stats", out Stats);
                this.Bind("Rules", out Rules);
                this.Bind("Forms", out Forms);
                bound = true;
            }
            gameObject.SetActive(card != null);
            if (card == null)
            {
                return;
            }
            if (_shownCardId != card.Id || _shownDefinitionId != card.DefinitionId)
            {
                _keyword = null;
                _shownCardId = card.Id;
                _shownDefinitionId = card.DefinitionId;
            }
            Title.text = CardPresentation.Name(card);
            Stats.text =
                card.CardType == 1
                    ? "NORMAL SPELL"
                    : $"ATK {card.Attack}   HP {card.Hp}/{card.MaxHp}";
            KeywordLinkView.Render(Rules, CardPresentation.Description(card), CardPresentation.Library.Localization,
                keyword =>
                {
                    _keyword = keyword;
                    Forms.text = keyword.Name + "\n" + keyword.Description;
                });
            Forms.text = _keyword == null ? CardPresentation.Manifestations(card)
                : _keyword.Name + "\n" + _keyword.Description;
        }

        public void Place(Vector3 world)
        {
            PopupPosition.Place(
                (RectTransform)transform,
                world + Vector3.right * 110 + Vector3.up * 100
            );
        }
    }
}
