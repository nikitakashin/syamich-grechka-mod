using System.Runtime.CompilerServices;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}

var repo = Path.GetFullPath(args.FirstOrDefault() ?? ".");
var moduleRoots = new[] { "GrechkaIndustry", "GrechkaStorage" };
var coreDir = new DirectoryInfo(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR")!);
while (coreDir != null && !Directory.Exists(Path.Combine(coreDir.FullName, "Data", "Core"))) coreDir = coreDir.Parent;
if (coreDir == null) throw new DirectoryNotFoundException("Cannot locate installed Data/Core near RIMWORLD_MANAGED_DIR");
var core = new XmlDocument();
core.Load(Path.Combine(coreDir.FullName, "Data", "Core", "Defs", "WorkGiverDefs", "WorkGivers.xml"));

// Apply the shipping patch with the native PatchOperation and resolve exactly the targets it produces.
var prefs = AccessTools.Field(typeof(Prefs), "data");
prefs.SetValue(null, Activator.CreateInstance(prefs.FieldType));
var tables = new Dictionary<string, ThingDef>();
var directRecipes = new Dictionary<string, List<string>>();
var recipeUsers = new Dictionary<string, HashSet<string>>();
var recipes = new Dictionary<string, RecipeDef>();
RecipeDef Recipe(string name)
{
    if (!recipes.TryGetValue(name, out var recipe))
    {
        recipe = new RecipeDef { defName = name, label = name, fixedIngredientFilter = new(), defaultIngredientFilter = new() };
        recipes.Add(name, recipe);
        DefDatabase<RecipeDef>.Add(recipe);
    }
    return recipe;
}
foreach (var module in moduleRoots)
{
    var names = new HashSet<string>();
    foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "mods", module, "Defs"), "*.xml", SearchOption.AllDirectories))
    {
        var defs = new XmlDocument(); defs.Load(file);
        foreach (XmlElement authored in defs.SelectNodes("/Defs/RecipeDef[defName]"))
        {
            var name = authored["defName"].InnerText;
            recipeUsers[name] = authored.SelectNodes("recipeUsers/li").Cast<XmlNode>().Select(x => x.InnerText).ToHashSet();
        }
        foreach (XmlElement element in defs.SelectNodes("/Defs/ThingDef[thingClass='Building_WorkTable' or @ParentName='BenchBase']"))
        {
            var name = element["defName"].InnerText;
            tables[name] = new ThingDef { defName = name, label = name, category = ThingCategory.Building,
                thingClass = typeof(Building_WorkTable), building = new BuildingProperties(), comps = new(), size = new IntVec2(1, 1) };
            directRecipes[name] = element.SelectNodes("recipes/li").Cast<XmlNode>().Select(n => n.InnerText).ToList();
            DefDatabase<ThingDef>.Add(tables[name]);
            names.Add(name);
        }
    }
    var patch = new XmlDocument(); patch.Load(Path.Combine(repo, "mods", module, "Patches", "PawnWorktables.xml"));
    var patchNode = patch.SelectSingleNode("/Patch/Operation");
    var added = patchNode.SelectNodes("value/li").Cast<XmlElement>().Select(n => n.InnerText).ToList();
    Check(added.Count == names.Count && names.SetEquals(added), module + " registers every owned worktable exactly once");
    var op = DirectXmlToObject.ObjectFromXml<PatchOperation>(patchNode, false);
    Check(op.Apply(core), module + " patch applies to the installed Core fabrication work giver");
}

