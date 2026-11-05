using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace ProjectC.Client
{
    public sealed class CardDetailView : MonoBehaviour, IPointerClickHandler
    {
        public TMP_Text Title,
            Stats,
            Rules,
            Forms;
        private AspectFillImage _artwork;
        private TMP_Text _type;
        private TMP_Text _artPlaceholder;
        private RectTransform _viewport, _content;
        private ScrollRect _scroll;
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
                if (!this.Bind("ArtworkImage", out _artwork) || !this.Bind("CardType", out _type))
                {
                    throw new System.InvalidOperationException("Card inspector requires artwork, placeholder and type.");
                }
                this.Bind("ArtPlaceholder", out _artPlaceholder);
                this.Bind("DetailViewport", out _viewport);
                this.Bind("DetailContent", out _content);
                if ((_viewport == null) != (_content == null))
                {
                    throw new System.InvalidOperationException("Card inspector requires both detail viewport and content.");
                }
                _scroll = _viewport != null ? _viewport.GetComponent<ScrollRect>() : null;
                BattleText.StyleHeading(Title);
                BattleText.StyleValue(Stats);
                BattleText.StyleBody(Rules);
                BattleText.StyleBody(Forms);
                BattleText.StyleHud(_type);
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
            CardArtProvider.Apply(_artwork, card.DefinitionId);
            if (_artPlaceholder != null)
            {
                _artPlaceholder.gameObject.SetActive(_artwork.sprite == null);
            }
            _type.text = BattleText.Get(card.CardType == 1 ? "ui.card.spell" : "ui.card.monster");
            string costs = ClientApp.Instance != null && !string.IsNullOrEmpty(ClientApp.Instance.Network.CatalogProblem)
                ? "" : CardPresentation.Costs(CardPresentation.Library.Find(card.DefinitionId));
            if (!string.IsNullOrEmpty(costs))
            {
                _type.text += " / " + costs;
            }
            Stats.gameObject.SetActive(card.CardType != 1);
            Stats.text =
                card.CardType == 1
                    ? ""
                    : BattleText.Get("ui.card.stats", card.Attack, card.Hp, card.MaxHp);
            KeywordLinkView.Render(Rules, CardPresentation.Description(card), CardPresentation.Library.Localization,
                keyword =>
                {
                    _keyword = keyword;
                    Forms.text = keyword.Name + "\n" + keyword.Description;
                    Forms.gameObject.SetActive(true);
                    RefreshHeight();
                });
            Forms.text = _keyword == null ? CardPresentation.Manifestations(card)
                : _keyword.Name + "\n" + _keyword.Description;
            Forms.gameObject.SetActive(!string.IsNullOrWhiteSpace(Forms.text));
            RefreshHeight();
        }

        private void RefreshHeight()
        {
            if (_viewport == null)
            {
                return;
            }
            const float contentTop = 323f;
            const float bottomPadding = 16f;
            const float minimumViewport = 80f;
            const float maximumViewport = 141f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            float preferred = LayoutUtility.GetPreferredHeight(_content);
            float height = Mathf.Clamp(preferred, minimumViewport, maximumViewport);
            _viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            ((RectTransform)transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                contentTop + height + bottomPadding);
            if (_scroll != null)
            {
                _scroll.verticalNormalizedPosition = 1;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Inspector clicks are UI interaction, not empty-field dismissal.
        }

        public void Place(Vector3 world)
        {
            // Kept for callers compiled against the old inspector API. Prefab fixes its position.
        }
    }
}
