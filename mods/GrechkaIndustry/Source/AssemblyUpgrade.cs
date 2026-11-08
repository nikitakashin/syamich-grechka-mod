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
        public string upgradeRecipeName = "Grechka_UpgradeAssemblyBenchT2";
        public string targetBenchName = "Grechka_AssemblyBenchT2_Building";
        [MustTranslate] public string upgradeLabel = "Улучшить до T2";
        [MustTranslate] public string upgradeDescription = "Пешка улучшит этот верстак на месте. Установленный корпус и подходящие материалы уже зачтены в цене. Задания, дробный бонус выпуска и относительная прочность сохраняются.";
        public CompProperties_AssemblyUpgrade() { compClass = typeof(CompAssemblyUpgrade); }
    }

    // The upgrade is a normal one-shot bill: pawns haul and consume its ingredients.
    // Only after the bill finishes do we replace the machine on the next tick.
    public sealed class CompAssemblyUpgrade : ThingComp
    {
        private CompProperties_AssemblyUpgrade Props { get { return (CompProperties_AssemblyUpgrade)props; } }
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
            if (bench == null || bench.Faction != Faction.OfPlayer || ready || HasUpgradeBill(bench)
                || DefDatabase<RecipeDef>.GetNamedSilentFail(Props.upgradeRecipeName)?.AvailableNow != true) yield break;

            yield return new Command_Action
            {
                defaultLabel = Props.upgradeLabel,
                defaultDesc = Props.upgradeDescription,
                icon = TexCommand.Install,
                action = QueueUpgrade
            };
        }

        private bool HasUpgradeBill(Building_WorkTable bench)
        {
            return bench.BillStack != null && bench.BillStack.Bills.Any(
                bill => IsUpgradeRecipe(bill.recipe));
        }

        private void QueueUpgrade()
        {
            Building_WorkTable bench = Bench;
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(Props.upgradeRecipeName);
            if (bench == null || !bench.Spawned || ready || HasUpgradeBill(bench) || recipe == null || !recipe.AvailableNow) return;

            var bill = new Bill_Production(recipe, null);
            bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
            bill.repeatCount = 1;
            bench.BillStack.AddBill(bill);
        }

        internal bool IsUpgradeRecipe(RecipeDef recipe)
        { return recipe != null && recipe.defName == Props.upgradeRecipeName; }

        internal void MarkReady(RecipeDef recipe) { if (IsUpgradeRecipe(recipe)) ready = true; }

        public override void CompTick()
        {
            base.CompTick();
            if (!ready || !parent.Spawned || Find.TickManager.TicksGame < nextAttemptTick) return;

            try { ReplaceBench(); }
            catch (Exception error)
            {
                nextAttemptTick = Find.TickManager.TicksGame + 250;
                Log.Error(IndustryText.Get("GrechkaIndustry_ac199f7978a8", "[Grechka Industry] Failed to complete bench upgrade: ") + error);
            }
        }

        private void ReplaceBench()
        {
            Building_WorkTable oldBench = Bench;
            ThingDef targetDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props.targetBenchName);
            if (oldBench == null || targetDef == null) throw new InvalidOperationException(IndustryText.Get("GrechkaIndustry_bfce43b7166c", "Target bench not found."));

            Map map = oldBench.Map;
            IntVec3 position = oldBench.Position;
            Rot4 rotation = oldBench.Rotation;
            Faction faction = oldBench.Faction;
            float healthFraction = (float)oldBench.HitPoints / oldBench.MaxHitPoints;
            var retainedBills = oldBench.BillStack.Bills.Where(
                bill => !IsUpgradeRecipe(bill.recipe)).ToList();

            var newBench = (Building_WorkTable)ThingMaker.MakeThing(targetDef);
            if (faction != null) newBench.SetFaction(faction);
            oldBench.DeSpawn(DestroyMode.Vanish);
            try
            {
                GenSpawn.Spawn(newBench, position, map, rotation, WipeMode.Vanish, false, false);
                newBench.HitPoints = Math.Max(1, Mathf.RoundToInt(newBench.MaxHitPoints * healthFraction));
                newBench.GetComp<CompTierBonuses>()?.CopyProgressFrom(oldBench.GetComp<CompTierBonuses>());
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
            Building_WorkTable bench = __instance.billStack?.billGiver as Building_WorkTable;
            bench?.GetComp<CompAssemblyUpgrade>()?.MarkReady(__instance.recipe);
        }
    }
}
