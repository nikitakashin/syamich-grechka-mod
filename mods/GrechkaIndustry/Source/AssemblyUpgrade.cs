using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Industry
{
    public sealed class GrechkaIndustryMod : Mod
    {
        public GrechkaIndustryMod(ModContentPack content) : base(content)
        {
            new Harmony("grechka.industry.assembly-upgrade").PatchAll(typeof(GrechkaIndustryMod).Assembly);
        }
    }

    public sealed class CompProperties_AssemblyUpgrade : CompProperties
    {
        public CompProperties_AssemblyUpgrade() { compClass = typeof(CompAssemblyUpgrade); }
    }

    // The upgrade is a normal one-shot bill: pawns haul and consume its ingredients.
    // Only after the bill finishes do we replace the machine on the next tick.
    public sealed class CompAssemblyUpgrade : ThingComp
    {
        private const string UpgradeRecipeName = "Grechka_UpgradeAssemblyBenchT2";
        private const string TargetBenchName = "Grechka_AssemblyBenchT2";
        private bool ready;
        private int nextAttemptTick;

        private Building_WorkTable Bench { get { return parent as Building_WorkTable; } }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ready, "grechkaUpgradeReady", false);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Building_WorkTable bench = Bench;
            if (bench == null || ready || HasUpgradeBill(bench)) yield break;

            yield return new Command_Action
            {
                defaultLabel = "Улучшить до T2",
                defaultDesc = "Пешка улучшит этот верстак на месте. Требуется: 120 стали, 20 пластали, 4 компонента и 6 продвинутых компонентов. Вложенный корпус T1 засчитывается. Производственные задания и прочность сохраняются. Новому верстаку потребуется 350 Вт.",
                icon = TexCommand.Install,
                action = QueueUpgrade
            };
        }

        private static bool HasUpgradeBill(Building_WorkTable bench)
        {
            return bench.BillStack != null && bench.BillStack.Bills.Any(
                bill => bill.recipe != null && bill.recipe.defName == UpgradeRecipeName);
        }

        private void QueueUpgrade()
        {
            Building_WorkTable bench = Bench;
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(UpgradeRecipeName);
            if (bench == null || !bench.Spawned || ready || HasUpgradeBill(bench) || recipe == null) return;

            var bill = new Bill_Production(recipe, null);
            bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
            bill.repeatCount = 1;
            bench.BillStack.AddBill(bill);
        }

        internal void MarkReady() { ready = true; }

        public override void CompTick()
        {
            base.CompTick();
            if (!ready || !parent.Spawned || Find.TickManager.TicksGame < nextAttemptTick) return;

            try { ReplaceBench(); }
            catch (Exception error)
            {
                nextAttemptTick = Find.TickManager.TicksGame + 250;
                Log.Error("[Grechka Industry] Не удалось завершить улучшение верстака: " + error);
            }
        }

        private void ReplaceBench()
        {
            Building_WorkTable oldBench = Bench;
            ThingDef targetDef = DefDatabase<ThingDef>.GetNamedSilentFail(TargetBenchName);
            if (oldBench == null || targetDef == null) throw new InvalidOperationException("Не найден верстак T2.");

            Map map = oldBench.Map;
            IntVec3 position = oldBench.Position;
            Rot4 rotation = oldBench.Rotation;
            Faction faction = oldBench.Faction;
            float healthFraction = (float)oldBench.HitPoints / oldBench.MaxHitPoints;
            var retainedBills = oldBench.BillStack.Bills.Where(
                bill => bill.recipe == null || bill.recipe.defName != UpgradeRecipeName).ToList();

            var newBench = (Building_WorkTable)ThingMaker.MakeThing(targetDef);
            if (faction != null) newBench.SetFaction(faction);
            oldBench.DeSpawn(DestroyMode.Vanish);
            try
            {
                GenSpawn.Spawn(newBench, position, map, rotation, WipeMode.Vanish, false, false);
                newBench.HitPoints = Math.Max(1, Mathf.RoundToInt(newBench.MaxHitPoints * healthFraction));
                foreach (Bill bill in retainedBills)
                {
                    oldBench.BillStack.Bills.Remove(bill);
                    bill.billStack = null;
                    newBench.BillStack.AddBill(bill);
                }
                oldBench.Destroy(DestroyMode.Vanish);
            }
            catch
            {
                // A failed spawn or bill transfer must leave the original bench usable.
                // Bills may already have moved, so put every surviving one back first.
                foreach (Bill bill in retainedBills)
                {
                    if (newBench.BillStack != null && bill.billStack == newBench.BillStack)
                    {
                        newBench.BillStack.Bills.Remove(bill);
                        bill.billStack = null;
                        oldBench.BillStack.AddBill(bill);
                    }
                    else if (bill.billStack == null)
                    {
                        oldBench.BillStack.AddBill(bill);
                    }
                }
                if (newBench.Spawned) newBench.DeSpawn(DestroyMode.Vanish);
                if (!oldBench.Spawned) GenSpawn.Spawn(oldBench, position, map, rotation, WipeMode.Vanish, false, false);
                throw;
            }
        }
    }

    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.Notify_IterationCompleted))]
    internal static class AssemblyUpgradeBillHook
    {
        private static void Postfix(Bill_Production __instance)
        {
            if (__instance.recipe == null || __instance.recipe.defName != "Grechka_UpgradeAssemblyBenchT2") return;
            Building_WorkTable bench = __instance.billStack?.billGiver as Building_WorkTable;
            bench?.GetComp<CompAssemblyUpgrade>()?.MarkReady();
        }
    }
}
