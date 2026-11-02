using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.Storage;
using Grechka.Quests;
using HarmonyLib;
using RimWorld;
using Verse;
using Quest = Grechka.Quests.Quest;

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    Console.WriteLine("PASS " + label);
}
var harmony = new Harmony("grechka.isolated.storage-items");
var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map>());
AccessTools.Field(typeof(Game), "components").SetValue(game, new List<GameComponent> { new StorageDiskClock(game) });
AccessTools.Field(typeof(Game), "tickManager").SetValue(game, new TickManager());
AccessTools.Field(typeof(Game), "uniqueIDsManager").SetValue(game, new UniqueIDsManager());
Current.Game = game;
var prefsField = AccessTools.Field(typeof(Prefs), "data");
prefsField.SetValue(null, Activator.CreateInstance(prefsField.FieldType));
foreach (Type hook in typeof(StorageDisk).Assembly.GetTypes().Where(t => t.Name.EndsWith("Hook")))
{
    harmony.CreateClassProcessor(hook).Patch();
    bool bound = Harmony.GetAllPatchedMethods().Any(m =>
    {
        var info = Harmony.GetPatchInfo(m);
        return info.Prefixes.Concat(info.Postfixes).Any(p => p.owner == harmony.Id && p.PatchMethod.DeclaringType == hook);
    });
    Check(bound, "Game hook bound: " + hook.Name);
}
// The fixture supplies construction, ambient temperature and a no-relic world. Splitting, ownership,
// comp serialization, rotting and storage transfers use the real game methods.
harmony.Patch(AccessTools.Method(typeof(ThingMaker), nameof(ThingMaker.MakeThing)),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.MakeThing)));
harmony.Patch(AccessTools.Method(typeof(ThingOwnerUtility), nameof(ThingOwnerUtility.TryGetFixedTemperature)),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Temperature)));
harmony.Patch(AccessTools.Method(typeof(ReliquaryUtility), nameof(ReliquaryUtility.IsRelic)),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.NoRelics)));

var steelDef = Fixture.Def("Grechka_TestSteel", typeof(ThingWithComps), 75);
var mealDef = Fixture.Def("Grechka_TestMeal", typeof(ThingWithComps), 10,
    new CompProperties_Rottable { daysToRotStart = 10, rotDestroys = true });
var armorDef = Fixture.Def("Grechka_TestArmor", typeof(ThingWithComps), 1, new CompProperties { compClass = typeof(CompQuality) });
armorDef.useHitPoints = true;
var metal = new StuffCategoryDef { defName = "Grechka_TestMetal", label = "test metal" };
DefDatabase<StuffCategoryDef>.Add(metal);
steelDef.stuffProps = new StuffProperties { categories = new List<StuffCategoryDef> { metal } };
armorDef.stuffCategories = new List<StuffCategoryDef> { metal };
var normalDef = Fixture.DiskDef("Grechka_TestDisk", 1024, false);
var frozenDef = Fixture.DiskDef("Grechka_TestFrozenDisk", 1024, true);
var largeDef = Fixture.DiskDef("Grechka_TestLargeDisk", 65536, false);
var holder = new TestHolder(30);
var disk = (StorageDisk)Fixture.Create(normalDef);
var cold = (StorageDisk)Fixture.Create(frozenDef);
var large = (StorageDisk)Fixture.Create(largeDef);
holder.Contents.TryAdd(disk, false); holder.Contents.TryAdd(cold, false); holder.Contents.TryAdd(large, false);
Thing steel = Fixture.Create(steelDef, 8000);
holder.Contents.TryAdd(steel, false);
Check(disk.Accept(steel, 8000) == 8000 && disk.Space.ItemCount == 8000 && !holder.Contents.Contains(steel)
    && ReferenceEquals(steel.holdingOwner.Owner, disk), "Whole transfer changes owner once without recreating the resource");
Thing extra = Fixture.Create(steelDef, 300); holder.Contents.TryAdd(extra, false);
Check(disk.Accept(extra, 300) == 128 && extra.stackCount == 172 && disk.Space.ItemCount == 8128,
    "A partially full disk transfers only its real remaining capacity");
Check(disk.Withdraw(steel, 75, holder.Contents) == 75 && disk.Space.ItemCount == 8053
    && holder.Contents.Where(t => t.def == steelDef).Sum(t => (long)t.stackCount) == 247,
    "Withdrawal preserves the remainder and total quantity across real owners");
