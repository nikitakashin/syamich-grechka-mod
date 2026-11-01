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
                if (PackCodec.Decode(pair.Value).id != pair.Key) throw new FormatException("ID сохранённой книги не совпадает с её содержимым: " + pair.Key);
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
                        conflicts.Add(quest.title + " / " + goal.id);
                }
            return conflicts;
        }

        public void Remember(QuestPack candidate)
        {
            var conflicts = Conflicts(candidate);
            if (conflicts.Count != 0) throw new InvalidOperationException("Смысл цели изменён с прежним ID: " + string.Join(", ", conflicts.Take(5)) + ". Назначьте ей новый ID в редакторе.");
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
                        removedLinks.Add(q.title + " ← " + (old.ContainsKey(parent) ? old[parent].title : parent));
            string title = same ? "Обновить книгу «" + after.title + "»?" : "Заменить книгу «" + (before?.title ?? "") + "» на «" + after.title + "»?";
            string links = removedLinks.Count == 0 ? "Удалённых связей нет." : "Удалённые связи: " + string.Join("; ", removedLinks.Take(8)) + (removedLinks.Count > 8 ? " и ещё " + (removedLinks.Count - 8) : "") + ".";
            return title + "\nЗаданий: добавлено " + added + ", изменено " + changed + ", удалено " + removed + ". Удалено или заменено целей: " + replaced + ".\n" + links;
        }
    }
}
