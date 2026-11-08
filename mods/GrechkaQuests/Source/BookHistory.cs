using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Grechka.Quests
{
    // These records live in the save, not in the exported book or on the computer globally.
    public sealed class BookHistory
    {
        readonly Dictionary<string, string> books;
        readonly Dictionary<string, string> definitions;
        static readonly JsonSerializerOptions CompareOptions = new JsonSerializerOptions { IncludeFields = true };

        public BookHistory(IDictionary<string, string> savedBooks, IDictionary<string, string> savedDefinitions)
        {
            books = savedBooks == null ? new Dictionary<string, string>() : new Dictionary<string, string>(savedBooks);
            definitions = savedDefinitions == null ? new Dictionary<string, string>() : new Dictionary<string, string>(savedDefinitions);
            foreach (var pair in books)
                if (PackCodec.Decode(pair.Value).id != pair.Key) throw new FormatException(QuestText.Get("GrechkaQuests_3b2ab84d9a1a", "ID сохранённой книги не совпадает с её содержимым: ") + pair.Key);
        }

        // Seed old saves from what is genuinely known. Achieved signatures take precedence
        // if an older version already allowed a conflicting edit with the same ID.
        public void Seed(QuestPack active, IDictionary<string, string> achieved)
        {
            if (achieved != null) foreach (var pair in achieved) if (!definitions.ContainsKey(pair.Key)) definitions.Add(pair.Key, pair.Value);
            if (active == null) return;
            foreach (Quest quest in active.quests)
                foreach (Objective goal in quest.objectives)
                {
                    string key = PackCodec.ObjectiveKey(active.id, quest.id, goal.id);
                    if (!definitions.ContainsKey(key)) definitions.Add(key, QuestProgress.Signature(goal));
                }
            books[active.id] = PackCodec.Encode(active);
        }

        public List<string> Conflicts(QuestPack candidate)
        {
            var conflicts = new List<string>();
            foreach (Quest quest in candidate.quests)
                foreach (Objective goal in quest.objectives)
                {
                    string key = PackCodec.ObjectiveKey(candidate.id, quest.id, goal.id), previous;
                    if (definitions.TryGetValue(key, out previous) && previous != QuestProgress.Signature(goal))
                        conflicts.Add(QuestText.Content(quest.title) + " / " + goal.id);
                }
            return conflicts;
        }

        public void Remember(QuestPack candidate)
        {
            var conflicts = Conflicts(candidate);
            if (conflicts.Count != 0) throw new InvalidOperationException(QuestText.Get("GrechkaQuests_e5a800354d13", "Смысл цели изменён с прежним ID: ") + string.Join(", ", conflicts.Take(5)) + QuestText.Get("GrechkaQuests_11eea5b09a37", ". Назначьте ей новый ID в редакторе."));
            foreach (Quest quest in candidate.quests)
                foreach (Objective goal in quest.objectives)
                    definitions[PackCodec.ObjectiveKey(candidate.id, quest.id, goal.id)] = QuestProgress.Signature(goal);
            books[candidate.id] = PackCodec.Encode(candidate);
        }

        public List<QuestPack> SavedBooks() { return books.Values.Select(PackCodec.Decode).OrderBy(p => p.title).ToList(); }
        public Dictionary<string, string> CopyBooks() { return new Dictionary<string, string>(books); }
        public Dictionary<string, string> CopyDefinitions() { return new Dictionary<string, string>(definitions); }

        public static string DescribeChange(QuestPack before, QuestPack after)
        {
            bool same = before != null && before.id == after.id;
            var old = same ? before.quests.ToDictionary(q => q.id) : new Dictionary<string, Quest>();
            var next = after.quests.ToDictionary(q => q.id);
            int added = next.Keys.Count(id => !old.ContainsKey(id));
            int removed = old.Keys.Count(id => !next.ContainsKey(id));
            int changed = next.Keys.Count(id => old.ContainsKey(id) && JsonSerializer.Serialize(old[id], CompareOptions) != JsonSerializer.Serialize(next[id], CompareOptions));
            int replaced = next.Keys.Where(old.ContainsKey).Sum(id => old[id].objectives.Count(o => !next[id].objectives.Any(n => n.id == o.id)));
            var removedLinks = new List<string>();
            foreach (Quest q in old.Values)
                foreach (string parent in q.requires)
                    if (!next.ContainsKey(q.id) || !next[q.id].requires.Contains(parent))
                        removedLinks.Add(QuestText.Content(q.title) + " ← " + (old.ContainsKey(parent) ? QuestText.Content(old[parent].title) : parent));
            string title = same ? QuestText.Get("GrechkaQuests_baeb677169a2", "Обновить книгу «") + QuestText.Content(after.title) + "»?" : QuestText.Get("GrechkaQuests_d97fc1baa61a", "Заменить книгу «") + QuestText.Content(before?.title ?? "") + QuestText.Get("GrechkaQuests_390490b366fa", "» на «") + QuestText.Content(after.title) + "»?";
            string links = removedLinks.Count == 0 ? QuestText.Get("GrechkaQuests_d4084173d241", "Удалённых связей нет.") : QuestText.Get("GrechkaQuests_3bdb05807148", "Удалённые связи: ") + string.Join("; ", removedLinks.Take(8)) + (removedLinks.Count > 8 ? QuestText.Get("GrechkaQuests_dba0256a8f97", " и ещё ") + (removedLinks.Count - 8) : "") + ".";
            return title + QuestText.Get("GrechkaQuests_5e94e791d455", "\nЗаданий: добавлено ") + added + QuestText.Get("GrechkaQuests_f8ae281176e2", ", изменено ") + changed + QuestText.Get("GrechkaQuests_ed56d9d96b8d", ", удалено ") + removed + QuestText.Get("GrechkaQuests_a67afb9a5623", ". Удалено или заменено целей: ") + replaced + ".\n" + links;
        }
    }
}
