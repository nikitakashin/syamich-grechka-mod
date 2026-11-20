using System.Reflection;
using Grechka.WorldIntegration;
using HarmonyLib;
using RimWorld;
using Verse;

static class FeedbackTierChecks
{
    static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }

    internal static void Run()
    {
        var assembly = typeof(WorldProfile).Assembly;
        var harmony = new Harmony("grechka.feedback-tiers.check");
        var stoveHook = assembly.GetType("Grechka.WorldIntegration.TierWorktableSpeed", true);
        var tick = (MethodBase)AccessTools.Method(stoveHook, "TargetMethod").Invoke(null, null);
        Check(tick != null, "Found the real recipe tick delegate that reads the worktable stat");
        foreach (string name in new[] { "TierWorktableSpeed", "FacilityTierPropertiesHook", "FacilityTierActiveHook", "TierGeothermalOutputHook" })
            harmony.CreateClassProcessor(assembly.GetType("Grechka.WorldIntegration." + name, true)).Patch();
        Check(Harmony.GetPatchInfo(tick).Transpilers.Any(p => p.owner == harmony.Id),
            "The speed fallback patches RimWorld 1.7's actual recipe tick");

        var added = new List<string>();
        ThingDef Tier(string family, int tier, int minimum)
        {
            var def = new ThingDef { defName = "FixtureFeedback_" + family + tier };
            ProfileVariants.Entries[def.defName] = new VariantEntry { name = def.defName, sourceName = family, tier = tier, minimum = minimum };
            WorldProfile.AppliedTiers[def.defName] = tier;
            added.Add(def.defName);
            return def;
        }
        void Comps(ThingWithComps thing, params ThingComp[] comps)
        {
            AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(thing, comps.ToList());
            foreach (ThingComp comp in comps) comp.parent = thing;
        }
        try
        {
            var recipe = new RecipeDef { defName = "FixtureTierRecipe", workTableSpeedStat = null };
            foreach (string family in new[] { "ElectricStove", "HiTechResearchBench", "FabricationBench", "VCE_CanningMachine", "FT_TableChemistry" })
            {
                var work = new Building_WorkTable { def = Tier(family, 4, 2) };
                ProfileVariants.Entry(work.def).worktable = true;
                Check(TierWorktableSpeed.ForRecipe(recipe, work) == StatDefOf.WorkTableWorkSpeedFactor
                    && StatDefOf.WorkTableWorkSpeedFactor != null, "An omitted recipe stat still honors the machine tier for " + family);
            }
            var stove = new Building_WorkTable { def = Tier("FixtureWorktable", 4, 2) };
            ProfileVariants.Entry(stove.def).worktable = true;
            var explicitStat = new StatDef();
            recipe.workTableSpeedStat = explicitStat;
            Check(TierWorktableSpeed.ForRecipe(recipe, stove) == explicitStat, "An explicit worktable stat keeps its native meaning");
            recipe.workTableSpeedStat = null;
            Check(TierWorktableSpeed.ForRecipe(recipe, new Building_WorkTable { def = new ThingDef() }) == null,
                "The fallback skips a non-tier machine");
            Check(TierWorktableSpeed.ForRecipe(new RecipeDef { defName = "GrechkaWorld_Upgrade_Fixture" }, stove) == null,
                "A queued upgrade keeps its own work duration");

            var powerProps = new CompProperties_Power { compClass = typeof(CompPowerPlantSteam) };
            AccessTools.Field(typeof(CompProperties_Power), "basePowerConsumption").SetValue(powerProps, -3600f);
            var steamDef = Tier("FixtureExternalGeothermal", 4, 2);
            steamDef.comps = new() { powerProps };
            var plant = new CompPowerPlantSteam { parent = new Building { def = steamDef }, props = powerProps };
            var powerOn = AccessTools.Field(typeof(CompPowerTrader), "powerOnInt");
            powerOn.SetValue(plant, true);
            var updateWatts = AccessTools.Method(typeof(CompPowerPlant), "UpdateDesiredPowerOutput");
            updateWatts.Invoke(plant, null);
            updateWatts.Invoke(plant, null);
            Check(plant.PowerOutput == 7200f && plant.EnergyOutputPerTick > 0f,
                "Native T4 geothermal ticks supply the grid with doubled power without compounding");
            powerOn.SetValue(plant, false);
            updateWatts.Invoke(plant, null);
            Check(plant.PowerOutput == 0f, "A stopped generator stays at zero watts");

            var stat = new StatDef { defName = "SurgerySuccessChanceFactor" };
            var original = new CompProperties_Facility { maxSimultaneous = 1, statOffsets = new() { new StatModifier { stat = stat, value = .04f } } };
            var baseMonitor = new ThingDef { defName = "Facility_VitalsCentre", comps = new() { original } };
            var t4Monitor = Tier("Facility_VitalsCentre", 4, 2);
            t4Monitor.comps = new() { original };
            var linkable = new CompProperties_AffectedByFacilities { linkableFacilities = new() { baseMonitor } };
            DefDatabase<ThingDef>.Add(baseMonitor);
            DefDatabase<ThingDef>.Add(t4Monitor);
            DefDatabase<ThingDef>.Add(new ThingDef { defName = "FixtureFeedbackHospitalBed", comps = new() { linkable } });
            AccessTools.Method(assembly.GetType("Grechka.WorldIntegration.FacilityTierLinks", true), "RefreshLinks").Invoke(null, null);
            Check(linkable.linkableFacilities.Contains(t4Monitor) && linkable.linkableFacilities.Contains(baseMonitor),
                "A bed's original facility whitelist accepts the new tier as well as the source def");
            var monitor = new CompFacility { props = original };
            var older = new CompFacility { props = original };
            var bed = new CompAffectedByFacilities();
            Comps(new Building { def = t4Monitor, thingIDNumber = 1100 }, monitor);
            Comps(new Building { def = Tier("Facility_VitalsCentre", 2, 2), thingIDNumber = 1101 }, older);
            Comps(new Building { def = new ThingDef() }, bed);
            AccessTools.Field(typeof(CompAffectedByFacilities), "linkedFacilities").SetValue(bed, new List<Thing> { older.parent, monitor.parent });
            Check(Math.Abs(bed.GetStatOffset(stat) - .08f) < .0001f, "The native bed uses the upgraded monitor at ×2 and does not stack its previous tier");
            Check(original.statOffsets.Single().value == .04f, "The facility adapter preserves shared base properties");

            var originalTool = new ThingDef { defName = "FixtureToolCabinet" };
            var toolVariant = Tier(originalTool.defName, 4, 2);
            var receiver = new ThingDef { defName = "FixtureConnectedMachine" };
            var receiverVariant = Tier(receiver.defName, 4, 2);
            var machineLinks = new CompProperties_AffectedByFacilities { linkableFacilities = new() { originalTool } };
            var workStat = new StatDef { defName = "FixtureAssemblyRate" };
            var toolLinks = new CompProperties_Facility { linkableBuildings = new() { receiver },
                statOffsets = new() { new StatModifier { stat = workStat, value = .1f } } };
            receiver.comps = receiverVariant.comps = new() { machineLinks };
            originalTool.comps = toolVariant.comps = new() { toolLinks };
            foreach (var def in new[] { originalTool, toolVariant, receiver, receiverVariant }) DefDatabase<ThingDef>.Add(def);
            var refresh = AccessTools.Method(assembly.GetType("Grechka.WorldIntegration.FacilityTierLinks", true), "RefreshLinks");
            refresh.Invoke(null, null);
            Check(machineLinks.linkableFacilities.Contains(toolVariant) && toolLinks.linkableBuildings.Contains(receiverVariant),
                "A third-party facility and its worktable recognize both upgraded endpoints");
            var toolComp = new CompFacility { props = toolLinks };
            Comps(new Building { def = toolVariant }, toolComp);
            Check(ReferenceEquals(toolComp.Props, toolLinks) && toolComp.Props.statOffsets[0].value == .1f,
                "A linked tool cabinet keeps its non-medical stat value");
            var vitals = new CompFacility { props = original };
            Comps(new Building { def = Tier("VitalsMonitor", 5, 2) }, vitals);
            Check(Math.Abs(vitals.StatOffsets[0].value - .12f) < .0001f,
                "The Core vitals monitor receives the same tier rules as other medical facilities");
            WorldProfile.AppliedTiers.Remove(toolVariant.defName);
            WorldProfile.AppliedTiers.Remove(receiverVariant.defName);
            refresh.Invoke(null, null);
            Check(machineLinks.linkableFacilities.SequenceEqual(new[] { originalTool })
                && toolLinks.linkableBuildings.SequenceEqual(new[] { receiver }),
                "Disabling the profile restores both original connection lists");
        }
        finally
        {
            foreach (string defName in added)
            {
                WorldProfile.AppliedTiers.Remove(defName);
                ProfileVariants.Entries.Remove(defName);
            }
            harmony.UnpatchAll(harmony.Id);
        }
    }
}
