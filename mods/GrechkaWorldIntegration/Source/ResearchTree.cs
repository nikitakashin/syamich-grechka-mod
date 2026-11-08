using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.WorldIntegration
{
    public sealed class ResearchUnlockDef : Def
    {
        public string packageId;
        public ResearchProjectDef research;
        public List<string> things = new List<string>();
        public List<string> recipes = new List<string>();
    }

    // Before inheritance and implied Make_ recipes: vanilla owns availability and unlock UI.
    // Catalogue names are explicit, package-checked, and optional modules may be absent.
    public static class ResearchTree
    {
        private static bool? originalButtonVisibility;
        public static void SyncResearchButton()
        {
            MainButtonDef button = DefDatabase<MainButtonDef>.GetNamedSilentFail("Research");
            if (button == null) return;
            if (!originalButtonVisibility.HasValue) originalButtonVisibility = button.buttonVisible;
            // MainButtonsRoot.VisibleButtonsCount reads this field directly, bypassing Worker.Visible.
            button.buttonVisible = originalButtonVisibility.Value && GrechkaWorldIntegrationMod.ResearchEnabled;
        }

        public static void ApplyXml(XmlDocument document, Dictionary<XmlNode, LoadableXmlAsset> owners)
        {
            XmlElement root = document.DocumentElement;
            var nodes = root.ChildNodes.Cast<XmlNode>().Where(n => n.SelectSingleNode("defName") != null)
                .GroupBy(n => n.Name + "/" + n.SelectSingleNode("defName").InnerText)
                .ToDictionary(g => g.Key, g => g.ToList());
            var named = root.ChildNodes.Cast<XmlNode>().Where(n => n.Attributes?["Name"] != null)
                .GroupBy(n => n.Attributes["Name"].Value).ToDictionary(g => g.Key, g => g.Last());
            bool HasInherited(XmlNode node, string field)
            {
                var seen = new HashSet<XmlNode>();
                while (node != null && seen.Add(node))
                {
                    if (node.SelectSingleNode(field) != null) return true;
                    string parent = node.Attributes?["ParentName"]?.Value;
                    node = parent != null && named.TryGetValue(parent, out XmlNode value) ? value : null;
                }
                return false;
            }
            IEnumerable<string> Requirements(XmlNode node)
            {
                var seen = new HashSet<XmlNode>();
                while (node != null && seen.Add(node))
                {
                    XmlNode list = node.SelectSingleNode("researchPrerequisites");
                    if (list != null)
                    {
                        foreach (XmlNode item in list.SelectNodes("li")) yield return item.InnerText;
                        if (string.Equals(list.Attributes?["Inherit"]?.Value, "false", StringComparison.OrdinalIgnoreCase)) yield break;
                    }
                    string parent = node.Attributes?["ParentName"]?.Value;
                    node = parent != null && named.TryGetValue(parent, out XmlNode value) ? value : null;
                }
            }
            bool Owned(XmlNode node, string package) => owners.TryGetValue(node, out LoadableXmlAsset asset)
                && string.Equals(asset.mod?.PackageIdPlayerFacing, package, StringComparison.OrdinalIgnoreCase);
            // Pharmacy is portable and has its own Main-tab research. In this profile
            // its production is governed by the Grechka branch, without double gates.
            // World projects reuse the same five IDs so saved research progress remains
            // resolvable when a standalone colony adopts or leaves this profile.
            bool PharmacyResearch(string name) => name != null
                && Enumerable.Range(1, 5).Any(tier => name == "GrechkaPharmacy_ResearchT" + tier);
            foreach (XmlNode pharmacy in root.ChildNodes.Cast<XmlNode>().Where(n => Owned(n, "grechka.pharmacy")).ToList())
            {
                foreach (XmlNode requirement in pharmacy.SelectNodes("researchPrerequisites/li|recipeMaker/researchPrerequisites/li")
                    .Cast<XmlNode>().Where(n => PharmacyResearch(n.InnerText)).ToList())
                    requirement.ParentNode.RemoveChild(requirement);
                if (pharmacy.Name == "ResearchProjectDef" && PharmacyResearch(pharmacy.SelectSingleNode("defName")?.InnerText))
                    root.RemoveChild(pharmacy);
            }
            foreach (XmlNode binding in root.SelectNodes("Grechka.WorldIntegration.ResearchUnlockDef"))
            {
                if (!Owned(binding, "grechka.world.integration")) continue;
                string project = binding.SelectSingleNode("research")?.InnerText;
                string package = binding.SelectSingleNode("packageId")?.InnerText;
                if (project == null || !nodes.ContainsKey("ResearchProjectDef/" + project)) continue;
                foreach (string kind in new[] { "things", "recipes" })
                    foreach (XmlNode name in binding.SelectNodes(kind + "/li"))
                    {
                        string tag = kind == "things" ? "ThingDef" : "RecipeDef";
                        if (!nodes.TryGetValue(tag + "/" + name.InnerText, out var targets)) continue;
                        foreach (XmlNode target in targets.Where(n => Owned(n, package)))
                        {
                            AddRequirement(target, project);
                            if (kind == "things" && HasInherited(target, "recipeMaker"))
                            {
                                XmlNode maker = target.SelectSingleNode("recipeMaker") ?? target.AppendChild(document.CreateElement("recipeMaker"));
                                AddRequirement(maker, project);
                            }
                        }
                    }
            }
            // Foreign source machines retain their original research. New tiers also require
            // the matching Industry level; no requirements are added to the source itself.
            foreach (VariantEntry entry in ProfileVariants.Entries.Values.Where(e => e.name != e.sourceName))
            {
                string project = "Grechka_Research_IndustryT" + entry.tier;
                if (!nodes.ContainsKey("ResearchProjectDef/" + project)) continue;
                if (nodes.TryGetValue("ThingDef/" + entry.name, out var variants))
                    foreach (XmlNode variant in variants) AddRequirement(variant, project);
                string upgrade = "GrechkaWorld_Upgrade_" + entry.sourceName + "_T" + entry.tier;
                if (nodes.TryGetValue("RecipeDef/" + upgrade, out var upgrades))
                    foreach (XmlNode recipe in upgrades)
                    {
                        AddRequirement(recipe, project);
                        if (nodes.TryGetValue("ThingDef/" + entry.sourceName, out var sources))
                            foreach (string prerequisite in Requirements(sources[0])) AddRequirement(recipe, prerequisite);
                    }
            }
        }

        private static void AddRequirement(XmlNode node, string project)
        {
            XmlNode list = node.SelectSingleNode("researchPrerequisites") ?? node.AppendChild(node.OwnerDocument.CreateElement("researchPrerequisites"));
            if (list.ChildNodes.Cast<XmlNode>().Any(n => n.InnerText == project)) return;
            XmlNode item = list.AppendChild(node.OwnerDocument.CreateElement("li"));
            item.InnerText = project;
        }

        // Native build commands, bills and tier-upgrade adapters all consult these same lists.
        // Keep the saved ResearchManager progress intact when the optional tree is switched off.
        private static readonly List<(List<ResearchProjectDef> list, int index, ResearchProjectDef project)> suspended = new();
        private static readonly List<(Action<ResearchProjectDef> restore, ResearchProjectDef project)> suspendedSingles = new();

        public static void SyncRequirements()
        {
            if (GrechkaWorldIntegrationMod.ResearchEnabled)
            {
                for (int i = suspended.Count - 1; i >= 0; i--)
                {
                    var (list, index, project) = suspended[i];
                    if (!list.Contains(project)) list.Insert(Math.Min(index, list.Count), project);
                }
                suspended.Clear();
                foreach (var (restore, project) in suspendedSingles) restore(project);
                suspendedSingles.Clear();
                return;
            }

            // Called after generation of implied Make_ and matter recipes, and whenever the setting changes.
            foreach (ThingDef thing in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                SuspendTreeRequirements(thing.researchPrerequisites);
                SuspendTreeRequirements(thing.recipeMaker?.researchPrerequisites);
                if (thing.recipeMaker != null)
                    SuspendSingleRequirement(thing.recipeMaker.researchPrerequisite, project => thing.recipeMaker.researchPrerequisite = project);
            }
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                SuspendTreeRequirements(recipe.researchPrerequisites);
                SuspendSingleRequirement(recipe.researchPrerequisite, project => recipe.researchPrerequisite = project);
            }
            foreach (TerrainDef terrain in DefDatabase<TerrainDef>.AllDefsListForReading)
                SuspendTreeRequirements(terrain.researchPrerequisites);
        }

        private static bool IsTreeRequirement(ResearchProjectDef project)
        {
            bool worldProject = project?.tab?.defName == "Grechka_ResearchTab"
                && MaterialDictionary.BelongsTo(project, "grechka.world.integration");
            // Portable Treasures has its own tab and saved research IDs.
            bool treasureProject = project?.tab?.defName == "GrechkaTreasures_ResearchTab"
                && MaterialDictionary.BelongsTo(project, "grechka.treasures");
            // A newly installed Robots++ must also work in an existing no-research colony.
            bool robotsProject = project?.tab?.defName == "RPP_Reasearch_Tab"
                && MaterialDictionary.BelongsTo(project, "alaestor.miscrobots.plusplus");
            return worldProject || treasureProject || robotsProject;
        }

        private static void SuspendSingleRequirement(ResearchProjectDef project, Action<ResearchProjectDef> set)
        {
            // AvailableNow checks this legacy field independently of the list.
            // RecipeDefGenerator also copies it from RecipeMakerProperties.
            if (!IsTreeRequirement(project)) return;
            suspendedSingles.Add((set, project));
            set(null);
        }

        private static void SuspendTreeRequirements(List<ResearchProjectDef> requirements)
        {
            if (requirements == null) return;
            for (int i = requirements.Count - 1; i >= 0; i--)
            {
                ResearchProjectDef project = requirements[i];
                if (!IsTreeRequirement(project)) continue;
                suspended.Add((requirements, i, project));
                requirements.RemoveAt(i);
            }
        }

        public static bool IsResearched(ThingDef def) => def != null
            && (def.researchPrerequisites == null || def.researchPrerequisites.All(r => r.IsFinished));
    }

    [StaticConstructorOnStartup]
    internal static class ResearchButtonStartup
    {
        static ResearchButtonStartup() { ResearchTree.SyncResearchButton(); }
    }

    [HarmonyPatch(typeof(MainButtonWorker), "get_Visible")]
    public static class ResearchButtonVisibility
    {
        public static void Postfix(MainButtonWorker __instance, ref bool __result)
        {
            if (__instance.def?.defName == "Research" && !GrechkaWorldIntegrationMod.ResearchEnabled) __result = false;
        }
    }
}
