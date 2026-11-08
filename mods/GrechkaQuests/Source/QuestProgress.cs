using System;
using System.Collections.Generic;
using System.Linq;

namespace Grechka.Quests
{
    // Pure bookkeeping: the game integration supplies observations and performs delivery.
    // Objective keys include the package and quest, so two imported books cannot share progress.
    public sealed class QuestProgress
    {
        readonly Dictionary<string, string> achieved;
        readonly HashSet<string> claimed;
        readonly Dictionary<string, string> legacyCompletions;
        readonly Dictionary<string, string> rewardChoices;
        readonly Dictionary<string, int> eventCounts;
        readonly Dictionary<string, string> eventSignatures;

        public QuestProgress(IDictionary<string, string> achievedObjectives, IEnumerable<string> claimedQuests, IDictionary<string, string> legacyCompleted = null, IDictionary<string, string> rewardChoices = null, IDictionary<string, int> eventCounts = null, IDictionary<string, string> eventSignatures = null)
        {
            achieved = achievedObjectives == null ? new Dictionary<string, string>() : new Dictionary<string, string>(achievedObjectives);
            claimed = claimedQuests == null ? new HashSet<string>() : new HashSet<string>(claimedQuests);
            legacyCompletions = legacyCompleted == null ? new Dictionary<string, string>() : new Dictionary<string, string>(legacyCompleted);
            this.rewardChoices = rewardChoices == null ? new Dictionary<string, string>() : new Dictionary<string, string>(rewardChoices);
            this.eventCounts = eventCounts == null ? new Dictionary<string, int>() : new Dictionary<string, int>(eventCounts);
            this.eventSignatures = eventSignatures == null ? new Dictionary<string, string>() : new Dictionary<string, string>(eventSignatures);
            if (this.eventCounts.Count != this.eventSignatures.Count || this.eventCounts.Any(pair => pair.Value < 0 || !this.eventSignatures.ContainsKey(pair.Key)))
                throw new FormatException(QuestText.Get("GrechkaQuests_2eb9d5fabd66", "Неполные данные событийных целей."));
        }

        public static void ValidateProgressVersion(int version)
        {
            if (version < 0 || version > 1) throw new FormatException(QuestText.Get("GrechkaQuests_4f6c86ea6531", "Неподдерживаемая версия прогресса: ") + version);
        }

