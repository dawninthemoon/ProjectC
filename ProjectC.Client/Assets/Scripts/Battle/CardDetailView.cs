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
        private TMP_Text _placeholder;
        private TMP_Text _type;
        private RectTransform _viewport;
        private RectTransform _content;
        private ScrollRect _scroll;
        private const float BodyTop = 260f;
        private const float BottomPadding = 20f;
        private const float MinimumPanelHeight = 340f;
        private const float MaximumPanelHeight = 780f;
        private const float MinimumBodyHeight = 60f;
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
                if (!this.Bind("ArtworkImage", out _artwork) || !this.Bind("ArtPlaceholder", out _placeholder)
                    || !this.Bind("CardType", out _type))
                {
                    throw new System.InvalidOperationException("Card inspector requires artwork, placeholder and type.");
                }
                if (!this.Bind("DetailViewport", out _viewport) || !this.Bind("DetailContent", out _content)
                    || !_viewport.TryGetComponent(out _scroll))
                {
                    throw new System.InvalidOperationException("Card inspector requires its description scroll area.");
                }
                BattleText.StyleHeading(Title);
                BattleText.StyleBody(Stats);
                BattleText.StyleBody(Rules);
                BattleText.StyleBody(Forms);
                BattleText.StyleBody(_type);
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
            CardArtProvider.Apply(_artwork, _placeholder, card.DefinitionId);
            _placeholder.text = "?";
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
                    ResizePanel();
                });
            Forms.text = _keyword == null ? CardPresentation.Manifestations(card)
                : _keyword.Name + "\n" + _keyword.Description;
            Forms.gameObject.SetActive(!string.IsNullOrWhiteSpace(Forms.text));
            ResizePanel();
        }

        private void ResizePanel()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            float bodyHeight = Mathf.Max(MinimumBodyHeight, LayoutUtility.GetPreferredHeight(_content));
            float panelHeight = Mathf.Clamp(BodyTop + bodyHeight + BottomPadding, MinimumPanelHeight, MaximumPanelHeight);
            ((RectTransform)transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);
            _viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight - BodyTop - BottomPadding);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            _scroll.verticalNormalizedPosition = 1f;
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
