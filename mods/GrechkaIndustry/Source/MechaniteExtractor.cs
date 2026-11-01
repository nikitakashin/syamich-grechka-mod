using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Grechka.Industry
{
    // Optional consumers can subscribe without making Industry depend on Quests.
    public static class IndustryProduction
    {
        public static event Action<Thing, ThingDef, int> OutputProduced;

        internal static void Report(Thing machine, ThingDef product, int amount)
        {
            if (amount <= 0 || OutputProduced == null) return;
            foreach (Action<Thing, ThingDef, int> subscriber in OutputProduced.GetInvocationList())
            {
                try { subscriber(machine, product, amount); }
                catch (Exception error) { Log.Error("[Grechka Industry] Ошибка подписчика выпуска: " + error); }
            }
        }
    }

    public sealed class CompProperties_MechaniteExtractor : CompProperties
    {
        public ThingDef product;
        public int cycleTicks = 2500;
        public int batchCount = 5;
        public CompProperties_MechaniteExtractor() { compClass = typeof(CompMechaniteExtractor); }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (product == null) yield return "Extractor product is missing.";
            if (cycleTicks <= 0 || batchCount <= 0 || product != null && batchCount > product.stackLimit)
                yield return "Extractor cycle/count must be positive and the batch must fit one stack.";
        }
    }

    public sealed class CompMechaniteExtractor : ThingComp
    {
        private readonly ProductionCycle cycle = new ProductionCycle();
        private CompPowerTrader power;
        private CompFlickable flick;
        private CompBreakdownable breakdown;
        private int nextOutputAttempt;
        private CompProperties_MechaniteExtractor Props { get { return (CompProperties_MechaniteExtractor)props; } }
        private bool Powered { get { return power != null && power.PowerOn
            && (flick == null || flick.SwitchIsOn) && (breakdown == null || !breakdown.BrokenDown); } }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            power = parent.GetComp<CompPowerTrader>();
            flick = parent.GetComp<CompFlickable>();
            breakdown = parent.GetComp<CompBreakdownable>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref cycle.WorkedTicks, "grechkaExtractionTicks", 0);
            Scribe_Values.Look(ref cycle.PendingCount, "grechkaExtractionPending", 0);
        }

        public override void CompTick()
        {
            if (!parent.Spawned || !Powered) return;
            cycle.Advance(true, 1, Props.cycleTicks, Props.batchCount);
            if (cycle.PendingCount > 0)
            {
                if (Find.TickManager.TicksGame < nextOutputAttempt) return;
                nextOutputAttempt = Find.TickManager.TicksGame + 250;
                TryDeliver();
            }
            else if (parent.IsHashIntervalTick(120))
            {
                FleckMaker.Static(parent.DrawPos, parent.Map, FleckDefOf.PsycastSkipOuterRingExit, 0.7f);
            }
        }

        private void TryDeliver()
        {
            int requested = cycle.PendingCount;
            if (!CompSpawner.TryFindSpawnCell(parent, Props.product, requested, out IntVec3 cell)) return;
            Thing output = ThingMaker.MakeThing(Props.product);
            output.stackCount = requested;
            int placed = 0;
            GenPlace.TryPlaceThing(output, cell, parent.Map, ThingPlaceMode.Direct,
                (thing, count) => placed += count);
            // Placement can merge stacks. Commit the actual transferred amount,
            // then discard any unplaced temporary item; its count remains pending.
            int delivered = Math.Min(requested, Math.Max(0, placed));
            cycle.RecordDelivery(delivered);
            if (!output.Spawned && !output.Destroyed) output.Destroy(DestroyMode.Vanish);
            IndustryProduction.Report(parent, Props.product, delivered);
        }

        public override string CompInspectStringExtra()
        {
            if (cycle.PendingCount > 0) return "Готово к выгрузке: " + cycle.PendingCount + " механитов";
            return (Powered ? "Извлечение: " : "Извлечение приостановлено: ")
                + ((float)cycle.WorkedTicks / Props.cycleTicks).ToString("P0");
        }
    }
}
