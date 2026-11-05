using Game.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    [RequireComponent(typeof(Button))]
    public sealed class TurnFrameGlow : MonoBehaviour
    {
        private static readonly Color ActiveColor = new Color(1f, 1f, 1f, .9f);
        private Button _button;
        private Image _glow;
        private Image _frame;
        private Image _arrow;
        private bool _opponentTurn;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _frame = GetComponent<Image>();
            // Keep the authored opaque frame visible when Unity disables the button.
            var colors = _button.colors;
            colors.disabledColor = Color.white;
            _button.colors = colors;
            this.Bind("TurnArrow", out _arrow);
            if (!this.Bind("TurnGlow", out _glow))
            {
                Debug.LogError("Turn button requires its TurnGlow image.", this);
                enabled = false;
            }
        }

        public void SetOpponentTurn(bool value)
        {
            _opponentTurn = value;
        }

        private void LateUpdate()
        {
            _glow.color = _button.interactable ? ActiveColor : Color.clear;
            _frame.color = _button.interactable ? Color.white
                : _opponentTurn ? new Color(.62f, .72f, .72f, 1f) : new Color(.8f, .86f, .8f, 1f);
            if (_arrow != null)
            {
                _arrow.color = _button.interactable ? Color.white : new Color(.65f, .69f, .66f, .7f);
            }
        }
    }
}