Thing meal = Fixture.Create(mealDef, 5); holder.Contents.TryAdd(meal, false);
var rottable = ((ThingWithComps)meal).GetComp<CompRottable>();
AccessTools.Field(typeof(CompRottable), "rotProgressInt").SetValue(rottable, 1234f);
Check(cold.Accept(meal, 5) == 5 && Math.Abs(meal.AmbientTemperature + 10) < 0.001f,
    "A portable freezing disk supplies subzero temperature through the real holder chain");
rottable.CompTickInterval(6000);
Check(Math.Abs((float)AccessTools.Field(typeof(CompRottable), "rotProgressInt").GetValue(rottable) - 1234f) < 0.001f,
    "Frozen contents do not rot when vanilla or an outside caller requests a tick");
Thing armor = Fixture.Create(armorDef); armor.HitPoints = 71;
AccessTools.Field(typeof(Thing), "stuffInt").SetValue(armor, steelDef);
AccessTools.Field(typeof(CompQuality), "qualityInt").SetValue(((ThingWithComps)armor).GetComp<CompQuality>(), QualityCategory.Excellent);
holder.Contents.TryAdd(armor, false);
Check(large.Accept(armor, 1) == 1, "Nonstackable equipment enters a disk as its original instance");

string path = Path.Combine(Path.GetTempPath(), "grechka-storage-roundtrip-" + Guid.NewGuid().ToString("N") + ".xml");
try
{
    StorageDisk savedCold = cold, savedGear = large;
    Scribe.saver.InitSaving(path, "storageFixture");
    Scribe_Deep.Look(ref savedCold, "cold"); Scribe_Deep.Look(ref savedGear, "gear");
    Scribe.saver.FinalizeSaving();
    StorageDisk loadedCold = null, loadedGear = null;
    Scribe.loader.InitLoading(path);
    Scribe_Deep.Look(ref loadedCold, "cold"); Scribe_Deep.Look(ref loadedGear, "gear");
    Scribe.loader.FinalizeLoading();
    Thing restoredMeal = loadedCold.Items.Single(), restoredArmor = loadedGear.Items.Single();
    Check(loadedCold.Space.ItemCount == 5 && restoredMeal.def == mealDef
        && Math.Abs((float)AccessTools.Field(typeof(CompRottable), "rotProgressInt").GetValue(((ThingWithComps)restoredMeal).GetComp<CompRottable>()) - 1234f) < 0.001f,
        "Real Scribe round trip preserves contained quantity, definition and freshness");
    Check(restoredArmor.HitPoints == 71 && restoredArmor.Stuff == steelDef && ((ThingWithComps)restoredArmor).GetComp<CompQuality>().Quality == QualityCategory.Excellent
        && ReferenceEquals(restoredArmor.holdingOwner.Owner, loadedGear),
        "Real Scribe restores equipment material, quality, HP and a single owning disk");
}
finally { if (File.Exists(path)) File.Delete(path); }

var rackDef = Fixture.Def("Grechka_TestRack", typeof(Building_DiskRack), 1);
rackDef.category = ThingCategory.Building; rackDef.building = new BuildingProperties();
var rack = (Building_DiskRack)Fixture.Create(rackDef);
Check(rack.Install(cold) && rack.Disks.Count() == 1 && cold.holdingOwner.Owner == rack
    && rack.Remove(cold, holder.Contents) && holder.Contents.Contains(cold),
    "Installing and removing a loaded disk preserve its physical contents");
DamageInfo damage = default;
rack.PreApplyDamage(ref damage, out bool absorbed);
Check(absorbed, "Disk rack absorbs incoming damage before destruction");
var cableDef = Fixture.Def("Grechka_TestCable", typeof(Building_QuartzCable), 1);
var cable = (Building_QuartzCable)Fixture.Create(cableDef);
cable.PreApplyDamage(ref damage, out absorbed);
Check(absorbed, "Underground signal cable absorbs all incoming damage");
var packedDef = Fixture.Def("Grechka_TestPacked", typeof(MinifiedStorageDevice), 1);
var packed = (MinifiedStorageDevice)Fixture.Create(packedDef); packed.InnerThing = rack;
packed.PreApplyDamage(ref damage, out absorbed);
Check(absorbed, "Uninstalled disk rack remains protected inside its minified wrapper");

