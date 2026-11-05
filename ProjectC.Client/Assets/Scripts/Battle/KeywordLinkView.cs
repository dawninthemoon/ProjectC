using System;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectC.Client
{
    public sealed class KeywordInfo
    {
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string Description { get; internal set; }
    }

    // Rendering and pointer handling consume parsed metadata; they never parse card rules.
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class KeywordLinkView : MonoBehaviour, IPointerClickHandler
    {
        const string LinkPrefix = "keyword:";
        TMP_Text _label;
        LocalizationService _localization;
        Action<KeywordInfo> _selected;
        public FormattedCardText Content { get; private set; }

        public static KeywordLinkView Render(TMP_Text label, FormattedCardText content,
            LocalizationService localization, Action<KeywordInfo> selected)
        {
            var view = label.GetComponent<KeywordLinkView>() ?? label.gameObject.AddComponent<KeywordLinkView>();
            view._label = label;
            view.Content = content;
            view._localization = localization;
            view._selected = selected;
            label.richText = true;
            label.raycastTarget = content.Segments.Any(segment => segment.IsKeyword);
            var text = new StringBuilder();
            foreach (var segment in content.Segments)
            {
                // Escape translated/argument markup while keeping generated TMP links active.
                string escaped = segment.Text.Replace("<", "<noparse><</noparse>");
                if (segment.IsKeyword)
                {
                    text.Append("<link=\"").Append(LinkPrefix).Append(segment.KeywordId)
                        .Append("\"><color=#FFD77A><u>").Append(escaped).Append("</u></color></link>");
                }
                else
                {
                    text.Append(escaped);
                }
            }
            label.text = text.ToString();
            return view;
        }

        public bool SelectKeyword(string id)
        {
            if (_localization == null || Content == null || !Content.Segments.Any(segment => segment.KeywordId == id))
            {
                return false;
            }
            _selected?.Invoke(new KeywordInfo
            {
                Id = id,
                Name = _localization.GetText("keyword_" + id + "_name"),
                Description = _localization.GetText("keyword_" + id + "_desc"),
            });
            return true;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            int index = TMP_TextUtilities.FindIntersectingLink(_label, eventData.position, eventData.pressEventCamera);
            if (index < 0)
            {
                return;
            }
            string link = _label.textInfo.linkInfo[index].GetLinkID();
            if (link.StartsWith(LinkPrefix, StringComparison.Ordinal))
            {
                SelectKeyword(link.Substring(LinkPrefix.Length));
            }
        }
    }
}
