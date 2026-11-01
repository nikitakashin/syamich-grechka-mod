using System;
using System.Collections.Generic;
using System.Linq;

namespace Grechka.Quests
{
    // Pure aggregation: the game adapter decides which locations and owners are eligible.
    public sealed class ObjectiveTally
    {
        sealed class Entry
        {
            public string key;
            public Objective goal;
        }
        readonly Dictionary<string, List<Entry>> targets = new Dictionary<string, List<Entry>>();
        readonly Dictionary<string, int> values = new Dictionary<string, int>();
        readonly Dictionary<string, Dictionary<string, int>> locations = new Dictionary<string, Dictionary<string, int>>();
        readonly HashSet<object> seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        static readonly string[] Qualities = { "Awful", "Poor", "Normal", "Good", "Excellent", "Masterwork", "Legendary" };

        public ObjectiveTally(QuestPack pack)
        {
            foreach (Quest quest in pack.quests)
                foreach (Objective goal in quest.objectives.Where(o => o.type == "have" || o.type == "build"))
                {
                    var entry = new Entry { key = PackCodec.ObjectiveKey(pack.id, quest.id, goal.id), goal = goal };
                    foreach (string def in new[] { goal.target }.Concat(goal.variants ?? new List<string>()).Distinct())
                    {
                        string index = goal.type + ":" + def;
                        List<Entry> list;
                        if (!targets.TryGetValue(index, out list)) targets[index] = list = new List<Entry>();
                        list.Add(entry);
                    }
                }
        }

        public void Add(object identity, string type, string def, string stuff, string quality, int count, string location = null)
        {
            List<Entry> entries;
            if (identity == null || count <= 0 || !targets.TryGetValue(type + ":" + def, out entries) || !seen.Add(identity)) return;
            foreach (Entry entry in entries)
            {
                Objective goal = entry.goal;
                if (!Matches(goal, def, stuff, quality)) continue;
                int old; values.TryGetValue(entry.key, out old);
                values[entry.key] = (int)Math.Min(int.MaxValue, (long)old + count);
                if (!string.IsNullOrEmpty(location))
                {
                    Dictionary<string, int> breakdown;
                    if (!locations.TryGetValue(entry.key, out breakdown)) locations[entry.key] = breakdown = new Dictionary<string, int>();
                    breakdown.TryGetValue(location, out old);
                    breakdown[location] = (int)Math.Min(int.MaxValue, (long)old + count);
                }
            }
        }

        public static bool Matches(Objective goal, string def, string stuff, string quality)
        {
            if (goal.target != def && !(goal.variants?.Contains(def) ?? false)) return false;
            if (!string.IsNullOrEmpty(goal.stuff) && goal.stuff != stuff) return false;
            if (!string.IsNullOrEmpty(goal.minQuality))
            {
                int wanted = Array.IndexOf(Qualities, goal.minQuality), actual = Array.IndexOf(Qualities, quality);
                if (wanted < 0 || actual < wanted) return false;
            }
            return true;
        }

        public int Value(QuestPack pack, Quest quest, Objective goal)
        {
            int value;
            return values.TryGetValue(PackCodec.ObjectiveKey(pack.id, quest.id, goal.id), out value) ? value : 0;
        }
        public Dictionary<string, int> Breakdown(QuestPack pack, Quest quest, Objective goal)
        {
            Dictionary<string, int> result;
            return locations.TryGetValue(PackCodec.ObjectiveKey(pack.id, quest.id, goal.id), out result)
                ? new Dictionary<string, int>(result) : new Dictionary<string, int>();
        }
    }
}
