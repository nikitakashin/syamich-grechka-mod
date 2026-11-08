using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.WorldIntegration
{
    // Explicit owned series plus the existing generated-variant registry. A matching
    // translated label or an arbitrary T suffix never establishes compatibility.
    public static class TierMachineFamilies
    {
        private sealed class Series
        {
            public string package, prefix;
            public int minimum;
            public Series(string package, string prefix, int minimum)
            { this.package = package; this.prefix = prefix; this.minimum = minimum; }
            public string Name(int tier) => prefix + tier + "_Building";
        }
        private static readonly Series[] owned = {
            new Series("grechka.industry", "Grechka_AssemblyBenchT", 1),
            new Series("grechka.industry", "Grechka_MatterRecyclerT", 4),
            new Series("grechka.industry", "Grechka_MatterSynthesizerT", 4),
            new Series("grechka.pharmacy", "GrechkaPharmacy_ReactorT", 3)
        };

        public static bool TryDescribe(ThingDef def, out string family, out int tier)
        {
            family = null; tier = 0;
            if (def == null) return false;
            foreach (Series series in owned)
                if (MaterialDictionary.BelongsTo(def, series.package))
                    for (int t = series.minimum; t <= 5; t++)
                        if (def.defName == series.Name(t))
                        { family = series.Name(series.minimum); tier = t; return true; }
            VariantEntry entry = ProfileVariants.Entry(def);
            ThingDef source = entry == null ? null : DefDatabase<ThingDef>.GetNamedSilentFail(entry.sourceName);
            if (source == null || source.modContentPack != def.modContentPack) return false;
            family = source.defName; tier = entry.tier;
            return true;
        }

        public static IEnumerable<ThingDef> Members(ThingDef def)
        {
            if (!TryDescribe(def, out string family, out _)) return new[] { def };
            return DefDatabase<ThingDef>.AllDefsListForReading.Where(d =>
                TryDescribe(d, out string other, out _) && family == other);
        }

        public static bool IsUpgrade(RecipeDef recipe) => recipe != null &&
            (ProfileVariants.IsUpgrade(recipe) || recipe.defName.StartsWith("Grechka_Upgrade", StringComparison.Ordinal));
    }

    [HarmonyPatch(typeof(ThingDef), "get_AllRecipes")]
    public static class TierRecipeInheritance
    {
        private sealed class Cache
        {
            public List<RecipeDef>[] inputs;
            public int[] counts;
            public List<RecipeDef> result;
        }
        private static readonly Dictionary<ThingDef, Cache> caches = new Dictionary<ThingDef, Cache>();
        private static readonly Dictionary<ThingDef, List<ThingDef>> lowerDefs = new Dictionary<ThingDef, List<ThingDef>>();
        public static void Clear() { caches.Clear(); lowerDefs.Clear(); }

        public static void Postfix(ThingDef __instance, ref List<RecipeDef> __result)
        {
            if (__result == null || __instance.thingClass == null || !typeof(IBillGiver).IsAssignableFrom(__instance.thingClass)
                || !TierMachineFamilies.TryDescribe(__instance, out _, out int tier)) return;
            if (!lowerDefs.TryGetValue(__instance, out var lower))
                lowerDefs[__instance] = lower = TierMachineFamilies.Members(__instance)
                    .Where(d => d != __instance && d.thingClass != null && typeof(IBillGiver).IsAssignableFrom(d.thingClass)
                        && TierMachineFamilies.TryDescribe(d, out _, out int t) && t < tier)
                    .OrderBy(d => { TierMachineFamilies.TryDescribe(d, out _, out int t); return t; }).ToList();
            if (lower.Count == 0) return;
            var inputs = new[] { __result }.Concat(lower.Select(d => d.AllRecipes)).ToArray();
            if (caches.TryGetValue(__instance, out Cache cached) && cached.inputs.Length == inputs.Length
                && inputs.Select((list, i) => ReferenceEquals(list, cached.inputs[i]) && list.Count == cached.counts[i]).All(v => v))
            { __result = cached.result; return; }
            var result = new List<RecipeDef>(__result);
            var seen = new HashSet<RecipeDef>(result);
            foreach (var list in inputs.Skip(1))
                foreach (RecipeDef recipe in list)
                    if (!TierMachineFamilies.IsUpgrade(recipe) && seen.Add(recipe)) result.Add(recipe);
            caches[__instance] = new Cache { inputs = inputs, counts = inputs.Select(l => l.Count).ToArray(), result = result };
            __result = result;
        }
    }
}