var node = (XmlElement)core.SelectSingleNode("/Defs/WorkGiverDef[defName='DoBillsFabricationBench']");
Check(node["workType"].InnerText == "Smithing" && node["giverClass"].InnerText == "WorkGiver_DoBill", "Original smithing work type and scanner retained");
var fixedNames = node.SelectNodes("fixedBillGiverDefs/li").Cast<XmlElement>().Select(n => n.InnerText).ToList();
var vanilla = new ThingDef { defName = "FabricationBench", category = ThingCategory.Building, thingClass = typeof(Building_WorkTable), building = new() };
Check(fixedNames.Contains(vanilla.defName) && fixedNames.Count == fixedNames.Distinct().Count(), "Vanilla registration survives both module patches");
var workGiver = new WorkGiver_DoBill { def = new WorkGiverDef { fixedBillGiverDefs = fixedNames.Select(n => n == vanilla.defName ? vanilla : tables[n]).ToList() } };
var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
map.listerThings = new ListerThings(ListerThingsUse.Global);
var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
AccessTools.Field(typeof(Game), "uniqueIDsManager").SetValue(game, new UniqueIDsManager());
AccessTools.Field(typeof(Game), "components").SetValue(game, new List<GameComponent>());
Current.Game = game;
var pawn = new Pawn();
AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)0);
var scannerShouldSkip = AccessTools.Method(typeof(FloatMenuOptionProvider_WorkGivers), "ScannerShouldSkip");
var usable = AccessTools.Method(typeof(WorkGiver_DoBill), "ThingIsUsableBillGiver");
var repeat = new BillRepeatModeDef { defName = "RepeatCount", label = "count" };
DefDatabase<BillRepeatModeDef>.Add(repeat); BillRepeatModeDefOf.RepeatCount = repeat;
foreach (var table in tables.Values) table.recipes = directRecipes[table.defName].Select(Recipe).ToList();
foreach (var (name, users) in recipeUsers)
{
    var actual = users.Where(tables.ContainsKey).Select(n => tables[n]).ToList();
    if (actual.Count != 0) Recipe(name).recipeUsers = actual;
}
// The two T5 matter benches intentionally have zero static recipes. Run the production recipe builder.
var fixedSpeed = new StatDef { defName = "Grechka_MatterFixedWorkSpeed" };
DefDatabase<StatDef>.Add(fixedSpeed);
var testMaterial = new ThingDef { defName = "PawnWorkMaterial", label = "material", category = ThingCategory.Item };
var matter = new ThingDef { defName = "Grechka_Matter", label = "matter", category = ThingCategory.Item };
var makeMatterRecipe = AccessTools.Method(typeof(Grechka.Industry.MatterRecipes), "Base");
foreach (var tier in new[] { 4, 5 })
    foreach (var recycle in new[] { true, false })
    {
        var name = $"PawnWork_{tier}_{recycle}";
        var batch = (RecipeDef)makeMatterRecipe.Invoke(null, new object[] { name, name, recycle ? testMaterial : matter,
            1f, recycle ? matter : testMaterial, 1, 100f, tier, recycle });
        DefDatabase<RecipeDef>.Add(batch);
    }
var recipe = Recipe("Grechka_MakeComponentBatchT1");

// A real BillStack, saved before the registration changes, reloads against the same recipe identity.
var sample = new Building_WorkTable { def = tables["Grechka_AssemblyBenchT1_Building"] };
var stack = new BillStack(sample);
stack.AddBill(new Bill_Production { recipe = recipe, ingredientFilter = new ThingFilter(), repeatMode = repeat, repeatCount = 7, RenamableLabel = "previous save" });
var save = Path.Combine(Path.GetTempPath(), "grechka-pawn-work-" + Guid.NewGuid().ToString("N") + ".xml");
BillStack restored = null;
try
{
    Scribe.saver.InitSaving(save, "fixture"); Scribe_Deep.Look(ref stack, "bills", sample); Scribe.saver.FinalizeSaving();
    Scribe.loader.InitLoading(save); Scribe_Deep.Look(ref restored, "bills", sample); Scribe.loader.FinalizeLoading();
}
finally { if (File.Exists(save)) File.Delete(save); }
var existing = (Bill_Production)restored.Bills.Single();
Check(existing.recipe == recipe && existing.repeatCount == 7 && existing.RenamableLabel == "previous save",
    "Pre-existing production bill round-trips with its recipe, repeat count and custom name");

var basicHousing = Recipe("Grechka_MakeHousingT1");
for (int tier = 1; tier <= 5; tier++)
{
    var bench = tables[$"Grechka_AssemblyBenchT{tier}_Building"];
    Check(bench.AllRecipes.Count(r => r == basicHousing) == 1,
        $"T{tier} assembly bench exposes exactly one basic housing bill");
}

foreach (var table in tables.Values)
{
    var thing = new Building_WorkTable { def = table };
    var tableBill = table == sample.def ? existing : new Bill_Production { recipe = table.AllRecipes.First(), repeatMode = repeat, repeatCount = 1 };
    Check(table.AllRecipes.Contains(tableBill.recipe), table.defName + " exposes its assigned recipe through native AllRecipes");
    thing.BillStack.AddBill(tableBill);
    var candidates = map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver);
    candidates.Clear(); candidates.Add(thing);
    Check((bool)usable.Invoke(workGiver, new object[] { thing }) && workGiver.PotentialWorkThingRequest.Accepts(thing)
        && !workGiver.ShouldSkip(pawn) && !(bool)scannerShouldSkip.Invoke(null, new object[] { pawn, workGiver, thing }),
        table.defName + " passes native AI discovery and the right-click menu scanner with an active bill");
    tableBill.suspended = true;
    Check(workGiver.ShouldSkip(pawn), table.defName + " respects suspended work");
    tableBill.suspended = false;
    thing.BillStack.Bills.Clear();
}
var unregistered = new Building_WorkTable { def = new ThingDef { defName = "UnrelatedBench", thingClass = typeof(Building_WorkTable) } };
Check(!(bool)usable.Invoke(workGiver, new object[] { unregistered }), "An unrelated table is never accepted by the expanded fabrication giver");

FacilityChecks.Run(repo, coreDir.FullName);
