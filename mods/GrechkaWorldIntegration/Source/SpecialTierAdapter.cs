using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.WorldIntegration
{
    public static class SpecialTierAdapter
    {
        private static readonly HashSet<string> native = new HashSet<string>
        {
            "Building_TurretGun", "Building_TurretFoam", "Building_TurretRocket",
            "Building_FermentingBarrel", "Building_PlantGrower", "Building_NutrientPasteDispenser",
            "Building_MechGestator", "Building_GeneAssembler", "Building_GeneExtractor", "Building_SubcoreScanner",
            "Building_GrowthVat", "Building_WastepackAtomizer", "Building_MechCharger"
        };
        private static readonly HashSet<string> external = new HashSet<string>
        {
            "Fortified.Building_WorkTableAutonomous", "Fortified.Building_TurretCapacity",
            "VFEC.Buildings.ScorpionTurret", "VFEC.Buildings.Thermaebath", "VFED.Building_TurretGunBarrels",
            "VFESecurity.Building_Artillery", "VFESecurity.Building_ConcealedTurret", "VFESecurity.Building_TurretSearchlight",
            "VFESecurity.Building_ConcealedBarrier", "VFEPirates.Building_WarcasketFoundry", "MFSpacer.Building_RepairStored",
            "VanillaFurnitureExpandedFactory.Building_Autofarmer", "VanillaFurnitureExpandedFactory.Building_FactoryHopper",
            "VNPE.Building_Dripper", "VNPE.Building_Feeder", "VNPE.Building_NutrientGrinder", "VNPE.Building_NutrientPasteTap", "PipeSystem.Building_PipeValve",
            "VanillaQuestsExpandedTheGenerator.Building_Genetron_Atomic", "VanillaQuestsExpandedTheGenerator.Building_Genetron_Basic",
            "VanillaQuestsExpandedTheGenerator.Building_Genetron_ChemfuelBoosted", "VanillaQuestsExpandedTheGenerator.Building_Genetron_ChemfuelCharged",
            "VanillaQuestsExpandedTheGenerator.Building_Genetron_ChemfuelFortified", "VanillaQuestsExpandedTheGenerator.Building_Genetron_ChemfuelPowered",
            "VanillaQuestsExpandedTheGenerator.Building_Genetron_Geothermal", "VanillaQuestsExpandedTheGenerator.Building_Genetron_HeatPowered",
            "VanillaQuestsExpandedTheGenerator.Building_Genetron_Isotopic", "VanillaQuestsExpandedTheGenerator.Building_Genetron_Nuclear",
            "VanillaQuestsExpandedTheGenerator.Building_Genetron_SteamPowered", "VanillaQuestsExpandedTheGenerator.Building_Genetron_ThermalVent",
            "VanillaQuestsExpandedTheGenerator.Building_Genetron_UraniumPowered", "VanillaQuestsExpandedTheGenerator.Building_Genetron_WoodBlasting",
            "VanillaQuestsExpandedTheGenerator.Building_Genetron_WoodFired", "VanillaQuestsExpandedTheGenerator.Building_Genetron_WoodFueled",
            "VanillaQuestsExpandedTheGenerator.Building_Genetron_WoodPowered"
        };
        public static bool SupportsXmlClass(string kind)
        {
            if (kind == null) return false;
            string simple = kind.StartsWith("RimWorld.", StringComparison.Ordinal) ? kind.Substring(9)
                : kind.StartsWith("Verse.", StringComparison.Ordinal) ? kind.Substring(6) : kind;
            return native.Contains(simple) || external.Contains(kind);
        }
        public static bool HasPowerBenefit(ThingDef def)
        {
            if (ProfileVariants.Entry(def) == null) return false;
            string name = def.thingClass?.FullName;
            return name == "RimWorld.Building_GeneAssembler" || name == "RimWorld.Building_GeneExtractor"
                || name == "RimWorld.Building_SubcoreScanner" || name == "RimWorld.Building_MechGestator"
                || name == "RimWorld.Building_NutrientPasteDispenser" || name == "RimWorld.Building_PlantGrower"
                || name == "RimWorld.Building_WastepackAtomizer" || name == "RimWorld.Building_GrowthVat"
                || name == "VFEPirates.Building_WarcasketFoundry" || name == "MFSpacer.Building_RepairStored"
                || name == "Fortified.Building_WorkTableAutonomous" || name == "VNPE.Building_NutrientGrinder"
                || name == "VanillaFurnitureExpandedFactory.Building_Autofarmer"
                || def.comps?.Any(p => p.compClass != null && (typeof(CompDeepDrill).IsAssignableFrom(p.compClass) || typeof(CompScanner).IsAssignableFrom(p.compClass))) == true;
        }
        public static int Tier(Thing machine) => machine?.def != null && ProfileVariants.Entry(machine.def) != null ? WorldProfile.GetTier(machine) : 0;
        public static float Speed(Thing machine) => ProfileVariants.Speed[Math.Max(0, Math.Min(5, Tier(machine)))];
        public static float Discount(Thing machine) => ProfileVariants.Discount[Math.Max(0, Math.Min(5, Tier(machine)))];
        public static float Output(Thing machine) => ProfileVariants.Output[Math.Max(0, Math.Min(5, Tier(machine)))];
    }
}
