using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    [RequireComponent(typeof(Button))]
    public sealed class TurnFrameGlow : MonoBehaviour
    {
        private static readonly Color ActiveColor = new Color(.2f, 1f, .88f, .75f);
        private Button _button;
        private Image _glow;
        private Image _frame;
        private Image _arrow;
        private TMP_Text _status;
        private bool _opponentTurn;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _frame = GetComponent<Image>();
            // Keep the authored opaque frame visible when Unity disables the button.
            var colors = _button.colors;
            colors.disabledColor = Color.white;
            _button.colors = colors;
            // Optional in the local layout; use an authored slot so runtime labels cannot overlap its decoration.
            this.Bind("TurnAvailability", out _status);
            if (_status != null)
            {
                BattleText.StyleHeading(_status);
            }
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
            if (_status != null)
            {
                _status.gameObject.SetActive(true);
                _status.text = BattleText.Get(_button.interactable ? "ui.hud.ready"
                    : _opponentTurn ? "ui.turn.opponent_waiting" : "ui.turn.waiting");
                _status.color = _button.interactable ? new Color(.76f, 1f, .93f)
                    : _opponentTurn ? new Color(.75f, .82f, .8f) : new Color(.9f, .83f, .63f);
            }
            if (_arrow != null)
            {
                _arrow.color = _button.interactable ? Color.white : new Color(.65f, .69f, .66f, .7f);
            }
        }
    }
}
