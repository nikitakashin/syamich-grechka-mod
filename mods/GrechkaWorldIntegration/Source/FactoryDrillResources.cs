using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Grechka.WorldIntegration
{
    public sealed class FactoryDrillResourceDef : Def
    {
        public string packageId;
        public string thingDefName;
        public float weight;
        public int count;
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(thingDefName)) yield return "Resource identity is required.";
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight <= 0) yield return "Weight must be finite and positive.";
            if (count <= 0) yield return "Output count must be positive.";
        }
    }

    // This pool belongs only to Factory's random-mineral worker. Scanner deposits and
    // vanilla deep-drill definitions remain under their original owners.
    public static class FactoryDrillResources
    {
        internal static readonly Type WorkerType = AccessTools.TypeByName("VanillaFurnitureExpandedFactory.ResultWorker_Metals");
        internal static readonly FieldInfo CachedMetal = WorkerType == null ? null : AccessTools.Field(WorkerType, "cachedMetal");
        internal static bool Available => CachedMetal?.FieldType == typeof(ThingDef)
            && AccessTools.Method(WorkerType, "GetResult") != null && AccessTools.Method(WorkerType, "GetCount") != null;
        private static readonly Dictionary<ThingDef, FactoryDrillResourceDef> resources = new Dictionary<ThingDef, FactoryDrillResourceDef>();

        public static void Refresh()
        {
            resources.Clear();
            foreach (FactoryDrillResourceDef rule in DefDatabase<FactoryDrillResourceDef>.AllDefsListForReading)
            {
                ThingDef resource = DefDatabase<ThingDef>.GetNamedSilentFail(rule.thingDefName);
                if (!MaterialDictionary.BelongsTo(resource, rule.packageId) || resource.category != ThingCategory.Item
                    || rule.count <= 0 || rule.weight <= 0 || float.IsNaN(rule.weight) || float.IsInfinity(rule.weight)) continue;
                resources[resource] = rule;
            }
        }

        public static ThingDef Select(IEnumerable<ThingDef> native, Func<ThingDef, float> nativeWeight)
        {
            // Preserve every native entry (including other mods' weighting), appending
            // our resources once without changing the external DrillableMetalsDef lists.
            var pool = native.ToList();
            foreach (ThingDef resource in resources.Keys) if (!pool.Contains(resource)) pool.Add(resource);
            return pool.RandomElementByWeight(d => resources.TryGetValue(d, out FactoryDrillResourceDef rule) ? rule.weight : nativeWeight(d));
        }

        internal static IEnumerable<CodeInstruction> SelectionInstructions(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int matches = 0;
            foreach (CodeInstruction instruction in code)
                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method
                    && method.DeclaringType == typeof(GenCollection) && method.Name == nameof(GenCollection.RandomElementByWeight)
                    && method.IsGenericMethod && method.GetGenericArguments().SequenceEqual(new[] { typeof(ThingDef) })
                    && method.GetParameters().Length == 2)
                {
                    instruction.operand = AccessTools.Method(typeof(FactoryDrillResources), nameof(Select));
                    matches++;
                }
            if (matches != 1) throw new InvalidOperationException("Unsupported Factory metal worker: expected one weighted selection, got " + matches);
            return code;
        }

        internal static void Count(object worker, ref int amount)
        {
            if (CachedMetal.GetValue(worker) is ThingDef resource && resources.TryGetValue(resource, out FactoryDrillResourceDef rule)) amount = rule.count;
        }
    }

    [StaticConstructorOnStartup]
    internal static class FactoryDrillResourceStartup
    {
        static FactoryDrillResourceStartup() { FactoryDrillResources.Refresh(); }
    }

    [HarmonyPatch]
    public static class FactoryDrillSelectionHook
    {
        public static bool Prepare() => FactoryDrillResources.Available;
        public static MethodBase TargetMethod() => AccessTools.Method(FactoryDrillResources.WorkerType, "GetResult");
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => FactoryDrillResources.SelectionInstructions(instructions);
    }

    [HarmonyPatch]
    public static class FactoryDrillCountHook
    {
        public static bool Prepare() => FactoryDrillResources.Available;
        public static MethodBase TargetMethod() => AccessTools.Method(FactoryDrillResources.WorkerType, "GetCount");
        public static void Postfix(object __instance, ref int __result) => FactoryDrillResources.Count(__instance, ref __result);
    }
}
