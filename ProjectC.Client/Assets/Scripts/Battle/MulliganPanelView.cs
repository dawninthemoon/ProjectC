using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    // A small additive overlay using the existing card prefab; no scene regeneration.
    public sealed class MulliganPanelView : MonoBehaviour
    {
        BattlePresenter presenter;
        CardWidget[] cards;
        public Button Confirm { get; private set; }
        public Button Keep { get; private set; }
        public Button Reconnect { get; private set; }
        public TMP_Text Status { get; private set; }
        TMP_Text feedback;
        StateView lastState;
        float deadline;
        MultiplayerDebugScreen Client => presenter.Client;

        public static MulliganPanelView Create(BattlePresenter presenter)
        {
            var root = new GameObject(
                "MulliganOverlay",
                typeof(RectTransform),
                typeof(Image),
                typeof(MulliganPanelView)
            );
            root.transform.SetParent(presenter.transform, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(.02f, .025f, .04f, .97f);
            var view = root.GetComponent<MulliganPanelView>();
            view.presenter = presenter;
            view.Status = view.Label("Status", new Vector2(0, 240), new Vector2(1450, 170), 30);
            view.feedback = view.Label("Feedback", new Vector2(0, -280), new Vector2(1400, 80), 22);
            view.cards = new CardWidget[5];
            for (int i = 0; i < 5; ++i)
            {
                var card = Instantiate(presenter.Hand.CardPrefab, root.transform);
                view.cards[i] = card;
                var cardRect = (RectTransform)card.transform;
                cardRect.anchorMin = cardRect.anchorMax = new Vector2(.5f, .5f);
                cardRect.sizeDelta = new Vector2(180, 218);
                cardRect.anchoredPosition = new Vector2((i - 2) * 210, 20);
                card.Clicked = view.Toggle;
                card.Hovered = null;
                card.HoverLeft = null;
            }
            view.Confirm = view.Button(
                "교체 확정",
                new Vector2(-260, -180),
                () => view.Submit(false)
            );
            view.Keep = view.Button("그대로 시작", new Vector2(0, -180), () => view.Submit(true));
            view.Reconnect = view.Button(
                "재접속",
                new Vector2(260, -180),
                () => view.Client.ReconnectNetwork()
            );
            view.Client.NoticeReceived += view.Notice;
            root.SetActive(false);
            return view;
        }

        TMP_Text Label(string name, Vector2 position, Vector2 size, int fontSize)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            obj.transform.SetParent(transform, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var text = obj.GetComponent<TMP_Text>();
            text.font = presenter.TurnLabel.font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        Button Button(string title, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            var obj = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(transform, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(235, 60);
            rect.anchoredPosition = position;
            obj.GetComponent<Image>().color = new Color(.24f, .3f, .38f);
            var label = Label(title + "Label", Vector2.zero, new Vector2(230, 60), 24);
            label.transform.SetParent(obj.transform, false);
            label.text = title;
            var button = obj.GetComponent<Button>();
            button.targetGraphic = obj.GetComponent<Image>();
            button.onClick.AddListener(action);
            return button;
        }

        void Notice(string text)
        {
            if (feedback != null && Client.State?.MulliganActive == true)
            {
                feedback.text = text;
            }
        }

        void OnDestroy()
        {
            if (presenter != null && presenter.Client != null)
            {
                presenter.Client.NoticeReceived -= Notice;
            }
        }

        void Toggle(CardWidget widget)
        {
            var own = Own();
            if (own == null || own.MulliganComplete || Client.Busy || !Client.Connected)
            {
                return;
            }
            if (!Client.MulliganSelection.Remove(widget.Card.Id))
            {
                Client.MulliganSelection.Add(widget.Card.Id);
            }
            Render();
        }

        PlayerView Own() =>
            Client.State?.Players == null
                ? null
                : Array.Find(Client.State.Players, p => p.PlayerId == Client.PlayerId);

        void Submit(bool keep)
        {
            if (!Client.Connected || Client.Busy)
            {
                return;
            }
            if (keep)
            {
                Client.MulliganSelection.Clear();
            }
            if (Client.SubmitMulligan(Client.MulliganSelection.OrderBy(id => id)))
            {
                feedback.text = "서버 확인 중…";
            }
            Render();
        }

        public void Render()
        {
            var state = Client.State;
            bool active = state?.MulliganActive == true;
            gameObject.SetActive(active);
            if (!active)
            {
                lastState = null;
                return;
            }
            var own = Own();
            if (own == null)
            {
                return;
            }
            if (lastState != state)
            {
                lastState = state;
                deadline = Time.unscaledTime + state.MulliganSecondsRemaining;
                if (own.MulliganComplete)
                {
                    Client.MulliganSelection.Clear();
                }
                else
                {
                    Client.MulliganSelection.RemoveWhere(id =>
                        !Array.Exists(own.Hand, c => c.Id == id)
                    );
                }
            }
            bool editable = Client.Connected && !Client.Busy && !own.MulliganComplete;
            Confirm.interactable = Keep.interactable = editable;
            Reconnect.interactable = !Client.Connected && !Client.Busy;
            for (int i = 0; i < cards.Length; ++i)
            {
                bool visible = i < own.Hand.Length;
                cards[i].gameObject.SetActive(visible);
                if (visible)
                {
                    bool selected = Client.MulliganSelection.Contains(own.Hand[i].Id);
                    cards[i].Bind(own.Hand[i], false, selected, false, false);
                    cards[i].Status.text =
                        own.MulliganComplete ? "확정 손패"
                        : selected ? "교체 예정"
                        : "유지";
                }
            }
            var other = Array.Find(state.Players, p => p.PlayerId != Client.PlayerId);
            string phase =
                own.MulliganComplete ? "완료 · 상대방을 기다리고 있습니다"
                : Client.Busy ? "제출 확인 중"
                : "교체할 카드를 선택하세요 (" + Client.MulliganSelection.Count + " / 5)";
            Status.text =
                "시작 손패 · 멀리건 1회\n"
                + phase
                + "\n상대: "
                + (other?.MulliganComplete == true ? "완료" : "선택 중")
                + " · 남은 시간 "
                + Mathf.Max(0, Mathf.CeilToInt(deadline - Time.unscaledTime))
                + "초";
            if (!Client.Connected)
            {
                feedback.text =
                    "연결이 끊어졌습니다. 재접속 후 서버의 손패와 완료 상태를 복원합니다.";
            }
        }
    }
}
