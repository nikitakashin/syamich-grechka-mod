using System.Runtime.CompilerServices;
using System.Xml;
using Grechka.WorldIntegration;
using Grechka.Industry;
using HarmonyLib;
using RimWorld;
using Verse;

int checks = 0;
void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS " + text); }
LoadableXmlAsset Owner(string id)
{
    var pack = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
    AccessTools.Field(typeof(ModContentPack), "packageIdPlayerFacingInt").SetValue(pack, id);
    var asset = (LoadableXmlAsset)RuntimeHelpers.GetUninitializedObject(typeof(LoadableXmlAsset));
    AccessTools.Field(typeof(LoadableXmlAsset), "mod").SetValue(asset, pack);
    return asset;
}
string repo = Path.GetFullPath(args[0]);
var prefsField = AccessTools.Field(typeof(Prefs), "data"); prefsField.SetValue(null, Activator.CreateInstance(prefsField.FieldType));
var doc = new XmlDocument(); doc.LoadXml("<Defs/>");
var owners = new Dictionary<XmlNode, LoadableXmlAsset>();
foreach (string module in new[] { "GrechkaIndustry", "GrechkaStorage", "GrechkaEquipment", "GrechkaDrones", "GrechkaPharmacy", "GrechkaWorldIntegration" })
{
    var metadata = new XmlDocument(); metadata.Load(Path.Combine(repo, "mods", module, "About/About.xml"));
    var owner = Owner(metadata.SelectSingleNode("/ModMetaData/packageId").InnerText);
    foreach (string path in Directory.GetFiles(Path.Combine(repo, "mods", module, "Defs"), "*.xml", SearchOption.AllDirectories))
    {
        var part = new XmlDocument(); part.Load(path);
        foreach (XmlNode node in part.DocumentElement.ChildNodes)
        {
            if (node is not XmlElement) continue;
            var imported = doc.ImportNode(node, true); doc.DocumentElement.AppendChild(imported); owners[imported] = owner;
        }
    }
}
var fixture = new XmlDocument(); fixture.LoadXml("<Defs><ThingDef Name='FixtureResearchBenchBase' Abstract='True'><thingClass>Building_WorkTable</thingClass><researchPrerequisites><li>VanillaFixtureResearch</li></researchPrerequisites></ThingDef><ThingDef ParentName='FixtureResearchBenchBase'><defName>FixtureResearchBench</defName></ThingDef><Grechka.WorldIntegration.MachineProfileRuleDef><defName>FixtureResearchRule</defName><packageId>fixture.external</packageId><targetDefName>FixtureResearchBench</targetDefName><minimumTier>2</minimumTier><addHousing>true</addHousing></Grechka.WorldIntegration.MachineProfileRuleDef></Defs>");
foreach (XmlNode node in fixture.DocumentElement.ChildNodes)
{
    var imported = doc.ImportNode(node, true); doc.DocumentElement.AppendChild(imported); owners[imported] = Owner("fixture.external");
}
ProfileVariantXmlHook.Prefix(doc, owners);
var pharmacyProjects = doc.SelectNodes("/Defs/ResearchProjectDef[starts-with(defName,'GrechkaPharmacy_ResearchT')]").Cast<XmlNode>().ToList();
Check(pharmacyProjects.Count == 5 && pharmacyProjects.All(p => owners[p].mod.PackageIdPlayerFacing == "grechka.world.integration")
    && pharmacyProjects.Select(p => p.SelectSingleNode("defName").InnerText).ToHashSet().SetEquals(Enumerable.Range(1,5).Select(t => "GrechkaPharmacy_ResearchT" + t)),
    "Profile preserves all five standalone Pharmacy research IDs with one World-owned definition each");
Check(doc.SelectNodes("/Defs/RecipeDef[starts-with(defName,'GrechkaPharmacy_')]").Cast<XmlNode>()
    .All(p => p.SelectNodes("researchPrerequisites/li[starts-with(text(),'GrechkaPharmacy_ResearchT')]").Count == 1),
    "Pharmacy production recipes use one preserved research gate without duplicate prerequisites");
