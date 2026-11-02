using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ProjectC.Client
{
    public sealed class LocalizationService
    {
        public static readonly IReadOnlyList<string> Languages = Array.AsReadOnly(new[]
        {
            "korean", "english", "japanese", "zh-tw", "zh-cn", "portuguese",
        });
        readonly Dictionary<string, Dictionary<string, string>> _entries = new Dictionary<string, Dictionary<string, string>>();
        readonly HashSet<string> _reported = new HashSet<string>();
        readonly Action<string> _diagnostic;
        string _language = "korean";

        public string Language
        {
            get => _language;
            set
            {
                if (!Languages.Contains(value))
                {
                    throw new ArgumentException("Unsupported language: " + value, nameof(value));
                }
                _language = value;
            }
        }

        LocalizationService(Action<string> diagnostic)
        {
            _diagnostic = diagnostic ?? (message => Debug.LogError(message));
        }

        public static LocalizationService Load(string text, Action<string> diagnostic = null)
        {
            if (!(CardDataLoader.Parse(text) is JArray rows))
            {
                throw new FormatException("Localization data must be an array.");
            }
            var service = new LocalizationService(diagnostic);
            foreach (var token in rows)
            {
                if (!(token is JObject row))
                {
                    throw new FormatException("A localization row must be an object.");
                }
                string key = CardDataLoader.RequiredText(row, "lmk");
                if (service._entries.ContainsKey(key))
                {
                    throw new FormatException("Duplicate localization key: " + key);
                }
                var translations = new Dictionary<string, string>();
                foreach (var field in row.Properties().Where(field => field.Name != "lmk"))
                {
                    if (!Languages.Contains(field.Name)
                        || (field.Value.Type != JTokenType.String && field.Value.Type != JTokenType.Null))
                    {
                        throw new FormatException("Invalid localization language field: " + field.Name);
                    }
                    translations.Add(field.Name, (string)field.Value);
                }
                service._entries.Add(key, translations);
            }
            return service;
        }

        public bool ContainsKey(string key) => key != null && _entries.ContainsKey(key);

        public string GetText(string key)
        {
            if (key != null && _entries.TryGetValue(key, out var translations))
            {
                // Requested language -> Korean -> English -> visible key.
                foreach (string language in new[] { Language, "korean", "english" }.Distinct())
                {
                    if (translations.TryGetValue(language, out string text) && !string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
            string visibleKey = key ?? "<null localization key>";
            string diagnosticKey = Language + ":" + visibleKey;
            if (_reported.Add(diagnosticKey))
            {
                _diagnostic("Missing localization text: " + diagnosticKey);
            }
            return visibleKey;
        }
    }
}
