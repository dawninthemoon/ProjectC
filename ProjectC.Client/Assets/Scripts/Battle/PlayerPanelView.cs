using UnityEngine;
using UnityEngine.UI;
namespace ProjectC.Client
{
    public sealed class PlayerPanelView : MonoBehaviour
    {
        public Text Name, Life, Counts;
        public Image TurnMark;
        public void Render(PlayerView player, bool own, bool active)
        {
            Name.text = (own ? "YOU" : "OPPONENT") + " / P" + player.PlayerId;
            Life.text = player.Life.ToString();
            Counts.text = $"DECK  {player.DeckCount}    HAND  {player.HandCount}    GRAVE  {player.Graveyard?.Length ?? 0}";
            TurnMark.color = active ? new Color(.25f,.8f,.7f) : new Color(.17f,.24f,.3f);
        }
    }
}
