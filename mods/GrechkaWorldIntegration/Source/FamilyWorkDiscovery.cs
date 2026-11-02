using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.WorldIntegration
{
    // A work request for the original fermenter/bench must also discover its upgrades.
    // Scope this to work selection: inventory, quest variants and exact Def queries stay exact.
    public static class FamilyWorkDiscovery
    {
        [ThreadStatic] private static int scopeDepth;
        private static readonly Dictionary<ThingDef, List<ThingDef>> families = new Dictionary<ThingDef, List<ThingDef>>();
        private static readonly Dictionary<ThingDef, ThingDef> originals = new Dictionary<ThingDef, ThingDef>();
        private static ConditionalWeakTable<ListerThings, Cache> caches = new ConditionalWeakTable<ListerThings, Cache>();
        private static readonly AccessTools.FieldRef<ListerThings, Dictionary<ThingDef, List<Thing>>> lists
            = AccessTools.FieldRefAccess<ListerThings, Dictionary<ThingDef, List<Thing>>>("listsByDef");
        private sealed class Cache { public readonly Dictionary<ThingDef, List<Thing>> results = new Dictionary<ThingDef, List<Thing>>(); }
        public static bool Active => scopeDepth > 0;
        public static int Enter() { int previous = scopeDepth; scopeDepth++; return previous; }
        public static void Leave(int previous) { scopeDepth = previous; }
        public static void Refresh()
        {
            families.Clear(); originals.Clear(); caches = new ConditionalWeakTable<ListerThings, Cache>();
            foreach (VariantEntry entry in ProfileVariants.Entries.Values)
            {
                ThingDef original = DefDatabase<ThingDef>.GetNamedSilentFail(entry.sourceName);
                ThingDef variant = DefDatabase<ThingDef>.GetNamedSilentFail(entry.name);
                if (original == null || variant == null || !WorldProfile.IsApplied(variant)) continue;
                if (!families.TryGetValue(original, out var family)) families.Add(original, family = new List<ThingDef>());
                if (!family.Contains(variant)) family.Add(variant);
                originals[variant] = original;
            }
            foreach (WorkGiverDef giver in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (giver.fixedBillGiverDefs == null) continue;
                // Remove only our derived identities, preserving other mods' own edits.
                var source = giver.fixedBillGiverDefs.Where(d => ProfileVariants.Entry(d) is not VariantEntry e || e.name == e.sourceName).Distinct().ToList();
                giver.fixedBillGiverDefs = source.SelectMany(d => families.TryGetValue(d, out var family) ? family : new List<ThingDef> { d }).Distinct().ToList();
            }
        }
        public static bool Accepts(ThingDef requested, Thing actual)
            => Active && requested != null && actual?.def != null && originals.TryGetValue(actual.def, out ThingDef source)
                && source == requested && families.ContainsKey(requested);
        public static bool TryList(ListerThings lister, ThingDef requested, out List<Thing> result)
        {
            result = null;
            if (!Active || requested == null || !families.TryGetValue(requested, out var family) || family.Count < 2) return false;
            Cache cache = caches.GetValue(lister, _ => new Cache());
            if (!cache.results.TryGetValue(requested, out result))
            {
                result = new List<Thing>();
                Dictionary<ThingDef, List<Thing>> raw = lists(lister);
                foreach (ThingDef def in family) if (raw.TryGetValue(def, out var found)) result.AddRange(found);
                cache.results[requested] = result;
            }
            return true;
        }
        public static void Changed(ListerThings lister, Thing thing)
        {
            if (thing?.def != null && originals.TryGetValue(thing.def, out ThingDef original) && caches.TryGetValue(lister, out Cache cache))
                cache.results.Remove(original);
        }
        public static void Cleared(ListerThings lister) => caches.Remove(lister);
    }
    [HarmonyPatch]
    public static class FamilyWorkScopeHook
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(JobGiver_Work), "TryIssueJobPackage");
            yield return AccessTools.Method(typeof(JobGiver_Work), "GiverTryGiveJobPrioritized");
            yield return AccessTools.Method(typeof(JobGiver_GetNeuralSupercharge), "ClosestSupercharger");
        }
        static void Prefix(out int __state) => __state = FamilyWorkDiscovery.Enter();
        static Exception Finalizer(Exception __exception, int __state) { FamilyWorkDiscovery.Leave(__state); return __exception; }
    }
    [HarmonyPatch(typeof(ThingRequest), nameof(ThingRequest.Accepts))]
    public static class FamilyWorkAcceptHook
    {
        static void Postfix(ThingRequest __instance, Thing __0, ref bool __result)
        { if (!__result) __result = FamilyWorkDiscovery.Accepts(__instance.singleDef, __0); }
    }
    [HarmonyPatch(typeof(ListerThings), nameof(ListerThings.ThingsMatching))]
    public static class FamilyWorkListHook
    {
        static bool Prefix(ListerThings __instance, ThingRequest __0, ref List<Thing> __result)
        {
            if (!FamilyWorkDiscovery.TryList(__instance, __0.singleDef, out List<Thing> found)) return true;
            __result = found; return false;
        }
    }
    [HarmonyPatch(typeof(ListerThings), nameof(ListerThings.AnyThingWithDef))]
    public static class FamilyWorkAnyHook
    {
        static void Postfix(ListerThings __instance, ThingDef __0, ref bool __result)
        { if (!__result && FamilyWorkDiscovery.TryList(__instance, __0, out var found)) __result = found.Count > 0; }
    }
    [HarmonyPatch]
    public static class FamilyWorkListChangeHook
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ListerThings), nameof(ListerThings.Add));
            yield return AccessTools.Method(typeof(ListerThings), nameof(ListerThings.Remove));
        }
        // Invalidate before callbacks inside Add/Remove can ask for a refreshed view.
        static void Prefix(ListerThings __instance, Thing __0) => FamilyWorkDiscovery.Changed(__instance, __0);
        static void Postfix(ListerThings __instance, Thing __0) => FamilyWorkDiscovery.Changed(__instance, __0);
    }
    [HarmonyPatch(typeof(ListerThings), nameof(ListerThings.Clear))]
    public static class FamilyWorkListClearHook
    { static void Prefix(ListerThings __instance) => FamilyWorkDiscovery.Cleared(__instance); }
}
