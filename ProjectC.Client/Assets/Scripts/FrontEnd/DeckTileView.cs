using System;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class DeckTileView : MonoBehaviour, IPointerEnterHandler
    {
        Action hover;
        public int CardId
        {
            get; private set;
        }

        public void Render(
            int id,
            LibraryCard card,
            int count,
            bool library,
            Action clicked,
            Action selected,
            bool showCount = true
        )
        {
            CardId = id;
            hover = selected;
            this.Bind("CardName", out TMP_Text name);
            this.Bind("CardKind", out TMP_Text kind);
            this.Bind("CardStats", out TMP_Text stats);
            this.Bind("CardCount", out TMP_Text copies);
            this.Bind("Crest", out TMP_Text crest);
            if (!this.Bind("ArtworkImage", out AspectFillImage artwork)
                || !this.Bind("AttackBadge", out TMP_Text attack)
                || !this.Bind("HealthBadge", out TMP_Text health))
            {
                throw new InvalidOperationException("Deck tile requires ArtworkImage, AttackBadge and HealthBadge.");
            }
            CardArtProvider.Apply(artwork, id);
            bool hasStats = card != null && card.Type == "Monster";
            attack.transform.parent.gameObject.SetActive(hasStats);
            health.transform.parent.gameObject.SetActive(hasStats);
            attack.text = hasStats ? card.Attack.ToString() : "";
            health.text = hasStats ? card.MaxHp.ToString() : "";
            kind.gameObject.SetActive(false);
            stats.gameObject.SetActive(false);
            copies.gameObject.SetActive(showCount);
            name.text = card?.Name ?? "不明 / 누락 카드 #" + id;
            kind.text =
                card == null
                    ? "UNKNOWN"
                    : card.Rarity + " / " + (card.Type == "Monster" ? "요괴" : "주문");
            stats.text = "";
            copies.text = (library ? "덱에 " : "× ") + count;
            crest.text =
                card == null ? "?"
                : card.Type == "Monster" ? "妖"
                : "呪";
            GetComponent<Image>().color =
                card == null ? new Color(.35f, .12f, .13f)
                : !card.MainDeckEligible ? new Color(.21f, .17f, .28f)
                : card.Type == "Monster" ? new Color(.17f, .23f, .23f)
                : new Color(.24f, .19f, .26f);
            var button = GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                hover?.Invoke();
                clicked?.Invoke();
            });
        }

        public void OnPointerEnter(PointerEventData data)
        {
            hover?.Invoke();
        }
    }
}
