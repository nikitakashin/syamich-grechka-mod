using System.Runtime.CompilerServices;
using System.Xml;
using Grechka.WorldIntegration;
using HarmonyLib;
using RimWorld;
using Verse;

internal static class HopperChecks
{
    private static void Check(bool ok, string label)
    { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }

    internal static void Run(ModContentPack core, ModContentPack industry)
    {
        var rules = new XmlDocument(); rules.Load("mods/GrechkaWorldIntegration/Defs/MachineProfile.xml");
        var document = new XmlDocument(); document.LoadXml("<Defs/>");
        var owners = new Dictionary<XmlNode, LoadableXmlAsset>();
        XmlNode Add(string xml, ModContentPack pack)
        {
            var source = new XmlDocument(); source.LoadXml(xml);
            var node = document.ImportNode(source.DocumentElement, true); document.DocumentElement.AppendChild(node);
            var owner = (LoadableXmlAsset)RuntimeHelpers.GetUninitializedObject(typeof(LoadableXmlAsset));
            AccessTools.Field(typeof(LoadableXmlAsset), "mod").SetValue(owner, pack); owners[node] = owner;
            return node;
        }
        for (int tier = 1; tier <= 5; tier++)
            Add("<ThingDef><defName>Grechka_HousingT" + tier + "_Item</defName></ThingDef>", industry);
        var steel = DefDatabase<ThingDef>.GetNamed("Steel");
        var retiredRules = new List<MachineProfileRuleDef>();
        foreach (string name in new[] { "Hopper", "VFEFactory_FactoryHopper" })
        {
            var xml = rules.DocumentElement.SelectSingleNode("Grechka.WorldIntegration.MachineProfileRuleDef[targetDefName='" + name + "']");
            Check(xml.SelectSingleNode("retired")?.InnerText == "true", name + " is explicitly retired without removing its stable rule ID");
            var pack = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
            AccessTools.Field(typeof(ModContentPack), "packageIdPlayerFacingInt").SetValue(pack, xml.SelectSingleNode("packageId").InnerText);
            Add("<ThingDef><defName>" + name + "</defName><label>hopper</label><thingClass>Building_Storage</thingClass><building><isHopper>true</isHopper></building><costList><Steel>20</Steel></costList></ThingDef>", pack);
            Add(xml.OuterXml, core);
            var def = new ThingDef { defName = name, modContentPack = pack, thingClass = typeof(Building_Storage),
                category = ThingCategory.Building, costList = new() { new(steel, 20) } };
            DefDatabase<ThingDef>.Add(def);
            var rule = new MachineProfileRuleDef { defName = xml.SelectSingleNode("defName").InnerText,
                packageId = pack.PackageIdPlayerFacing, targetDefName = name, minimumTier = 2, addHousing = true, retired = true };
            DefDatabase<MachineProfileRuleDef>.Add(rule); retiredRules.Add(rule);
        }
        ProfileVariantXmlHook.EntriesFromXml(document, owners);
        XmlInheritance.Clear();
        foreach (XmlNode node in document.DocumentElement.ChildNodes) XmlInheritance.TryRegister(node, owners[node].mod);
        XmlInheritance.Resolve();
        foreach (var rule in retiredRules)
        {
            string source = rule.targetDefName;
            var original = document.DocumentElement.SelectSingleNode("ThingDef[defName='" + source + "']");
            Check(ProfileVariants.Entry(DefDatabase<ThingDef>.GetNamed(source)) == null
                && original.SelectSingleNode("designatorDropdown") == null && original.SelectSingleNode("comps") == null,
                source + " keeps ordinary construction with no tier dropdown or upgrade components");
            for (int tier = 3; tier <= 5; tier++)
            {
                string name = "GrechkaWorld_" + source + "_T" + tier + "_Building";
                var clone = document.DocumentElement.SelectSingleNode("ThingDef[defName='" + name + "']");
                var resolved = XmlInheritance.GetResolvedNodeFor(clone);
                Check(resolved.SelectSingleNode("thingClass").InnerText == "Building_Storage"
                    && resolved.SelectSingleNode("building/isHopper").InnerText == "true",
                    name + " remains loadable with native hopper behavior");
                var legacy = new ThingDef { defName = name, thingClass = typeof(Building_Storage), modContentPack = DefDatabase<ThingDef>.GetNamed(source).modContentPack };
                DefDatabase<ThingDef>.Add(legacy);
                bool visible = true;
                var designator = (Designator_Build)RuntimeHelpers.GetUninitializedObject(typeof(Designator_Build));
                AccessTools.Field(typeof(Designator_Build), "entDef").SetValue(designator, legacy);
                ProfileVariantVisibilityHook.Postfix(designator, ref visible);
                AcceptanceReport placement = true;
                Check(!visible && !ProfileVariantPlacementHook.Prefix(designator, ref placement) && !placement.Accepted,
                    name + " cannot be offered or placed as a new tier");
            }
        }
        WorldProfile.Capture();
        var state = new WorldProfileState(null) { applied = true };
        state.approvedMachines.AddRange(retiredRules.Select(r => r.Signature));
        WorldProfile.Apply(state, false);
        Check(retiredRules.All(r => !WorldProfile.IsApplied(DefDatabase<ThingDef>.GetNamed(r.targetDefName))
            && DefDatabase<ThingDef>.GetNamed(r.targetDefName).costList.Single().thingDef == steel),
            "Old colony approvals cannot restore hopper tiers or housing costs");
        WorldProfile.Apply(state, true);
        Check(retiredRules.All(r => !state.approvedMachines.Contains(r.Signature))
            && ProfileVariants.Entries.Keys.All(n => !WorldProfile.AppliedTiers.ContainsKey(n)),
            "Reapplying the profile leaves all legacy hopper tiers inactive");
        WorldProfile.RestoreBaseline();
    }
}