var foreignRecipe = doc.CreateElement("RecipeDef");
foreignRecipe.InnerXml = "<defName>FixtureForeignPharmacyRecipe</defName><researchPrerequisites><li>GrechkaPharmacy_ResearchT3</li></researchPrerequisites>";
doc.DocumentElement.AppendChild(foreignRecipe); owners[foreignRecipe] = Owner("fixture.external");
ResearchTree.ApplyXml(doc, owners);
Check(foreignRecipe.SelectSingleNode("researchPrerequisites/li").InnerText == "GrechkaPharmacy_ResearchT3",
    "Standalone research redirection never rewrites foreign package recipe requirements");
int covered = 0;
foreach (XmlNode binding in doc.SelectNodes("/Defs/Grechka.WorldIntegration.ResearchUnlockDef"))
{
    string project = binding.SelectSingleNode("research").InnerText;
    foreach (string kind in new[] { "things", "recipes" })
        foreach (XmlNode target in binding.SelectNodes(kind + "/li"))
        {
            string tag = kind == "things" ? "ThingDef" : "RecipeDef";
            var node = doc.SelectSingleNode("/Defs/" + tag + "[defName='" + target.InnerText + "']");
            if (node?.SelectSingleNode("researchPrerequisites/li[text()='" + project + "']") == null) throw new Exception("Ungated: " + target.InnerText);
            if (node.SelectSingleNode("recipeMaker") is XmlNode maker && maker.SelectSingleNode("researchPrerequisites/li[text()='" + project + "']") == null) throw new Exception("Ungated implied recipe: " + target.InnerText);
            covered++;
        }
}
Check(covered > 250, "Actual module XML: all " + covered + " catalogue targets receive native prerequisites");
Check(doc.SelectSingleNode("/Defs/ThingDef[defName='GrechkaWorld_FixtureResearchBench_T3_Building']/researchPrerequisites/li[text()='Grechka_Research_IndustryT3']") != null,
    "Generated external tier requires matching Industry research");
Check(doc.SelectSingleNode("/Defs/RecipeDef[defName='GrechkaWorld_Upgrade_FixtureResearchBench_T3']/researchPrerequisites/li[text()='Grechka_Research_IndustryT3']") != null,
    "In-place external upgrade cannot bypass tier research");
Check(doc.SelectSingleNode("/Defs/RecipeDef[defName='GrechkaWorld_Upgrade_FixtureResearchBench_T3']/researchPrerequisites/li[text()='VanillaFixtureResearch']") != null,
    "In-place upgrade of a found external machine also requires original inherited technology");
Check(doc.SelectSingleNode("/Defs/ThingDef[defName='FixtureResearchBench']/researchPrerequisites") == null
    && doc.SelectSingleNode("/Defs/ThingDef[@Name='FixtureResearchBenchBase']/researchPrerequisites/li").InnerText == "VanillaFixtureResearch",
    "Original external machine keeps its inherited research");
string before = doc.OuterXml; ResearchTree.ApplyXml(doc, owners);
Check(doc.OuterXml == before, "Repeated binding does not duplicate prerequisites");
XmlInheritance.Clear();
foreach (XmlNode node in new[] { doc.SelectSingleNode("/Defs/ThingDef[@Name='FixtureResearchBenchBase']"),
    doc.SelectSingleNode("/Defs/ThingDef[defName='FixtureResearchBench']"),
    doc.SelectSingleNode("/Defs/ThingDef[defName='GrechkaWorld_FixtureResearchBench_T3_Building']") })
    XmlInheritance.TryRegister(node, owners[node].mod);
XmlInheritance.Resolve();
var resolved = XmlInheritance.GetResolvedNodeFor(doc.SelectSingleNode("/Defs/ThingDef[defName='GrechkaWorld_FixtureResearchBench_T3_Building']"));
Check(resolved.SelectSingleNode("researchPrerequisites/li[text()='VanillaFixtureResearch']") != null
    && resolved.SelectSingleNode("researchPrerequisites/li[text()='Grechka_Research_IndustryT3']") != null,
    "Actual XML inheritance preserves both original and tier prerequisites on generated buildings");
