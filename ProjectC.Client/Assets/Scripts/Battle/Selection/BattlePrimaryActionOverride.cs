using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    // Borrows the existing button's exact raycast rectangle without replacing its listeners or art.
    public sealed class BattlePrimaryActionOverride : IDisposable
    {
        private readonly Button _original;
        private readonly Button _overlay;
        private readonly TMP_Text _label;
        private readonly string _previousText;
        private readonly string _labelKey;
        private readonly Vector4 _previousMargin;
        private readonly bool _previousAutoSize;
        private readonly float _previousMinSize, _previousMaxSize, _previousFontSize;
        private readonly TextWrappingModes _previousWrapping;
        private const float VisibleTextWidth = 200;
        private bool _disposed;

        public BattlePrimaryActionOverride(Button original, string labelKey, Action clicked, string overlayName = "SelectionPrimaryAction")
        {
            _original = original != null ? original : throw new ArgumentNullException(nameof(original));
            _label = original.GetComponentInChildren<TMP_Text>();
            _previousText = _label.text;
            _labelKey = labelKey;
            _previousMargin = _label.margin;
            _previousAutoSize = _label.enableAutoSizing;
            _previousMinSize = _label.fontSizeMin;
            _previousMaxSize = _label.fontSizeMax;
            _previousFontSize = _label.fontSize;
            _previousWrapping = _label.textWrappingMode;
            var root = new GameObject(overlayName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = (RectTransform)root.transform;
            rect.SetParent(original.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = Color.clear;
            _overlay = root.GetComponent<Button>();
            _overlay.transition = Selectable.Transition.None;
            _overlay.onClick.AddListener(() =>
            {
                if (!_disposed && _overlay.interactable)
                {
                    clicked?.Invoke();
                }
            });
        }

        public void Render(bool submitting)
        {
            if (_disposed)
            {
                return;
            }
            _original.interactable = false;
            _label.text = BattleText.Get(_labelKey);
            float inset = Mathf.Max(0, (_label.rectTransform.rect.width - VisibleTextWidth) * .5f);
            _label.margin = new Vector4(Mathf.Max(_previousMargin.x, inset), _previousMargin.y,
                Mathf.Max(_previousMargin.z, inset), _previousMargin.w);
            _label.enableAutoSizing = true;
            _label.fontSizeMin = 18;
            _label.fontSizeMax = _previousMaxSize;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _overlay.interactable = !submitting;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_overlay != null)
            {
                _overlay.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(_overlay.gameObject);
            }
            if (_label != null)
            {
                _label.text = _previousText;
                _label.margin = _previousMargin;
                _label.enableAutoSizing = _previousAutoSize;
                _label.fontSizeMin = _previousMinSize;
                _label.fontSizeMax = _previousMaxSize;
                _label.fontSize = _previousFontSize;
                _label.textWrappingMode = _previousWrapping;
            }
        }
    }
}
