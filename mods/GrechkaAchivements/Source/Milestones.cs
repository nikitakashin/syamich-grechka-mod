using System;
using System.Collections.Generic;
using Grechka.Quests;
using Verse;

namespace Grechka.Achivements
{
    public enum MilestoneSeries { Mechanites, Silicon, Processors, Housings, Blocks, Quests }

    public sealed class ColonyMilestones : GameComponent
    {
        readonly Game game;
        Dictionary<MilestoneSeries, int> totals = new();

        public ColonyMilestones(Game game) { this.game = game; }

        public override void FinalizeInit()
        {
            var quests = game.GetComponent<QuestGame>();
            quests.ColonyProduced -= OnProduced;
            quests.ColonyProduced += OnProduced;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref totals, "grechkaMilestoneTotals", LookMode.Value, LookMode.Value);
            totals ??= new();
        }

        public int Total(MilestoneSeries series)
        {
            if (series == MilestoneSeries.Quests)
                return game.GetComponent<QuestGame>()?.ClaimedQuestCount ?? 0;
            return totals.TryGetValue(series, out int total) ? total : 0;
        }

        void Add(MilestoneSeries series, int amount)
        {
            totals[series] = (int)Math.Min(int.MaxValue, (long)Total(series) + amount);
        }

        public void BlockOpened() => Add(MilestoneSeries.Blocks, 1);

        void OnProduced(ThingDef product, int amount)
        {
            switch (product.defName)
            {
                case "Grechka_Mechanites": Add(MilestoneSeries.Mechanites, amount); break;
                case "Grechka_Silicon": Add(MilestoneSeries.Silicon, amount); break;
                case "Grechka_QuartzProcessor":
                case "Grechka_SilverProcessor":
                case "Grechka_GoldProcessor": Add(MilestoneSeries.Processors, amount); break;
                case "Grechka_HousingT1_Item":
                case "Grechka_HousingT2_Item":
                case "Grechka_HousingT3_Item":
                case "Grechka_HousingT4_Item":
                case "Grechka_HousingT5_Item": Add(MilestoneSeries.Housings, amount); break;
            }
        }
    }

    public sealed class MilestoneTracker : GrechkaTracker
    {
        public MilestoneSeries series;
        public int target;
        public MilestoneTracker() { }
        public MilestoneTracker(MilestoneTracker reference) : base(reference)
        {
            series = reference.series;
            target = reference.target;
        }
        int Total => Current.Game?.GetComponent<ColonyMilestones>()?.Total(series) ?? 0;
        public override Func<bool> AttachToLongTick => Trigger;
        public override bool Trigger() => target > 0 && Total >= target;
        public override (float, string) PercentComplete
        {
            get
            {
                int value = Math.Min(Total, target);
                return target > 0 ? ((float)value / target, $"{value} / {target}") : (0f, "");
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref series, "series");
            Scribe_Values.Look(ref target, "target");
        }
    }
}
