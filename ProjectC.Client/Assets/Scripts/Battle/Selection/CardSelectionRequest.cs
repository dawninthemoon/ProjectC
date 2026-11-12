using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectC.Client
{
    // Presentation requests contain only cards already authorized for this viewer.
    // They are deliberately separate from wire DTOs and never define game rules.
    public sealed class CardSelectionItem
    {
        public string Key;
        public CardView Card;
        public bool Enabled = true;
        public string SourceLabel;
        public string SourceKey;
        public string DisabledReason;
    }

    public sealed class CardSelectionRequest
    {
        public string RequestId;
        public string Title;
        public string TitleKey;
        public object[] TitleArguments = Array.Empty<object>();
        public CardSelectionItem[] Items = Array.Empty<CardSelectionItem>();
        public int MinCount;
        public int MaxCount;
        public bool AllowCancel = true;
        public bool AllowEye = true;
        public bool AllowPass;
    }

    // A disposable draft; committing it is the caller's responsibility.
    public sealed class CardSelectionDraft
    {
        private readonly HashSet<string> _selected = new HashSet<string>();
        private CardSelectionRequest _request;
        public IReadOnlyCollection<string> Selected => _selected;
        public bool CanConfirm => _request != null && _selected.Count >= _request.MinCount
            && _selected.Count <= _request.MaxCount;

        public void Refresh(CardSelectionRequest request)
        {
            if (request == null || request.MinCount < 0 || request.MaxCount < request.MinCount
                || request.Items == null || request.Items.Any(item => item == null || string.IsNullOrEmpty(item.Key))
                || request.Items.Select(item => item.Key).Distinct().Count() != request.Items.Length)
            {
                throw new ArgumentException("Selection requires unique item keys and valid bounds.", nameof(request));
            }
            _request = request;
            _selected.RemoveWhere(key => !request.Items.Any(item => item.Key == key && item.Enabled));
            if (_selected.Count > request.MaxCount)
            {
                _selected.Clear();
            }
        }

        public bool Toggle(string key)
        {
            if (_request == null || !_request.Items.Any(item => item.Key == key && item.Enabled))
            {
                return false;
            }
            if (_selected.Remove(key))
            {
                return true;
            }
            if (_selected.Count >= _request.MaxCount)
            {
                return false;
            }
            return _selected.Add(key);
        }

        public void Restore(IEnumerable<string> selected)
        {
            _selected.Clear();
            foreach (string key in selected)
            {
                if (_selected.Count < _request.MaxCount && _request.Items.Any(item => item.Key == key && item.Enabled))
                {
                    _selected.Add(key);
                }
            }
        }
    }
}