XmlInheritance.Clear();
var targetNode = doc.SelectSingleNode("/Defs/ThingDef[defName='Grechka_AssemblyBenchT1_Building']");
targetNode.RemoveChild(targetNode.SelectSingleNode("researchPrerequisites")); owners[targetNode] = Owner("fixture.impostor");
ResearchTree.ApplyXml(doc, owners);
Check(targetNode.SelectSingleNode("researchPrerequisites") == null, "Foreign package with matching spelling cannot receive Grechka bindings");
DefDatabase<ResearchTabDef>.Add(new ResearchTabDef { defName = "Grechka_ResearchTab" });
foreach (string name in new[] { "Electricity", "Fabrication", "MicroelectronicsBasics" })
    DefDatabase<ResearchProjectDef>.Add(new ResearchProjectDef { defName = name, baseCost = 100 });
var projects = doc.SelectNodes("/Defs/ResearchProjectDef").Cast<XmlNode>()
    .Select(n => DirectXmlToObject.ObjectFromXml<ResearchProjectDef>(n, false)).ToList();
foreach (var project in projects) DefDatabase<ResearchProjectDef>.Add(project);
var parsedBindings = doc.SelectNodes("/Defs/Grechka.WorldIntegration.ResearchUnlockDef").Cast<XmlNode>()
    .Select(n => DirectXmlToObject.ObjectFromXml<ResearchUnlockDef>(n, false)).ToList();
DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
Check(projects.Count == 42 && projects.All(p => p.baseCost > 0 && p.tab?.defName == "Grechka_ResearchTab")
    && parsedBindings.All(b => b.research != null), "Native XML parser loads all research projects, tiers, tab and catalogue cross references");
AccessTools.Method(typeof(ResearchProjectDef), "GenerateNonOverlappingCoordinates").Invoke(null, null);
Check(projects.All(p => Math.Abs(p.ResearchViewX - p.researchViewX) < .001f && Math.Abs(p.ResearchViewY - p.researchViewY) < .001f)
    && projects.Max(p => p.ResearchViewX * 190 + 140 + 40) <= 1100
    && projects.Max(p => p.ResearchViewY * 100 + 56 + 40) <= 750,
    "Native coordinate relaxation retains compact non-overlapping layout within 1100x750 UI pixels");
Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
Current.Game.researchManager = new ResearchManager();
var research = DefDatabase<ResearchProjectDef>.GetNamed("Grechka_Research_MatterT4");
var productResearch = new ResearchProjectDef { defName = "FixtureProductResearch", baseCost = 200 };
DefDatabase<ResearchProjectDef>.Add(productResearch);
var progress = (Dictionary<ResearchProjectDef, float>)AccessTools.Field(typeof(ResearchManager), "progress").GetValue(Find.ResearchManager);
var product = new ThingDef { defName = "FixtureProduct", label = "product", researchPrerequisites = new() { productResearch }, recipeMaker = new RecipeMakerProperties() };
var matter = new ThingDef { defName = "Grechka_Matter", label = "matter" };
DefDatabase<ThingDef>.Add(new ThingDef { defName = "Grechka_MatterRecyclerT4_Building" });
DefDatabase<StatDef>.Add(new StatDef { defName = "Grechka_MatterFixedWorkSpeed" });
var create = AccessTools.Method(typeof(MatterRecipes), "Base");
var synth = (RecipeDef)create.Invoke(null, new object[] { "FixtureSynthesis", "synthesis", matter, 10f, product, 1, 100f, 4, false });
var recycle = (RecipeDef)create.Invoke(null, new object[] { "FixtureRecycle", "recycle", product, 1f, matter, 1, 100f, 4, true });
Check(!synth.AvailableNow && !recycle.AvailableNow, "Native RecipeDef.AvailableNow blocks unresearched matter recipes");
progress[research] = research.baseCost;
Check(recycle.AvailableNow && !synth.AvailableNow, "Matter research enables recycling; locked product still cannot be synthesized");
progress[productResearch] = productResearch.baseCost;
Check(synth.AvailableNow && ResearchTree.IsResearched(product), "Product research enables actual synthesis availability");
GrechkaWorldIntegrationMod.Settings = new GrechkaWorldSettings();
var button = new MainButtonDef { defName = "Research", buttonVisible = true, defaultHotKey = UnityEngine.KeyCode.F6 };
DefDatabase<MainButtonDef>.Add(button);
ResearchTree.SyncResearchButton();
var buttons = (MainButtonsRoot)RuntimeHelpers.GetUninitializedObject(typeof(MainButtonsRoot));
AccessTools.Field(typeof(MainButtonsRoot), "allButtonsInOrder").SetValue(buttons, new List<MainButtonDef> { button });
int VisibleCount() => (int)AccessTools.Property(typeof(MainButtonsRoot), "VisibleButtonsCount").GetValue(buttons);
Check(VisibleCount() == 0, "Default mode also removes Research from the native button layout count");
var worker = new MainButtonWorker_ToggleResearchTab { def = button };
var harmony = new Harmony("grechka.research.tests");
harmony.CreateClassProcessor(typeof(ResearchButtonVisibility)).Patch();
foreach (string name in new[] { "NewGameResearchHook", "ResearchTabGuard" })
    harmony.CreateClassProcessor(typeof(ResearchTree).Assembly.GetType("Grechka.WorldIntegration." + name)).Patch();
