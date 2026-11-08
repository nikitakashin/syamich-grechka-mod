using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Grechka.WorldIntegration
{
    public sealed class FactoryProcessBalanceProperties : DefModExtension
    {
        public bool refreshFixedInputs;
        public int previousTicks;
    }

    internal static class FactoryProcessBalance
    {
        private static object Get(object item, string name) => item == null ? null
            : AccessTools.Property(item.GetType(), name)?.GetValue(item) ?? AccessTools.Field(item.GetType(), name)?.GetValue(item);

        internal static void RefreshIdleInputs(object process, IList inputs, FactoryBatchState state)
        {
            if (Get(process, "def") is not Def def || def.GetModExtension<FactoryProcessBalanceProperties>() is not FactoryProcessBalanceProperties policy
                || !policy.refreshFixedInputs
                || Get(def, "ingredients") is not IList ingredients || ingredients.Count != inputs.Count
                || Get(process, "progress") is float progress && progress > 0
                || Get(process, "currentResult") is int resultIndex && resultIndex > 0
                || Get(process, "pickUpReady") is true) return;
            foreach (object owner in inputs)
                if (Get(owner, "Count") is not int count || count != 0 || Get(owner, "BeingFilled") is true) return;
            var baseline = new System.Collections.Generic.List<int>();
            for (int i = 0; i < ingredients.Count; i++)
            {
                object ingredient = ingredients[i];
                if (Get(ingredient, "nutritionGetter") is true || Get(ingredient, "thing") is not ThingDef thing
                    || !ReferenceEquals(Get(inputs[i], "ThingDef"), thing) || Get(ingredient, "countNeeded") is not float wanted
                    || wanted <= 0 || wanted > int.MaxValue || float.IsNaN(wanted) || float.IsInfinity(wanted)) return;
                baseline.Add((int)wanted);
            }
            state.baseline = baseline;
            // Keep native overclock/room factors, but only migrate the exact prior
            // clock of an empty batch. In-flight work keeps its original duration.
            if (policy.previousTicks > 0 && Get(process, "cachedInitialTicks") is int cached && cached == policy.previousTicks
                && Get(def, "ticks") is int ticks && ticks > 0 && Get(process, "ticksOrQualityTicks") is int adjusted
                && Get(process, "tickLeft") is int left)
            {
                AccessTools.Field(process.GetType(), "ticksOrQualityTicks").SetValue(process, Math.Max(1, (int)Math.Ceiling((double)adjusted * ticks / cached)));
                AccessTools.Field(process.GetType(), "tickLeft").SetValue(process, Math.Max(1, (int)Math.Ceiling((double)left * ticks / cached)));
                AccessTools.Field(process.GetType(), "cachedInitialTicks").SetValue(process, ticks);
            }
        }

        internal static bool IsTargeted(Def def) => def?.defName == "GrechkaWorld_DrillSteel"
            || def?.defName == "GrechkaWorld_DrillPlasteel" || def?.defName == "GrechkaWorld_DrillUranium";

        internal static bool CanFit(int existing, int amount, int limit) => amount > 0 && existing >= 0
            && (long)existing + amount <= limit;

        // Native PipeSystem absorbs into an existing output stack without checking
        // whether the full batch fits. Targeted extraction must wait rather than
        // count a partially merged stack as a completed batch.
        internal static bool BeforeSpawn(object process, object result, IntVec3 cell, Map map, ref Thing output, ref bool success)
        {
            if (!IsTargeted(Get(process, "def") as Def) || map == null || !cell.InBounds(map)) return true;
            ThingDef product = Get(result, "thing") as ThingDef;
            if (product == null) return true;
            int amount = (int)AccessTools.Method(result.GetType(), "GetCount").Invoke(result, new[] { process });
            Thing existing = cell.GetThingList(map).FirstOrDefault(t => t.def == product);
            if (CanFit(existing?.stackCount ?? 0, amount, product.stackLimit)) return true;
            AccessTools.Field(process.GetType(), "outputFactoryHopperTooFull")?.SetValue(process, true);
            output = null; success = false; return false;
        }
    }

    [HarmonyPatch]
    public static class FactoryTargetedOutputHook
    {
        public static bool Prepare() => FactoryTierAdapter.Available;
        public static MethodBase TargetMethod() => AccessTools.Method(FactoryTierAdapter.ProcessType, "TrySpawnAtCell",
            new[] { FactoryTierAdapter.ResultType, typeof(IntVec3), typeof(Map), typeof(Thing).MakeByRefType() });
        public static bool Prefix(object __instance, object __0, IntVec3 __1, Map __2, ref Thing __3, ref bool __result)
            => FactoryProcessBalance.BeforeSpawn(__instance, __0, __1, __2, ref __3, ref __result);
    }
}
