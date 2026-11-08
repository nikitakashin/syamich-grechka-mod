using System.Globalization;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

internal static class FacilityChecks
{
    public static void Run(string repo, string gameRoot)
    {
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        var xml = new XmlDocument(); xml.LoadXml("<Defs />");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "mods/GrechkaIndustry/Defs"), "*.xml", SearchOption.AllDirectories))
        {
            var source = new XmlDocument(); source.Load(file);
            foreach (XmlNode node in source.SelectNodes("/Defs/ThingDef[starts-with(defName,'Grechka_AssemblyBenchT') and contains(defName,'_Building')]"))
                xml.DocumentElement.AppendChild(xml.ImportNode(node, true));
        }
        var benches = xml.SelectNodes("/Defs/ThingDef").Cast<XmlElement>().ToList();
        Check(benches.Count == 5, "Facility regression covers the five shipping assembly benches");
        foreach (var bench in benches)
        {
            Check(bench.SelectNodes("comps/li[@Class='CompProperties_AffectedByFacilities']").Count == 1
                && bench.SelectNodes("comps/li[@Class='CompProperties_AffectedByFacilities']/linkableFacilities/li[.='ToolCabinet']").Count == 1
                && bench.SelectNodes("placeWorkers/li[.='PlaceWorker_ShowFacilitiesConnections']").Count == 1,
                bench["defName"].InnerText + " has one native facility component, tool cabinet and placement overlay");
        }
        var patch = new XmlDocument(); patch.Load(Path.Combine(repo, "mods/GrechkaIndustry/Patches/WorkbenchFacilities.xml"));
        bool Apply() => DirectXmlToObject.ObjectFromXml<PatchOperation>(patch.SelectSingleNode("/Patch/Operation"), false).Apply(xml);
        Check(Apply() && !xml.SelectNodes("//linkableFacilities/li[.='VFE_FabricationCabinet']").Cast<XmlNode>().Any(),
            "Industry alone loads without an absent optional fabrication cabinet");

        // Read installed third-party rules as evidence, without copying them into the repository.
        var workshop = Path.GetFullPath(Path.Combine(gameRoot, "../../workshop/content/294100"));
        var more = Path.Combine(workshop, "1103809207");
        var production = Path.Combine(workshop, "1880253632/1.6/Defs/Buildings_Linkables.xml");
        var facilities = new List<XmlElement>();
        void AddFacilityFile(string file, params string[] names)
        {
            var source = new XmlDocument(); source.Load(file);
            foreach (XmlElement def in source.SelectNodes("/Defs/ThingDef"))
                if (names.Contains(def["defName"]?.InnerText))
                {
                    var imported = (XmlElement)xml.ImportNode(def, true);
                    xml.DocumentElement.AppendChild(imported); facilities.Add(imported);
                }
        }
        AddFacilityFile(Path.Combine(gameRoot, "Data/Core/Defs/ThingDefs_Buildings/Buildings_Misc.xml"), "ToolCabinet");
        bool moreInstalled = File.Exists(Path.Combine(more, "Patches/zzCore.xml"));
        if (moreInstalled)
        {
            AddFacilityFile(Path.Combine(more, "Defs/Buildings/Cabinets.xml"), "ToolCabinetA", "ToolCabinetB", "ToolCabinetC");
            var source = new XmlDocument(); source.Load(Path.Combine(more, "Patches/zzCore.xml"));
            foreach (XmlNode operation in source.SelectNodes("/Patch/Operation"))
            {
                string xpath = operation["xpath"]?.InnerText;
                if (xpath?.Contains("linkableFacilities/li[text()=\"ToolCabinet\"]") == true
                    || xpath == "/Defs/ThingDef[defName=\"ToolCabinet\"]/comps/li[@Class=\"CompProperties_Facility\"]")
                    Check(DirectXmlToObject.ObjectFromXml<PatchOperation>(operation, false).Apply(xml),
                        "Installed More Linkables patch applies to shipping native connections");
            }
        }
        bool vfeInstalled = File.Exists(production);
        if (vfeInstalled) AddFacilityFile(production, "VFE_FabricationCabinet");
        Check(Apply() && Apply(), "Optional fabrication patch applies and is safe to repeat");
        var speed = new StatDef { defName = "FacilityFixture_WorkSpeed" };
        var research = new StatDef { defName = "FacilityFixture_ResearchSpeed" };
        var definitions = new Dictionary<string, ThingDef>();
        foreach (var node in facilities)
        {
            var element = node.SelectSingleNode("comps/li[@Class='CompProperties_Facility']");
            var properties = new CompProperties_Facility
            {
                maxSimultaneous = int.Parse(element["maxSimultaneous"].InnerText, CultureInfo.InvariantCulture),
                statOffsets = new()
            };
            foreach (XmlElement stat in element.SelectNodes("statOffsets/*"))
                properties.statOffsets.Add(new StatModifier { stat = stat.Name == "WorkTableWorkSpeedFactor" ? speed : research,
                    value = float.Parse(stat.InnerText, CultureInfo.InvariantCulture) });
            var def = new ThingDef { defName = node["defName"].InnerText, comps = new() { properties } };
            definitions.Add(def.defName, def); DefDatabase<ThingDef>.Add(def);
        }
        Check(definitions.ContainsKey("ToolCabinet"), "Installed native tool cabinet definition found");
        foreach (var node in benches)
        {
            string name = node["defName"].InnerText;
            var props = new CompProperties_AffectedByFacilities { linkableFacilities = node.SelectNodes("comps/li[@Class='CompProperties_AffectedByFacilities']/linkableFacilities/li")
                .Cast<XmlNode>().Select(n => definitions[n.InnerText]).ToList() };
            Check(props.linkableFacilities.Count == props.linkableFacilities.Distinct().Count(), name + " contains no duplicate facility links");
            Check(!moreInstalled || new[] { "ToolCabinetA", "ToolCabinetB", "ToolCabinetC" }.All(n => props.linkableFacilities.Contains(definitions[n])),
                name + " accepts installed Improved, Mechanic and Scientist cabinets");
            Check(!vfeInstalled || props.linkableFacilities.Contains(definitions["VFE_FabricationCabinet"]), name + " accepts installed fabrication cabinet");
            var def = new ThingDef { defName = name + "_FacilityFixture", comps = new() { props } }; DefDatabase<ThingDef>.Add(def);
            var affected = new CompAffectedByFacilities { props = props, parent = new Building_WorkTable { def = def } };
            foreach (var facility in props.linkableFacilities)
            {
                var facilityProps = facility.GetCompProperties<CompProperties_Facility>();
                facilityProps.ResolveReferences(facility);
                Check(facilityProps.linkableBuildings.Contains(def), name + " appears in native reverse links for " + facility.defName);
                var thing = new ThingWithComps { def = facility };
                AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(thing,
                    new List<ThingComp> { new CompFacility { parent = thing, props = facilityProps } });
                affected.LinkedFacilitiesListForReading.Clear(); affected.LinkedFacilitiesListForReading.Add(thing);
                float expected = facilityProps.statOffsets.Single(s => s.stat == speed).value;
                Check(Math.Abs(affected.GetStatOffset(speed) - expected) < .00001f,
                    name + " receives the actual native work-speed offset from " + facility.defName);
            }
        }
        Console.WriteLine("Facility checks use isolated native components; no game session was controlled.");
    }
}
