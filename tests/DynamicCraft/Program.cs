using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using Grechka.Storage;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse.AI;
using Verse;

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    Console.WriteLine("PASS " + label);
}
var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map>());
AccessTools.Field(typeof(Game), "components").SetValue(game, new List<GameComponent> { new StorageDiskClock(game) });
AccessTools.Field(typeof(Game), "tickManager").SetValue(game, new TickManager());
AccessTools.Field(typeof(Game), "uniqueIDsManager").SetValue(game, new UniqueIDsManager());
var storyteller = (Storyteller)RuntimeHelpers.GetUninitializedObject(typeof(Storyteller)); storyteller.difficulty = new Difficulty();
AccessTools.Field(typeof(Game), "storyteller").SetValue(game, storyteller);
var world = (World)RuntimeHelpers.GetUninitializedObject(typeof(World)); world.factionManager = new FactionManager();
AccessTools.Field(typeof(Game), "worldInt").SetValue(game, world);
Current.Game = game;
var prefs = AccessTools.Field(typeof(Prefs), "data"); prefs.SetValue(null, Activator.CreateInstance(prefs.FieldType));
var harmony = new Harmony("grechka.isolated.dynamic-craft");
harmony.Patch(AccessTools.PropertyGetter(typeof(GenTypes), "AllActiveAssemblies"), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.ActiveAssemblies)));
// Construction and the empty test network are supplied. Product iterators, filters,
// ownership, ConsumeIngredient, native count arithmetic and Scribe stay real.
harmony.Patch(AccessTools.Method(typeof(ThingFilter), nameof(ThingFilter.Allows), new[] { typeof(Thing) }),
    transpiler: new HarmonyMethod(typeof(Fixture), nameof(Fixture.NoAnomaly)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Ideo)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.NoIdeology)));
harmony.Patch(AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Error)));
harmony.Patch(AccessTools.Method(typeof(ThingMaker), nameof(ThingMaker.MakeThing)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.MakeThing)));
harmony.Patch(AccessTools.Method(typeof(ReliquaryUtility), nameof(ReliquaryUtility.IsRelic)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.NoRelics)));
harmony.Patch(AccessTools.Method(typeof(Building_StorageController), nameof(Building_StorageController.ActiveDisks)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.ActiveDisks)));
var chunks = Fixture.Def("Grechka_DynamicChunks", 75);
var blocks = Fixture.Def("Grechka_DynamicBlocks", 75);
var metal = Fixture.Def("Grechka_DynamicMetal", 75); metal.smeltable = true;
var detailed = Fixture.Def("Grechka_DynamicIntricate", 75); detailed.smeltable = true; detailed.intricate = true;
var salvage = Fixture.Def("Grechka_DynamicSalvage", 75);
var gear = Fixture.Def("Grechka_DynamicGear", 75);
chunks.butcherProducts = new() { new ThingDefCountClass(blocks, 20) };
gear.costList = new() { new ThingDefCountClass(metal, 40), new ThingDefCountClass(detailed, 12) };
gear.smeltProducts = new() { new ThingDefCountClass(salvage, 3) };
var rawChunks = Fixture.Create(chunks, 5);
Fixture.HeadlessBootstrap = false;
var stoneOutput = rawChunks.StoneBlockProducts(null, 2).ToList();
Check(stoneOutput.Count == 1 && stoneOutput[0].def == blocks && stoneOutput[0].stackCount == 40 && rawChunks.stackCount == 5,
    "Native stone output uses input definition and efficiency once, not the full input stack count");
storyteller.difficulty.butcherYieldFactor = 1.5f;
var shredOutput = rawChunks.ButcherProducts(null, 2).ToList();
Check(shredOutput.Single().def == blocks && shredOutput.Single().stackCount == 60 && rawChunks.stackCount == 5,
    "Native base-Thing salvage uses efficiency and storyteller yield once without corpse or map effects");
storyteller.difficulty.butcherYieldFactor = 1f;
var rawGear = Fixture.Create(gear, 7);
var smeltOutput = rawGear.SmeltProducts(8).ToList();
Check(smeltOutput.Single(t => t.def == metal).stackCount == 10 && smeltOutput.Single(t => t.def == salvage).stackCount == 3
    && smeltOutput.All(t => t.def != detailed) && rawGear.stackCount == 7,
    "Native smelting preserves 25-percent cost recovery, explicit salvage and intricate exclusion without multiplying by stack or efficiency");
