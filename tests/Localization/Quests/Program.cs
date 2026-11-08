using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Grechka.Quests;

string root = args[0];
Dictionary<string, string> Language(string language) => Directory.GetFiles(Path.Combine(root, "Languages", language, "Keyed"), "*.xml")
    .SelectMany(path => XDocument.Load(path).Root.Elements()).ToDictionary(node => node.Name.LocalName, node => node.Value);
var english = Language("English");
var russian = Language("Russian");
if (!english.Keys.ToHashSet().SetEquals(russian.Keys)) throw new Exception("Language keys differ");
if (english.Values.Any(value => Regex.IsMatch(value, "[А-Яа-яЁё]"))) throw new Exception("English contains untranslated Russian");
int checkedTexts = 0;
foreach (string path in Directory.GetFiles(Path.Combine(root, "QuestPacks"), "*.json"))
{
    QuestText.Translate = (key, fallback) => fallback;
    var pack = PackCodec.Decode(File.ReadAllText(path));
    string before = PackCodec.Encode(pack);
    using var doc = JsonDocument.Parse(before);
    QuestText.Translate = (key, fallback) => english.TryGetValue(key, out string translated) ? translated : fallback;
    void Visit(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) Visit(child);
        else if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is "title" or "description" or "text" && property.Value.ValueKind == JsonValueKind.String)
                {
                    string source = property.Value.GetString();
                    string translated = QuestPackText.Content(source);
                    if (Regex.IsMatch(translated ?? "", "[А-Яа-яЁё]")) throw new Exception("Untranslated pack content: " + source);
                    if (!string.IsNullOrEmpty(source) && QuestPackText.Content(source + " custom edit") != source + " custom edit")
                        throw new Exception("Custom content was replaced");
                    checkedTexts++;
                }
                else Visit(property.Value);
            }
    }
    Visit(doc.RootElement);
    if (PackCodec.Encode(pack) != before) throw new Exception("Displaying translations changed serialized quest data");
    QuestText.Translate = (key, fallback) => russian.TryGetValue(key, out string translated) ? translated : fallback;
    if (QuestPackText.Content(pack.title) != pack.title) throw new Exception("Russian source not preserved");
}
Console.WriteLine($"PASS quest localization: {english.Count} matching keys, {checkedTexts} pack text fields, custom edits and serialized data preserved");
