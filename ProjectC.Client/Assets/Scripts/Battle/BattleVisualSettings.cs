using UnityEngine;
namespace ProjectC.Client
{
    [CreateAssetMenu(menuName="Project C/Battle visual settings")]
    public sealed class BattleVisualSettings : ScriptableObject
    {
        public float HoverDelay=.2f, LayoutMove=.22f, Rollback=.22f;
        public float TurnFadeIn=.2f, TurnHold=.7f, TurnFadeOut=.3f;
        public float DrawDuration=.45f, PopupDuration=1.35f, DrawerDuration=.25f, SettleDuration=.35f;
    }
}