var diskDef = Fixture.Def("Grechka_DynamicDisk", 1); diskDef.thingClass = typeof(StorageDisk);
diskDef.modExtensions = new() { new StorageDiskProperties { capacityBytes = 1024 } };
var disk = new StorageDisk { def = diskDef, thingIDNumber = 99100 }; Fixture.Disks.Add(disk);
var inputHolder = new Holder(); inputHolder.Things.TryAdd(rawChunks, false); inputHolder.Things.TryAdd(rawGear, false);
Check(disk.Accept(rawChunks, 5) == 5 && disk.Accept(rawGear, 7) == 7, "Real disk takes ownership of selected recipe inputs");
Thing selected = disk.Items.Single(t => t.def == chunks);
var count = new IngredientCount(); count.SetBaseCount(1); count.filter.SetAllow(chunks, true);
var recipe = new RecipeDef { defName = "Grechka_DynamicStoneRecipe", workerClass = typeof(RecipeWorker), ingredients = new() { count },
    products = new(), specialProducts = new() { SpecialProductType.StoneBlocks }, fixedIngredientFilter = new ThingFilter(), defaultIngredientFilter = new ThingFilter() };
recipe.fixedIngredientFilter.SetAllow(chunks, true); recipe.defaultIngredientFilter.SetAllow(chunks, true);
AccessTools.Field(typeof(RecipeDef), "ingredientValueGetterClass").SetValue(recipe, typeof(IngredientValueGetter_Volume));
DefDatabase<RecipeDef>.Add(recipe);
var table = new Building_WorkTable { def = new ThingDef { defName = "Grechka_DynamicTable", comps = new() } };
var controller = new Building_StorageController();
var dynamicType = typeof(StorageDisk).Assembly.GetType("Grechka.Storage.DynamicCraftRecipes", true);
var selectMethod = AccessTools.Method(dynamicType, "SelectSelected");
List<(StorageDisk disk, Thing item, int count)> Select(Thing input, out string problem)
{
    object[] args = { controller, table, recipe, input, null };
    var result = (List<(StorageDisk disk, Thing item, int count)>)selectMethod.Invoke(null, args);
    problem = (string)args[4]; return result;
}
var picked = Select(selected, out string issue);
Check(picked != null && picked.Count == 1 && picked[0].item == selected && picked[0].count == 1 && selected.stackCount == 5,
    "Ordinary dynamic plan reserves one real item without changing its stack or owner");
count.SetBaseCount(3);
picked = Select(selected, out issue);
Check(picked == null && selected.stackCount == 5,
    "Multi-unit dynamic recipe is refused because native special products do not scale by input stack");
count.SetBaseCount(1); recipe.ignoreIngredientCountTakeEntireStacks = true;
picked = Select(selected, out issue);
Check(picked != null && picked.Single().count == 5, "Full-stack recipe explicitly reserves the entire selected stack instead of one unit");
var growthOrder = new CraftOrder { Id = 500, Recipe = recipe, Remaining = 1, SelectedInputOrder = true };
var selectedField = AccessTools.Field(typeof(CraftOrder), "SelectedInput"); selectedField.SetValue(growthOrder, selected);
var selectedCountField = AccessTools.Field(typeof(CraftOrder), "SelectedCount"); selectedCountField.SetValue(growthOrder, 5);
var selectOrder = AccessTools.Method(dynamicType, "SelectOrder");
selected.stackCount = 6;
object[] growthArgs = { controller, table, growthOrder, null };
Check(selectOrder.Invoke(null, growthArgs) == null && selected.stackCount == 6,
    "A queued whole-stack order refuses stack growth instead of consuming newly added items");
selected.stackCount = 4;
growthArgs = new object[] { controller, table, growthOrder, null };
Check(selectOrder.Invoke(null, growthArgs) == null && selected.stackCount == 4,
    "A queued whole-stack order refuses partial withdrawal without consuming the remainder");
