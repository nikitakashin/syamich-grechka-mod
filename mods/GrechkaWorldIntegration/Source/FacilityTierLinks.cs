using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.WorldIntegration
{
    internal static class FacilityTierLinks
    {
        private static readonly Dictionary<CompProperties_AffectedByFacilities, List<ThingDef>> receiverLinks = new();
        private static readonly Dictionary<CompProperties_Facility, List<ThingDef>> facilityLinks = new();
        private static readonly ConditionalWeakTable<CompProperties_Facility, CompProperties_Facility[]> scaled = new();
        private static readonly MethodInfo shallowCopy = AccessTools.Method(typeof(object), "MemberwiseClone");
        private static readonly System.Action<CompFacility> relink = AccessTools.MethodDelegate<System.Action<CompFacility>>(
            AccessTools.Method(typeof(CompFacility), "RelinkAll"));
        private static Game pendingRelink;
        private static readonly HashSet<ThingDef> changedFacilities = new();
        private static readonly HashSet<CompProperties_Facility> changedEndpoints = new();
        private static readonly float[] factors = { 1f, 1f, 1f, 1.5f, 2f, 3f };

        private static bool MedicalOffset(StatModifier offset) => offset.stat?.defName is
            "MedicalTendQualityOffset" or "ImmunityGainSpeedFactor" or "SurgerySuccessChanceFactor";

        internal static bool HasScaledOffsets(ThingDef def)
            => def?.GetCompProperties<CompProperties_Facility>()?.statOffsets?.Any(MedicalOffset) == true;

        internal static void RefreshLinks()
        {
            var families = ProfileVariants.Entries.Values
                .Where(e => e.name != e.sourceName && WorldProfile.AppliedTiers.ContainsKey(e.name))
                .GroupBy(e => e.sourceName).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.tier)
                    .Select(e => DefDatabase<ThingDef>.GetNamedSilentFail(e.name)).Where(d => d != null).ToList());
            if (pendingRelink != Current.Game)
            {
                changedFacilities.Clear();
                changedEndpoints.Clear();
            }
            bool HasVariant(ThingDef def) => def != null && families.ContainsKey(def.defName);
            List<ThingDef> Expand(List<ThingDef> baseline) => baseline.SelectMany(d =>
                d != null && families.TryGetValue(d.defName, out var variants) ? variants.Append(d) : new[] { d }).Distinct().ToList();

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                var receiver = def.GetCompProperties<CompProperties_AffectedByFacilities>();
                if (receiver?.linkableFacilities != null && (receiverLinks.ContainsKey(receiver)
                    || receiver.linkableFacilities.Any(HasVariant)))
                {
                    if (!receiverLinks.TryGetValue(receiver, out var baseline)) receiverLinks[receiver] = baseline = receiver.linkableFacilities;
                    var updated = Expand(baseline);
                    if (!receiver.linkableFacilities.SequenceEqual(updated))
                    {
                        changedFacilities.UnionWith(receiver.linkableFacilities);
                        changedFacilities.UnionWith(updated);
                        receiver.linkableFacilities = updated;
                    }
                }
                var facility = def.GetCompProperties<CompProperties_Facility>();
                if (facility?.linkableBuildings != null && (facilityLinks.ContainsKey(facility)
                    || facility.linkableBuildings.Any(HasVariant)))
                {
                    if (!facilityLinks.TryGetValue(facility, out var baseline)) facilityLinks[facility] = baseline = facility.linkableBuildings;
                    var updated = Expand(baseline);
                    if (!facility.linkableBuildings.SequenceEqual(updated))
                    {
                        changedEndpoints.Add(facility);
                        facility.linkableBuildings = updated;
                    }
                }
            }
            pendingRelink = Current.Game;
        }

        internal static void RelinkLoadedMapBuildings(Game owner)
        {
            if (owner == null || pendingRelink != owner || Current.Game != owner || LongEventHandler.AnyEventNowOrWaiting) return;
            pendingRelink = null;
            foreach (Map map in owner.Maps)
                foreach (Thing building in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
                    if (building.TryGetComp<CompFacility>() is CompFacility comp && (changedFacilities.Contains(building.def)
                        || comp.props is CompProperties_Facility props && changedEndpoints.Contains(props)))
                        relink(comp);
            changedFacilities.Clear();
            changedEndpoints.Clear();
        }

        internal static CompProperties_Facility TierProperties(CompFacility comp, CompProperties_Facility props)
        {
            int tier = WorldProfile.GetTier(comp.parent);
            if (tier <= 2 || props?.statOffsets?.Any(MedicalOffset) != true) return props;
            var entries = scaled.GetValue(props, _ => new CompProperties_Facility[6]);
            var result = entries[tier];
            if (result == null)
            {
                result = (CompProperties_Facility)shallowCopy.Invoke(props, null);
                result.statOffsets = props.statOffsets.Select(s => new StatModifier
                    { stat = s.stat, value = MedicalOffset(s) ? s.value * factors[tier] : s.value }).ToList();
                entries[tier] = result;
            }
            result.linkableBuildings = props.linkableBuildings; // The profile can be reapplied during play.
            return result;
        }
    }

    [HarmonyPatch(typeof(CompFacility), nameof(CompFacility.Props), MethodType.Getter)]
    internal static class FacilityTierPropertiesHook
    {
        static void Postfix(CompFacility __instance, ref CompProperties_Facility __result)
            => __result = FacilityTierLinks.TierProperties(__instance, __result);
    }

    [HarmonyPatch(typeof(CompAffectedByFacilities), nameof(CompAffectedByFacilities.IsFacilityActive))]
    internal static class FacilityTierActiveHook
    {
        static void Postfix(CompAffectedByFacilities __instance, Thing facility, ref bool __result)
        {
            if (!__result || !WorldProfile.IsApplied(facility.def)) return;
            string family = WorldProfile.OriginalDefName(facility.def);
            int tier = WorldProfile.GetTier(facility);
            var props = facility.TryGetComp<CompFacility>().Props;
            var linked = __instance.LinkedFacilitiesListForReading;
            if (!linked.Any(other => other.def != facility.def && WorldProfile.OriginalDefName(other.def) == family)) return;
            // Keep native stacking limits across tiers of the same facility.
            int better = linked.Count(other => other != facility
                && WorldProfile.OriginalDefName(other.def) == family && other.TryGetComp<CompFacility>()?.CanBeActive == true
                && (WorldProfile.GetTier(other) > tier || WorldProfile.GetTier(other) == tier && other.thingIDNumber < facility.thingIDNumber));
            __result = better < props.maxSimultaneous;
        }
    }
}
