using System.Runtime.CompilerServices;
using System.Xml;
using Grechka.Farms;
using HarmonyLib;
using RimWorld;
using Verse;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
string repo = Path.GetFullPath(args.FirstOrDefault() ?? ".");
string configRoot = Path.Combine(Path.GetTempPath(), "grechka-farms-native-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(configRoot, "Config"));
File.WriteAllText(Path.Combine(configRoot,"Config/ModsConfig.xml"), "<ModsConfigData><version>1.7.4998</version><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>");
AccessTools.Field(typeof(GenFilePaths), "saveDataPath").SetValue(null, configRoot);
Directory.CreateDirectory(Path.Combine(configRoot,"Mods"));
Directory.CreateDirectory(Path.Combine(configRoot,"Data/Core/About"));
File.WriteAllText(Path.Combine(configRoot,"Data/Core/About/About.xml"), "<ModMetaData><name>Core</name><author>Ludeon Studios</author><packageId>ludeon.rimworld</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>");
AccessTools.Field(typeof(GenFilePaths), "modsFolderPath").SetValue(null, Path.Combine(configRoot,"Mods"));
AccessTools.Field(typeof(GenFilePaths), "officialModsFolderPath").SetValue(null, Path.Combine(configRoot,"Data"));
var prefs = AccessTools.Field(typeof(Prefs), "data"); prefs.SetValue(null, new PrefsData());
var core = new DirectoryInfo(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"));
while (core != null && !Directory.Exists(Path.Combine(core.FullName, "Data/Core"))) core = core.Parent;
var xml = new XmlDocument(); xml.Load(Path.Combine(repo, "mods/GrechkaFarms/Defs/Terrain.xml"));
var floors = new XmlDocument(); floors.Load(Path.Combine(core.FullName, "Data/Core/Defs/TerrainDefs/Terrain_Floors.xml"));
var pack = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
AccessTools.Field(typeof(ModContentPack), "packageIdPlayerFacingInt").SetValue(pack, "grechka.farms");
XmlInheritance.Clear();
XmlInheritance.TryRegister(floors.SelectSingleNode("/Defs/TerrainDef[@Name='FloorBase']"), pack);
var node = xml.SelectSingleNode("/Defs/TerrainDef"); XmlInheritance.TryRegister(node, pack); XmlInheritance.Resolve();
var resolved = XmlInheritance.GetResolvedNodeFor(node);
foreach (XmlNode stat in resolved.SelectNodes("statBases/*")) DefDatabase<StatDef>.Add(new StatDef { defName = stat.Name });
foreach (string name in resolved.SelectNodes("affordances/li").Cast<XmlNode>().Select(n => n.InnerText).Distinct())
    DefDatabase<TerrainAffordanceDef>.Add(new TerrainAffordanceDef { defName = name });
DefDatabase<DrawStyleCategoryDef>.Add(new DrawStyleCategoryDef { defName = "Floors" });
DefDatabase<DesignationCategoryDef>.Add(new DesignationCategoryDef { defName = "Grechka_IndustryCategory" });
DefDatabase<EffecterDef>.Add(new EffecterDef { defName = "ConstructDirt" });
DefDatabase<ShaderTypeDef>.Add(new ShaderTypeDef { defName = "TerrainFadeRoughLinearBurn" });
var terrain = DirectXmlToObject.ObjectFromXml<TerrainDef>(resolved, false);
DefDatabase<TerrainDef>.Add(terrain);
DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
Check(terrain.fertility == 2.28f && terrain.defName == FarmFertility.TerrainDefName, "Shipping terrain loads with 228% fertility");
Check(terrain.designationCategory?.defName == "Grechka_IndustryCategory" && terrain.BuildableByPlayer, "Farm has a native build command in Grechka");
Check(terrain.constructionSkillPrerequisite == 0 && terrain.artisticSkillPrerequisite == 0 && terrain.IsResearchFinished, "No skill or research prerequisites");
Check(terrain.CostList.NullOrEmpty() && terrain.costStuffCount == 0, "No materials are required or refunded");
Check(terrain.statBases.Single(s => s.stat.defName == "WorkToBuild").value == 30f, "Quick native construction work: 30");
Check(terrain.affordances.Any(a => a?.defName == "GrowSoil") && terrain.affordances.Any(a => a?.defName == "Heavy"), "Resolved floor retains structural support and permits crops");
Check(terrain.layerable && !terrain.natural && terrain.IsSoil, "Removable constructed layer uses the soil category");
var vanilla = new TerrainDef { defName = "Soil", fertility = 1f }; DefDatabase<TerrainDef>.Add(vanilla);
var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
map.terrainGrid = (TerrainGrid)RuntimeHelpers.GetUninitializedObject(typeof(TerrainGrid));
var fertility = new FertilityGrid(map);
var fixture = new Harmony("grechka.farms.fixture");
fixture.Patch(AccessTools.Method(typeof(GridsUtility), nameof(GridsUtility.GetEdifice), new[]{typeof(IntVec3), typeof(Map)}), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.NoBuilding)));
fixture.Patch(AccessTools.Method(typeof(TerrainGrid), nameof(TerrainGrid.TerrainAt), new[]{typeof(IntVec3)}), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Terrain)));
Fixture.CurrentTerrain = terrain;
foreach (int percent in new[]{100,228,375,1000,228})
{
    GrechkaFarmsMod.Settings.fertilityPercent = percent; FarmFertility.ApplySettings();
    Check(Math.Abs(fertility.FertilityAt(IntVec3.Zero) - percent / 100f) < .00001f, "Native fertility query updates existing terrain to " + percent + "%");
    Check(vanilla.fertility == 1f, "Vanilla soil remains unchanged at " + percent + "%");
}
foreach (int value in new[]{int.MinValue, int.MaxValue})
{
    GrechkaFarmsMod.Settings.fertilityPercent = value; FarmFertility.ApplySettings();
    Check(terrain.fertility == (value < 0 ? 1f : 10f), "Out-of-range setting is bounded: " + value);
}
string path = Path.Combine(Path.GetTempPath(), "grechka-farms-settings-" + Guid.NewGuid().ToString("N") + ".xml");
try
{
    foreach (int percent in new[]{100,228,375,1000})
    {
        var saved = new FarmSettings { fertilityPercent = percent };
        Scribe.saver.InitSaving(path,"settings"); Scribe_Deep.Look(ref saved,"farms"); Scribe.saver.FinalizeSaving();
        FarmSettings loaded = null; Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded,"farms"); Scribe.loader.FinalizeLoading();
        Check(loaded.fertilityPercent == percent, "Native Scribe persists " + percent + "%");
    }
    File.WriteAllText(path, "<settings><farms Class=\"Grechka.Farms.FarmSettings\" /></settings>");
    FarmSettings legacy = null; Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref legacy,"farms"); Scribe.loader.FinalizeLoading();
    Check(legacy.fertilityPercent == 228, "Missing setting defaults to 228%");
}
finally { if (File.Exists(path)) File.Delete(path); }
// Standalone and shared category patches must be idempotent in either order.
foreach (var order in new[]{new[]{"GrechkaFarms"},new[]{"GrechkaFarms","GrechkaStorage"},new[]{"GrechkaStorage","GrechkaFarms"}})
{
    var defs = new XmlDocument(); defs.LoadXml("<Defs/>");
    foreach (string module in order.Concat(order))
    {
        var patch = new XmlDocument(); patch.Load(Path.Combine(repo,"mods",module,"Patches/ArchitectCategory.xml"));
        Check(DirectXmlToObject.ObjectFromXml<PatchOperation>(patch.SelectSingleNode("/Patch/Operation"),false).Apply(defs), "Native shared category patch: " + module);
    }
    Check(defs.SelectNodes("Defs/DesignationCategoryDef").Count == 1, "Exactly one standalone/shared category: " + string.Join(",",order));
}
fixture.UnpatchAll(fixture.Id);
Directory.Delete(configRoot, true);
Console.WriteLine($"PASS {checks} Farms checks against installed libraries. In-game construction and settings UI remain untested; game was not controlled.");
static class Fixture
{
    public static TerrainDef CurrentTerrain;
    public static bool NoBuilding(ref Building __result) { __result = null; return false; }
    public static bool Terrain(ref TerrainDef __result) { __result = CurrentTerrain; return false; }
}
