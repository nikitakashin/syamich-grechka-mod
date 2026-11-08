using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Grechka.Quests;
using HarmonyLib;
using Verse;

internal static class QuestSnapshotRegression
{
    internal static void Run(QuestGame fixture, string[] args, Action<bool,string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(), "grechka-book-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            // Include real line breaks, a literal backslash-n, escaped quotes and a trailing slash.
            fixture.Pack.quests.Last().description = "Первая строка\nВторая \\\\n \\n «текст» \"цитата\" \\";
            var archive = PackCodec.Clone(fixture.Pack);
            archive.id = "archived.fixture"; archive.quests.Last().description += " своя архивная редакция";
            ((BookHistory)AccessTools.Property(typeof(QuestGame), "History").GetValue(fixture)).Remember(archive);
            Save(temp, fixture);
            CheckComponent(XElement.Load(temp), temp, check);

            int arg = Array.IndexOf(args, "--snapshot-save");
            if (arg >= 0)
            {
                // Opt-in forensic reproduction. The real save is opened read-only;
                // all native Scribe writes target our unique temporary XML.
                var doc = XDocument.Load(args[arg + 1]);
                var component = doc.Descendants("grechkaQuestPack").Single().Parent;
                CheckComponent(new XElement("sample", component.Elements()), temp, check);
            }
        }
        finally
        {
            Scribe.mode = LoadSaveMode.Inactive;
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    static void CheckComponent(XElement component, string temp, Action<bool,string> check)
    {
        string canonical = PackCodec.Encode(PackCodec.Decode(component.Element("grechkaQuestPack").Value));
        string[] Values(string label) => component.Element(label)?.Elements("li").Select(x => x.Value).ToArray() ?? Array.Empty<string>();
        var books = Values("grechkaBookArchiveValues").Select(PackCodec.Decode)
            .ToDictionary(p => p.id, p => PackCodec.Encode(p));
        var claims = Values("grechkaClaimed").ToHashSet();
        var counts = QuestProgress.ReadCounts(Values("grechkaEventKeys"), Values("grechkaEventCounts").Select(int.Parse).ToArray());
        var achieved = QuestProgress.ReadPairs(Values("grechkaGoalKeys"), Values("grechkaGoalSignatures"));
        var choices = QuestProgress.ReadPairs(Values("grechkaRewardChoiceKeys"), Values("grechkaRewardChoiceValues"));
        component.Save(temp);
        string ordinaryRead = null;
        List<string> ordinaryArchive = null;
        Scribe.loader.InitLoading(temp);
        Scribe_Values.Look(ref ordinaryRead, "grechkaQuestPack", null);
        Scribe_Collections.Look(ref ordinaryArchive, "grechkaBookArchiveValues", LookMode.Value);
        Scribe.loader.FinalizeLoading();
        JsonException originalError = null;
        try { PackCodec.Decode(ordinaryRead); } catch (JsonException e) { originalError = e; }
        check(originalError != null, "Native Scribe string conversion reproduces defective legacy JSON");
        Console.WriteLine("Legacy decoder: " + originalError.Message);

        void Verify(QuestGame restored, string label)
        {
            var progress = (QuestProgress)AccessTools.Property(typeof(QuestGame), "Progress").GetValue(restored);
            var restoredBooks = restored.SavedBooks().ToDictionary(p => p.id, p => PackCodec.Encode(p));
            bool Same<T>(Dictionary<string,T> left, Dictionary<string,T> right)
                => left.Count == right.Count && left.All(p => right.TryGetValue(p.Key, out var v) && EqualityComparer<T>.Default.Equals(p.Value, v));
            check(restored.LoadError == null && PackCodec.Encode(restored.Pack) == canonical && Same(books, restoredBooks),
                label + " preserves all authored content and archived books (" + restored.Pack.quests.Count + " quests)");
            check(claims.SetEquals(progress.CopyClaimed()) && Same(counts, progress.CopyEventCounts())
                && Same(achieved, progress.CopyAchieved()) && Same(choices, progress.CopyRewardChoices()),
                label + " preserves recorded rewards, chosen alternatives, objective completions and counts");
        }

        Verify(Load(temp), "Original save");
        component.Element("grechkaQuestPack").Value = ordinaryRead;
        int index = 0;
        foreach (var li in component.Element("grechkaBookArchiveValues").Elements("li")) li.Value = ordinaryArchive[index++];
        component.Save(temp);
        var repaired = Load(temp);
        Verify(repaired, "Previously re-saved damaged JSON");
        Save(temp, repaired);
        Verify(Load(temp), "Next save/reload");
    }

    static QuestGame Load(string path)
    {
        var restored = (QuestGame)RuntimeHelpers.GetUninitializedObject(typeof(QuestGame));
        Scribe.loader.InitLoading(path); restored.ExposeData(); Scribe.loader.FinalizeLoading();
        Scribe.mode = LoadSaveMode.PostLoadInit;
        try { restored.ExposeData(); } finally { Scribe.mode = LoadSaveMode.Inactive; }
        return restored;
    }

    static void Save(string path, QuestGame game)
    {
        Scribe.saver.InitSaving(path, "sample"); game.ExposeData(); Scribe.saver.FinalizeSaving();
    }
}