bool visible = true;
ResearchButtonVisibility.Postfix(worker, ref visible);
Check(!visible && !GrechkaWorldIntegrationMod.ResearchEnabled, "Default mode hides research; Harmony binds to actual visibility getter");
GrechkaWorldIntegrationMod.Settings.enableResearch = true;
ResearchTree.SyncResearchButton();
visible = true;
ResearchButtonVisibility.Postfix(worker, ref visible);
Check(visible && button.buttonVisible && VisibleCount() == 1 && button.defaultHotKey == UnityEngine.KeyCode.F6,
    "Enabled mode restores native button, layout count and preserves F6");
var tabGuard = AccessTools.Method(typeof(ResearchTree).Assembly.GetType("Grechka.WorldIntegration.ResearchTabGuard"), "Prefix");
Check((bool)tabGuard.Invoke(null, new object[] { button }), "Enabled mode permits opening research by tab or saved hotkey");
GrechkaWorldIntegrationMod.Settings.enableResearch = false;
Check(!(bool)tabGuard.Invoke(null, new object[] { button }) && research.IsFinished,
    "Default mode blocks research tab without clearing existing research progress");
GrechkaWorldIntegrationMod.Settings.enableResearch = true;
progress.Remove(productResearch);
AccessTools.Method(typeof(ResearchTree).Assembly.GetType("Grechka.WorldIntegration.NewGameResearchHook"), "Postfix").Invoke(null, null);
Check(research.IsFinished && !productResearch.IsFinished, "Enabled new-game hook preserves completed and unfinished research");
// Existing colony predates the Grechka research tree. Exercise the actual LoadedGame path:
// its research manager has no progress for newly installed projects.
var storageGate = DefDatabase<ResearchProjectDef>.GetNamed("Grechka_Research_IndustryT2");
storageGate.modContentPack = Owner("grechka.world.integration").mod;
var oldBench = new ThingDef { defName = "FixtureExistingColonyBench", researchPrerequisites = new() { storageGate } };
var oldBill = new RecipeDef { defName = "FixtureExistingColonyBill", researchPrerequisites = new() { storageGate } };
var mixed = new RecipeDef { defName = "FixtureMixedExistingColonyBill", researchPrerequisites = new() { storageGate, productResearch } };
DefDatabase<ThingDef>.Add(oldBench);
DefDatabase<RecipeDef>.Add(oldBill);
DefDatabase<RecipeDef>.Add(mixed);
// The native Architect queries DLC flags. Supply an isolated Core-only ModsConfig for that check.
var userData = Directory.CreateTempSubdirectory("grechka-architect-mode-");
var configDir = userData.CreateSubdirectory("Config");
File.WriteAllText(Path.Combine(configDir.FullName, "ModsConfig.xml"),
    "<ModsConfigData><version>1.7.4998</version><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>");
AccessTools.Field(typeof(GenFilePaths), "saveDataPath").SetValue(null, userData.FullName);
AccessTools.Field(typeof(GenFilePaths), "modsFolderPath").SetValue(null, userData.CreateSubdirectory("Mods").FullName);
var dataDir = userData.CreateSubdirectory("Data");
var coreAbout = dataDir.CreateSubdirectory("Core/About");
File.WriteAllText(Path.Combine(coreAbout.FullName, "About.xml"),
    "<ModMetaData><name>Core</name><author>Ludeon Studios</author><packageId>ludeon.rimworld</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>");
