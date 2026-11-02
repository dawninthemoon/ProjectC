using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProjectC.Client
{
    // One atomic document; definitions and reconnect credentials never enter this file.
    public sealed class DeckRepository
    {
        readonly string path;
        List<PersonalDeck> decks = new List<PersonalDeck>();
        public IReadOnlyList<PersonalDeck> Decks => decks.Select(d => d.Copy()).ToArray();
        public string SelectedId { get; private set; } = "";
        public string Warning { get; private set; } = "";
        public PersonalDeck Selected => Find(SelectedId);

        public DeckRepository(string directory)
        {
            path = Path.Combine(directory, "PersonalDecks.json");
        }

        public PersonalDeck Find(string id) => decks.Find(d => d.DeckId == id)?.Copy();

        public void Load()
        {
            decks.Clear();
            SelectedId = "";
            Warning = "";
            if (!File.Exists(path))
            {
                return;
            }
            try
            {
                Read(path);
            }
            catch (Exception error)
                when (error is IOException
                    || error is UnauthorizedAccessException
                    || error is JsonException
                    || error is FormatException
                    || error is InvalidCastException
                    || error is OverflowException
                )
            {
                Warning = "덱 파일을 읽지 못했습니다. 원본을 보존합니다. " + error.Message;
                decks.Clear();
                SelectedId = "";
                try
                {
                    if (File.Exists(path + ".bak"))
                    {
                        Read(path + ".bak");
                        Warning += " 이전 백업을 복구했습니다.";
                    }
                }
                catch (Exception)
                {
                    decks.Clear();
                    SelectedId = "";
                }
            }
        }

        void Read(string file)
        {
            if (new FileInfo(file).Length > 1024 * 1024)
            {
                throw new FormatException("덱 파일이 너무 큽니다.");
            }
            var root = CardLibrary.ParseObject(File.ReadAllText(file));
            if (
                root["Version"]?.Type != JTokenType.Integer
                || (int)root["Version"] != 1
                || !(root["Decks"] is JArray items)
                || items.Count > 200
            )
            {
                throw new FormatException("지원하지 않는 덱 파일 형식입니다.");
            }
            var loaded = new List<PersonalDeck>();
            var ids = new HashSet<string>();
            foreach (var item in items)
            {
                if (
                    !(item is JObject)
                    || item["DeckId"]?.Type != JTokenType.String
                    || item["Name"]?.Type != JTokenType.String
                    || !(item["Cards"] is JArray values)
                    || values.Count > 512
                )
                {
                    throw new FormatException("손상된 덱 항목입니다.");
                }
                string id = (string)item["DeckId"];
                if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !ids.Add(id))
                {
                    id = Guid.NewGuid().ToString("N");
                    ids.Add(id);
                    Warning += "중복/잘못된 DeckId를 새 ID로 복구했습니다. ";
                }
                string name = (string)item["Name"];
                if (name.Length > 60)
                {
                    name = name.Substring(0, 60);
                }
                loaded.Add(
                    new PersonalDeck
                    {
                        DeckId = id,
                        Name = string.IsNullOrWhiteSpace(name) ? "이름 없는 덱" : name,
                        Cards = values.Select(CardLibrary.Integer).ToList(),
                    }
                );
            }
            var selected = root["SelectedDeckId"];
            if (
                selected != null
                && selected.Type != JTokenType.Null
                && selected.Type != JTokenType.String
            )
            {
                throw new FormatException("SelectedDeckId must be a string.");
            }
            decks = loaded;
            SelectedId = (string)selected ?? "";
            if (!decks.Any(d => d.DeckId == SelectedId))
            {
                SelectedId = decks.FirstOrDefault()?.DeckId ?? "";
            }
        }

        public bool Save(PersonalDeck deck, out string error)
        {
            if (
                deck == null
                || string.IsNullOrWhiteSpace(deck.Name)
                || deck.Name.Trim().Length > 60
                || deck.Cards.Count > 512
            )
            {
                error = "덱 이름은 1~60자여야 합니다.";
                return false;
            }
            var next = decks.Select(d => d.Copy()).ToList();
            var index = next.FindIndex(d => d.DeckId == deck.DeckId);
            var copy = deck.Copy();
            copy.Name = copy.Name.Trim();
            if (index < 0)
            {
                next.Add(copy);
            }
            else
            {
                next[index] = copy;
            }
            return Commit(next, deck.DeckId, out error);
        }

        public bool Select(string id, out string error) =>
            Commit(decks, Find(id) != null ? id : "", out error);

        public bool Delete(string id, out string error) =>
            Commit(
                decks.Where(d => d.DeckId != id).ToList(),
                SelectedId == id
                    ? decks.FirstOrDefault(d => d.DeckId != id)?.DeckId ?? ""
                    : SelectedId,
                out error
            );

        bool Commit(IEnumerable<PersonalDeck> next, string selected, out string error)
        {
            error = "";
            var list = next.Select(d => d.Copy()).ToList();
            if (list.Count > 200)
            {
                error = "저장 가능한 덱은 최대 200개입니다.";
                return false;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var json = new JObject
                {
                    ["Version"] = 1,
                    ["SelectedDeckId"] = selected,
                    ["Decks"] = new JArray(
                        list.Select(d => new JObject
                        {
                            ["DeckId"] = d.DeckId,
                            ["Name"] = d.Name,
                            ["Cards"] = new JArray(d.Cards),
                        })
                    ),
                };
                File.WriteAllText(path + ".tmp", json.ToString(Formatting.Indented));
                if (File.Exists(path))
                {
                    if (!string.IsNullOrEmpty(Warning))
                    {
                        File.Copy(path, path + ".preserved-" + Guid.NewGuid().ToString("N"), false);
                    }
                    File.Replace(path + ".tmp", path, path + ".bak");
                }
                else
                {
                    File.Move(path + ".tmp", path);
                }
                decks = list;
                SelectedId = selected;
                Warning = "";
                return true;
            }
            catch (Exception exception)
                when (exception is IOException || exception is UnauthorizedAccessException)
            {
                error = "저장 실패: " + exception.Message;
                return false;
            }
        }
    }
}
