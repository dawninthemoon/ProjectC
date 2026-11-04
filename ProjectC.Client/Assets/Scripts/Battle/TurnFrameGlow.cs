using Game.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    [RequireComponent(typeof(Button))]
    public sealed class TurnFrameGlow : MonoBehaviour
    {
        private static readonly Color ActiveColor = new Color(1f, .8f, .38f, .7f);
        private Button _button;
        private Image _glow;
        private Image _frame;
        private Image _arrow;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _frame = GetComponent<Image>();
            this.Bind("TurnArrow", out _arrow);
            if (!this.Bind("TurnGlow", out _glow))
            {
                Debug.LogError("Turn button requires its TurnGlow image.", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            _glow.color = _button.interactable ? ActiveColor : Color.clear;
            _frame.color = _button.interactable ? Color.white : new Color(.72f, .83f, .82f, 1f);
            if (_arrow != null)
            {
                _arrow.color = _button.interactable ? Color.white : new Color(.65f, .69f, .66f, .7f);
            }
        }
    }
}
