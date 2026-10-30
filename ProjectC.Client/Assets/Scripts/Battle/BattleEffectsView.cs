using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace ProjectC.Client
{
    // Cosmetic feedback never delays receipt or application of authoritative snapshots.
    public sealed class BattleEffectsView : MonoBehaviour
    {
        public CanvasGroup TurnGroup, PopupGroup, DrawGroup;
        public Text TurnTitle, TurnSubtitle, PopupTitle, PopupBody, DrawLabel;
        public RectTransform DrawCard, OwnDeck, OwnHand, OpponentDeck, OpponentHand;
        readonly Queue<(string title, string body)> popups = new Queue<(string, string)>();
        readonly Queue<bool> draws = new Queue<bool>();
        Coroutine popupRoutine, drawRoutine, turnRoutine;
        public int DrawsPresented { get; private set; }
        public int PopupsPresented { get; private set; }
        public void ResetFeedback()
        {
            StopAllCoroutines(); popups.Clear(); draws.Clear();
            popupRoutine = drawRoutine = turnRoutine = null;
            TurnGroup.alpha = PopupGroup.alpha = DrawGroup.alpha = 0;
        }
        public void Turn(bool own, int number)
        {
            if (turnRoutine != null) StopCoroutine(turnRoutine);
            TurnTitle.text = own ? "YOUR TURN" : "OPPONENT TURN"; TurnSubtitle.text = "TURN " + number;
            turnRoutine = StartCoroutine(Flash(TurnGroup, 1.1f));
        }
        public void Popup(string title, string body)
        {
            if (popups.Count < 24) popups.Enqueue((title,body));
            if (popupRoutine == null) popupRoutine = StartCoroutine(Popups());
        }
        public void Draw(bool own)
        {
            if (draws.Count < 24) draws.Enqueue(own);
            if (drawRoutine == null) drawRoutine = StartCoroutine(Draws());
        }
        IEnumerator Popups()
        {
            while (popups.Count > 0) { var item = popups.Dequeue(); PopupTitle.text = item.title; PopupBody.text = item.body; ++PopupsPresented; yield return Flash(PopupGroup, 1.3f); }
            popupRoutine = null;
        }
        IEnumerator Draws()
        {
            while (draws.Count > 0)
            {
                bool own = draws.Dequeue(); ++DrawsPresented; DrawLabel.text = own ? "DRAW" : "HIDDEN";
                var from = own ? OwnDeck : OpponentDeck; var to = own ? OwnHand : OpponentHand;
                for (float t = 0; t < .45f; t += Time.unscaledDeltaTime)
                { DrawGroup.alpha = 1; DrawCard.position = Vector3.Lerp(from.TransformPoint(from.rect.center), to.TransformPoint(to.rect.center), Mathf.SmoothStep(0,1,t/.45f)); yield return null; }
                DrawGroup.alpha = 0;
            }
            drawRoutine = null;
        }
        IEnumerator Flash(CanvasGroup group, float duration)
        {
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            { group.alpha = Mathf.Min(t/.12f, 1, (duration-t)/.25f); yield return null; }
            group.alpha = 0;
        }
    }
}