selected.stackCount = 5;
growthArgs = new object[] { controller, table, growthOrder, null };
Check(selectOrder.Invoke(null, growthArgs) != null, "Unchanged selected whole stack remains eligible for its queued order");
recipe.ignoreIngredientCountTakeEntireStacks = false; count.SetBaseCount(1);
Check(Select(disk.Items.Single(t => t.def == gear), out issue) == null,
    "An explicitly selected wrong ingredient still fails the native recipe filter");
var outside = Fixture.Create(chunks, 1);
Check(Select(outside, out issue) == null, "Matching items outside connected disks are never consumed by a dynamic order");
var category = new ThingCategoryDef { defName = "Grechka_DynamicCategory" }; chunks.thingCategories.Add(category);
var special = new SpecialThingFilterDef { defName = "Grechka_DynamicReject", parentCategory = category, workerClass = typeof(RejectLargeStack) };
recipe.fixedIngredientFilter.SetAllow(special, false);
Check(!recipe.fixedIngredientFilter.Allows(selected) && Select(selected, out issue) == null,
    "Real special Thing filter is respected for selected input, not just AllowedThingDefs");
recipe.fixedIngredientFilter.SetAllow(special, true);
var customInput = new CustomSmelter { def = gear, stackCount = 1, thingIDNumber = 99340 };
recipe.specialProducts = new() { SpecialProductType.Smelted };
Check(Select(customInput, out issue) == null && !CustomSmelter.Invoked,
    "Unadapted virtual smelting override is rejected without executing its side effects");
Check(Select(new MinifiedThing { def = gear }, out issue) == null,
    "A minified machine cannot be destroyed through a loose-item salvage order");
recipe.specialProducts = new() { SpecialProductType.Butchery };
Check(Select(selected, out issue) != null, "Non-corpse input with native base ButcherProducts can be selected for salvage");
var customButcher = new CustomButcher { def = chunks, stackCount = 1, thingIDNumber = 99341 };
Check(Select(customButcher, out issue) == null && !CustomButcher.Invoked,
    "Unadapted virtual butcher override is rejected without executing its side effects");
recipe.specialProducts = new() { SpecialProductType.StoneBlocks };
var consumeHolder = new Holder();
Check(disk.Withdraw(selected, 1, consumeHolder.Things) == 1, "One item is physically reserved with native container transfer");
Thing reserved = consumeHolder.Things.Single(); int before = selected.stackCount;
var worker = new Pawn { def = Fixture.Def("Grechka_DynamicWorker", 1) };
var actualProducts = GenRecipe.MakeRecipeProducts(recipe, worker, new() { reserved }, reserved, table).ToList();
Check(actualProducts.Single().def == blocks && actualProducts.Single().stackCount == 20 && !reserved.Destroyed,
    "Native GenRecipe creates actual stone products before input consumption");
recipe.Worker.ConsumeIngredient(reserved, recipe, null);
Check(reserved.Destroyed && selected.stackCount == before && disk.Items.Contains(selected),
    "Native ConsumeIngredient destroys only the separated reserved object, leaving disk remainder intact");
var order = new CraftOrder { Id = 501, Recipe = recipe, Remaining = 1, SelectedInputOrder = true };
selectedField.SetValue(order, selected); selectedCountField.SetValue(order, before);
var state = new SavedState { Disk = disk, Order = order };
string path = Path.Combine(Path.GetTempPath(), "grechka-dynamic-" + Guid.NewGuid().ToString("N") + ".xml");
try
{
    Scribe.saver.InitSaving(path, "fixture"); Scribe_Deep.Look(ref state, "state"); Scribe.saver.FinalizeSaving();
    SavedState restored = null; Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref restored, "state"); Scribe.loader.FinalizeLoading();
    Thing restoredInput = (Thing)selectedField.GetValue(restored.Order);
    Check(restoredInput != null && restored.Disk.Items.Contains(restoredInput) && restoredInput.stackCount == before
        && restored.Order.SelectedInputOrder && restored.Order.Remaining == 1 && restored.Order.Id == 501 && (int)selectedCountField.GetValue(restored.Order) == before,
        "Native save/load resolves SelectedInput to the actual restored disk object and preserves one-cycle order");
}
finally { if (File.Exists(path)) File.Delete(path); }
// Orders run through the actual manager, while topology remains the isolated empty map.
var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
DefDatabase<DesignationDef>.Add(new DesignationDef { defName = "Grechka_DynamicDesignation" });
map.designationManager = new DesignationManager(map);
map.reservationManager = new ReservationManager(map);
map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
var manager = new CraftMap(map); var factory = new FactoryCraftMap(map);
AccessTools.Field(typeof(Map), "components").SetValue(map, new List<MapComponent> { manager, factory });
((List<Map>)AccessTools.Field(typeof(Game), "maps").GetValue(game)).Add(map);
Check(manager.Add(controller, recipe, 1, true) == null && manager.Add(controller, recipe, 1, false) == null,
    "Dynamic recipes cannot enter fixed-output or maintain-stock order paths");
