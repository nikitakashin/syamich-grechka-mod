using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.Industry;
using HarmonyLib;
using RimWorld;
using Verse;

System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

void Check(bool condition, string text)
{
    if (!condition) throw new Exception(text);
    Console.WriteLine("PASS " + text);
}
var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map>());
AccessTools.Field(typeof(Game), "components").SetValue(game, new List<GameComponent>());
AccessTools.Field(typeof(Game), "tickManager").SetValue(game, new TickManager());
AccessTools.Field(typeof(Game), "uniqueIDsManager").SetValue(game, new UniqueIDsManager());
Current.Game = game;
var prefsField = AccessTools.Field(typeof(Prefs), "data");
prefsField.SetValue(null, Activator.CreateInstance(prefsField.FieldType));
var harmony = new Harmony("grechka.industry.isolated-tests");
foreach (Type hook in typeof(CompTierBonuses).Assembly.GetTypes().Where(t => t.Name.EndsWith("Hook")))
{
    harmony.CreateClassProcessor(hook).Patch();
    Check(Harmony.GetAllPatchedMethods().Any(m =>
    {
        var info = Harmony.GetPatchInfo(m);
        return info.Prefixes.Concat(info.Postfixes).Any(p => p.owner == harmony.Id && p.PatchMethod.DeclaringType == hook);
    }), "Native hook binds to installed 1.7 API: " + hook.Name);
}
var steel = new ThingDef { defName = "TestSteel", stackLimit = 75 };
var recipe = new RecipeDef { defName = "TestRecipe", products = new List<ThingDefCountClass> { new ThingDefCountClass(steel, 75) } };
var bonuses = new CompTierBonuses { props = new CompProperties_TierBonuses { outputBonus = 1 } };
Check(bonuses.TakeExtra(recipe, new Thing { def = steel, stackCount = 75 }) == 75,
    "A full stack still earns the entire T5 output bonus");
var panel = new ThingDef { defName = "TestSolar", stackLimit = 1 };
Check(bonuses.TakeExtra(recipe, new Thing { def = panel, stackCount = 1 }) == 1,
    "A fresh nonstackable machine is eligible for T5 production output");
var fractional = new CompTierBonuses { props = new CompProperties_TierBonuses { outputBonus = .1f } };
int extra = 0;
for (int i=0;i<10;i++) extra += fractional.TakeExtra(recipe, new Thing { def = steel, stackCount = 1 });
Check(extra == 1, "Ten small cycles retain one accumulated 10% bonus");
var matter = new RecipeDef { modExtensions = new List<DefModExtension> { new MatterRecipeProperties() } };
Check(MatterWorkContract.EffectiveWorkerMultiplier(matter, 60) == 1
    && MatterWorkContract.EffectiveWorkerMultiplier(recipe, 60) == 60,
    "Autocraft energy contract blocks worker speed only for matter conversion");
// Exact comp types and backing API members used by XML, without launching the game.
Check(typeof(CompPowerPlant).IsAssignableFrom(typeof(CompPowerPlantLateSolar)), "Late solar remains a native power plant");
Check(AccessTools.Field(typeof(HediffComp_Disappears), "ticksToDisappear") != null,
    "Emitter expiry uses the real installed hediff lifetime field");
Console.WriteLine("Isolated Industry native checks complete; no game session was launched.");

harmony.Patch(AccessTools.Method(typeof(ThingMaker), nameof(ThingMaker.MakeThing)),
    prefix: new HarmonyMethod(typeof(NativeFixture), nameof(NativeFixture.Make)));
var machineDef = new ThingDef { defName = "GrechkaNativeTest_Machine", thingClass = typeof(Building),
    category = ThingCategory.Building, building = new BuildingProperties(), stackLimit = 1, comps = new List<CompProperties>() };
var packageDef = new ThingDef { defName = "GrechkaNativeTest_Package", thingClass = typeof(MinifiedThing), stackLimit = 1,
    comps = new List<CompProperties>(), modExtensions = new List<DefModExtension> { new PackedBuildingProperties { building = machineDef } } };
machineDef.minifiedDef = packageDef;
var refund = (MinifiedThing)ThingMaker.MakeThing(packageDef);
Check(refund.InnerThing != null && refund.InnerThing.def == machineDef && refund.InnerThing.holdingOwner.Owner == refund,
    "A construction refund creates a complete packed machine with one owner");
var refund2 = (MinifiedThing)ThingMaker.MakeThing(packageDef);
Check(refund.InnerThing != refund2.InnerThing && refund.InnerThing.thingIDNumber != refund2.InnerThing.thingIDNumber,
    "Separate refunds never share the contained machine or its identity");
// Exercise the actual vanilla wrapping method, with only object allocation supplied by the fixture.
var original = ThingMaker.MakeThing(machineDef);
var wrapped = original.MakeMinified();
Check(ReferenceEquals(wrapped.InnerThing, original) && original.holdingOwner.Owner == wrapped,
    "Vanilla minification preserves the original instance instead of generating a replacement");

