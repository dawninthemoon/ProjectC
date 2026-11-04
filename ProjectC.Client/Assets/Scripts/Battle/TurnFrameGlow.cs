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

        private void Awake()
        {
            _button = GetComponent<Button>();
            if (!this.Bind("TurnGlow", out _glow))
            {
                Debug.LogError("Turn button requires its TurnGlow image.", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            _glow.color = _button.interactable ? ActiveColor : Color.clear;
        }
    }
}
