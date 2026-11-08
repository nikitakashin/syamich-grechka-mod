using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Industry
{
    public sealed class SelfCleaningFloorProperties : DefModExtension
    {
        public int intervalTicks = 600;
        public override IEnumerable<string> ConfigErrors()
        {
            if (intervalTicks != 300 && intervalTicks != 600)
                yield return "Self-cleaning floors must use a 300 or 600 tick interval.";
        }
    }

    public sealed class CompositeFloorMaterial : DefModExtension
    {
        public ThingDef stoneBlocks;
    }

    // Native terrain frames retain an exact material cost. Every installed type of
    // stone block gets a stable terrain ID under the same construction dropdown.
    [HarmonyPatch(typeof(LoadedModManager), "ParseAndProcessXML")]
    public static class CompositeFloorXmlHook
    {
        public static void Prefix(XmlDocument xmlDoc, Dictionary<XmlNode, LoadableXmlAsset> assetlookup)
        { AddVariants(xmlDoc, assetlookup); }

        public static void AddVariants(XmlDocument document, Dictionary<XmlNode, LoadableXmlAsset> owners)
        {
            XmlElement root = document.DocumentElement;
            XmlNode template = root.SelectSingleNode("TerrainDef[@Name='Grechka_CompositeFloorBase']");
            if (template == null || !owners.TryGetValue(template, out LoadableXmlAsset owner)
                || !string.Equals(owner.mod?.PackageIdPlayerFacing, "grechka.industry", StringComparison.OrdinalIgnoreCase)) return;
            var parents = root.ChildNodes.Cast<XmlNode>().Where(n => n.Attributes?["Name"] != null)
                .GroupBy(n => n.Attributes["Name"].Value).ToDictionary(g => g.Key, g => g.Last());
            bool IsStoneBlock(XmlNode node)
            {
                var seen = new HashSet<XmlNode>();
                while (node != null && seen.Add(node))
                {
                    XmlNode categories = node.SelectSingleNode("thingCategories");
                    if (categories?.SelectSingleNode("li[text()='StoneBlocks']") != null) return true;
                    if (string.Equals(categories?.Attributes?["Inherit"]?.Value, "false", StringComparison.OrdinalIgnoreCase)) return false;
                    string name = node.Attributes?["ParentName"]?.Value;
                    node = name != null && parents.TryGetValue(name, out XmlNode parent) ? parent : null;
                }
                return false;
            }
            foreach (XmlNode block in root.SelectNodes("ThingDef[defName]").Cast<XmlNode>().Where(n =>
                !string.Equals(n.Attributes?["Abstract"]?.Value, "true", StringComparison.OrdinalIgnoreCase) && IsStoneBlock(n)).ToList())
            {
                string material = block.SelectSingleNode("defName").InnerText;
                string name = "Grechka_CompositeFloor_" + material;
                if (root.SelectSingleNode("TerrainDef[defName='" + name + "']") != null) continue;
                XmlElement terrain = document.CreateElement("TerrainDef");
                terrain.SetAttribute("ParentName", "Grechka_CompositeFloorBase");
                foreach (string requirement in new[] { "MayRequire", "MayRequireAnyOf" })
                    if (block.Attributes?[requirement] != null)
                        terrain.SetAttribute(requirement, block.Attributes[requirement].Value);
                void Field(string field, string value)
                { XmlElement element = document.CreateElement(field); element.InnerText = value; terrain.AppendChild(element); }
                Field("defName", name);
                Field("label", "polished composite (" + (block.SelectSingleNode("label")?.InnerText ?? material) + ")");
                var costs = document.CreateElement("costList");
                foreach (var cost in new[] { (material, "4"), ("Steel", "1") })
                { var entry = document.CreateElement(cost.Item1); entry.InnerText = cost.Item2; costs.AppendChild(entry); }
                terrain.AppendChild(costs);
                var extensions = document.CreateElement("modExtensions");
                var extension = document.CreateElement("li"); extension.SetAttribute("Class", typeof(CompositeFloorMaterial).FullName);
                var resource = document.CreateElement("stoneBlocks"); resource.InnerText = material;
                extension.AppendChild(resource); extensions.AppendChild(extension); terrain.AppendChild(extensions);
                root.AppendChild(terrain); owners[terrain] = owner;
            }
        }
    }

    [StaticConstructorOnStartup]
    public static class CompositeFloorLabels
    {
        static CompositeFloorLabels()
        {
            foreach (TerrainDef terrain in DefDatabase<TerrainDef>.AllDefsListForReading)
            {
                ThingDef material = terrain.GetModExtension<CompositeFloorMaterial>()?.stoneBlocks;
                if (material != null)
                {
                    terrain.label = "GrechkaIndustry_CompositeFloorMaterial".Translate(material.label);
                    terrain.description = "GrechkaIndustry_CompositeFloorDescription".Translate();
                }
            }
        }
    }

    public sealed class SelfCleaningFloorMap : MapComponent
    {
        public SelfCleaningFloorMap(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 300 != 0) return;
            CleanCycle(map, map.listerThings.ThingsInGroup(ThingRequestGroup.Filth).OfType<Filth>().ToArray(), now);
        }

        public static int CleanCycle(Map map, IEnumerable<Filth> filth, int now)
        {
            if (now % 300 != 0) return 0;
            int count = 0;
            var cleanedCells = new HashSet<IntVec3>();
            foreach (Filth dirt in filth)
            {
                if (dirt == null || !dirt.Spawned || dirt.Destroyed || dirt.Map != map
                    || dirt.GetType() != typeof(Filth) || dirt.def.filth == null
                    || dirt.def.filth.cleaningWorkToReduceThickness <= 0
                    || !dirt.def.comps.NullOrEmpty() || cleanedCells.Contains(dirt.Position)) continue;
                var properties = map.terrainGrid.TerrainAt(dirt.Position)?.GetModExtension<SelfCleaningFloorProperties>();
                if (properties == null || properties.intervalTicks <= 0 || now % properties.intervalTicks != 0) continue;
                cleanedCells.Add(dirt.Position);
                dirt.ThinFilth();
                count++;
            }
            return count;
        }
    }
}
