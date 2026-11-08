using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using AIRobot;
using HarmonyLib;
using RimWorld;
using Verse;

string repo = Path.GetFullPath(args[0]);
string root = Environment.GetEnvironmentVariable("MISC_ROBOTS_PLUS_ROOT")
    ?? Path.Combine(repo, "work/external-mods/294100/3253308913");
int checks = 0;
void Check(bool ok, string message)
{
    if (!ok) throw new Exception(message);
    checks++; Console.WriteLine("PASS " + message);
}
AccessTools.Field(typeof(Prefs), "data").SetValue(null, new PrefsData());
// Resolve legacy Unity module names through the installed 1.7 compatibility types.
AppDomain.CurrentDomain.AssemblyResolve += (_, e) => new AssemblyName(e.Name).Name.StartsWith("UnityEngine")
    ? typeof(UnityEngine.Vector3).Assembly : null;
var assembly = Assembly.LoadFrom(Path.Combine(root, "1.6/Assemblies/MiscRobotsPlusPlus.dll"));
Check(assembly.GetTypes().Length > 0 && typeof(X2_AIRobot).Assembly.GetTypes().Length > 0,
    "Workshop 3.9.3 runtime types resolve against installed RimWorld 1.7 and Misc Robots");
var metadata = new XmlDocument(); metadata.Load(Path.Combine(root, "About/About.xml"));
Check(metadata.SelectSingleNode("/ModMetaData/supportedVersions/li[text()='1.7']") != null,
    "Installed Workshop package declares 1.7 support (its latest content folder is 1.6)");
RuntimeHelpers.RunClassConstructor(assembly.GetType("MiscRobotsPlusPlus.MiscRobotsPlusPlusMod", true).TypeHandle);
Check(Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m)?.Owners.Contains("MiscRobotsPlusPlus") == true),
    "Workshop startup Harmony patches install against the actual 1.7 methods");

var doc = new XmlDocument(); doc.Load(Path.Combine(root, "1.6/Defs/RecipeDefs/RPP_Recipe_RoboticsWorkbench.xml"));
void Import(string path, string xpath = "/Defs/*")
{
    var part = new XmlDocument(); part.Load(path);
    foreach (XmlNode node in part.SelectNodes(xpath)) doc.DocumentElement.AppendChild(doc.ImportNode(node, true));
}
foreach (string file in new[] { "ThingDefs/RPP_Buildings_RechargeStation.xml", "ThingDefs_Items/RPP_Items_CraftingResources.xml", "ThingCategoryDefs/RPP_ThingCategories.xml", "RecipeDefs/RPP_Recipe_Recycling.xml" })
    Import(Path.Combine(root, "1.6/Defs", file));
var gameRoot = new DirectoryInfo(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"));
while (gameRoot != null && !Directory.Exists(Path.Combine(gameRoot.FullName, "Data/Core"))) gameRoot = gameRoot.Parent;
Import(Path.Combine(gameRoot.FullName, "Data/Core/Defs/ThingDefs_Items/Items_Resource_Base.xml"));
Import(Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("MISC_ROBOTS_ASSEMBLY")), "../Defs/ThingDefs/AIRobot_Buildings_RechargeStation.xml"), "/Defs/ThingDef[@Abstract='True']");
var housing = doc.CreateElement("ThingDef"); housing.InnerXml = "<defName>Grechka_HousingT1_Item</defName>";
doc.DocumentElement.AppendChild(housing);
var smelter = doc.CreateElement("ThingDef"); smelter.InnerXml = "<defName>ElectricSmelter</defName><recipes><li>RPP_Recycle_Matrix</li></recipes>";
doc.DocumentElement.AppendChild(smelter);
var oldBench = doc.CreateElement("ThingDef");
oldBench.InnerXml = "<defName>Table_RPP_RoboticsBench</defName><designationCategory>Production</designationCategory>";
doc.DocumentElement.AppendChild(oldBench);
for (int tier = 1; tier <= 5; tier++)
{
    var node = doc.CreateElement("ThingDef");
    node.InnerXml = "<defName>Grechka_AssemblyBenchT" + tier + "_Building</defName>";
    doc.DocumentElement.AppendChild(node);
}
var concrete = doc.SelectNodes("/Defs/RecipeDef[defName]").Cast<XmlNode>().ToList();
var baseline = concrete.ToDictionary(n => n["defName"].InnerText, n => n.OuterXml);
var patch = new XmlDocument(); patch.Load(Path.Combine(repo, "mods/GrechkaWorldIntegration/Patches/MiscRobots.xml"));
bool Apply(XmlDocument target) => DirectXmlToObject.ObjectFromXml<PatchOperation>(patch.SelectSingleNode("/Patch/Operation"), false).Apply(target);
Check(Apply(doc), "Shipping optional integration patch applies to the actual Workshop recipe XML");
string once = doc.OuterXml;
Check(Apply(doc) && doc.OuterXml == once, "Repeated application neither duplicates users nor restores the hidden bench");
Check(oldBench.SelectSingleNode("designationCategory") == null && doc.SelectSingleNode("/Defs/ThingDef[defName='Table_RPP_RoboticsBench']") != null,
    "Only the bench construction category is removed; its stable definition survives");