var api = typeof(StorageDisk).Assembly.GetType("Grechka.Storage.StorageInventory");
var adapter = typeof(QuestGame).Assembly.GetType("Grechka.Quests.StorageInventoryAdapter");
var read = (IEnumerable<Thing>)AccessTools.Method(adapter, "Held").Invoke(null, new object[] { cold });
Check(read.Single() == meal, "Quests optional adapter reads the original stored item through its delegate");
var pack = new QuestPack { id = "storage", quests = new List<Quest> { new Quest { id = "q", objectives = new List<Objective>
    { new Objective { id = "have", type = "have", target = mealDef.defName, count = 20 } } } } };
var tally = new ObjectiveTally(pack);
foreach (Thing item in read.Concat(read)) tally.Add(item, "have", item.def.defName, null, null, item.stackCount);
Check(tally.Value(pack, pack.quests[0], pack.quests[0].objectives[0]) == 5,
    "Repeated storage enumeration cannot double-count a possession objective");

Thing normalMeal = Fixture.Create(mealDef, 2); holder.Contents.TryAdd(normalMeal, false);
large.Accept(normalMeal, 2);
CompRottable normalRot = ((ThingWithComps)normalMeal).GetComp<CompRottable>();
AccessTools.Method(typeof(StorageDisk), "AdvanceRot").Invoke(large, new object[] { 6000 });
float normalAge = (float)AccessTools.Field(typeof(CompRottable), "rotProgressInt").GetValue(normalRot);
Check(normalAge > 0, "Ordinary disk contents accrue real vanilla rot at their carrier's ambient temperature");
normalRot.CompTickInterval(6000);
Check((float)AccessTools.Field(typeof(CompRottable), "rotProgressInt").GetValue(normalRot) == normalAge,
    "An outside container tick cannot age disk contents a second time");
large.Withdraw(normalMeal, 2, holder.Contents);
Check((float)AccessTools.Field(typeof(CompRottable), "rotProgressInt").GetValue(normalRot) == normalAge,
    "Extraction retains accumulated spoilage rather than resetting freshness");

harmony.Patch(AccessTools.Method(typeof(Building_StorageController), nameof(Building_StorageController.Online)),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Online)));
harmony.Patch(AccessTools.Method(typeof(Building_StorageController), nameof(Building_StorageController.ActiveDisks)),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.ActiveDisks)));
harmony.Patch(AccessTools.Method(typeof(StorageDisk), "AcceptWithResult"),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.TransferFault)));
harmony.Patch(AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) }),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.ExpectedError)));
var controllerDef = Fixture.Def("Grechka_TestController", typeof(Building_StorageController), 1);
controllerDef.category = ThingCategory.Building; controllerDef.building = new BuildingProperties();
var controller = (Building_StorageController)Fixture.Create(controllerDef);
Fixture.Networks[controller] = new List<StorageDisk> { disk, cold, large };
// The native Thing overload consults installed DLC via the graphics host.
// This fixture supplies its def-only eligibility, while allocation, value
// conversion, bills and all ownership are still the real implementations.
harmony.Patch(AccessTools.Method(typeof(ThingFilter), nameof(ThingFilter.Allows), new[] { typeof(Thing) }),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.FilterDef)));
// Real ingredient filters/value getters: generic and specific inputs cannot
// reserve the same copper twice, and a mixed provider group stays mixed.
var copperA = Fixture.Def("Grechka_TestCopperA", typeof(ThingWithComps), 75);
var copperB = Fixture.Def("Grechka_TestCopperB", typeof(ThingWithComps), 75);
var craftDisk = (StorageDisk)Fixture.Create(normalDef);
var craftController = (Building_StorageController)Fixture.Create(controllerDef);
Fixture.Networks[craftController] = new List<StorageDisk> { craftDisk };
foreach (var pair in new[] { (copperA, 3), (copperB, 4) })
{ Thing t = Fixture.Create(pair.Item1, pair.Item2); holder.Contents.TryAdd(t, false); craftDisk.Accept(t, t.stackCount); }
var benchDef = Fixture.Def("Grechka_TestBench", typeof(Building_WorkTable), 1);
benchDef.category = ThingCategory.Building; benchDef.building = new BuildingProperties();
var bench = (Building_WorkTable)Fixture.Create(benchDef);
var anyCopper = new IngredientCount(); anyCopper.SetBaseCount(4);
anyCopper.filter.SetAllow(copperA, true); anyCopper.filter.SetAllow(copperB, true);
var onlyA = new IngredientCount(); onlyA.SetBaseCount(3); onlyA.filter.SetAllow(copperA, true);
var craftRecipe = new RecipeDef { defName = "Grechka_TestMixedCopper", label = "mixed copper", workerClass = typeof(RecipeWorker),
    ingredients = new List<IngredientCount> { anyCopper, onlyA }, products = new List<ThingDefCountClass> { new ThingDefCountClass(steelDef, 1) },
    fixedIngredientFilter = new ThingFilter(), defaultIngredientFilter = new ThingFilter() };
