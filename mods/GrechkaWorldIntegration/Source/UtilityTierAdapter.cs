using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld;
using HarmonyLib;
using Verse;

namespace Grechka.WorldIntegration
{
    internal static class TierGeothermal
    {
        internal static bool Applies(ThingDef def) => def?.comps?.Any(p => p.compClass != null
            && typeof(CompPowerPlantSteam).IsAssignableFrom(p.compClass)) == true;

        internal static float Multiplier(ThingDef def, int tier)
            => Math.Max(0, tier - (ProfileVariants.Entry(def)?.minimum ?? tier)) switch
            {
                1 => 1.5f,
                2 => 2f,
                >= 3 => 3f,
                _ => 1f
            };
    }

    [HarmonyPatch(typeof(CompPowerPlant), "UpdateDesiredPowerOutput")]
    internal static class TierGeothermalOutputHook
    {
        static void Postfix(CompPowerPlant __instance)
        {
            var parent = __instance.parent;
            int tier = WorldProfile.GetTier(parent);
            if (tier == 0 || !TierGeothermal.Applies(parent.def)) return;
            float watts = __instance.PowerOutput;
            if (watts > 0f) __instance.PowerOutput = watts * TierGeothermal.Multiplier(parent.def, tier);
        }
    }
    /// <summary>
    /// Explicit non-production families. Battery scaling has its own native adapter.
    /// In particular, a decorative T5 lamp does not draw 80 times as much power for no benefit.
    /// </summary>
    public static class UtilityTierAdapter
    {
        private static readonly HashSet<string> Classes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Building", "Building_Battery", "Building_Cooler", "Building_Heater",
            "Building_OrbitalTradeBeacon", "Building_CommsConsole", "Building_PowerSwitch",
            "Building_ShipReactor", "Building_MultiTileDoor", "Building_Door", "Building_PodLauncher",
            "Building_ShipComputerCore", "Building_CryptosleepCasket", "Building_SunLamp",
            "Building_Bed", "Building_Storage", "Building_MechCharger",
            "VanillaPowerExpanded.Building_SmallBattery", "AdaptiveStorage.ThingClass",
            "MFSpacer.Building_RepairStored", "VFEC.Buildings.Thermaebath"
        };
        public static bool SupportsXmlClass(string kind)
        {
            if (kind == null) return false;
            if (Classes.Contains(kind)) return true;
            if (kind.StartsWith("RimWorld.", StringComparison.Ordinal)) return Classes.Contains(kind.Substring(9));
            if (kind.StartsWith("Verse.", StringComparison.Ordinal)) return Classes.Contains(kind.Substring(6));
            return false;
        }
    }

    // Adaptive Storage associates graphics with concrete ThingDef identities outside XML inheritance.
    // Register derived identities through its own initializer, preserving its tabs/filter preparation too.
    [HarmonyPatch]
    public static class AdaptiveStorageVariantGraphicsHook
    {
        private static MethodBase TargetMethod()
        {Type type=AccessTools.TypeByName("AdaptiveStorage.GraphicsDef");return type==null?null:AccessTools.Method(type,"InitializeOnDef");}
        private static bool Prepare() => TargetMethod()!=null;
        public static void Postfix(object __instance,ThingDef __0)
        {
            VariantEntry source=ProfileVariants.Entry(__0);
            if(source==null||source.sourceName!=__0.defName)return;
            MethodInfo initialize=AccessTools.Method(__instance.GetType(),"InitializeOnDef");
            foreach(VariantEntry entry in ProfileVariants.Entries.Values.Where(e=>e.sourceName==source.sourceName&&e.name!=source.name))
            {
                ThingDef variant=DefDatabase<ThingDef>.GetNamedSilentFail(entry.name);
                if(variant!=null)initialize.Invoke(__instance,new object[]{variant});
            }
        }
    }

    [HarmonyPatch]
    public static class ShipVariantIdentityHook
    {
        private static readonly FieldInfo ThingDefField=AccessTools.Field(typeof(Thing),"def");
        private static IEnumerable<Type> Types(Type type)
        {yield return type;foreach(Type nested in type.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic))foreach(Type item in Types(nested))yield return item;}
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(Type type in Types(typeof(ShipUtility)))
                foreach(MethodInfo method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly))
                    if(method.GetMethodBody()!=null&&PatchProcessor.GetOriginalInstructions(method).Any(i=>i.LoadsField(ThingDefField)))yield return method;
        }
        public static ThingDef OriginalDefinition(Thing thing)
        {
            string original=WorldProfile.OriginalDefName(thing?.def);
            return original==null?thing?.def:DefDatabase<ThingDef>.GetNamedSilentFail(original)??thing.def;
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach(CodeInstruction instruction in instructions)
            {
                // Only native ShipUtility identity reads, including its compiled predicates/iterators.
                if(instruction.LoadsField(ThingDefField))
                {instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(ShipVariantIdentityHook),nameof(OriginalDefinition));}
                yield return instruction;
            }
        }
    }

    public sealed class CompProperties_ProfileTierInfo : CompProperties
    { public CompProperties_ProfileTierInfo() { compClass = typeof(CompProfileTierInfo); } }

    public sealed class CompProfileTierInfo : ThingComp
    {
        public override string CompInspectStringExtra()
        {
            int tier = WorldProfile.GetTier(parent);
            VariantEntry entry = ProfileVariants.Entry(parent.def);
            if (tier == 0 || entry == null) return null;
            string tierLabel = WorldText.Get("GrechkaWorldIntegration_TierInspector", "Grechka • T") + tier;
            if (entry.worktable || entry.factory)
                return tierLabel + WorldText.Get("GrechkaWorldIntegration_4dc34041f85e", " • speed ×") + ProfileVariants.Speed[tier]
                    + WorldText.Get("GrechkaWorldIntegration_aa2dc98e87d8", ", input −") + Math.Round(ProfileVariants.Discount[tier] * 100) + "%"
                    + WorldText.Get("GrechkaWorldIntegration_bf266285ec80", ", output +") + Math.Round(ProfileVariants.Output[tier] * 100) + "%";
            if (parent.TryGetComp<CompPowerBattery>() != null) return null; // Native battery inspector shows the effective capacity and efficiency.
            if (FacilityTierLinks.HasScaledOffsets(parent.def)) return null; // The native facility shows the real offsets and link status.
            if (TierGeothermal.Applies(parent.def))
                return tierLabel + WorldText.Get("GrechkaWorldIntegration_GeothermalOutput", " • power ×") + TierGeothermal.Multiplier(parent.def, tier);
            if (SpecialTierAdapter.HasPowerBenefit(parent.def))
                return tierLabel + WorldText.Get("GrechkaWorldIntegration_75f3dec4f377", " • faster device operation.");
            return tierLabel + WorldText.Get("GrechkaWorldIntegration_4ed65b1894f6", " • housing and upgrades. Original device stats preserved.");
        }
    }
}