        // Decode paired values conservatively: malformed data is an error, not an empty history.
        public static Dictionary<string, string> ReadPairs(IList<string> keys, IList<string> values)
        {
            if (keys == null && values == null) return new Dictionary<string, string>();
            if (keys == null || values == null || keys.Count != values.Count)
                throw new FormatException(QuestText.Get("GrechkaQuests_11502022f31b", "Неполные данные прогресса."));
            var result = new Dictionary<string, string>();
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.IsNullOrEmpty(keys[i]) || values[i] == null || result.ContainsKey(keys[i]))
                    throw new FormatException(QuestText.Get("GrechkaQuests_ca2ddf7750c1", "Некорректные или повторяющиеся ключи прогресса."));
                result.Add(keys[i], values[i]);
            }
            return result;
        }
        public static Dictionary<string, int> ReadCounts(IList<string> keys, IList<int> values)
        {
            if (keys == null && values == null) return new Dictionary<string, int>();
            if (keys == null || values == null || keys.Count != values.Count) throw new FormatException(QuestText.Get("GrechkaQuests_d1d5dc8dd95d", "Неполные счётчики событий."));
            var result = new Dictionary<string, int>();
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.IsNullOrEmpty(keys[i]) || values[i] < 0 || result.ContainsKey(keys[i])) throw new FormatException(QuestText.Get("GrechkaQuests_43806287b2fa", "Некорректные счётчики событий."));
                result.Add(keys[i], values[i]);
            }
            return result;
        }

        // The required quantity is deliberately excluded: changing it preserves an attained goal.
        // When the target or type changes, an old milestone is not silently reused.
        public static string Signature(Objective goal)
        {
            if (goal.type == "manual") return "manual";
            var variants = goal.variants ?? new List<string>();
            string target = variants.Count == 0 ? goal.target
                : "set[" + string.Join(",", variants.Concat(new[] { goal.target }).OrderBy(x => x, StringComparer.Ordinal)) + "]";
            return goal.type + ":" + target
                + (string.IsNullOrEmpty(goal.stuff) ? "" : "|stuff:" + goal.stuff)
                + (string.IsNullOrEmpty(goal.minQuality) ? "" : "|quality:" + goal.minQuality);
        }

        public bool Achieved(QuestPack pack, Quest quest, Objective goal)
        {
            string signature;
            return achieved.TryGetValue(PackCodec.ObjectiveKey(pack.id, quest.id, goal.id), out signature)
                && signature == Signature(goal);
        }

        public void Observe(QuestPack pack, Quest quest, Objective goal, int amount, bool hasColony)
        {
            if (!hasColony || amount < goal.count) return;
            achieved[PackCodec.ObjectiveKey(pack.id, quest.id, goal.id)] = Signature(goal);
        }

        public int EventValue(QuestPack pack, Quest quest, Objective goal)
        {
            string key = PackCodec.ObjectiveKey(pack.id, quest.id, goal.id), signature; int count;
            return eventSignatures.TryGetValue(key, out signature) && signature == Signature(goal) && eventCounts.TryGetValue(key, out count) ? count : 0;
        }

        // Only verified completed output or a player-caused death may call this. Moving, buying or
        // receiving an existing Thing must never be reported as a production event.
        public void RecordEvent(QuestPack pack, string type, string def, string stuff, string quality, int amount, bool hasColony,
            Func<string, string> canonicalize = null)
        {
            if (!hasColony || amount <= 0 || (type != "produce" && type != "mine" && type != "harvest" && type != "kill")) return;
            foreach (Quest quest in pack.quests)
                foreach (Objective goal in quest.objectives)
                {
                    if (goal.type != type || !ObjectiveTally.Matches(goal, def, stuff, quality, canonicalize) || Achieved(pack, quest, goal)) continue;
                    string key = PackCodec.ObjectiveKey(pack.id, quest.id, goal.id), signature = Signature(goal), previous;
                    if (eventSignatures.TryGetValue(key, out previous) && previous != signature) continue;
                    int old; eventCounts.TryGetValue(key, out old);
                    int total = (int)Math.Min(int.MaxValue, (long)old + amount);
                    eventCounts[key] = total; eventSignatures[key] = signature;
                    Observe(pack, quest, goal, total, true);
                }
        }

        public bool Claimed(QuestPack pack, Quest quest)
        {
            return claimed.Contains(PackCodec.Key(pack.id, quest.id));
        }

        public bool Unlocked(QuestPack pack, Quest quest)
        {
            return quest.requires.All(id => claimed.Contains(PackCodec.Key(pack.id, id)));
        }

        // Legacy completed quests have a quest-level fact only: preserve it without
        // inventing per-objective timestamps or values. Semantic edits invalidate it.
        public static string QuestSignature(Quest quest)
        {
            return string.Join("|", quest.objectives.OrderBy(o => o.id).Select(o => o.id + ":" + Signature(o)));
        }

        public void RememberLegacyCompletion(QuestPack pack, Quest quest)
        {
            legacyCompletions[PackCodec.Key(pack.id, quest.id)] = QuestSignature(quest);
        }

        public bool ObjectivesComplete(QuestPack pack, Quest quest)
        {
            string signature;
            return (legacyCompletions.TryGetValue(PackCodec.Key(pack.id, quest.id), out signature)
                    && signature == QuestSignature(quest))
                || quest.objectives.All(goal => Achieved(pack, quest, goal));
        }

        public Dictionary<string, string> CopyLegacyCompletions() { return new Dictionary<string, string>(legacyCompletions); }

        public bool Ready(QuestPack pack, Quest quest, bool hasColony)
        {
            return hasColony && !Claimed(pack, quest) && Unlocked(pack, quest)
                && ObjectivesComplete(pack, quest);
        }

        // Compute the closure from the milestones captured when the button was pressed.
        // This plans only: recording a claim must follow successful reward delivery.
        public List<string> PlanClaimAll(QuestPack pack, bool hasColony, Func<Quest, bool> compatible)
        {
            var result = new List<string>();
            if (!hasColony) return result;
            var available = new HashSet<string>(claimed);
            var pending = pack.quests.Where(q => !Claimed(pack, q)
                && ObjectivesComplete(pack, q)
                && (compatible == null || compatible(q))).ToList();
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < pending.Count;)
                {
                    Quest q = pending[i];
                    if (!q.requires.All(id => available.Contains(PackCodec.Key(pack.id, id)))) { i++; continue; }
                    result.Add(q.id);
                    available.Add(PackCodec.Key(pack.id, q.id));
                    pending.RemoveAt(i);
                    changed = true;
                }
            } while (changed);
            return result;
        }

        // Recheck the captured plan before creating anything. Never expand it with newly met goals.
        public string ValidatePlan(QuestPack pack, QuestClaimPlan plan, IDictionary<string, string> choices, bool hasColony, Func<Quest, bool> compatible)
        {
            if (!hasColony) return QuestText.Get("GrechkaQuests_731c3711ff2a", "Создайте колонию");
            if (plan == null || plan.bookSnapshot != PackCodec.Encode(pack)) return QuestText.Get("GrechkaQuests_4acd53a591cd", "Книга изменилась. Откройте выбор наград заново.");
            if (plan.questIds.Count == 0 || plan.questIds.Count != plan.questIds.Distinct().Count()) return QuestText.Get("GrechkaQuests_83247b030b1b", "Нет доступных наград.");
            var available = new HashSet<string>(claimed);
            var included = new HashSet<string>(plan.questIds);
            if (choices != null && choices.Keys.Any(id => !included.Contains(id))) return QuestText.Get("GrechkaQuests_3b7f30889c7e", "Выбор относится к другому заданию.");
            foreach (string id in plan.questIds)
            {
                Quest q = pack.quests.FirstOrDefault(item => item.id == id);
                if (q == null || Claimed(pack, q) || !ObjectivesComplete(pack, q) || (compatible != null && !compatible(q))
                    || !q.requires.All(parent => available.Contains(PackCodec.Key(pack.id, parent))))
                    return QuestText.Get("GrechkaQuests_2d8c5db4d4cf", "Состояние заданий изменилось. Откройте получение наград заново.");
                bool hasChoice = choices != null && choices.ContainsKey(q.id);
                if (q.alternatives.Count > 0)
                {
                    if (!hasChoice || !q.alternatives.Any(option => option.id == choices[q.id])) return QuestText.Get("GrechkaQuests_11c4a721f130", "Выберите набор наград для «") + q.title + "».";
                }
                else if (hasChoice) return QuestText.Get("GrechkaQuests_912cf6bfac83", "У задания «") + q.title + QuestText.Get("GrechkaQuests_5991199c8022", "» нет выбора наград.");
                available.Add(PackCodec.Key(pack.id, q.id));
            }
            return null;
        }

        public void RecordClaim(QuestPack pack, Quest quest, string rewardOption = null)
        {
            string key = PackCodec.Key(pack.id, quest.id);
            if (!claimed.Add(key)) return;
            if (rewardOption != null) rewardChoices[key] = rewardOption;
        }

        public Dictionary<string, string> CopyAchieved() { return new Dictionary<string, string>(achieved); }
        public Dictionary<string, int> CopyEventCounts() { return new Dictionary<string, int>(eventCounts); }
        public Dictionary<string, string> CopyEventSignatures() { return new Dictionary<string, string>(eventSignatures); }
        public Dictionary<string, string> CopyKnownDefinitions()
        {
            var result = new Dictionary<string, string>(eventSignatures);
            foreach (var pair in achieved) result[pair.Key] = pair.Value;
            return result;
        }
        public int ClaimedCount => claimed.Count;
        public List<string> CopyClaimed() { return claimed.ToList(); }
        public Dictionary<string, string> CopyRewardChoices() { return new Dictionary<string, string>(rewardChoices); }
        public string ChosenReward(QuestPack pack, Quest quest)
        {
            string result;
            return rewardChoices.TryGetValue(PackCodec.Key(pack.id, quest.id), out result) ? result : null;
        }
    }

    public sealed class QuestClaimPlan
    {
        public readonly string bookSnapshot;
        public readonly List<string> questIds;
        public QuestClaimPlan(string snapshot, IEnumerable<string> ids)
        {
            bookSnapshot = snapshot; questIds = new List<string>(ids);
        }
    }
}