var fresh = ThingMaker.MakeThing(machineDef).MakeMinified();
var manufacturing = new RecipeDef { defName = "GrechkaNativeTest_Manufacturing", products = new List<ThingDefCountClass> { new ThingDefCountClass(machineDef, 1) } };
var bonusMethod = AccessTools.Method(typeof(CompTierBonuses).Assembly.GetType("Grechka.Industry.TierProductHook"), "WithBonus");
var produced = ((IEnumerable<Thing>)bonusMethod.Invoke(null, new object[] { new[] { fresh }, manufacturing, bonuses })).Cast<MinifiedThing>().ToList();
Check(produced.Count == 2 && ReferenceEquals(produced[0], fresh)
    && produced[0].InnerThing != produced[1].InnerThing
    && produced[0].InnerThing.thingIDNumber != produced[1].InnerThing.thingIDNumber,
    "T5 production creates two independently owned machines without cloning the first identity");
var testBench = new ThingDef { defName = "GrechkaNativeTest_Bench", recipes = new List<RecipeDef>() };
DefDatabase<ThingDef>.Add(testBench);
var generatedRecipe = new RecipeDef { defName = "GrechkaNativeTest_Recipe", label = "test", recipeUsers = new List<ThingDef> { testBench },
    products = new List<ThingDefCountClass> { new ThingDefCountClass(machineDef, 1) } };
_ = testBench.AllRecipes;
AccessTools.Method(typeof(MatterRecipes), "Register").Invoke(null, new object[] { generatedRecipe });
Check(testBench.AllRecipes.Count(r => r == generatedRecipe) == 1,
    "Generated matter recipe invalidates native cache and appears exactly once");
Check(generatedRecipe.shortHash != 0 && DefDatabase<RecipeDef>.GetNamed(generatedRecipe.defName) == generatedRecipe,
    "Generated recipe has a native database entry and save/network identity");


var hashRegistry = (Dictionary<Type,HashSet<ushort>>)AccessTools.Field(typeof(ShortHashGiver), "takenHashesPerDeftype").GetValue(null);
var recipeHashes = hashRegistry[typeof(RecipeDef)];
var originalHashes = recipeHashes.ToArray();
try
{
    for (int i = 1; i <= 60000; i++) recipeHashes.Add((ushort)i);
    var blocked = new RecipeDef { defName = "GrechkaNativeTest_HashCapacity" };
    AccessTools.Method(typeof(MatterRecipes), "Register").Invoke(null, new object[] { blocked });
    Check(blocked.shortHash == 0 && DefDatabase<RecipeDef>.GetNamedSilentFail(blocked.defName) == null,
        "Late recipe capacity guard stops before exhausting the native ushort probe space");
}
finally { recipeHashes.Clear(); foreach (ushort value in originalHashes) recipeHashes.Add(value); }

var matterIngredient = new IngredientCount(); matterIngredient.SetBaseCount(100);
var conversion = new RecipeDef { defName = "GrechkaNativeTest_Conversion", workAmount = 12345,
    ingredients = new List<IngredientCount> { matterIngredient },
    modExtensions = new List<DefModExtension> { new MatterRecipeProperties { factor = 10, efficiency = .05f } } };
var savedBatch = MatterBatchSnapshot.Capture(conversion);
string batchPayload = MatterSnapshots.Capture(conversion);
conversion.workAmount = 999;
conversion.GetModExtension<MatterRecipeProperties>().factor = 99;
matterIngredient.SetBaseCount(7);
using (MatterSnapshots.UseSnapshot(batchPayload))
{
    var readSnapshot = AccessTools.Method(typeof(MatterSnapshots), "For", new[] { typeof(RecipeDef), typeof(Pawn) });
    var captured = (MatterBatchSnapshot)readSnapshot.Invoke(null, new object[] { conversion, null });
    Check(captured.factor == 10 && captured.workAmount == 12345 && captured.ingredientCounts.Single() == 100,
        "Autocraft snapshot retains accepted coefficients, work and material quote after configuration changes");
}
string savePath = Path.Combine(Path.GetTempPath(), "grechka-matter-snapshot-" + Guid.NewGuid().ToString("N") + ".xml");
try
{
    Scribe.saver.InitSaving(savePath, "fixture"); Scribe_Deep.Look(ref savedBatch, "batch"); Scribe.saver.FinalizeSaving();
    MatterBatchSnapshot loaded = null;
    Scribe.loader.InitLoading(savePath); Scribe_Deep.Look(ref loaded, "batch"); Scribe.loader.FinalizeLoading();
    Check(loaded.factor == 10 && loaded.efficiency == .05f && loaded.workAmount == 12345 && loaded.ingredientCounts.Single() == 100,
        "Real Scribe preserves a running matter batch independently of reloaded XML prices");
}
finally { if (File.Exists(savePath)) File.Delete(savePath); }

public static class NativeFixture
{
    private static int id = 1000;
    public static bool Make(ThingDef def, ref Thing __result)
    {
        if (!def.defName.StartsWith("GrechkaNativeTest_")) return true;
        __result = (Thing)Activator.CreateInstance(def.thingClass);
        __result.def = def;
        __result.thingIDNumber = ++id;
        __result.stackCount = 1;
        return false;
    }
}
