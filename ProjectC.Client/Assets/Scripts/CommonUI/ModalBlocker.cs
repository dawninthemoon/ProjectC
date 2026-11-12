using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectC.Client
{
    // One full-screen graphic, shared by leases. Pointer holes must satisfy every active modal.
    public sealed class ModalBlocker : MonoBehaviour, ICanvasRaycastFilter, IPointerClickHandler
    {
        private const int BlockerSortingOffset = 100;
        private const int ForegroundSortingOffset = 1;
        private readonly Dictionary<Guid, Lease> _leases = new Dictionary<Guid, Lease>();
        private readonly Dictionary<RectTransform, Foreground> _foregrounds = new Dictionary<RectTransform, Foreground>();
        private Image _image;
        private int _sortingOrder;
        private long _nextOrder;
        private bool _destroying;
        public int ActiveCount => _leases.Count;
        private Color _baseTint = new Color(0, 0, 0, .12f);
        public Color Tint
        {
            get => _image.color;
            set
            {
                _baseTint = value;
                Refresh();
            }
        }

        public sealed class Lease : IDisposable
        {
            private ModalBlocker _blocker;
            internal readonly Guid Id = Guid.NewGuid();
            internal UnityEngine.Object Owner;
            internal Func<Vector2, Camera, bool> PassThrough;
            internal RectTransform[] Foregrounds;
            internal long Order;
            // Only browsing leases opt into dismissal; mandatory selections never do.
            public Action OutsideClicked { get; set; }
            private Color? _backdropTint;
            public Color? BackdropTint
            {
                get => _backdropTint;
                set
                {
                    _backdropTint = value;
                    _blocker?.Refresh();
                }
            }
            internal Lease(ModalBlocker blocker)
            {
                _blocker = blocker;
            }
            public void Dispose()
            {
                if (_blocker != null)
                {
                    _blocker._leases.Remove(Id);
                    _blocker.Refresh();
                    _blocker = null;
                }
            }
        }

        private sealed class Foreground
        {
            public Canvas Canvas;
            public GraphicRaycaster Raycaster;
            public bool OverrideSorting, CanvasEnabled, RaycasterEnabled;
            public int SortingOrder;
        }

        public static ModalBlocker Create(RectTransform root)
        {
            var obj = new GameObject("GameplayModalBlocker", typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster), typeof(Image), typeof(ModalBlocker));
            obj.transform.SetParent(root, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var blocker = obj.GetComponent<ModalBlocker>();
            blocker._image = obj.GetComponent<Image>();
            blocker._image.color = new Color(0, 0, 0, .12f);
            blocker._image.raycastTarget = true;
            blocker._sortingOrder = root.GetComponentInParent<Canvas>().rootCanvas.sortingOrder + BlockerSortingOffset;
            var canvas = obj.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = blocker._sortingOrder;
            obj.SetActive(false);
            return blocker;
        }

        public Lease Acquire(UnityEngine.Object owner, Func<Vector2, Camera, bool> passThrough,
            params RectTransform[] foregrounds)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            var lease = new Lease(this) { Owner = owner, PassThrough = passThrough,
                Foregrounds = foregrounds ?? Array.Empty<RectTransform>(), Order = ++_nextOrder };
            _leases.Add(lease.Id, lease);
            Refresh();
            return lease;
        }

        private void Refresh()
        {
            foreach (var pair in _foregrounds)
            {
                var value = pair.Value;
                if (value.Canvas != null)
                {
                    value.Canvas.overrideSorting = value.OverrideSorting;
                    value.Canvas.sortingOrder = value.SortingOrder;
                    value.Canvas.enabled = value.CanvasEnabled;
                }
                if (value.Raycaster != null)
                {
                    value.Raycaster.enabled = value.RaycasterEnabled;
                }
            }
            var top = TopLease();
            if (_image != null)
            {
                _image.color = top?.BackdropTint ?? _baseTint;
            }
            if (top != null)
            {
                foreach (var root in top.Foregrounds)
                {
                    if (root == null)
                    {
                        continue;
                    }
                    if (!_foregrounds.TryGetValue(root, out var value))
                    {
                        var canvas = root.GetComponent<Canvas>();
                        var raycaster = root.GetComponent<GraphicRaycaster>();
                        value = new Foreground();
                        value.Canvas = canvas != null ? canvas : root.gameObject.AddComponent<Canvas>();
                        value.Raycaster = raycaster != null ? raycaster : root.gameObject.AddComponent<GraphicRaycaster>();
                        value.OverrideSorting = value.Canvas.overrideSorting;
                        value.SortingOrder = value.Canvas.sortingOrder;
                        value.CanvasEnabled = value.Canvas.enabled;
                        value.RaycasterEnabled = value.Raycaster.enabled;
                        _foregrounds.Add(root, value);
                    }
                    value.Canvas.enabled = value.Raycaster.enabled = true;
                    value.Canvas.overrideSorting = true;
                    value.Canvas.sortingOrder = _sortingOrder + ForegroundSortingOffset;
                }
            }
            if (!_destroying)
            {
                gameObject.SetActive(_leases.Count != 0);
            }
        }

        public bool IsRaycastLocationValid(Vector2 position, Camera camera)
        {
            if (_leases.Count == 0)
            {
                return false;
            }
            foreach (var lease in _leases.Values)
            {
                if (lease.PassThrough == null || !lease.PassThrough(position, camera))
                {
                    return true;
                }
            }
            return false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            var top = TopLease();
            // Consume this event before releasing the modal. Never raycast/redispatch
            // against newly exposed controls underneath the blocker.
            eventData.Use();
            top?.OutsideClicked?.Invoke();
        }

        private Lease TopLease()
        {
            Lease top = null;
            foreach (var lease in _leases.Values)
            {
                if (top == null || lease.Order > top.Order)
                {
                    top = lease;
                }
            }
            return top;
        }

        private void LateUpdate()
        {
            var expired = new List<Lease>();
            foreach (var lease in _leases.Values)
            {
                if (lease.Owner == null || (lease.Owner is Behaviour owner && !owner.isActiveAndEnabled)
                    || (lease.Owner is GameObject ownerObject && !ownerObject.activeInHierarchy))
                {
                    expired.Add(lease);
                }
            }
            foreach (var lease in expired)
            {
                lease.Dispose();
            }
            var top = TopLease();
            if (top != null)
            {
                foreach (var root in top.Foregrounds)
                {
                    // A Canvas added to an inactive picker may reset its sorting on activation.
                    if (root != null && root.gameObject.activeInHierarchy
                        && _foregrounds.TryGetValue(root, out var value)
                        && (!value.Canvas.overrideSorting || value.Canvas.sortingOrder != _sortingOrder + ForegroundSortingOffset))
                    {
                        Refresh();
                        break;
                    }
                }
            }
        }

        public void Clear()
        {
            _leases.Clear();
            Refresh();
        }

        private void OnDestroy()
        {
            _destroying = true;
            _leases.Clear();
            // Restore sorting/raycast settings even when the blocker itself is destroyed.
            Refresh();
        }
    }
}