AccessTools.Field(typeof(RecipeDef), "ingredientValueGetterClass").SetValue(craftRecipe, typeof(IngredientValueGetter_Volume));
craftRecipe.fixedIngredientFilter.SetAllow(copperA, true); craftRecipe.fixedIngredientFilter.SetAllow(copperB, true);
var selectIngredients = AccessTools.Method(typeof(StorageDisk).Assembly.GetType("Grechka.Storage.CraftRecipes"), "Select");
object[] craftArgs = { craftController, bench, craftRecipe, null, null };
var selected = (List<(StorageDisk disk, Thing item, int count)>)selectIngredients.Invoke(null, craftArgs);
Check(selected != null && selected.Sum(x => x.count) == 7 && selected.Where(x => x.item.def == copperA).Sum(x => x.count) == 3
    && craftDisk.Space.ItemCount == 7, "Craft planning combines equivalent materials without double reservation or consuming stock");
anyCopper.SetBaseCount(5); craftArgs = new object[] { craftController, bench, craftRecipe, null, null };
Check(selectIngredients.Invoke(null, craftArgs) == null && craftArgs[3] != null && craftDisk.Space.ItemCount == 7,
    "Insufficient mixed input reports the actual shortage and leaves ownership unchanged");
craftDisk.SealedContents = true;
Check(!StorageInventory.HeldContents(craftDisk).Any(), "Sealed trader disk does not expose its contents through the inventory adapter");
holder.Contents.TryAdd(craftDisk, false);
Check(rack.Install(craftDisk) && !craftDisk.SealedContents && StorageInventory.HeldContents(craftDisk).Sum(t => t.stackCount) == 7,
    "Installing a purchased disk reveals its original stored items exactly once");
Thing bulk = Fixture.Create(steelDef, 100000); holder.Contents.TryAdd(bulk, false); large.Accept(bulk, 100000);
string optimized = StorageOptimization.Optimize(controller);
Check(!controller.HasRecovery && disk.Items.Contains(armor) && large.Items.Where(t => t.def == steelDef).Sum(t => (long)t.stackCount) == 108053
    && cold.Items.Single() == meal && armor.HitPoints == 71 && armor.Stuff == steelDef,
    "Actual optimization relocates original objects, preserves cold and leaves no staged residue");
var beforeFailure = new[] { disk, cold, large }.SelectMany(d => d.Items.Select(t => (Thing: t, Disk: d, Count: t.stackCount))).ToList();
Fixture.TransferCalls = 0; Fixture.FailTransfer = true;
string rolledBack = StorageOptimization.Optimize(controller);
Fixture.FailTransfer = false;
Check(!controller.HasRecovery && beforeFailure.All(v => ReferenceEquals(v.Thing.holdingOwner.Owner, v.Disk) && v.Thing.stackCount == v.Count)
    && Fixture.ExpectedErrors == 1,
    "An injected transfer failure restores every original owner and quantity without discarding staged things");