var absent = new XmlDocument(); absent.LoadXml("<Defs/>");
Check(Apply(absent) && absent.DocumentElement.ChildNodes.Count == 0, "Without Robots++ integration does nothing");
var standalone = new XmlDocument(); standalone.LoadXml("<Defs><ThingDef><defName>Table_RPP_RoboticsBench</defName><designationCategory>Production</designationCategory></ThingDef></Defs>");
string standaloneBefore = standalone.OuterXml;
Check(Apply(standalone) && standalone.OuterXml == standaloneBefore, "Without Industry the original Robotics bench remains buildable");
foreach (XmlNode node in concrete.Where(n => !n["defName"].InnerText.StartsWith("RPP_Production_Matrix_") && n["defName"].InnerText != "RPP_Production_Component" && n["defName"].InnerText != "RPP_Recycle_Matrix"))
{
    var copy = node.CloneNode(true); copy.RemoveChild(copy.SelectSingleNode("recipeUsers"));
    Check(copy.OuterXml == baseline[node["defName"].InnerText], "Concrete recipe IDs, product, work, skill and research preserved: " + node["defName"].InnerText);
}
XmlInheritance.Clear();
foreach (XmlNode node in doc.SelectNodes("/Defs/RecipeDef|/Defs/ThingDef|/Defs/ThingCategoryDef")) XmlInheritance.TryRegister(node, null);
XmlInheritance.Resolve();
var recipes = concrete.Select(XmlInheritance.GetResolvedNodeFor).ToList();
var benches = Enumerable.Range(1, 5).Select(t => new ThingDef {
    defName = "Grechka_AssemblyBenchT" + t + "_Building", thingClass = typeof(Building_WorkTable), recipes = new()
}).ToList();
var legacy = new ThingDef { defName = "Table_RPP_RoboticsBench", thingClass = typeof(Building_WorkTable) };
foreach (var bench in benches.Append(legacy)) DefDatabase<ThingDef>.Add(bench);
foreach (XmlNode node in recipes)
{
    var users = DirectXmlToObject.ObjectFromXml<List<ThingDef>>(node.SelectSingleNode("recipeUsers"), false);
    DefDatabase<RecipeDef>.Add(new RecipeDef { defName = node["defName"].InnerText, recipeUsers = users });
}
DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
string[] roman = { "I", "II", "III", "IV", "V" };
for (int tier = 1; tier <= 5; tier++)
{
    var actual = benches[tier - 1].AllRecipes;
    var expected = recipes.Where(n => !n["defName"].InnerText.StartsWith("RPP_Production_Matrix_") && n["defName"].InnerText != "RPP_Production_Component" && n["defName"].InnerText != "RPP_Recycle_Matrix"
        && roman.Take(tier).Any(r => n["defName"].InnerText.EndsWith("_" + r, StringComparison.Ordinal)))
        .Select(n => n["defName"].InnerText).ToHashSet();
    Check(actual.Count == actual.Distinct().Count() && actual.Select(r => r.defName).ToHashSet().SetEquals(expected),
        "Native ThingDef.AllRecipes T" + tier + " has exactly " + expected.Count + " recipes with no higher-tier leak");
}
Check(recipes.Count == 42 && legacy.AllRecipes.Count == 0, "Legacy bench has no robot, matrix or component recipes");
Check(smelter.SelectNodes("recipes/li").Count == 0, "Matrix recycling is removed from the native smelter");
var active = recipes.Where(n => n["defName"].InnerText.StartsWith("RPP_Production_")
    && n.SelectNodes("recipeUsers/li").Count > 0).ToList();
