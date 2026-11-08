using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Traders
{
    // The shared StorytellerDef is never edited. Each storyteller component gets its
    // own shallow copy; lists we edit are copied separately. Restoring x1 restores
    // the original properties, and applying settings twice never compounds them.
    public static class ArrivalControl
    {
        private sealed class Baseline
        {
            public StorytellerCompProperties original;
            public StorytellerCompProperties applied;
            public bool configured;
            public float orbital;
            public float caravan;
        }

        private static readonly ConditionalWeakTable<StorytellerComp, Baseline> originals =
            new ConditionalWeakTable<StorytellerComp, Baseline>();
        private static readonly MethodInfo clone = AccessTools.Method(typeof(object), "MemberwiseClone");

        public static float ClampFrequency(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? 1f : Math.Max(0f, Math.Min(5f, value));

        public static float MultiplierFor(IncidentDef incident, float orbital, float caravan)
        {
            if (incident?.defName == "OrbitalTraderArrival") return ClampFrequency(orbital);
            if (incident?.defName == "TraderCaravanArrival") return ClampFrequency(caravan);
            return 1f;
        }

        public static IncidentDef DedicatedIncident(StorytellerCompProperties props)
        {
            if (props is StorytellerCompProperties_OnOffCycle cycle) return cycle.incident;
            if (props is StorytellerCompProperties_FactionInteraction faction) return faction.incident;
            if (props is StorytellerCompProperties_SingleMTB mtb) return mtb.incident;
            return null;
        }

        public static void ApplySettings()
        {
            if (Current.Game == null) return;
            Apply(Find.Storyteller);
        }

        public static void Apply(Storyteller storyteller)
        {
            if (storyteller?.storytellerComps == null) return;
            foreach (StorytellerComp comp in storyteller.storytellerComps)
                ConfigureComp(comp, GrechkaTradersMod.Settings.orbitalFrequency,
                    GrechkaTradersMod.Settings.caravanFrequency);
        }

        public static void ConfigureComp(StorytellerComp comp, float orbital, float caravan)
        {
            if (comp?.props == null) return;
            orbital = ClampFrequency(orbital);
            caravan = ClampFrequency(caravan);
            Baseline baseline = originals.GetValue(comp, c => new Baseline { original = c.props });
            // A different mod may replace the component's props after initialization.
            if (comp.props != baseline.original && comp.props != baseline.applied)
            {
                baseline.original = comp.props;
                baseline.configured = false;
            }
            if (baseline.configured && comp.props == baseline.applied &&
                baseline.orbital == orbital && baseline.caravan == caravan)
                return;
            StorytellerCompProperties source = baseline.original;
            float multiplier = MultiplierFor(DedicatedIncident(source), orbital, caravan);
            StorytellerCompProperties result = source;
            if (multiplier > 0f && multiplier != 1f)
            {
                result = (StorytellerCompProperties)clone.Invoke(source, null);
                if (result is StorytellerCompProperties_OnOffCycle cycle)
                {
                    cycle.onDays /= multiplier;
                    cycle.offDays /= multiplier;
                    cycle.minSpacingDays /= multiplier;
                    cycle.onDaysNoTreeConnectors /= multiplier;
                    cycle.offDaysNoTreeConnectors /= multiplier;
                }
                else if (result is StorytellerCompProperties_FactionInteraction faction)
                {
                    faction.baseIncidentsPerYear *= multiplier;
                    faction.minSpacingDays /= multiplier;
                }
                else if (result is StorytellerCompProperties_SingleMTB mtb)
                    mtb.mtbDays /= multiplier;
            }
            if (source is StorytellerCompProperties_RandomMain random &&
                (orbital != 1f || caravan != 1f))
            {
                var adjusted = (StorytellerCompProperties_RandomMain)clone.Invoke(random, null);
                adjusted.categoryWeights = new List<IncidentCategoryEntry>();
                float oldTotal = 0f, newTotal = 0f;
                foreach (IncidentCategoryEntry entry in random.categoryWeights)
                {
                    var candidates = DefDatabase<IncidentDef>.AllDefsListForReading
                        .Where(i => i.category == entry.category).ToList();
                    float oldWeight = candidates.Sum(i => Math.Max(0f, i.baseChance));
                    float newWeight = candidates.Sum(i => Math.Max(0f, i.baseChance) * MultiplierFor(i, orbital, caravan));
                    float weight = oldWeight > 0f ? entry.weight * newWeight / oldWeight : entry.weight;
                    adjusted.categoryWeights.Add(new IncidentCategoryEntry { category = entry.category, weight = weight });
                    oldTotal += entry.weight;
                    newTotal += weight;
                }
                // Preserve the nominal rates of unrelated categories as trading
                // takes more/fewer slots in Randy's pool. Eligibility, population,
                // recent-incident weighting and forced raids still belong to Randy.
                if (oldTotal > 0f && newTotal > 0f) adjusted.mtbDays *= oldTotal / newTotal;
                result = adjusted;
            }
            comp.props = result;
            baseline.applied = result;
            baseline.orbital = orbital;
            baseline.caravan = caravan;
            baseline.configured = true;
        }

        public static bool AllowDedicated(StorytellerComp comp, float orbital, float caravan) =>
            MultiplierFor(DedicatedIncident(comp?.props), orbital, caravan) > 0f;

        public static float AdjustWeight(StorytellerComp comp, IncidentDef incident,
            float weight, float orbital, float caravan)
        {
            float factor = MultiplierFor(incident, orbital, caravan);
            // Dedicated schedules already scale their time/rate parameters.
            // They use this weight only to select the incident, never to scale time.
            if (factor > 0f && DedicatedIncident(comp?.props) == incident) return weight;
            return weight * factor;
        }
    }

    [HarmonyPatch(typeof(Storyteller), "InitializeStorytellerComps")]
    public static class TraderStorytellerInitialization
    {
        public static void Postfix(Storyteller __instance) => ArrivalControl.Apply(__instance);
    }

    [HarmonyPatch]
    public static class TraderDedicatedSchedule
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(StorytellerComp_OnOffCycle), nameof(StorytellerComp.MakeIntervalIncidents));
            yield return AccessTools.Method(typeof(StorytellerComp_FactionInteraction), nameof(StorytellerComp.MakeIntervalIncidents));
            yield return AccessTools.Method(typeof(StorytellerComp_SingleMTB), nameof(StorytellerComp.MakeIntervalIncidents));
        }

        public static bool Prefix(StorytellerComp __instance, ref IEnumerable<FiringIncident> __result)
        {
            if (ArrivalControl.AllowDedicated(__instance, GrechkaTradersMod.Settings.orbitalFrequency,
                GrechkaTradersMod.Settings.caravanFrequency)) return true;
            __result = Enumerable.Empty<FiringIncident>();
            return false;
        }
    }

    [HarmonyPatch(typeof(StorytellerComp), "IncidentChanceFinal")]
    public static class TraderRandomIncidentWeight
    {
        public static void Postfix(StorytellerComp __instance, IncidentDef def, ref float __result)
        {
            __result = ArrivalControl.AdjustWeight(__instance, def, __result,
                GrechkaTradersMod.Settings.orbitalFrequency, GrechkaTradersMod.Settings.caravanFrequency);
        }
    }
}
