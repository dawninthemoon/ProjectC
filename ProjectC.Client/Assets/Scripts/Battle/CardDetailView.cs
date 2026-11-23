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
        private TMP_Text _attackBadge, _healthBadge;
        private GameObject _statsGroup;
        private RectTransform _viewport, _content;
        private ScrollRect _scroll;
        bool bound;
        int _shownCardId, _shownDefinitionId;
        KeywordInfo _keyword;

        public void Show(CardView card)
        {
            if (!bound)
            {
                this.Bind("NameText", out Title);
                this.Bind("StatsText", out Stats);
                this.Bind("RulesText", out Rules);
                this.Bind("FormsText", out Forms);
                if (!this.Bind("ArtworkImage", out _artwork) || !this.Bind("CardTypeText", out _type))
                {
                    throw new System.InvalidOperationException("Card inspector requires artwork, placeholder and type.");
                }
                this.Bind("ArtPlaceholderText", out _artPlaceholder);
                if (!this.Bind("InspectorStats", out RectTransform statsGroup)
                    || !statsGroup.Bind("AttackBadgeText", out _attackBadge)
                    || !statsGroup.Bind("HealthBadgeText", out _healthBadge))
                {
                    throw new System.InvalidOperationException("Card inspector requires attack and health badges.");
                }
                _statsGroup = statsGroup.gameObject;
                this.Bind("DetailViewportImage", out _viewport);
                this.Bind("DetailContentImage", out _content);
                if ((_viewport == null) != (_content == null))
                {
                    throw new System.InvalidOperationException("Card inspector requires both detail viewport and content.");
                }
                _scroll = _viewport != null ? _viewport.GetComponent<ScrollRect>() : null;
                BattleText.StyleHeading(Title);
                BattleText.StyleValue(_attackBadge);
                BattleText.StyleValue(_healthBadge);
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
            bool cardChanged = _shownCardId != card.Id || _shownDefinitionId != card.DefinitionId;
            if (cardChanged)
            {
                _keyword = null;
                _shownCardId = card.Id;
                _shownDefinitionId = card.DefinitionId;
            }
            Title.text = CardPresentation.Name(card);
            if (card.Yakou)
            {
                CardArtProvider.ApplyYakou(_artwork, card.DefinitionId);
            }
            else
            {
                CardArtProvider.Apply(_artwork, card.DefinitionId);
            }
            if (_artPlaceholder != null)
            {
                _artPlaceholder.gameObject.SetActive(_artwork.sprite == null);
            }
            _type.text = BattleText.Get(card.CardType == 1 ? "ui.card.spell" : "ui.card.monster");
            var library = CardPresentation.LibraryFor(card);
            string costs = card.Yakou || (ClientApp.Instance != null && !string.IsNullOrEmpty(ClientApp.Instance.Network.CatalogProblem))
                ? "" : CardPresentation.Costs(library.Find(card.DefinitionId));
            if (!string.IsNullOrEmpty(costs))
            {
                _type.text += " / " + costs;
            }
            Stats.gameObject.SetActive(false);
            _statsGroup.SetActive(card.CardType != 1);
            if (card.CardType != 1)
            {
                _attackBadge.text = card.Attack.ToString();
                _healthBadge.text = card.Hp.ToString();
            }
            KeywordLinkView.Render(Rules, CardPresentation.Description(card), library.Localization,
                keyword =>
                {
                    _keyword = keyword;
                    Forms.text = keyword.Name + "\n" + keyword.Description;
                    Forms.gameObject.SetActive(true);
                    RefreshHeight();
                    if (_scroll != null)
                    {
                        _scroll.verticalNormalizedPosition = 0;
                    }
                });
            Forms.text = _keyword == null ? CardPresentation.Manifestations(card)
                : _keyword.Name + "\n" + _keyword.Description;
            Forms.gameObject.SetActive(!string.IsNullOrWhiteSpace(Forms.text));
            RefreshHeight();
            if (cardChanged && _scroll != null)
            {
                _scroll.verticalNormalizedPosition = 1;
            }
        }

        private void RefreshHeight()
        {
            if (_viewport == null)
            {
                return;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
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
