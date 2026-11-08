using System;
using System.Collections.Generic;
using System.Linq;

namespace Grechka.Quests
{
    // Pure draft operations. They never read or modify a colony's quest progress.
    public static class QuestEditor
    {
        public static bool TryAddDependency(QuestPack pack, Quest child, Quest predecessor, out string error)
        {
            error = null;
            if (pack == null || child == null || predecessor == null || !pack.quests.Contains(child) || !pack.quests.Contains(predecessor))
                error = QuestText.Get("GrechkaQuests_03e3e9f6924e", "Оба задания должны принадлежать книге.");
            else if (child.id == predecessor.id) error = QuestText.Get("GrechkaQuests_cf5cdfeb5ffd", "Нельзя связать задание с самим собой.");
            else if (child.requires.Contains(predecessor.id)) error = QuestText.Get("GrechkaQuests_02b8fd9551ec", "Эта связь уже существует.");
            else if (child.requires.Count >= 100) error = QuestText.Get("GrechkaQuests_f39eca6321e6", "Максимум 100 предшественников.");
            else
            {
                var byId = pack.quests.GroupBy(q => q.id).ToDictionary(g => g.Key, g => g.First());
                var todo = new Stack<string>(); var visited = new HashSet<string>();
                todo.Push(predecessor.id);
                while (todo.Count > 0)
                {
                    string id = todo.Pop();
                    if (id == child.id) { error = QuestText.Get("GrechkaQuests_d72190be5794", "Эта связь создаст цикл."); break; }
                    Quest item;
                    if (visited.Add(id) && byId.TryGetValue(id, out item))
                        foreach (string required in item.requires) todo.Push(required);
                }
            }
            if (error != null) return false;
            child.requires.Add(predecessor.id);
            return true;
        }

        public static bool ChangeObjective(Objective goal, string type, string target)
        {
            string before = QuestProgress.Signature(goal);
            if (goal.type != type) { goal.variants.Clear(); goal.stuff = null; goal.minQuality = null; }
            goal.type = type;
            goal.target = type == "manual" ? "" : target;
            if (goal.variants.Contains(goal.target)) goal.variants.Remove(goal.target);
            if (before == QuestProgress.Signature(goal)) return false;
            goal.id = PackCodec.NewId("goal.");
            return true;
        }

        public static bool EditObjective(Objective goal, Action<Objective> edit)
        {
            string before = QuestProgress.Signature(goal);
            edit(goal);
            if (before == QuestProgress.Signature(goal)) return false;
            goal.id = PackCodec.NewId("goal.");
            return true;
        }

        // External dependencies are deliberately not copied. Internal edges get new IDs.
        public static List<Quest> CopySelected(QuestPack pack, IEnumerable<string> questIds, string destinationChapter = null)
        {
            var ids = new HashSet<string>(questIds ?? Enumerable.Empty<string>());
            var original = pack.quests.Where(q => ids.Contains(q.id)).ToList();
            if (original.Count == 0 || original.Count != ids.Count) throw new InvalidOperationException(QuestText.Get("GrechkaQuests_f6b2f475b0c7", "Выберите существующие задания книги."));
            if (pack.quests.Count + original.Count > 500) throw new InvalidOperationException(QuestText.Get("GrechkaQuests_8210d9d1210a", "Лимит: 500 заданий."));
            if (destinationChapter != null && !pack.chapters.Any(c => c.id == destinationChapter)) throw new InvalidOperationException(QuestText.Get("GrechkaQuests_78aa5d16dd7e", "Глава не найдена."));
            var newIds = original.ToDictionary(q => q.id, q => PackCodec.NewId("quest."));
            var copies = new List<Quest>();
            foreach (Quest source in original)
            {
                var copy = new Quest {
                    id = newIds[source.id], chapter = destinationChapter ?? source.chapter,
                    title = source.title + QuestText.Get("GrechkaQuests_cf8ecce177df", " (копия)"), description = source.description, icon = source.icon,
                    graphX = Math.Min(100000f, source.graphX + 35f), graphY = Math.Min(100000f, source.graphY + 35f), graphPositioned = source.graphPositioned,
                    requires = source.requires.Where(newIds.ContainsKey).Select(id => newIds[id]).ToList(),
                    objectives = source.objectives.Select(o => new Objective { id = PackCodec.NewId("goal."), type = o.type, target = o.target, variants = new List<string>(o.variants), stuff = o.stuff, minQuality = o.minQuality, count = o.count, text = o.text }).ToList(),
                    rewards = source.rewards.Select(CopyReward).ToList(),
                    alternatives = source.alternatives.Select(a => new RewardOption {
                        id = PackCodec.NewId("choice."), title = a.title, items = a.items.Select(CopyReward).ToList()
                    }).ToList()
                };
                if (copy.title.Length > 200) copy.title = copy.title.Substring(0, 200);
                copies.Add(copy);
            }
            pack.quests.AddRange(copies);
            return copies;
        }

        static Reward CopyReward(Reward reward)
        {
            return new Reward { def = reward.def, count = reward.count, stuff = reward.stuff, quality = reward.quality };
        }

        public static Chapter CopyChapter(QuestPack pack, Chapter source)
        {
            if (source == null || !pack.chapters.Contains(source)) throw new InvalidOperationException(QuestText.Get("GrechkaQuests_78aa5d16dd7e", "Глава не найдена."));
            if (pack.chapters.Count >= 100) throw new InvalidOperationException(QuestText.Get("GrechkaQuests_e719c846a94c", "Лимит: 100 глав."));
            var ids = pack.quests.Where(q => q.chapter == source.id).Select(q => q.id).ToList();
            if (pack.quests.Count + ids.Count > 500) throw new InvalidOperationException(QuestText.Get("GrechkaQuests_8210d9d1210a", "Лимит: 500 заданий."));
            var chapter = new Chapter { id = PackCodec.NewId("chapter."), title = source.title + QuestText.Get("GrechkaQuests_cf8ecce177df", " (копия)"), description = source.description, icon = source.icon };
            if (chapter.title.Length > 200) chapter.title = chapter.title.Substring(0, 200);
            pack.chapters.Add(chapter);
            try { if (ids.Count > 0) CopySelected(pack, ids, chapter.id); }
            catch { pack.chapters.Remove(chapter); throw; }
            return chapter;
        }

        public static void RemoveQuest(QuestPack pack, Quest quest)
        {
            if (!pack.quests.Remove(quest)) throw new InvalidOperationException(QuestText.Get("GrechkaQuests_72fafafe4304", "Задание не найдено."));
            foreach (Quest other in pack.quests) other.requires.RemoveAll(id => id == quest.id);
        }
    }
}
