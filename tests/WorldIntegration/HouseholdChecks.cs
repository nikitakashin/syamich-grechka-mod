using System.Runtime.CompilerServices;
using System.Xml;
using Grechka.WorldIntegration;
using HarmonyLib;
using RimWorld;
using Verse;

internal static class HouseholdChecks
{
    private static void Check(bool ok, string label)
    { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }

    internal static void Run(ModContentPack core, ModContentPack industry)
    {
        string[] sources = {
            "Cooler", "Heater", "PassiveCooler", "Cooler_Over", "Cooler_Over2W", "VFEC_HeatStones", "VFEC_LargePassiveCooler",
            "sbz_DoubleChestFridge", "sbz_DoubleWallFridge", "sbz_Fridge", "sbz_LargeFridge", "sbz_SingleChestFridge", "sbz_SingleWallFridge",
            "FlatscreenTelevision", "FloodLight", "MegascreenTelevision", "StandingLamp", "TubeTelevision", "WallLamp",
            "Autobong", "Loudspeaker", "SleepAccelerator", "Chemlamp", "ChemlampPost", "VFEC_Thermaebath",
            "Joy_Arcade", "Joy_InudstrialComputer", "Joy_ModernComputer", "Light_Streetlamp", "Radio_Industrial", "Radio_Spacer",
            "VFE_DeskLamp", "VFE_FloorLamp", "VFE_RoyalLamp", "Shelf_RepairRack", "Table_IlluminatedDresser", "Table_IlluminatedEndTable",
            "Table_interactive_1x1c", "Table_interactive_2x2c", "UltrascreenTV", "VFES_AirPurifier", "VFES_Table_IlluminatedWardrobe",
            "VFES_TelevisionSpeaker", "VFES_WallMountedTelevision"
        };
        var rules = new XmlDocument(); rules.Load("mods/GrechkaWorldIntegration/Defs/MachineProfile.xml");
        var document = new XmlDocument(); document.LoadXml("<Defs/>");
        var owners = new Dictionary<XmlNode, LoadableXmlAsset>();
        XmlNode Add(string xml, ModContentPack pack)
        {
            var source = new XmlDocument(); source.LoadXml(xml);
            XmlNode node = document.ImportNode(source.DocumentElement, true); document.DocumentElement.AppendChild(node);
            var asset = (LoadableXmlAsset)RuntimeHelpers.GetUninitializedObject(typeof(LoadableXmlAsset));
            AccessTools.Field(typeof(LoadableXmlAsset), "mod").SetValue(asset, pack); owners[node] = asset;
            return node;
        }
        for (int tier = 1; tier <= 5; tier++)
        {
            Add("<ThingDef><defName>Grechka_HousingT" + tier + "_Item</defName></ThingDef>", industry);
            Add("<ResearchProjectDef><defName>Grechka_Research_IndustryT" + tier + "</defName></ResearchProjectDef>", core);
        }
        var steel = DefDatabase<ThingDef>.GetNamed("Steel");
        var retired = new List<MachineProfileRuleDef>();
        foreach (string source in sources)
        {
            XmlNode xml = rules.DocumentElement.SelectSingleNode("Grechka.WorldIntegration.MachineProfileRuleDef[targetDefName='" + source + "']");
            Check(xml?.SelectSingleNode("retired")?.InnerText == "true", source + " has no active tiers or housings");
            var pack = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
            AccessTools.Field(typeof(ModContentPack), "packageIdPlayerFacingInt").SetValue(pack, xml.SelectSingleNode("packageId").InnerText);
            // Exercise native inheritance with an unchanged source component and footprint.
            Add("<ThingDef><defName>" + source + "</defName><label>" + source + "</label><thingClass>Building</thingClass>"
                + "<size>(2,1)</size><designationCategory>Joy</designationCategory><comps><li Class='CompProperties_Flickable'/></comps>"
                + "<costList><Steel>20</Steel></costList></ThingDef>", pack);
            Add(xml.OuterXml, PackIntegration());
            var original = MakeDef(source, pack, steel);
            var rule = new MachineProfileRuleDef { defName = xml.SelectSingleNode("defName").InnerText,
                packageId = pack.PackageIdPlayerFacing, targetDefName = source,
                minimumTier = int.Parse(xml.SelectSingleNode("minimumTier").InnerText), addHousing = true, retired = true };
            DefDatabase<MachineProfileRuleDef>.Add(rule); retired.Add(rule);
        }
        ProfileVariantXmlHook.EntriesFromXml(document, owners);
        ResearchTree.ApplyXml(document, owners);
        XmlInheritance.Clear();
        foreach (XmlNode node in document.DocumentElement.ChildNodes) XmlInheritance.TryRegister(node, owners[node].mod);
        XmlInheritance.Resolve();
        int count = 0;
        foreach (var rule in retired)
        {
            var original = DefDatabase<ThingDef>.GetNamed(rule.targetDefName);
            XmlNode source = document.DocumentElement.SelectSingleNode("ThingDef[defName='" + rule.targetDefName + "']");
            Check(ProfileVariants.Entry(original) == null && source.SelectSingleNode("designatorDropdown") == null
                && source.SelectNodes("comps/li").Count == 1,
                rule.targetDefName + " has ordinary construction and no tier/upgrade components");
            for (int tier = rule.minimumTier + 1; tier <= 5; tier++)
            {
                string name = "GrechkaWorld_" + rule.targetDefName + "_T" + tier + "_Building";
                XmlNode clone = document.DocumentElement.SelectSingleNode("ThingDef[defName='" + name + "']");
                XmlNode resolved = XmlInheritance.GetResolvedNodeFor(clone);
                Check(resolved.SelectSingleNode("thingClass").InnerText == "Building"
                    && resolved.SelectSingleNode("size").InnerText == "(2,1)"
                    && resolved.SelectNodes("comps/li").Count == 1
                    && resolved.SelectSingleNode("label").InnerText == original.label
                    && resolved.SelectSingleNode("researchPrerequisites") == null,
                    name + " retains its historical identity and source behavior without a tier label or research gate");
                var legacy = MakeDef(name, original.modContentPack, steel);
                var designator = (Designator_Build)RuntimeHelpers.GetUninitializedObject(typeof(Designator_Build));
                AccessTools.Field(typeof(Designator_Build), "entDef").SetValue(designator, legacy);
                bool visible = true; AcceptanceReport placement = true;
                ProfileVariantVisibilityHook.Postfix(designator, ref visible);
                Check(!visible && !ProfileVariantPlacementHook.Prefix(designator, ref placement) && !placement.Accepted,
                    name + " is hidden and cannot be built through a retained build command");
                count++;
            }
        }
        Check(count == 127 && ProfileVariants.Entries.Count == count, "All 127 historical household variant names remain available");
        Check(document.SelectNodes("//RecipeDef|//DesignatorDropdownGroupDef").Count == 0, "Retired household families emit no upgrades or tier dropdowns");
        WorldProfile.Capture();
        var state = new WorldProfileState(null) { applied = true };
        state.approvedMachines.AddRange(retired.Select(r => r.Signature));
        state.approvedMachines.AddRange(ProfileVariants.Entries.Values.Select(e => retired.Single(r => r.defName == e.ruleName).Signature + "/variantT" + e.tier));
        foreach (bool reapprove in new[] { false, true })
        {
            WorldProfile.Apply(state, reapprove);
            Check(retired.All(r => WorldProfile.GetTier(new Building { def = DefDatabase<ThingDef>.GetNamed(r.targetDefName) }) == 0)
                && sources.Concat(ProfileVariants.Entries.Keys).All(n => DefDatabase<ThingDef>.GetNamed(n).costList.All(c => c.thingDef == steel))
                && ProfileVariants.Entries.Keys.All(n => !WorldProfile.AppliedTiers.ContainsKey(n)),
                "Old approvals and reapplication cannot restore household tiers or housing costs (reapprove=" + reapprove + ")");
        }
        RoundTrip();
        WorldProfile.RestoreBaseline();
    }

