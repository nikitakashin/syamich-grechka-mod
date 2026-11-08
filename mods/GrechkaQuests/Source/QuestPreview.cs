using System;
using System.Collections.Generic;
using System.Linq;

namespace Grechka.Quests
{
    // A disposable copy: no game component, reward delivery or colony state is referenced.
    public sealed class QuestPreview
    {
        public QuestPack Pack { get; private set; }
        QuestProgress progress;
        public QuestPreview(QuestPack draft)
        {
            Pack = PackCodec.Decode(PackCodec.Encode(draft));
            Reset();
        }
        public void Reset() { progress = new QuestProgress(null, null); }
        public bool Achieved(Quest quest, Objective objective) { return progress.Achieved(Pack, quest, objective); }
        public bool Claimed(Quest quest) { return progress.Claimed(Pack, quest); }
        public bool Unlocked(Quest quest) { return progress.Unlocked(Pack, quest); }
        public bool Ready(Quest quest) { return progress.Ready(Pack, quest, true); }
        public void Mark(Quest quest, Objective objective)
        {
            if (!Pack.quests.Contains(quest) || !quest.objectives.Contains(objective)) throw new InvalidOperationException(QuestText.Get("GrechkaQuests_997e6e2ed87d", "Цель не найдена в предпросмотре."));
            progress.Observe(Pack, quest, objective, objective.count, true);
        }
        public string Claim(Quest quest, string choice)
        {
            if (!Pack.quests.Contains(quest)) return QuestText.Get("GrechkaQuests_3914dc442fbd", "Задание не найдено в предпросмотре.");
            var plan = new QuestClaimPlan(PackCodec.Encode(Pack), new[] { quest.id });
            var choices = new Dictionary<string, string>();
            if (choice != null) choices.Add(quest.id, choice);
            string error = progress.ValidatePlan(Pack, plan, choices, true, null);
            if (error != null) return error;
            progress.RecordClaim(Pack, quest, choice);
            return null;
        }
        public QuestClaimPlan PrepareClaimAll()
        {
            return new QuestClaimPlan(PackCodec.Encode(Pack), progress.PlanClaimAll(Pack, true, null));
        }
        public string FinishClaimAll(QuestClaimPlan plan, IDictionary<string, string> choices)
        {
            string error = progress.ValidatePlan(Pack, plan, choices, true, null);
            if (error != null) return error;
            foreach (string id in plan.questIds)
            {
                Quest quest = Pack.quests.First(item => item.id == id);
                string choice = null;
                if (choices != null) choices.TryGetValue(id, out choice);
                progress.RecordClaim(Pack, quest, choice);
            }
            return null;
        }
    }
}
