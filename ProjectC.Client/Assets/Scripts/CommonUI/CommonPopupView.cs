using System;
using System.Collections.Generic;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class PopupAction
    {
        public string Label { get; }
        public Action Callback { get; }
        public PopupAction(string label, Action callback)
        {
            Label = label;
            Callback = callback;
        }
    }

    public sealed class CommonPopupView : MonoBehaviour
    {
        private TMP_Text _message;
        private RectTransform _buttons;
        private Button _template;
        private readonly List<Button> _pool = new List<Button>();

        public void Show(string message, params PopupAction[] actions)
        {
            if (actions == null || actions.Length == 0)
            {
                throw new ArgumentException("A common popup requires at least one action.", nameof(actions));
            }
            if (_message == null && (!this.Bind("Message", out _message)
                || !this.Bind("Buttons", out _buttons) || !this.Bind("ActionTemplate", out _template)))
            {
                throw new InvalidOperationException("CommonPopup requires Message, Buttons and ActionTemplate bindings.");
            }
            _message.text = message;
            _template.gameObject.SetActive(false);
            while (_pool.Count < actions.Length)
            {
                _pool.Add(Instantiate(_template, _buttons));
            }
            for (int i = 0; i < _pool.Count; ++i)
            {
                var button = _pool[i];
                button.onClick.RemoveAllListeners();
                button.gameObject.SetActive(i < actions.Length);
                if (i >= actions.Length)
                {
                    continue;
                }
                var action = actions[i];
                button.GetComponentInChildren<TMP_Text>(true).text = action.Label;
                button.onClick.AddListener(() =>
                {
                    Hide();
                    action.Callback?.Invoke();
                });
            }
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            foreach (var button in _pool)
            {
                button.onClick.RemoveAllListeners();
            }
            // The blocker belongs to this root; hiding closes the entire modal.
            gameObject.SetActive(false);
        }
    }
}
