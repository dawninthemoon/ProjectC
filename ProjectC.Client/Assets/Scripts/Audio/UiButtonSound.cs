using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectC.Client
{
    // Input handlers survive pooled buttons' onClick.RemoveAllListeners calls.
    // Sound on valid press also works when the button closes/destroys its own panel.
    [RequireComponent(typeof(Button))]
    public sealed class UiButtonSound : MonoBehaviour, IPointerDownHandler, ISubmitHandler
    {
        private Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && _button.IsActive() && _button.IsInteractable())
            {
                ClientAudio.Play(SoundCue.ButtonClick);
            }
        }

        public void OnSubmit(BaseEventData eventData)
        {
            // EventSystem dispatched this to an active control. Its Button handler may
            // already have hidden the panel before this submit handler runs.
            if (_button.IsInteractable())
            {
                ClientAudio.Play(SoundCue.ButtonClick);
            }
        }
    }
}
