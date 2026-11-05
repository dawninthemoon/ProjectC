using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class PlayerPanelView : MonoBehaviour
    {
        public TMP_Text Name,
            Life,
            Counts;
        public Image TurnMark;

        public void Render(PlayerView player, bool own, bool active, int maxYokai = 0)
        {
            this.Bind("Name", out Name);
            this.Bind("Life", out Life);
            this.Bind("Counts", out Counts);
            this.Bind("TurnMark", out TurnMark);
            BattleText.StyleValue(Life);
            Name.text = (own ? "YOU" : "OPPONENT") + " / P" + player.PlayerId;
            Life.text = player.Life.ToString();
            Counts.text =
                $"DECK  {player.DeckCount}    HAND  {player.HandCount}    GRAVE  {player.Graveyard?.Length ?? 0}";
            if (maxYokai > 0)
            {
                Counts.text +=
                    $"\nYOKAI {player.YokaiPower}/{maxYokai}   EXILE {player.Exile?.Length ?? 0} + {player.HiddenExileCount} hidden";
            }
            TurnMark.color = active ? new Color(.25f, .8f, .7f) : new Color(.17f, .24f, .3f);
        }
    }
}
