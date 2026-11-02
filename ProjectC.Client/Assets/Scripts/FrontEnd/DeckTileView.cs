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
        public int CardId { get; private set; }

        public void Render(
            int id,
            LibraryCard card,
            int count,
            bool library,
            Action clicked,
            Action selected
        )
        {
            CardId = id;
            hover = selected;
            this.Bind("CardName", out TMP_Text name);
            this.Bind("CardKind", out TMP_Text kind);
            this.Bind("CardStats", out TMP_Text stats);
            this.Bind("CardCount", out TMP_Text copies);
            this.Bind("Crest", out TMP_Text crest);
            name.text = card?.Name ?? "不明 / 누락 카드 #" + id;
            kind.text =
                card == null
                    ? "UNKNOWN"
                    : card.Rarity + " / " + (card.Type == "Monster" ? "요괴" : "주문");
            stats.text =
                card == null ? "클릭하여 제거"
                : !card.MainDeckEligible ? "현현 전용 · 투입 불가"
                : card.Type == "Monster" ? $"ATK {card.Attack}   HP {card.MaxHp}"
                : "SPELL";
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
                clicked();
            });
        }

        public void OnPointerEnter(PointerEventData data)
        {
            hover?.Invoke();
        }
    }
}