    private static ModContentPack PackIntegration()
    {
        var pack = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        AccessTools.Field(typeof(ModContentPack), "packageIdPlayerFacingInt").SetValue(pack, "grechka.world.integration");
        return pack;
    }

    private static ThingDef MakeDef(string name, ModContentPack pack, ThingDef steel)
    {
        var def = new ThingDef { defName = name, label = name, modContentPack = pack, thingClass = typeof(Building),
            category = ThingCategory.Building, stackLimit = 1, building = new BuildingProperties(),
            costList = new() { new(steel, 20) }, comps = new() { new CompProperties { compClass = typeof(CompQuality) } } };
        DefDatabase<ThingDef>.Add(def); return def;
    }

    private static void RoundTrip()
    {
        // Real Scribe/ThingWithComps serialization, using stand-ins for external mod components.
        // This checks save identities and native item state, not a full live-colony load.
        var legacy = DefDatabase<ThingDef>.GetNamed("GrechkaWorld_Table_IlluminatedDresser_T4_Building");
        var saved = new Building { def = legacy, thingIDNumber = 921345 };
        AccessTools.Method(typeof(ThingWithComps), "InitializeComps").Invoke(saved, null);
        AccessTools.Field(typeof(Thing), "hitPointsInt").SetValue(saved, 71);
        AccessTools.Field(typeof(Thing), "stuffInt").SetValue(saved, DefDatabase<ThingDef>.GetNamed("Steel"));
        AccessTools.Field(typeof(CompQuality), "qualityInt").SetValue(saved.GetComp<CompQuality>(), QualityCategory.Excellent);
        string path = Path.Combine(Path.GetTempPath(), "grechka-household-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            Scribe.saver.InitSaving(path, "householdFixture"); Scribe_Deep.Look(ref saved, "building"); Scribe.saver.FinalizeSaving();
            Building loaded = null;
            Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "building"); Scribe.loader.FinalizeLoading();
            Check(loaded.def == legacy && loaded.thingIDNumber == 921345 && loaded.HitPoints == 71
                && loaded.Stuff == DefDatabase<ThingDef>.GetNamed("Steel") && loaded.GetComp<CompQuality>().Quality == QualityCategory.Excellent,
                "Real Scribe restores the old T4 dresser's Def, ThingID, HP, material and quality");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
