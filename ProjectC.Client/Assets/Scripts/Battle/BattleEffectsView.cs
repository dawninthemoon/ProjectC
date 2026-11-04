using System.Collections;
using System.Collections.Generic;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class BattleEffectsView : MonoBehaviour
    {
        public CanvasGroup TurnGroup,
            PopupGroup,
            DrawGroup;
        public TMP_Text TurnTitle,
            TurnSubtitle,
            PopupTitle,
            PopupBody,
            DrawLabel;
        public RectTransform DrawCard,
            OwnDeck,
            OwnHand,
            OpponentDeck,
            OpponentHand;
        public BattleVisualSettings Settings;
        [SerializeField] private GameObject _numberPopupPrefab;
        private const float NumberLifetime = .75f;

        public void PresentNumber(EventView e, BattlePresenter presenter, StateView before)
        {
            var card = BattlePresentationState.Find(before, e.TargetCardId);
            var player = before?.Players == null ? null : System.Array.Find(before.Players, p => p.PlayerId == e.TargetPlayerId);
            string label = null;
            Color color = new Color(.95f, .25f, .28f);
            if (e.Type == (int)PresentationEvent.Damage || e.Type == (int)PresentationEvent.PlayerDamage)
            {
                label = "−" + e.Damage;
            }
            else if (e.Type == (int)PresentationEvent.Life && player != null && player.Life != e.LifeAfter)
            {
                int delta = e.LifeAfter - player.Life;
                label = (delta > 0 ? "+" : "") + delta;
                color = delta > 0 ? new Color(.35f, 1, .65f) : color;
            }
            else if (e.Type == (int)PresentationEvent.Heal && card != null)
            {
                label = "+" + Mathf.Max(0, e.HpAfter - card.Hp);
                color = new Color(.35f, 1, .65f);
            }
            else if (e.Type == (int)PresentationEvent.Stats && card != null && card.Attack != e.AttackAfter)
            {
                int delta = e.AttackAfter - card.Attack;
                label = (delta > 0 ? "+" : "") + delta + " ATK";
                color = new Color(1, .81f, .37f);
            }
            else if (e.Type == (int)PresentationEvent.Stats && card != null && card.Hp != e.HpAfter)
            {
                int delta = e.HpAfter - card.Hp;
                label = (delta > 0 ? "+" : "") + delta + " HP";
                color = delta > 0 ? new Color(.35f, 1, .65f) : color;
            }
            else if (e.Type == (int)PresentationEvent.Yokai && player != null && e.YokaiPowerAfter > player.YokaiPower)
            {
                label = "+" + (e.YokaiPowerAfter - player.YokaiPower);
                color = new Color(.35f, 1, .85f);
            }
            if (label == null || _numberPopupPrefab == null)
            {
                return;
            }
            Transform target = e.TargetPlayerId == presenter.Client.PlayerId ? presenter.OwnPlayer.transform : presenter.EnemyPlayer.transform;
            foreach (var zone in presenter.Zones)
            {
                var widget = zone.Widgets.Find(w => !w.Hidden && w.Card?.Id == e.TargetCardId);
                if (widget != null)
                {
                    target = widget.Face;
                    break;
                }
            }
            if (e.Type == (int)PresentationEvent.Yokai && e.TargetPlayerId == presenter.Client.PlayerId)
            {
                this.Bind("YokaiValue", out TMP_Text yokai);
                target = yokai.transform;
            }
            var popup = Instantiate(_numberPopupPrefab, _flightRoot);
            popup.transform.position = target.TransformPoint(((RectTransform)target).rect.center);
            var text = popup.GetComponentInChildren<TMP_Text>();
            text.text = label;
            text.color = color;
            Destroy(popup, NumberLifetime);
        }

        int turnGeneration;
        private Coroutine _popupRoutine;
        CanvasGroup dim;
        private RectTransform _flightRoot;
        private Coroutine _turnRoutine;
        private readonly HashSet<CardZoneView> _flightZones = new HashSet<CardZoneView>();
        public int ActiveDraws { get; private set; }
        public int ActiveSummons { get; private set; }
        public int SummonsPresented { get; private set; }
        private BattleVfxPlayer _vfx;
        private int _feedbackGeneration;
        private const float MinimumDuration = .01f;
        private const float DeckScale = .48f;
        bool bound;
        public int DrawsPresented { get; private set; }
        public int PopupsPresented { get; private set; }

        void BindReferences()
        {
            if (bound)
            {
                return;
            }
            if (!this.Bind("DragVisualRoot", out _flightRoot)
                || !this.Bind("TurnBanner", out TurnGroup)
                || !this.Bind("CardEventPopup", out PopupGroup)
                || !this.Bind("DrawCard", out DrawGroup)
                || !this.Bind("TurnDim", out dim)
                || !this.Bind("YourDeck", out OwnDeck)
                || !this.Bind("OpponentDeck", out OpponentDeck))
            {
                throw new System.InvalidOperationException(
                    "Battle effects require DragVisualRoot, TurnBanner, CardEventPopup, DrawCard, TurnDim and both deck anchors.");
            }
            if (!TurnGroup.transform.Bind("Title", out TurnTitle)
                || !TurnGroup.transform.Bind("Subtitle", out TurnSubtitle)
                || !PopupGroup.transform.Bind("Title", out PopupTitle)
                || !PopupGroup.transform.Bind("Description", out PopupBody)
                || !DrawGroup.transform.Bind("Back", out DrawLabel))
            {
                throw new System.InvalidOperationException("Battle effects are missing banner, popup or draw text bindings.");
            }
            DrawCard = (RectTransform)DrawGroup.transform;
            this.Bind("YourHand", out OwnHand);
            this.Bind("OpponentHand", out OpponentHand);
            this.Bind("BattleVfxLayer", out _vfx);
            bound = true;
            foreach (var group in new[] { TurnGroup, PopupGroup, DrawGroup, dim })
            {
                group.blocksRaycasts = group.interactable = false;
            }
        }

        public void ResetFeedback()
        {
            BindReferences();
            ++turnGeneration;
            ++_feedbackGeneration;
            StopAllCoroutines();
            _vfx?.ResetEffects();
            _popupRoutine = null;
            _turnRoutine = null;
            foreach (var zone in _flightZones)
            {
                if (zone != null)
                {
                    zone.CancelFlights();
                }
            }
            _flightZones.Clear();
            ActiveDraws = ActiveSummons = 0;
            TurnGroup.alpha = PopupGroup.alpha = DrawGroup.alpha = dim.alpha = 0;
        }

        public void Turn(bool own, int number)
        {
            if (_turnRoutine != null)
            {
                StopCoroutine(_turnRoutine);
            }
            _turnRoutine = StartCoroutine(TurnAnimation(own, number, ++turnGeneration));
        }

        public void Phase(string name)
        {
            Popup("PHASE", name);
        }

        public void Popup(string title, string body)
        {
            // A new explanation belongs to the effect starting now, not to a stale
            // popup backlog. The battle log keeps every notification.
            if (_popupRoutine != null)
            {
                StopCoroutine(_popupRoutine);
            }
            _popupRoutine = StartCoroutine(PopupAnimation(title, body));
        }

        public void Draw(CardZoneView zone, CardWidget widget, bool own)
        {
            BindReferences();
            if (zone == null || widget == null || zone.Layout == null)
            {
                throw new System.InvalidOperationException("Draw presentation requires a hand zone, widget and CardTableLayout.");
            }
            ClientAudio.Play(SoundCue.Draw);
            _flightZones.Add(zone);
            StartCoroutine(DrawAnimation(zone, widget, own));
        }

        public void Summon(CardZoneView zone, CardWidget widget, float duration)
        {
            BindReferences();
            if (zone == null || widget == null || zone.Layout == null)
            {
                return;
            }
            _flightZones.Add(zone);
            StartCoroutine(SummonAnimation(zone, widget, Mathf.Max(MinimumDuration, duration), _feedbackGeneration));
        }

        private IEnumerator SummonAnimation(CardZoneView zone, CardWidget widget, float duration, int generation)
        {
            ++ActiveSummons;
            ++SummonsPresented;
            BattleVfxCue cue = null;
            try
            {
                widget.SetInFlight(true);
                var rect = (RectTransform)widget.transform;
                var opacity = widget.GetComponent<CanvasGroup>();
                cue = _vfx?.AcquireSummon();
                for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
                {
                    int index = zone.Widgets.IndexOf(widget);
                    if (widget == null || index < 0 || generation != _feedbackGeneration)
                    {
                        yield break;
                    }
                    float progress = elapsed / duration;
                    zone.Layout.Place(widget, index, zone.Widgets.Count);
                    var center = widget.WorldCenter;
                    float reveal = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.38f, .82f, progress));
                    rect.localScale *= Mathf.Lerp(.72f, 1, reveal) + Mathf.Sin(reveal * Mathf.PI) * .06f;
                    if (opacity != null)
                    {
                        opacity.alpha = reveal;
                    }
                    cue?.Sample(center, progress);
                    yield return null;
                }
            }
            finally
            {
                _vfx?.Release(cue);
                if (widget != null)
                {
                    widget.SetInFlight(false);
                    int index = zone != null ? zone.Widgets.IndexOf(widget) : -1;
                    if (index >= 0 && zone.isActiveAndEnabled)
                    {
                        zone.Layout.Place(widget, index, zone.Widgets.Count);
                        zone.RestoreSorting();
                    }
                }
                if (generation == _feedbackGeneration)
                {
                    ActiveSummons = Mathf.Max(0, ActiveSummons - 1);
                }
            }
        }

        private void OnDisable()
        {
            if (bound)
            {
                ResetFeedback();
            }
        }

        IEnumerator TurnAnimation(bool own, int number, int generation)
        {
            if (generation != turnGeneration)
            {
                yield break;
            }
            TurnTitle.text = own ? "YOUR TURN" : "OPPONENT TURN";
            TurnSubtitle.text = "TURN " + number;
            yield return Flash(
                TurnGroup,
                (Settings?.TurnFadeIn ?? .2f)
                    + (Settings?.TurnHold ?? .7f)
                    + (Settings?.TurnFadeOut ?? .3f),
                dim,
                generation
            );
        }

        IEnumerator PopupAnimation(string title, string body)
        {
            PopupTitle.text = title;
            PopupBody.text = body;
            ++PopupsPresented;
            yield return Flash(PopupGroup, Settings?.PopupDuration ?? 1.35f);
        }

        private Vector3 DrawOrigin(CardZoneView zone)
        {
            // Deck counters stay in the HUD. Flights enter from the right edge at
            // hand height, in the same canvas space as their live destination slots.
            var canvas = (RectTransform)_flightRoot.GetComponentInParent<Canvas>().rootCanvas.transform;
            Vector3 handCenter = canvas.InverseTransformPoint(zone.Content.TransformPoint(zone.Content.rect.center));
            handCenter.x = canvas.rect.xMax + zone.Layout.CardSize.x * DeckScale;
            return _flightRoot.InverseTransformPoint(canvas.TransformPoint(handCenter));
        }

        private IEnumerator DrawAnimation(CardZoneView zone, CardWidget widget, bool own)
        {
            ++DrawsPresented;
            ++ActiveDraws;
            try
            {
                DrawLabel.text = own ? "DRAW" : "HIDDEN";
                widget.SetInFlight(true);
                var rect = (RectTransform)widget.transform;
                rect.SetParent(_flightRoot, true);
                rect.SetAsLastSibling();
                Vector3 start = DrawOrigin(zone);
                Vector3 end = start;
                Vector3 velocity = Vector3.zero;
                Quaternion startRotation = Quaternion.Euler(0, 0, Settings?.DrawRotation ?? -16f);
                float duration = Mathf.Max(MinimumDuration, Settings?.DrawDuration ?? .65f);
                float arc = (Settings?.DrawArcHeight ?? 120f) * (own ? 1 : -1);
                // All coordinates are in the shared overlay's space, including scaled canvases.
                Vector3 targetScale = new Vector3(
                    zone.Content.lossyScale.x / _flightRoot.lossyScale.x,
                    zone.Content.lossyScale.y / _flightRoot.lossyScale.y, 1);
                rect.sizeDelta = zone.Layout.CardSize;
                for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
                {
                    int index = zone.Widgets.IndexOf(widget);
                    if (index < 0 || widget == null)
                    {
                        yield break;
                    }
                    zone.Layout.GetSlot(index, zone.Widgets.Count, out var slot, out var rotation);
                    Vector3 destination = _flightRoot.InverseTransformPoint(zone.Content.TransformPoint(zone.Content.rect.center + slot));
                    // A later overlapping draw can move this slot; blend that target as well.
                    float remaining = duration - elapsed;
                    end = elapsed == 0 ? destination : Vector3.SmoothDamp(
                        end, destination, ref velocity, Mathf.Min(Settings?.HandRepositionDuration ?? .28f, remaining),
                        Mathf.Infinity, Time.unscaledDeltaTime);
                    float progress = elapsed / duration;
                    float u = Settings?.Ease(progress) ?? Mathf.SmoothStep(0, 1, progress);
                    start = DrawOrigin(zone);
                    Vector3 control = (start + end) * .5f + Vector3.up * arc;
                    rect.localPosition = (1 - u) * (1 - u) * start + 2 * (1 - u) * u * control + u * u * end;
                    Quaternion destinationRotation = Quaternion.Inverse(_flightRoot.rotation) * zone.Content.rotation * rotation;
                    rect.localRotation = Quaternion.Slerp(startRotation, destinationRotation, u);
                    float scale = Mathf.Lerp(DeckScale, 1, u)
                        + ((Settings?.DrawScale ?? 1.08f) - 1) * Mathf.Sin(Mathf.PI * u);
                    rect.localScale = targetScale * (scale * zone.Layout.CardScale);
                    yield return null;
                }
            }
            finally
            {
                if (widget != null && zone != null)
                {
                    if (zone.isActiveAndEnabled)
                    {
                        widget.transform.SetParent(zone.Content, false);
                    }
                    widget.SetInFlight(false);
                    int index = zone.Widgets.IndexOf(widget);
                    if (index >= 0 && zone.isActiveAndEnabled)
                    {
                        zone.Layout.Place(widget, index, zone.Widgets.Count);
                    }
                    zone.RestoreSorting();
                }
                ActiveDraws = Mathf.Max(0, ActiveDraws - 1);
            }
        }

        IEnumerator Flash(
            CanvasGroup group,
            float duration,
            CanvasGroup shade = null,
            int generation = -1
        )
        {
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                if (generation >= 0 && generation != turnGeneration)
                {
                    break;
                }
                group.alpha = Mathf.Min(
                    t / (Settings?.TurnFadeIn ?? .2f),
                    1,
                    (duration - t) / (Settings?.TurnFadeOut ?? .3f)
                );
                if (shade != null)
                {
                    shade.alpha = group.alpha;
                }
                yield return null;
            }
            group.alpha = 0;
            if (shade != null)
            {
                shade.alpha = 0;
            }
        }
    }
}