// Save a complete physical installation, including a recovery state and a referenced hauling order.
var savedController = (Building_StorageController)Fixture.Create(controllerDef);
savedController.Output.Contents.TryAdd(Fixture.Create(steelDef, 75), false);
savedController.ColdRecovery.Contents.TryAdd(Fixture.Create(mealDef, 3), false);
Thing savedSource = Fixture.Create(steelDef, 20);
savedController.Queue(savedSource, 12, StorageOrderKind.Import);
var savedRack = (Building_DiskRack)Fixture.Create(rackDef);
var savedRackDisk = (StorageDisk)Fixture.Create(frozenDef);
holder.Contents.TryAdd(savedRackDisk, false);
Thing savedFood = Fixture.Create(mealDef, 4); holder.Contents.TryAdd(savedFood, false);
savedRackDisk.Accept(savedFood, 4); savedRack.Install(savedRackDisk);
path = Path.Combine(Path.GetTempPath(), "grechka-storage-installation-" + Guid.NewGuid().ToString("N") + ".xml");
try
{
    Scribe.saver.InitSaving(path, "installation");
    Scribe_Deep.Look(ref savedController, "controller"); Scribe_Deep.Look(ref savedRack, "rack"); Scribe_Deep.Look(ref savedSource, "source");
    Scribe.saver.FinalizeSaving();
    Building_StorageController loadedController = null; Building_DiskRack loadedRack = null; Thing loadedSource = null;
    Scribe.loader.InitLoading(path);
    Scribe_Deep.Look(ref loadedController, "controller"); Scribe_Deep.Look(ref loadedRack, "rack"); Scribe_Deep.Look(ref loadedSource, "source");
    Scribe.loader.FinalizeLoading();
    Thing recovered = loadedController.ColdRecovery.Contents.Single();
    Check(loadedController.Output.Contents.Single().stackCount == 75 && recovered.stackCount == 3
        && ReferenceEquals(recovered.holdingOwner.Owner, loadedController.ColdRecovery)
        && Math.Abs(recovered.AmbientTemperature + 10) < 0.001f,
        "Controller output and cold recovery survive real Scribe with correct ownership and freezing");
    Check(loadedController.Orders.Single().Source == loadedSource && loadedController.Orders.Single().Count == 12
        && loadedRack.Disks.Single().Space.ItemCount == 4
        && ReferenceEquals(loadedRack.Disks.Single().holdingOwner.Owner, loadedRack),
        "Installed loaded disks and hauling order references survive real Scribe without copies");
}
finally { if (File.Exists(path)) File.Delete(path); }
cold.Destroy(DestroyMode.Vanish);
Check(meal.Destroyed && !holder.Contents.Contains(cold), "Destroying a disk destroys its actual contents exactly once");
Console.WriteLine("Isolated library checks only; no RimWorld process was launched or controlled.");

sealed class TestHolder : IThingHolder
{
    public readonly ThingOwner<Thing> Contents;
    public readonly float Temperature;
    public TestHolder(float temperature) { Temperature = temperature; Contents = new ThingOwner<Thing>(this, false, LookMode.Deep); }
    public IThingHolder ParentHolder { get { return null; } }
    public ThingOwner GetDirectlyHeldThings() { return Contents; }
    public void GetChildHolders(List<IThingHolder> children) { ThingOwnerUtility.AppendThingHoldersFromThings(children, Contents); }
}
static class Fixture
{
    private static int nextId = 400000;
    public static ThingDef Def(string name, Type type, int stack, params CompProperties[] comps)
    {
        var def = new ThingDef { defName = name, label = name, category = ThingCategory.Item, thingClass = type,
            stackLimit = stack, useHitPoints = false, comps = comps.ToList() };
        DefDatabase<ThingDef>.Add(def); return def;
    }
    public static ThingDef DiskDef(string name, long capacity, bool cold)
    {
        var def = Def(name, typeof(StorageDisk), 1);
        def.modExtensions = new List<DefModExtension> { new StorageDiskProperties { capacityBytes = capacity, freezing = cold } };
        return def;
    }
    public static Thing Create(ThingDef def, int count = 1)
    {
        var thing = (Thing)Activator.CreateInstance(def.thingClass);
        thing.def = def; thing.stackCount = count; thing.thingIDNumber = nextId++;
        if (thing is ThingWithComps) AccessTools.Method(typeof(ThingWithComps), "InitializeComps").Invoke(thing, null);
        return thing;
    }
    public static bool MakeThing(ThingDef __0, ref Thing __result)
    { __result = Create(__0); return false; }
    public static bool Temperature(IThingHolder __0, ref float __2, ref bool __result)
    {
        if (!(__0 is TestHolder holder)) return true;
        __2 = holder.Temperature; __result = true; return false;
    }
    public static bool FilterDef(ThingFilter __instance, Thing t, ref bool __result) { __result = __instance.Allows(t.def); return false; }
    public static bool NoRelics(ref bool __result) { __result = false; return false; }
    public static readonly Dictionary<Building_StorageController, List<StorageDisk>> Networks = new();
    public static bool FailTransfer;
    public static int TransferCalls, ExpectedErrors;
    public static bool Online(Building_StorageController __instance, ref bool __result)
    { if (!Networks.ContainsKey(__instance)) return true; __result = true; return false; }
    public static bool ActiveDisks(Building_StorageController __instance, ref List<StorageDisk> __result)
    { if (!Networks.TryGetValue(__instance, out var disks)) return true; __result = disks.ToList(); return false; }
    public static void TransferFault()
    { if (FailTransfer && ++TransferCalls == 2) throw new InvalidOperationException("Injected transfer failure"); }
    public static bool ExpectedError(string __0)
    {
        if (!FailTransfer) return true;
        ExpectedErrors++; Console.WriteLine("EXPECTED rollback diagnostic: " + __0.Split('\n')[0]); return false;
    }
}
