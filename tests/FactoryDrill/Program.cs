using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Grechka.WorldIntegration;
using HarmonyLib;
using PipeSystem;
using Verse;
using Process = PipeSystem.Process;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
ModContentPack Pack(string id)
{
    var pack = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
    AccessTools.Field(typeof(ModContentPack), "packageIdPlayerFacingInt").SetValue(pack, id); return pack;
}
AccessTools.Field(typeof(Prefs), "data").SetValue(null, new PrefsData());
var factoryPath = Environment.GetEnvironmentVariable("SPECIAL_FACTORY_DLL") ?? throw new Exception("Set SPECIAL_FACTORY_DLL to the installed Factory assembly");
var factory = Assembly.LoadFrom(factoryPath);
var workerType = factory.GetType("VanillaFurnitureExpandedFactory.ResultWorker_Metals", true);
var metalsType = factory.GetType("VanillaFurnitureExpandedFactory.DrillableMetalsDef", true);
var nativeDef = (Def)Activator.CreateInstance(metalsType); nativeDef.defName = "FactoryDrillFixtureMetals";
var nativeList = new List<ThingDef>();
var core = Pack("ludeon.rimworld");
var gamePath = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"), "../../.."));
var coreXml = XDocument.Load(Path.Combine(gamePath, "Data/Core/Defs/ThingDefs_Items/Items_Resource_Stuff.xml"));
foreach (string name in new[] { "Steel", "Gold", "Silver", "Plasteel", "Uranium", "Jade" })
{
    var xml = coreXml.Root.Elements().Single(e => (string)e.Element("defName") == name);
    var def = new ThingDef { defName = name, category = ThingCategory.Item, modContentPack = core,
        deepCommonality = float.Parse(xml.Element("deepCommonality").Value), deepCountPerPortion = int.Parse(xml.Element("deepCountPerPortion").Value) };
    DefDatabase<ThingDef>.Add(def); nativeList.Add(def);
}
AccessTools.Field(metalsType, "drillableMetals").SetValue(nativeDef, nativeList);
var db = typeof(DefDatabase<>).MakeGenericType(metalsType);
db.GetMethod("Add", new[] { metalsType }).Invoke(null, new object[] { nativeDef });

var rules = new List<FactoryDrillResourceDef>();
var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
foreach (var xml in XDocument.Load(Path.Combine(root, "mods/GrechkaWorldIntegration/Defs/FactoryDrillResources.xml")).Root.Elements())
{
    var rule = new FactoryDrillResourceDef { defName = xml.Element("defName").Value, packageId = xml.Element("packageId").Value,
        thingDefName = xml.Element("thingDefName").Value, weight = float.Parse(xml.Element("weight").Value), count = int.Parse(xml.Element("count").Value) };
    Check(!rule.ConfigErrors().Any(), rule.defName + " has valid positive weight and batch size");
    var def = new ThingDef { defName = rule.thingDefName, modContentPack = Pack(rule.packageId), category = ThingCategory.Item };
    DefDatabase<ThingDef>.Add(def); DefDatabase<FactoryDrillResourceDef>.Add(rule); rules.Add(rule);
}
Check(rules.Count == 9 && rules.Select(r => r.thingDefName).Distinct().Count() == 9, "Exactly three minerals and six raw gems are configured once");
var deepBefore = nativeList.Concat(rules.Select(r => DefDatabase<ThingDef>.GetNamed(r.thingDefName)))
    .ToDictionary(d => d, d => (d.deepCommonality, d.deepCountPerPortion));
var spoof = new ThingDef { defName = "FactoryDrillSpoof", category = ThingCategory.Item, modContentPack = Pack("untrusted.fixture") };
DefDatabase<ThingDef>.Add(spoof);
DefDatabase<FactoryDrillResourceDef>.Add(new FactoryDrillResourceDef { defName = "spoof", packageId = "grechka.storage", thingDefName = spoof.defName, weight = 100000, count = 100000 });
DefDatabase<FactoryDrillResourceDef>.Add(new FactoryDrillResourceDef { defName = "missing", packageId = "grechka.storage", thingDefName = "MissingOptionalResource", weight = 100000, count = 100000 });
FactoryDrillResources.Refresh();
var harmony = new Harmony("grechka.test.factory-drill");
foreach (Type hook in new[] { typeof(FactoryDrillSelectionHook), typeof(FactoryDrillCountHook), typeof(FactoryTierCountHook) })
{
    var patched = harmony.CreateClassProcessor(hook).Patch();
    Check(patched?.Count > 0, hook.Name + " binds to the installed native library");
}
var result = new ProcessDef.Result { thing = nativeList[0], count = 1, workerClass = workerType };
var machineDef = new ThingDef { defName = "FactoryDrillFixture", comps = new() { new CompProperties { compClass = typeof(CompAdvancedResourceProcessor) } } };
var machine = new ThingWithComps { def = machineDef };
var process = (Process)RuntimeHelpers.GetUninitializedObject(typeof(Process));
AccessTools.Field(typeof(Process), "parent").SetValue(process, machine);
AccessTools.Field(typeof(Process), "def").SetValue(process, new ProcessDef { results = new() { result } });
var observed = new HashSet<ThingDef>();
Rand.PushState(3912);
try
{
    for (int i = 0; i < 20000; i++)
    {
        var chosen = result.GetOutput(process); observed.Add(chosen);
        var rule = rules.FirstOrDefault(r => r.thingDefName == chosen.defName);
        int expected = rule?.count ?? chosen.deepCountPerPortion;
        if (result.GetCount(process) != expected || expected <= 0) throw new Exception("Native cached output/count mismatch: " + chosen.defName);
    }
}
finally { Rand.PopState(); }
Check(observed.Count == 15 && !observed.Contains(spoof), "Native worker reaches all six original and nine added resources; missing and wrong-owner entries are excluded");
Check(nativeList.Count == 6 && deepBefore.All(p => p.Value == (p.Key.deepCommonality, p.Key.deepCountPerPortion)),
    "Native mineral lists, scanner weights and deep-drill deposit yields remain untouched");
ProfileVariants.Entries[machineDef.defName] = new VariantEntry { sourceName = machineDef.defName, name = machineDef.defName, tier = 5, minimum = 2 };
WorldProfile.AppliedTiers[machineDef.defName] = 5;
foreach (var rule in rules)
{
    AccessTools.Field(workerType, "cachedMetal").SetValue(result.Worker, DefDatabase<ThingDef>.GetNamed(rule.thingDefName));
    Check(result.GetCount(process) == 2 * rule.count, rule.thingDefName + " receives the existing T5 bonus once");
}
WorldProfile.AppliedTiers.Clear();
DefDatabase<FactoryDrillResourceDef>.Clear(); FactoryDrillResources.Refresh();
Rand.PushState(555);
var expectedSequence = Enumerable.Range(0, 100).Select(_ => nativeList.RandomElementByWeight(d => d.deepCommonality)).ToArray(); Rand.PopState();
Rand.PushState(555);
var actualSequence = Enumerable.Range(0, 100).Select(_ => result.GetOutput(process)).ToArray(); Rand.PopState();
Check(expectedSequence.SequenceEqual(actualSequence), "Without optional resources the native weighted sequence is unchanged");
ProductionChecks.Run(root, nativeList[0]);
Console.WriteLine("Factory drill native checks complete; no game process or save modified.");
