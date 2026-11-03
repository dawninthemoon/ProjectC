using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    [RequireComponent(typeof(Button))]
    public sealed class PublicZoneButton : MonoBehaviour
    {
        public bool Exile;

        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(() =>
            {
                var presenter = GetComponentInParent<BattlePresenter>();
                if (presenter.Client.Busy || presenter.Client.PendingChoice != null
                    || presenter.Interaction.State == BattleInputState.SelectingEffectTarget)
                {
                    return;
                }
                presenter.GetComponentInChildren<CardGalleryView>(true).ShowPublicZones(Exile);
            });
        }
    }
}
