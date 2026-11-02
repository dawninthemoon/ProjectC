using UnityEngine;

namespace ProjectC.Client
{
    [CreateAssetMenu(menuName = "Project C/Battle visual settings")]
    public sealed class BattleVisualSettings : ScriptableObject
    {
        public float HoverDelay = .2f,
            LayoutMove = .22f,
            Rollback = .22f;
        public float TurnFadeIn = .2f,
            TurnHold = .7f,
            TurnFadeOut = .3f;
        public float DrawDuration = .65f,
            PopupDuration = 1.35f,
            DrawerDuration = .25f,
            SettleDuration = .35f;

        [Header("Draw and hand presentation")]
        [SerializeField, Min(.01f)] private float _drawInterval = .16f;
        [SerializeField, Min(0)] private float _drawArcHeight = 120f;
        [SerializeField, Min(.01f)] private float _drawScale = 1.08f;
        [SerializeField] private float _drawRotation = -16f;
        [SerializeField, Min(.01f)] private float _handRepositionDuration = .28f;
        [SerializeField, Min(0)] private float _handSpacing = 22f;
        [SerializeField, Min(0)] private float _handCurve = 24f;
        [SerializeField, Min(0)] private float _handRotation = 7f;
        [SerializeField] private AnimationCurve _animationEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

        public float DrawInterval => _drawInterval;
        public float DrawArcHeight => _drawArcHeight;
        public float DrawScale => _drawScale;
        public float DrawRotation => _drawRotation;
        public float HandRepositionDuration => _handRepositionDuration;
        public float HandSpacing => _handSpacing;
        public float HandCurve => _handCurve;
        public float HandRotation => _handRotation;

        public float Ease(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (progress == 0 || progress == 1)
            {
                return progress;
            }
            return Mathf.Clamp01(_animationEase?.Evaluate(progress) ?? Mathf.SmoothStep(0, 1, progress));
        }
    }
}
