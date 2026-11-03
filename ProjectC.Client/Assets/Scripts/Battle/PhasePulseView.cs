using UnityEngine;

namespace ProjectC.Client
{
    [RequireComponent(typeof(Animator))]
    public sealed class PhasePulseView : MonoBehaviour
    {
        private const string StateName = "PhaseChange";
        private BattlePresenter _presenter;
        private Animator _animator;
        private int _phase = -1;
        private int _turn = -1;

        private void Awake()
        {
            _presenter = GetComponentInParent<BattlePresenter>();
            _animator = GetComponent<Animator>();
        }

        private void LateUpdate()
        {
            var state = _presenter.Client.State;
            if (state != null && (_phase != state.Phase || _turn != state.TurnNumber))
            {
                _phase = state.Phase;
                _turn = state.TurnNumber;
                _animator.Play(StateName, 0, 0);
            }
        }
    }
}
