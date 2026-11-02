using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProjectC.Client
{
    public sealed class TextSegment
    {
        public string Text { get; }
        public string KeywordId { get; }
        public bool IsKeyword => KeywordId != null;

        public TextSegment(string text, string keywordId = null)
        {
            Text = text;
            KeywordId = keywordId;
        }
    }

    public sealed class FormattedCardText
    {
        public IReadOnlyList<TextSegment> Segments { get; }
        public string PlainText => string.Concat(Segments.Select(segment => segment.Text));

        public FormattedCardText(IEnumerable<TextSegment> segments)
        {
            Segments = Array.AsReadOnly(segments.ToArray());
        }
    }

    public sealed class CardTextFormatter
    {
        const int MaximumPathLength = 256;
        static readonly Regex PathGrammar = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*(?:\[[0-9]+\]|\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant);
        static readonly Regex PathPart = new Regex(@"([A-Za-z_][A-Za-z0-9_]*)|\[([0-9]+)\]", RegexOptions.CultureInvariant);
        static readonly Regex Keyword = new Regex(@"^([^{}]+):@([A-Za-z][A-Za-z0-9_]*)$", RegexOptions.CultureInvariant);

        public static void ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > MaximumPathLength || !PathGrammar.IsMatch(path))
            {
                throw new FormatException("Invalid read-only description path: " + path);
            }
        }

        public static JToken ReadPath(JObject card, string path)
        {
            ValidatePath(path);
            JToken current = card;
            foreach (Match part in PathPart.Matches(path))
            {
                if (part.Groups[1].Success)
                {
                    current = (current as JObject)?[part.Groups[1].Value];
                }
                else if (current is JArray array
                    && int.TryParse(part.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                    && index < array.Count)
                {
                    current = array[index];
                }
                else
                {
                    return null;
                }
                if (current == null || current.Type == JTokenType.Null)
                {
                    return null;
                }
            }
            return current;
        }

        public FormattedCardText Format(string template, JObject card, JArray arguments)
        {
            var values = new List<string>();
            foreach (var token in arguments ?? new JArray())
            {
                if (!(token is JObject argument))
                {
                    throw new FormatException("Description argument must be an object.");
                }
                string path = CardDataLoader.RequiredText(argument, "path");
                var value = ReadPath(card, path);
                // Zero, false and empty strings are values. Only an absent/null path uses default.
                if (value == null)
                {
                    value = argument["default"];
                }
                if (value == null || value.Type == JTokenType.Null || value is JContainer)
                {
                    throw new FormatException("Missing or non-scalar description path: " + path);
                }
                values.Add(value.Type == JTokenType.String ? (string)value : value.ToString(Formatting.None));
            }
            return Parse(template, values);
        }

        public FormattedCardText Parse(string template, IReadOnlyList<string> arguments = null)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }
            var segments = new List<TextSegment>();
            var plain = new StringBuilder();
            Action flush = () =>
            {
                if (plain.Length > 0)
                {
                    segments.Add(new TextSegment(plain.ToString()));
                    plain.Clear();
                }
            };
            for (int offset = 0; offset < template.Length; ++offset)
            {
                char current = template[offset];
                if ((current == '{' || current == '}') && offset + 1 < template.Length && template[offset + 1] == current)
                {
                    plain.Append(current);
                    ++offset;
                    continue;
                }
                if (current == '}')
                {
                    throw new FormatException("Unmatched closing brace in localized text.");
                }
                if (current != '{')
                {
                    plain.Append(current);
                    continue;
                }
                int close = template.IndexOf('}', offset + 1);
                if (close < 0)
                {
                    throw new FormatException("Unclosed localized text token.");
                }
                string content = template.Substring(offset + 1, close - offset - 1);
                var keyword = Keyword.Match(content);
                if (keyword.Success)
                {
                    flush();
                    segments.Add(new TextSegment(keyword.Groups[1].Value, keyword.Groups[2].Value));
                }
                else if (int.TryParse(content, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                    && arguments != null && index < arguments.Count)
                {
                    plain.Append(arguments[index]);
                }
                else
                {
                    throw new FormatException("Invalid localized argument/token: {" + content + "}");
                }
                offset = close;
            }
            flush();
            return new FormattedCardText(segments);
        }
    }
}