AccessTools.Field(typeof(GenFilePaths), "officialModsFolderPath").SetValue(null, dataDir.FullName);
Current.Game.storyteller = (Storyteller)RuntimeHelpers.GetUninitializedObject(typeof(Storyteller));
Current.Game.storyteller.difficulty = new Difficulty { allowTraps = true, allowTurrets = true, allowMortars = true };
oldBench.building = new BuildingProperties();
var buildCommand = (Designator_Build)RuntimeHelpers.GetUninitializedObject(typeof(Designator_Build));
AccessTools.Field(typeof(Designator_Build), "entDef").SetValue(buildCommand, oldBench);
var category = new DesignationCategoryDef { defName = "Grechka_IndustryCategory" };
AccessTools.Field(typeof(DesignationCategoryDef), "resolvedDesignators").SetValue(category, new List<Designator> { buildCommand });
harmony.CreateClassProcessor(typeof(ResearchTree).Assembly.GetType("Grechka.Shared.ArchitectEmptyCategoryVisibility", true)).Patch();
Check(!category.Visible && !buildCommand.Visible, "Workshop category is inactive when all of its buildings require unfinished research");
Check(!oldBench.IsResearchFinished && !oldBill.AvailableNow, "Enabled research gates the native build command and bill");
GrechkaWorldIntegrationMod.Settings.enableResearch = false;
new WorldProfileState(Current.Game).LoadedGame();
ResearchTree.SyncRequirements();
Check(category.Visible && buildCommand.Visible && oldBench.IsResearchFinished && oldBill.AvailableNow && !mixed.AvailableNow && !storageGate.IsFinished,
    "Default-mode load opens the new tree's buildings and bills without changing saved progress or foreign requirements");
GrechkaWorldIntegrationMod.Settings.enableResearch = true;
ResearchTree.SyncRequirements();
Check(!category.Visible && !oldBench.IsResearchFinished && !oldBill.AvailableNow && mixed.researchPrerequisites.SequenceEqual(new[] { storageGate, productResearch }),
    "Re-enabling the tree restores the exact requirements once after repeated loads");
progress[storageGate] = storageGate.baseCost;
Check(category.Visible && oldBench.IsResearchFinished && oldBill.AvailableNow && !mixed.AvailableNow,
    "A completed project unlocks just its own buildings and bills in research mode");
var treasureGate = new ResearchProjectDef { defName = "GrechkaTreasures_Jewelcraft", baseCost = 800,
    tab = new ResearchTabDef { defName = "GrechkaTreasures_ResearchTab" }, modContentPack = Owner("grechka.treasures").mod };
var impostorGate = new ResearchProjectDef { defName = "FixtureTreasureImpostor", baseCost = 800,
    tab = treasureGate.tab, modContentPack = Owner("fixture.other").mod };
DefDatabase<ResearchProjectDef>.Add(treasureGate); DefDatabase<ResearchProjectDef>.Add(impostorGate);
var treasureBench = new ThingDef { defName = "FixtureJeweler", researchPrerequisites = new() { treasureGate } };
var treasureBill = new RecipeDef { defName = "FixtureCutGem", researchPrerequisites = new() { treasureGate } };
var foreignBill = new RecipeDef { defName = "FixtureForeignGem", researchPrerequisites = new() { impostorGate } };
DefDatabase<ThingDef>.Add(treasureBench); DefDatabase<RecipeDef>.Add(treasureBill); DefDatabase<RecipeDef>.Add(foreignBill);
// Match the shipped Crafting.xml and the native Make_ recipe generator: both
// use the independent singular field, which AvailableNow checks first.
var singleBill = new RecipeDef { defName = "FixtureCutGemSingle", researchPrerequisite = treasureGate };
var singleForeignBill = new RecipeDef { defName = "FixtureForeignGemSingle", researchPrerequisite = impostorGate };
var mixedSingleBill = new RecipeDef { defName = "FixtureMixedGemSingle", researchPrerequisite = treasureGate, researchPrerequisites = new() { productResearch } };
treasureBench.recipeMaker = new RecipeMakerProperties { researchPrerequisite = treasureGate };
DefDatabase<RecipeDef>.Add(singleBill); DefDatabase<RecipeDef>.Add(singleForeignBill); DefDatabase<RecipeDef>.Add(mixedSingleBill);
Check(!singleBill.AvailableNow && !singleForeignBill.AvailableNow, "Native singular recipe requirements block unfinished gem research");
Check(!treasureBench.IsResearchFinished && !treasureBill.AvailableNow, "Portable Treasures starts gated in research mode");
GrechkaWorldIntegrationMod.Settings.enableResearch = false; ResearchTree.SyncRequirements(); ResearchTree.SyncRequirements();
Check(treasureBench.IsResearchFinished && treasureBill.AvailableNow && !foreignBill.AvailableNow && !treasureGate.IsFinished,
    "Installing Treasures in an old default-mode colony opens its bench and bills, preserves progress and foreign gates");
