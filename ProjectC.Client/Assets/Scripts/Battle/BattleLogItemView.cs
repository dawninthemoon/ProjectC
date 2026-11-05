using System;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class BattleLogItemView : MonoBehaviour
    {
        private TMP_Text _actor, _message, _detail;
        private Button _button;
        private Image _background;
        private BattleLogView _owner;
        public BattleLogEntry Entry { get; private set; }

        private void Awake()
        {
            BindReferences();
        }

        private void BindReferences()
        {
            if (_button != null)
            {
                return;
            }
            if (!this.Bind("LogActor", out _actor) || !this.Bind("LogMessage", out _message)
                || !this.Bind("LogCardHint", out _detail))
            {
                throw new InvalidOperationException("Battle log row requires actor, message and card hint.");
            }
            _button = GetComponent<Button>();
            _background = GetComponent<Image>();
            _button.onClick.AddListener(() => _owner.Inspect(Entry));
        }

        public void Render(BattleLogView owner, BattleLogEntry entry, int ownPlayer, bool header = false)
        {
            BindReferences();
            _owner = owner;
            Entry = entry;
            _actor.gameObject.SetActive(!header);
            _detail.gameObject.SetActive(!header && entry.Card != null);
            _button.interactable = !header && entry.Card != null;
            _message.text = header ? BattleText.Get(entry.Turn > 0 ? "ui.log.turn_header" : "ui.log.preparation", entry.Turn) : entry.Text(ownPlayer);
            _actor.text = entry.Actor(ownPlayer);
            _detail.text = BattleText.Get("ui.log.details");
            _background.color = header ? new Color(.12f, .31f, .31f, .95f) : new Color(.025f, .14f, .16f, .9f);
            var rect = _message.rectTransform;
            rect.anchoredPosition = new Vector2(14, header ? -9 : -32);
            rect.sizeDelta = new Vector2(-28, header ? 28 : 50);
            _message.fontSize = header ? 23 : 20;
            GetComponent<LayoutElement>().preferredHeight = header ? 48 : 96;
        }
    }
}