var emptyGear = Fixture.Def("Grechka_DynamicNoSalvage", 1); emptyGear.costList = new();
var emptyIngredient = new IngredientCount(); emptyIngredient.SetBaseCount(1); emptyIngredient.filter.SetAllow(emptyGear, true);
var emptyRecipe = new RecipeDef { defName = "Grechka_DynamicEmptySmelt", workerClass = typeof(RecipeWorker), ingredients = new() { emptyIngredient },
    specialProducts = new() { SpecialProductType.Smelted }, products = new(), fixedIngredientFilter = new ThingFilter(), defaultIngredientFilter = new ThingFilter() };
emptyRecipe.fixedIngredientFilter.SetAllow(emptyGear, true); emptyRecipe.defaultIngredientFilter.SetAllow(emptyGear, true);
AccessTools.Field(typeof(RecipeDef), "ingredientValueGetterClass").SetValue(emptyRecipe, typeof(IngredientValueGetter_Volume));
var crafterDef = Fixture.Def("Grechka_DynamicCrafter", 1); crafterDef.thingClass = typeof(Building_StorageCrafter);
crafterDef.category = ThingCategory.Building; crafterDef.building = new BuildingProperties();
var crafter = new Building_StorageCrafter { def = crafterDef };
AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(crafter, (sbyte)0);
var emptyOrder = new CraftOrder { Id = 502, Recipe = emptyRecipe, Remaining = 1, Controller = controller };
AccessTools.Field(typeof(CraftOrder), "SelectedInputOrder").SetValue(emptyOrder, true);
((List<CraftOrder>)AccessTools.Field(typeof(CraftMap), "orders").GetValue(manager)).Add(emptyOrder);
var needs = AccessTools.Method(typeof(CraftMap), "Needs");
Check((bool)needs.Invoke(manager, new object[] { emptyOrder, controller }), "Unstarted selected-input order is eligible once");
((HashSet<Building_StorageCrafter>)AccessTools.Field(typeof(CraftMap), "Crafters").GetValue(manager)).Add(crafter);
AccessTools.Field(typeof(Building_StorageCrafter), "orderId").SetValue(crafter, emptyOrder.Id);
AccessTools.Field(typeof(Building_StorageCrafter), "recipe").SetValue(crafter, emptyRecipe);
AccessTools.Field(typeof(Building_StorageCrafter), "table").SetValue(crafter, table);
Check(!(bool)needs.Invoke(manager, new object[] { emptyOrder, controller }),
    "A second crafter cannot reserve the same selected-input order while its first cycle is active");
var activeBuffer = (StorageBuffer)AccessTools.Field(typeof(Building_StorageCrafter), "buffer").GetValue(crafter);
var doomed = Fixture.Create(emptyGear, 1); activeBuffer.Contents.TryAdd(doomed, false);
AccessTools.Field(typeof(Building_StorageCrafter), "reservedQuantity").SetValue(crafter, 1L);
AccessTools.Field(typeof(Building_StorageCrafter), "reservedInputs").SetValue(crafter, new Dictionary<string, int> { [doomed.GetUniqueLoadID()] = 1 });
harmony.Patch(AccessTools.Method(typeof(Building_StorageCrafter), "Worker"), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Worker)));
harmony.Patch(AccessTools.Method(typeof(Building_StorageCrafter), "Flush"), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.NoFlush)));
AccessTools.Method(typeof(Building_StorageCrafter), "Finish").Invoke(crafter, null);
Check(doomed.Destroyed && emptyOrder.Remaining == 0 && crafter.Committed
    && !((bool)AccessTools.Field(typeof(Building_StorageCrafter), "faulted").GetValue(crafter)),
    "Native zero-yield smelting consumes its reserved input and completes one order cycle instead of waiting for output");