Check(singleBill.AvailableNow && singleBill.researchPrerequisite == null && treasureBench.recipeMaker.researchPrerequisite == null
    && !singleForeignBill.AvailableNow && !mixedSingleBill.AvailableNow,
    "Default mode opens singular cutting and implied recipe gates, preserves foreign and mixed requirements");
GrechkaWorldIntegrationMod.Settings.enableResearch = true; ResearchTree.SyncRequirements(); ResearchTree.SyncRequirements();
Check(!treasureBench.IsResearchFinished && !treasureBill.AvailableNow && treasureBill.researchPrerequisites.Count == 1,
    "Research toggle restores portable Treasures requirements exactly once");
Check(!singleBill.AvailableNow && singleBill.researchPrerequisite == treasureGate && treasureBench.recipeMaker.researchPrerequisite == treasureGate,
    "Research mode restores exact singular requirements after repeated synchronization");
var robotGate = new ResearchProjectDef { defName = "RPP_Research_Matrix_I", baseCost = 800,
    tab = new ResearchTabDef { defName = "RPP_Reasearch_Tab" }, modContentPack = Owner("alaestor.miscrobots.plusplus").mod };
var foreignRobotGate = new ResearchProjectDef { defName = "FixtureForeignRobotGate", baseCost = 800,
    tab = robotGate.tab, modContentPack = Owner("fixture.other").mod };
DefDatabase<ResearchProjectDef>.Add(robotGate); DefDatabase<ResearchProjectDef>.Add(foreignRobotGate);
var robotBill = new RecipeDef { defName = "FixtureRobotBill", researchPrerequisite = robotGate };
var foreignRobotBill = new RecipeDef { defName = "FixtureForeignRobotBill", researchPrerequisite = foreignRobotGate };
DefDatabase<RecipeDef>.Add(robotBill); DefDatabase<RecipeDef>.Add(foreignRobotBill);
Check(!robotBill.AvailableNow, "Newly installed Robots++ recipes initially respect unfinished research");
GrechkaWorldIntegrationMod.Settings.enableResearch = false; ResearchTree.SyncRequirements(); ResearchTree.SyncRequirements();
Check(robotBill.AvailableNow && !foreignRobotBill.AvailableNow && !robotGate.IsFinished,
    "No-research mode opens new Robots++ bills in old colonies without changing progress or foreign gates");
GrechkaWorldIntegrationMod.Settings.enableResearch = true; ResearchTree.SyncRequirements(); ResearchTree.SyncRequirements();
Check(!robotBill.AvailableNow && robotBill.researchPrerequisite == robotGate,
    "Research mode restores the original Robots++ requirement exactly once");

string save = Path.Combine(Path.GetTempPath(), "grechka-research-settings-" + Guid.NewGuid().ToString("N") + ".xml");
try
{
    var settings = GrechkaWorldIntegrationMod.Settings;
    Scribe.saver.InitSaving(save, "fixture"); Scribe_Deep.Look(ref settings, "settings"); Scribe.saver.FinalizeSaving();
    GrechkaWorldSettings loaded = null;
    Scribe.loader.InitLoading(save); Scribe_Deep.Look(ref loaded, "settings"); Scribe.loader.FinalizeLoading();
    Check(loaded.enableResearch, "Native Scribe retains research toggle");
}
finally { if (File.Exists(save)) File.Delete(save); }
userData.Delete(true);
Console.WriteLine("Research native checks: " + checks);