Check(active.Count == 35, "Exactly the 35 robot recipes remain craftable");
Check(recipes.Where(n => n.SelectNodes("recipeUsers/li").Count == 0).All(n =>
    DirectXmlToObject.ObjectFromXml<ResearchProjectDef>(n.SelectSingleNode("researchPrerequisite"), false) == null
    && n.SelectNodes("researchPrerequisites/li").Count == 0), "Retired matrix/component recipes cannot appear among research unlocks");
foreach (var node in active)
{
    int tier = Array.IndexOf(roman, node["defName"].InnerText.Split('_').Last()) + 1;
    string part = "Grechka_HousingT" + tier + "_Item";
    Check(node.SelectNodes("ingredients/li/filter/thingDefs/li[text()='" + part + "']").Count == 1
        && node.SelectSingleNode("ingredients/li[filter/thingDefs/li='" + part + "']/count").InnerText == "1"
        && node.SelectNodes("fixedIngredientFilter/thingDefs/li[text()='" + part + "']").Count == 1
        && !node.SelectNodes("ingredients//li|fixedIngredientFilter//li").Cast<XmlNode>().Any(n => n.InnerText.StartsWith("RPP_Matrix_")),
        "Robot requires exactly one matching housing, never a matrix: " + node["defName"].InnerText);
}
var stations = doc.SelectNodes("/Defs/ThingDef[defName][starts-with(@ParentName,'RPP_RechargeStation_')]").Cast<XmlNode>().ToList();
Check(stations.Count == 35, "Every native Robots++ station is covered");
foreach (var authored in stations)
{
    var node = XmlInheritance.GetResolvedNodeFor(authored);
    int tier = Array.IndexOf(roman, authored.Attributes["ParentName"].Value.Split('_').Last()) + 1;
    Check(node.SelectSingleNode("costList/Grechka_HousingT" + tier + "_Item")?.InnerText == "1"
        && (tier == 1 || node.SelectSingleNode("robotRepairCosts/Grechka_HousingT" + (tier - 1) + "_Item")?.InnerText == "1"),
        "Station cost/repair uses the correct housing tier: " + node["defName"].InnerText);
    Check(!node.SelectNodes("costList/*|robotRepairCosts/*").Cast<XmlNode>().Any(n => n.Name.StartsWith("RPP_Matrix_")),
        "Build/salvage and repair costs contain no matrices: " + node["defName"].InnerText);
    Check(DirectXmlToObject.ObjectFromXml<RecipeMakerProperties>(node.SelectSingleNode("recipeMaker"), false) == null,
        "No implied FabricationBench recipe bypasses the owned benches: " + node["defName"].InnerText);
}
var items = doc.SelectNodes("/Defs/ThingDef[defName='RPP_Component' or starts-with(defName,'RPP_Matrix_')]").Cast<XmlNode>().Select(XmlInheritance.GetResolvedNodeFor).ToList();
Check(items.Count == 6 && items.All(n => n["tradeability"].InnerText == "None"
    && n.SelectNodes("tradeTags/li|thingCategories/li|thingSetMakerTags/li").Count == 0 && n["resourceReadoutPriority"].InnerText == "Uncounted"),
    "Legacy matrices/component stay loadable but have no trading, item-filter or reward tags");


XmlInheritance.Clear();
Console.WriteLine("PASS " + checks + " Misc Robots++ checks. No running game or save was controlled.");