Check(!((List<CraftOrder>)AccessTools.Field(typeof(CraftMap), "orders").GetValue(manager)).Contains(emptyOrder),
    "Completed zero-output order is removed from the actual manager");
Check(Fixture.Errors == 0, "No native errors were swallowed during dynamic ownership, save/load or finishing");
Console.WriteLine("Dynamic recipe fixtures use native special-product iterators and ownership; no running game controlled.");

public sealed class CustomButcher : ThingWithComps
{
    public static bool Invoked;
    public override IEnumerable<Thing> ButcherProducts(Pawn butcher, float efficiency) { Invoked = true; return Enumerable.Empty<Thing>(); }
}
public sealed class CustomSmelter : ThingWithComps
{
    public static bool Invoked;
    public override IEnumerable<Thing> SmeltProducts(float efficiency) { Invoked = true; return Enumerable.Empty<Thing>(); }
}
public sealed class RejectLargeStack : SpecialThingFilterWorker
{
    public override bool Matches(Thing t) => t.stackCount > 2;
}
public sealed class SavedState : IExposable
{
    public StorageDisk Disk; public CraftOrder Order;
    public void ExposeData() { Scribe_Deep.Look(ref Disk, "disk"); Scribe_Deep.Look(ref Order, "order"); }
}
public sealed class Holder : IThingHolder
{
    public ThingOwner<Thing> Things; public Holder() { Things = new ThingOwner<Thing>(this, false); }
    public IThingHolder ParentHolder => null;
    public ThingOwner GetDirectlyHeldThings() => Things;
    public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, Things);
}
public static class Fixture
{
    static int next = 99200;
    public static List<StorageDisk> Disks = new();
    public static bool ActiveAssemblies(ref IEnumerable<Assembly> __result) { __result = new[] { typeof(Thing).Assembly, typeof(StorageDisk).Assembly, typeof(Fixture).Assembly }; return false; }
    public static bool ActiveDisks(ref List<StorageDisk> __result) { __result = Disks; return false; }
    public static IEnumerable<CodeInstruction> NoAnomaly(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(AccessTools.PropertyGetter(typeof(ModsConfig), nameof(ModsConfig.AnomalyActive))))
            {
                var replacement = new CodeInstruction(OpCodes.Ldc_I4_0); replacement.labels.AddRange(instruction.labels); replacement.blocks.AddRange(instruction.blocks); yield return replacement;
            }
            else yield return instruction;
        }
    }
    public static bool NoIdeology(ref Ideo __result) { __result = null; return false; }
    public static bool Worker(ref Pawn __result) { __result = null; return false; }
    public static bool NoFlush() => false;
    public static int Errors;
    public static bool HeadlessBootstrap = true;
    public static bool Error(string text)
    {
        // Unity's static fallback graphics cannot load in this headless executable.
        // Exempt only those known initialization diagnostics before the first native operation.
        if (HeadlessBootstrap && (text.Contains("from a different thread") || text.StartsWith("Attempted to load a graphic off the main thread:")))
        { Console.WriteLine("HEADLESS GRAPHICS SETUP: " + text); return false; }
        Errors++; Console.WriteLine("GAME ERROR: " + text); return false;
    }
    public static bool NoRelics(ref bool __result) { __result = false; return false; }
    public static ThingDef Def(string name, int stack)
    {
        var def = new ThingDef { defName = name, label = name, category = ThingCategory.Item, thingClass = typeof(ThingWithComps),
            stackLimit = stack, useHitPoints = false, comps = new(), thingCategories = new() };
        DefDatabase<ThingDef>.Add(def); return def;
    }
    public static Thing Create(ThingDef def, int count = 1)
    {
        var thing = new ThingWithComps { def = def, stackCount = count, thingIDNumber = next++ };
        AccessTools.Method(typeof(ThingWithComps), "InitializeComps").Invoke(thing, null); return thing;
    }
    public static bool MakeThing(ThingDef __0, ref Thing __result) { __result = Create(__0); return false; }
}
