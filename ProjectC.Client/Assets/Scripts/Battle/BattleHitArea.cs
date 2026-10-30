using UnityEngine;
using UnityEngine.EventSystems;
namespace ProjectC.Client
{
    public sealed class BattleHitArea : MonoBehaviour, IPointerClickHandler
    {
        public enum AreaKind { Background, OwnField, OwnHero, EnemyHero }
        public AreaKind Kind;
        public BattleInteractionController Controller { get; set; }
        public void OnPointerClick(PointerEventData e) { if(e.button==PointerEventData.InputButton.Left) Controller?.ClickArea(this); }
    }
}
