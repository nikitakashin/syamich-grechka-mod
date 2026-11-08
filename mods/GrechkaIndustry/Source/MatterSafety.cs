using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grechka.Industry
{
    public static class MatterSafety
    {
        public static string PauseReason => IndustryText.Get("GrechkaIndustry_RecycleUnverified",
            "Recycling is paused: this item's production value cannot yet be verified safely. Reserved materials are preserved; cancel the bill or unfinished item to recover them.");
        public static bool Blocked(RecipeDef recipe)
        {
            if (recipe?.GetModExtension<MatterRecipeProperties>()?.recycle != true) return false;
            if (recipe.ingredients == null || recipe.ingredients.Count != 1) return true;
            bool any = false;
            foreach (ThingDef def in recipe.ingredients[0].filter.AllowedThingDefs)
            {
                any = true;
                if (MatterCraftValue.UnitValue(def, double.MaxValue) <= 0) return true;
            }
            return !any;
        }
    }
    [HarmonyPatch(typeof(RecipeDef), "get_AvailableNow")]
    internal static class MatterAvailabilityHook
    {
        private static void Postfix(RecipeDef __instance, ref bool __result)
        { if (MatterSafety.Blocked(__instance)) __result = false; }
    }
    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow))]
    internal static class MatterBillPauseHook
    {
        private static bool Prefix(Bill_Production __instance, ref bool __result)
        {
            if (!MatterSafety.Blocked(__instance.recipe)) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch]
    internal static class MatterActiveJobPauseHook
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(JobDriver), nameof(JobDriver.DriverTick));
            yield return AccessTools.Method(typeof(JobDriver), nameof(JobDriver.DriverTickInterval));
        }
        private static bool Prefix(JobDriver __instance)
        {
            if (!(__instance is JobDriver_DoBill) || !MatterSafety.Blocked(__instance.job?.bill?.recipe)) return true;
            // Native interruption leaves unfinished items and their ingredients
            // intact. No work/finish toil can run on this tick, including saves
            // restored immediately before the old recipe's completion.
            __instance.EndJobWith(JobCondition.InterruptForced);
            return false;
        }
    }
}
