using System;
using System.Collections.Generic;
using System.Linq;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectC.Client
{
    // UI-only defense draft. No network command is invented for the unfinished server feature.
    [DefaultExecutionOrder(100)]
    public sealed class DefenseAssignmentView : MonoBehaviour
    {
        private readonly DefenseAssignmentDraft _draft = new DefenseAssignmentDraft();
        private readonly List<DefenseCardHandle> _handles = new List<DefenseCardHandle>();
        private readonly Dictionary<int, TMP_Text> _counters = new Dictionary<int, TMP_Text>();
        private readonly Dictionary<int, GameObject> _shields = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, DefenseBezierGraphic> _lines = new Dictionary<int, DefenseBezierGraphic>();
        private RectTransform _lineRoot;
        private RectTransform _handleRoot;
        private DefenseBezierGraphic _dragLine;
        private DefenseCardHandle _source;
        private DefenseAssignmentRequest _request;
        private BattlePresenter _presenter;
        private IDisposable _scope;
        private BattlePrimaryActionOverride _primaryAction;
        private bool _submitting;
        public bool IsOpen { get; private set; }
        public DefenseAssignmentDraft Draft => _draft;
        public event Action<DefenseAssignmentPair[]> Confirmed;
        public event Action AssignmentsChanged;

        public void Show(BattlePresenter presenter, DefenseAssignmentRequest request)
        {
            bool refreshing = IsOpen;
            _presenter = presenter != null ? presenter : throw new ArgumentNullException(nameof(presenter));
            _draft.Refresh(request);
            if (!refreshing)
            {
                _draft.Restore(request.Assignments);
                _scope = presenter.Client.Input.PushScope("DefenseAssignmentPreview", new[]
                {
                    GameplayInputAction.OpenCardDetails, GameplayInputAction.ConfirmSelection,
                });
                presenter.Client.ViewInvalidated += Close;
            }
            _request = request;
            _submitting = false;
            if (!this.Bind("DefenseLines", out _lineRoot) || !this.Bind("DefenseHandles", out _handleRoot))
            {
                throw new InvalidOperationException("DefenseAssignment requires line and handle layers.");
            }
            ClearHandles();
            foreach (var card in presenter.EnemyField.Widgets)
            {
                AddHandle(card, true);
            }
            foreach (var card in presenter.OwnField.Widgets)
            {
                AddHandle(card, false);
            }
            if (_primaryAction == null)
            {
                _primaryAction = new BattlePrimaryActionOverride(presenter.EndTurn, "ui.defense.confirm", () =>
                {
                    if (!_submitting && _presenter.Client.Input.CanExecute(GameplayInputAction.ConfirmSelection))
                    {
                        Confirmed?.Invoke(_draft.Export());
                    }
                }, "DefenseConfirmOverlay");
            }
            IsOpen = true;
            gameObject.SetActive(true);
            RefreshLines();
        }

        private void AddHandle(CardWidget card, bool attacker)
        {
            if (card.Card == null)
            {
                return;
            }
            var root = new GameObject("DefenseCard_" + card.Card.Id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(DefenseCardHandle));
            root.transform.SetParent(_handleRoot, false);
            root.GetComponent<Image>().color = Color.clear;
            var handle = root.GetComponent<DefenseCardHandle>();
            handle.View = this;
            handle.Card = card;
            handle.Attacker = attacker;
            _handles.Add(handle);
            if (_request.AttackCounters.TryGetValue(card.Card.Id, out int counter))
            {
                var tag = new GameObject("AttackCounter", typeof(RectTransform), typeof(CanvasRenderer), typeof(SelectionFrameGraphic));
                var rect = (RectTransform)tag.transform;
                rect.SetParent(root.transform, false);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
                rect.anchoredPosition = new Vector2(0, 4);
                rect.sizeDelta = new Vector2(60, 30);
                var graphic = tag.GetComponent<SelectionFrameGraphic>();
                graphic.color = new Color(.16f, .12f, .13f);
                graphic.Border = counter <= 1 ? new Color(.85f, .21f, .17f) : new Color(.9f, .77f, .56f);
                graphic.raycastTarget = false;
                var text = new GameObject("AttackCounterValue", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                var label = text.GetComponent<TMP_Text>();
                label.rectTransform.SetParent(rect, false);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                label.text = counter.ToString();
                label.fontSize = 25;
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
                BattleText.StyleValue(label);
                _counters[card.Card.Id] = label;
            }
            if (!attacker)
            {
                var shield = new GameObject("AssignedShield", typeof(RectTransform), typeof(CanvasRenderer), typeof(SelectionFrameGraphic));
                var rect = (RectTransform)shield.transform;
                rect.SetParent(root.transform, false);
                rect.anchorMin = rect.anchorMax = Vector2.one;
                rect.anchoredPosition = new Vector2(-8, -20);
                rect.sizeDelta = new Vector2(36, 42);
                var graphic = shield.GetComponent<SelectionFrameGraphic>();
                graphic.Shape = SelectionFrameGraphic.FrameShape.Shield;
                graphic.color = new Color(.74f, .22f, .15f);
                graphic.Border = new Color(1, .83f, .48f);
                graphic.raycastTarget = false;
                _shields[card.Card.Id] = shield;
            }
        }

        private void LateUpdate()
        {
            if (!IsOpen)
            {
                return;
            }
            foreach (var handle in _handles)
            {
                if (handle.Card == null || !handle.Card.gameObject.activeInHierarchy)
                {
                    Close();
                    return;
                }
                var rect = (RectTransform)handle.transform;
                var face = handle.Card.Face;
                rect.localPosition = _handleRoot.InverseTransformPoint(handle.Card.WorldCenter);
                rect.rotation = face.rotation;
                rect.sizeDelta = new Vector2(_handleRoot.InverseTransformVector(face.TransformVector(Vector3.right * face.rect.width)).magnitude,
                    _handleRoot.InverseTransformVector(face.TransformVector(Vector3.up * face.rect.height)).magnitude);
                int id = handle.Card.Card.Id;
                bool assigned = !handle.Attacker && _draft.Assignments.Values.Contains(id);
                bool eligible = _source == null ? _request.AllowedPairs.Any(pair => handle.Attacker ? pair.AttackerId == id : pair.DefenderId == id)
                    : handle.Attacker != _source.Attacker && PairAllowed(_source, handle);
                handle.Card.SetHint(eligible ? TargetHint.Available : TargetHint.Unavailable);
                handle.Card.SetSelected(assigned || handle == _source);
                if (_shields.TryGetValue(id, out var shield))
                {
                    shield.SetActive(assigned);
                }
                if (_counters.TryGetValue(id, out var counter))
                {
                    int value = _request.AttackCounters[id];
                    counter.color = value <= 1 ? new Color(1, .42f, .28f) : new Color(1, .95f, .8f);
                    var group = counter.GetComponent<CanvasGroup>();
                    if (group == null)
                    {
                        group = counter.gameObject.AddComponent<CanvasGroup>();
                    }
                    group.alpha = value == 0 ? .8f + .2f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2 / 1.2f) : 1;
                }
            }
            RefreshLines();
            RenderPrimaryAction();
        }

        public void RenderPrimaryAction()
        {
            if (!IsOpen)
            {
                return;
            }
            _primaryAction?.Render(_submitting);
        }

        public void SetSubmitting(bool submitting) => _submitting = submitting;
        public void Inspect(CardView card) => _presenter.InspectCard(card);

        public void Select(DefenseCardHandle handle)
        {
            if (_submitting)
            {
                return;
            }
            if (_source != null && _source.Attacker != handle.Attacker)
            {
                Commit(_source, handle);
                _source = null;
            }
            else
            {
                _source = _source == handle ? null : handle;
            }
        }

        public void BeginDrag(DefenseCardHandle handle, PointerEventData pointer)
        {
            if (_submitting)
            {
                return;
            }
            _source = handle;
            _dragLine = _dragLine != null ? _dragLine : CreateLine("DefenseDragPreview");
            _dragLine.gameObject.SetActive(true);
            MoveDrag(pointer);
        }

        public void MoveDrag(PointerEventData pointer)
        {
            if (_source == null || _dragLine == null || _submitting)
            {
                return;
            }
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(_lineRoot, pointer.position, pointer.pressEventCamera, out Vector3 point))
            {
                // Canonical direction remains attacker -> defender even for reverse gestures.
                Vector3 origin = Edge(_source.Card, _source.Attacker);
                _dragLine.SetEndpoints(_source.Attacker ? origin : point, _source.Attacker ? point : origin);
            }
        }

        public void EndDrag(PointerEventData pointer)
        {
            if (_source != null && !_submitting)
            {
                Camera camera = GetComponentInParent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay
                    ? null : GetComponentInParent<Canvas>().worldCamera;
                var target = _handles.LastOrDefault(handle => handle.Attacker != _source.Attacker
                    && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)handle.transform, pointer.position, camera));
                if (target != null)
                {
                    Commit(_source, target);
                }
            }
            _source = null;
            if (_dragLine != null)
            {
                _dragLine.gameObject.SetActive(false);
            }
        }

        private bool PairAllowed(DefenseCardHandle first, DefenseCardHandle second) => _draft.CanAssign(
            first.Attacker ? first.Card.Card.Id : second.Card.Card.Id,
            first.Attacker ? second.Card.Card.Id : first.Card.Card.Id);

        private void Commit(DefenseCardHandle first, DefenseCardHandle second)
        {
            if (_draft.Assign(first.Attacker ? first.Card.Card.Id : second.Card.Card.Id,
                first.Attacker ? second.Card.Card.Id : first.Card.Card.Id))
            {
                RefreshLines();
                AssignmentsChanged?.Invoke();
            }
        }

        private void RefreshLines()
        {
            foreach (var line in _lines.Values)
            {
                line.gameObject.SetActive(false);
            }
            foreach (var pair in _draft.Assignments)
            {
                var attacker = _handles.FirstOrDefault(handle => handle.Attacker && handle.Card.Card.Id == pair.Key);
                var defender = _handles.FirstOrDefault(handle => !handle.Attacker && handle.Card.Card.Id == pair.Value);
                if (attacker == null || defender == null)
                {
                    continue;
                }
                if (!_lines.TryGetValue(pair.Key, out var line))
                {
                    line = CreateLine("DefenseLink_" + pair.Key);
                    _lines.Add(pair.Key, line);
                }
                line.gameObject.SetActive(true);
                line.SetEndpoints(Edge(attacker.Card, true), Edge(defender.Card, false));
            }
        }

        private static Vector3 Edge(CardWidget card, bool attacker) => card.Face.TransformPoint(
            new Vector3(card.Face.rect.center.x, attacker ? card.Face.rect.yMin - 8 : card.Face.rect.yMax + 8));

        private DefenseBezierGraphic CreateLine(string name)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(DefenseBezierGraphic));
            var rect = (RectTransform)root.transform;
            rect.SetParent(_lineRoot, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var line = root.GetComponent<DefenseBezierGraphic>();
            line.raycastTarget = false;
            line.color = new Color(1, .76f, .3f, 1);
            return line;
        }

        private void ClearHandles()
        {
            foreach (var handle in _handles)
            {
                if (handle.Card != null)
                {
                    handle.Card.SetSelected(false);
                    handle.Card.SetHint(TargetHint.None);
                }
                handle.gameObject.SetActive(false);
                Destroy(handle.gameObject);
            }
            _handles.Clear();
            _counters.Clear();
            _shields.Clear();
        }

        public void Close()
        {
            if (_presenter != null)
            {
                _presenter.Client.ViewInvalidated -= Close;
            }
            IsOpen = false;
            _source = null;
            _scope?.Dispose();
            _scope = null;
            ClearHandles();
            foreach (var line in _lines.Values)
            {
                if (line != null)
                {
                    line.gameObject.SetActive(false);
                }
            }
            if (_dragLine != null)
            {
                _dragLine.gameObject.SetActive(false);
            }
            _primaryAction?.Dispose();
            _primaryAction = null;
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            if (IsOpen)
            {
                Close();
            }
        }
    }
}
