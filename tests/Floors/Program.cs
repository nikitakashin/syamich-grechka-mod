using System.Runtime.CompilerServices;
using System.Xml;
using Grechka.Industry;
using Grechka.WorldIntegration;
using HarmonyLib;
using RimWorld;
using Verse;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
string repo = Path.GetFullPath(args.FirstOrDefault() ?? ".");
string configRoot = Path.Combine(Path.GetTempPath(), "grechka-floors-native-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(configRoot, "Config"));
Directory.CreateDirectory(Path.Combine(configRoot, "Mods"));
Directory.CreateDirectory(Path.Combine(configRoot, "Data/Core/About"));
File.WriteAllText(Path.Combine(configRoot, "Data/Core/About/About.xml"), "<ModMetaData><name>Core</name><author>Fixture</author><packageId>ludeon.rimworld</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>");
foreach (string id in new[] { "grechka.industry", "grechka.treasures", "grechka.world.integration", "fixture.stones" })
{
    Directory.CreateDirectory(Path.Combine(configRoot, "Mods", id, "About"));
    File.WriteAllText(Path.Combine(configRoot, "Mods", id, "About/About.xml"), $"<ModMetaData><name>{id}</name><author>Fixture</author><packageId>{id}</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>");
}
File.WriteAllText(Path.Combine(configRoot, "Config/ModsConfig.xml"), "<ModsConfigData><version>1.7.4998</version><activeMods><li>ludeon.rimworld</li><li>grechka.industry</li><li>grechka.treasures</li><li>grechka.world.integration</li><li>fixture.stones</li></activeMods></ModsConfigData>");
AccessTools.Field(typeof(GenFilePaths), "saveDataPath").SetValue(null, configRoot);
AccessTools.Field(typeof(GenFilePaths), "modsFolderPath").SetValue(null, Path.Combine(configRoot, "Mods"));
AccessTools.Field(typeof(GenFilePaths), "officialModsFolderPath").SetValue(null, Path.Combine(configRoot, "Data"));
var prefs = AccessTools.Field(typeof(Prefs), "data"); prefs.SetValue(null, new PrefsData());
var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
AccessTools.Field(typeof(Game), "components").SetValue(game, new List<GameComponent>());
Current.Game = game;
var core = new DirectoryInfo(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"));
while (core != null && !Directory.Exists(Path.Combine(core.FullName, "Data/Core"))) core = core.Parent;
var pack = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
AccessTools.Field(typeof(ModContentPack), "packageIdPlayerFacingInt").SetValue(pack, "grechka.industry");
AccessTools.Field(typeof(ModContentPack), "rootDirInt").SetValue(pack, new DirectoryInfo(Path.Combine(repo,"mods/GrechkaIndustry")));
pack.assetBundles = new ModAssetBundlesHandler(pack);
AccessTools.Field(typeof(ModContentPack), "textures").SetValue(pack, new ModContentHolder<UnityEngine.Texture2D>(pack));
AccessTools.Field(typeof(ModContentPack), "audioClips").SetValue(pack, new ModContentHolder<UnityEngine.AudioClip>(pack));
AccessTools.Field(typeof(ModContentPack), "strings").SetValue(pack, new ModContentHolder<string>(pack));
pack.assemblies = new ModAssemblyHandler(pack);
pack.assemblies.loadedAssemblies.Add(typeof(SelfCleaningFloorProperties).Assembly);
LoadedModManager.RunningModsListForReading.Add(pack);
GenTypes.ClearCache();
var asset = (LoadableXmlAsset)RuntimeHelpers.GetUninitializedObject(typeof(LoadableXmlAsset));
AccessTools.Field(typeof(LoadableXmlAsset), "mod").SetValue(asset, pack);
var doc = new XmlDocument(); doc.LoadXml("<Defs/>");
var owners = new Dictionary<XmlNode, LoadableXmlAsset>();
void Import(XmlDocument source)
{
    foreach (XmlNode node in source.DocumentElement.ChildNodes)
        if (node is XmlElement) { var copy = doc.ImportNode(node, true); doc.DocumentElement.AppendChild(copy); owners[copy] = asset; }
}
var floors = new XmlDocument(); floors.Load(Path.Combine(repo, "mods/GrechkaIndustry/Defs/Terrain_Floors.xml")); Import(floors);
var native = new XmlDocument(); native.Load(Path.Combine(core.FullName, "Data/Core/Defs/ThingDefs_Misc/Various_Stone.xml")); Import(native);
var custom = new XmlDocument(); custom.LoadXml("<Defs><ThingDef Name='FixtureBlocksBase' ParentName='StoneBlocksBase' Abstract='True'/><ThingDef ParentName='FixtureBlocksBase' MayRequire='fixture.stones'><defName>FixtureBlocks</defName><label>modded blocks</label></ThingDef><ThingDef ParentName='StoneBlocksBase'><defName>FixtureMetal</defName><thingCategories Inherit='False'><li>ResourcesRaw</li></thingCategories></ThingDef></Defs>"); Import(custom);
CompositeFloorXmlHook.AddVariants(doc, owners);
var variants = doc.SelectNodes("/Defs/TerrainDef[starts-with(defName,'Grechka_CompositeFloor_')]").Cast<XmlNode>().ToList();
Check(variants.Count == 6, "All five native stone blocks and a modded inherited block receive composite variants");
Check(variants.All(n => n.SelectSingleNode("costList/Steel").InnerText == "1" && n.SelectNodes("costList/*[text()='4']").Count == 1), "Every composite tile costs exactly four selected blocks and one steel");
Check(variants.All(n => ReferenceEquals(owners[n], asset)), "Generated terrain retains Industry ownership");
Check(doc.SelectSingleNode("/Defs/TerrainDef[defName='Grechka_CompositeFloor_FixtureBlocks']/@MayRequire").Value == "fixture.stones", "Optional mod stone keeps its source dependency gate");
string once = doc.OuterXml; CompositeFloorXmlHook.AddVariants(doc, owners);
Check(once == doc.OuterXml, "Repeated XML generation preserves IDs without duplicates");

var baseFloors = new XmlDocument(); baseFloors.Load(Path.Combine(core.FullName, "Data/Core/Defs/TerrainDefs/Terrain_Floors.xml"));
XmlInheritance.Clear(); XmlInheritance.TryRegister(baseFloors.SelectSingleNode("/Defs/TerrainDef[@Name='FloorBase']"), pack);
foreach (XmlNode node in doc.SelectNodes("/Defs/TerrainDef")) XmlInheritance.TryRegister(node, pack);
XmlInheritance.Resolve();
var resolved = doc.SelectNodes("/Defs/TerrainDef[defName]").Cast<XmlNode>().Select(XmlInheritance.GetResolvedNodeFor).ToList();
foreach (string name in resolved.SelectMany(n => n.SelectNodes("statBases/*").Cast<XmlNode>()).Select(n => n.Name).Distinct()) DefDatabase<StatDef>.Add(new StatDef { defName = name });
foreach (string name in new[] { "Light", "Medium", "Heavy", "Walkable" }) DefDatabase<TerrainAffordanceDef>.Add(new TerrainAffordanceDef { defName = name });
DefDatabase<DrawStyleCategoryDef>.Add(new DrawStyleCategoryDef { defName = "Floors" });
DefDatabase<DesignationCategoryDef>.Add(new DesignationCategoryDef { defName = "Floors" });
DefDatabase<EffecterDef>.Add(new EffecterDef { defName = "ConstructDirt" });
DefDatabase<ShaderTypeDef>.Add(new ShaderTypeDef { defName = "TerrainFadeRoughLinearBurn" });
DefDatabase<DesignatorDropdownGroupDef>.Add(new DesignatorDropdownGroupDef { defName = "Grechka_CompositeFloors" });
var tab = new ResearchTabDef { defName = "Grechka_ResearchTab" };
var world = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
AccessTools.Field(typeof(ModContentPack), "packageIdPlayerFacingInt").SetValue(world, "grechka.world.integration");
var research = new ResearchProjectDef { defName = "Grechka_Research_MechaT3", tab = tab, modContentPack = world };
DefDatabase<ResearchProjectDef>.Add(research);
foreach (string name in resolved.SelectMany(n => n.SelectNodes("costList/*").Cast<XmlNode>()).Select(n => n.Name).Distinct())
    DefDatabase<ThingDef>.Add(new ThingDef { defName = name, label = name });
var terrains = resolved.Select(n => DirectXmlToObject.ObjectFromXml<TerrainDef>(n, false)).ToList();
foreach (var terrain in terrains) DefDatabase<TerrainDef>.Add(terrain);
DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
var sterile = terrains.Single(t => t.defName == "Grechka_SterileMechaniteFloor");
var palace = terrains.Single(t => t.defName == "Grechka_PalaceDiamondFloor");
Check(sterile.GetModExtension<SelfCleaningFloorProperties>()?.intervalTicks == 600
    && palace.GetModExtension<SelfCleaningFloorProperties>()?.intervalTicks == 300,
    "Native XML loader instantiates both self-cleaning extensions and their exact intervals");
float Stat(TerrainDef terrain, string name) => terrain.statBases.Single(s => s.stat.defName == name).value;
Check(terrains.All(t => t.BuildableByPlayer && t.layerable && t.pathCost == 0 && t.CostList.All(c => c.thingDef != null)), "Native TerrainDef loader resolves costs and removable buildable floors");
Check(Stat(sterile, "Beauty") == 1 && Stat(sterile, "Cleanliness") == .8f && Stat(palace, "Beauty") == 15 && Stat(palace, "Cleanliness") == 1, "Accepted beauty and inherent cleanliness load into native stats");
Check(palace.IsFine && !sterile.IsFine && terrains.Where(t => t.GetModExtension<CompositeFloorMaterial>() != null).All(t => !t.IsFine && Stat(t,"Beauty") == 3), "Actual royal fine-floor predicate recognizes only palace mosaic");
Check(palace.CostList.Single(c => c.thingDef.defName == "BlocksMarble").count == 6 && palace.CostList.Single(c => c.thingDef.defName == "GrechkaTreasures_DiamondCut").count == 1, "Palace retains agreed marble and diamond cost");
GrechkaWorldIntegrationMod.Settings = new GrechkaWorldSettings { enableResearch = false };
ResearchTree.SyncRequirements(); ResearchTree.SyncRequirements();
Check(sterile.researchPrerequisites.Count == 0 && palace.researchPrerequisites.Count == 0, "Existing no-research mode also removes floor research requirements");
GrechkaWorldIntegrationMod.Settings.enableResearch = true; ResearchTree.SyncRequirements(); ResearchTree.SyncRequirements();
Check(sterile.researchPrerequisites.SequenceEqual(new[] { research }) && palace.researchPrerequisites.SequenceEqual(new[] { research }), "Enabling research restores floor requirements once");

var harmony = new Harmony("grechka.floors.native.fixture");
harmony.CreateClassProcessor(typeof(CompositeFloorXmlHook)).Patch();
Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(LoadedModManager), "ParseAndProcessXML")).Prefixes.Any(p => p.owner == harmony.Id), "XML hook binds to installed game API");
map.terrainGrid = (TerrainGrid)RuntimeHelpers.GetUninitializedObject(typeof(TerrainGrid));
harmony.Patch(AccessTools.Method(typeof(TerrainGrid), nameof(TerrainGrid.TerrainAt), new[] { typeof(IntVec3) }), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Terrain)));
// Keep real ThinFilth/thickness semantics; replace renderer and destruction side effects only.
harmony.Patch(AccessTools.Method(typeof(Filth), "UpdateMesh"), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.NoMesh)));
harmony.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.Destroy)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Destroy)));
var dirtDef = new ThingDef { defName = "FixtureDirt", filth = new FilthProperties { cleaningWorkToReduceThickness = 100 }, comps = new() };
Filth Dirt(int x, TerrainDef terrain, int layers = 3)
{
    var dirt = new Filth { def = dirtDef, thickness = layers, Position = new IntVec3(x,0,0) };
    AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(dirt, (sbyte)0);
    Fixture.Terrains[dirt.Position] = terrain;
    return dirt;
}
var a = Dirt(0, sterile); var b = Dirt(1, palace); var normal = Dirt(2, new TerrainDef { defName = "NormalFloor" });
Check(SelfCleaningFloorMap.CleanCycle(map, new[] { a,b,normal }, 299) == 0, "No cleaning between scheduled cycles");
int firstCycle = SelfCleaningFloorMap.CleanCycle(map, new[] { a,b,normal }, 300);
Check(firstCycle == 1 && a.thickness == 3 && b.thickness == 2 && normal.thickness == 3, "Five-second cycle cleans palace only and leaves neighboring ordinary floor dirty");
Check(SelfCleaningFloorMap.CleanCycle(map, new[] { a,b,normal }, 600) == 2 && a.thickness == 2 && b.thickness == 1, "Ten-second cycle cleans both active coverings by one layer");
var sameCell = Dirt(0, sterile); int before = a.thickness + sameCell.thickness;
Check(SelfCleaningFloorMap.CleanCycle(map, new[] { a,sameCell }, 1200) == 1 && a.thickness + sameCell.thickness == before - 1, "Multiple filth types cannot multiply cleaning budget on one cell");
Fixture.Terrains[a.Position] = new TerrainDef { defName = "RemovedCovering" };
Check(SelfCleaningFloorMap.CleanCycle(map, new[] { a }, 1800) == 0, "Removing a floor immediately removes its cleaning effect");
var last = Dirt(3, palace, 1);
Check(SelfCleaningFloorMap.CleanCycle(map, new[] { last }, 2100) == 1 && last.thickness == 0 && last.Destroyed, "Native ThinFilth destroys the final layer");
var protectedDirt = Dirt(4, palace); protectedDirt.def = new ThingDef { filth = new FilthProperties { cleaningWorkToReduceThickness = 100 }, comps = new() { new CompProperties() } };
Check(SelfCleaningFloorMap.CleanCycle(map, new[] { protectedDirt }, 2400) == 0 && protectedDirt.thickness == 3, "Special comp-bearing filth is preserved");
var unspawned = Dirt(5, palace); AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(unspawned, (sbyte)-1);
Check(SelfCleaningFloorMap.CleanCycle(map, new[] { unspawned }, 2400) == 0, "Despawned or unloaded filth is not processed");
harmony.UnpatchAll(harmony.Id);
Directory.Delete(configRoot, true);
Console.WriteLine($"PASS {checks} floor checks against installed game libraries. No game was launched or controlled.");

static class Fixture
{
    public static readonly Dictionary<IntVec3,TerrainDef> Terrains = new();
    public static bool Terrain(IntVec3 c, ref TerrainDef __result) { Terrains.TryGetValue(c, out __result); return false; }
    public static bool NoMesh() => false;
    public static bool Destroy(Thing __instance)
    { AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(__instance, (sbyte)-2); return false; }
}
