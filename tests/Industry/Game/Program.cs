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
var hugeStackDef=new ThingDef {defName="GrechkaNativeTest_HugeStack",thingClass=typeof(Thing),stackLimit=int.MaxValue};
var hugeRecipe=new RecipeDef {defName="GrechkaNativeTest_HugeRecipe",products=new(){new ThingDefCountClass(hugeStackDef,1)}};
var hugeProduct=new Thing {def=hugeStackDef,stackCount=int.MaxValue};
var hugeBonuses=new CompTierBonuses {props=new CompProperties_TierBonuses {outputBonus=1}};
Check(hugeBonuses.TakeExtra(hugeRecipe,hugeProduct)==int.MaxValue,"T5 output bonus uses wide multiplication for a maximum-sized modded stack");
var hugeProducts=((IEnumerable<Thing>)bonusMethod.Invoke(null,new object[]{new[]{hugeProduct},hugeRecipe,hugeBonuses,new List<Thing>()})).ToList();
Check(hugeProducts.All(p=>p.stackCount>0) && hugeProducts.Sum(p=>(long)p.stackCount)==2L*int.MaxValue,
    "Native splitting preserves all T5 output units when total exceeds Int32 without negative stacks");
var produced = ((IEnumerable<Thing>)bonusMethod.Invoke(null, new object[] { new[] { fresh }, manufacturing, bonuses, new List<Thing>() })).Cast<MinifiedThing>().ToList();
Check(produced.Count == 2 && ReferenceEquals(produced[0], fresh)
    && produced[0].InnerThing != produced[1].InnerThing
    && produced[0].InnerThing.thingIDNumber != produced[1].InnerThing.thingIDNumber,
    "T5 production creates two independently owned machines without cloning the first identity");
// The same hook must distinguish newly made salvage from returned input identity.
var raw = new Thing { def = steel, stackCount = 3 };
var returned = ((IEnumerable<Thing>)bonusMethod.Invoke(null, new object[] { new[] { raw }, recipe, bonuses, new List<Thing> { raw } })).ToList();
Check(returned.Count == 1 && ReferenceEquals(returned[0], raw) && raw.stackCount == 3,
    "A returned input never earns replicas even when its definition is an explicit recipe product");
raw.stackCount = raw.def.stackLimit + 5;
var intactReturn = ((IEnumerable<Thing>)bonusMethod.Invoke(null, new object[] { new[] { raw }, recipe, bonuses, new List<Thing> { raw } })).ToList();
Check(intactReturn.Count == 1 && ReferenceEquals(intactReturn[0], raw) && raw.stackCount == raw.def.stackLimit + 5,
    "An oversized returned input remains one unchanged object instead of being split before native consumption");
var returnedPackage = ((IEnumerable<Thing>)bonusMethod.Invoke(null, new object[] { new[] { wrapped }, manufacturing, bonuses, new List<Thing> { wrapped.InnerThing } })).ToList();
Check(returnedPackage.Count == 1 && ReferenceEquals(returnedPackage[0].GetInnerIfMinified(), original),
    "Wrapping an existing ingredient cannot disguise it as a freshly manufactured machine");
var salvageDef = new ThingDef { defName = "GrechkaNativeTest_Salvage", thingClass = typeof(Thing), category = ThingCategory.Item, stackLimit = 75 };
foreach (SpecialProductType kind in new[] { SpecialProductType.StoneBlocks, SpecialProductType.Smelted, SpecialProductType.Butchery })
{
    var specialRecipe = new RecipeDef { defName = "GrechkaNativeTest_Special" + kind, workerClass = typeof(RecipeWorker), products = new(), specialProducts = new() { kind } };
    var freshSalvage = new Thing { def = salvageDef, stackCount = 20 };
    var result = ((IEnumerable<Thing>)bonusMethod.Invoke(null, new object[] { new[] { freshSalvage }, specialRecipe, bonuses, new List<Thing> { raw } })).ToList();
    Check(result.Sum(t => t.stackCount) == 40, "Fresh native special yield receives T5 output bonus: " + kind);
}
var corpseRecipe = new RecipeDef { defName = "GrechkaNativeTest_CorpseProducts", workerClass = typeof(RecipeWorker), products = new(), specialProducts = new() { SpecialProductType.Butchery } };
var corpseInput = new Corpse { def = new ThingDef { defName = "GrechkaNativeTest_Corpse", thingClass = typeof(Corpse), category = ThingCategory.Item, comps = new() } };
var nestedSource = new NestedSourceFixture { def = steel };
var retainedGear = new Thing { def = salvageDef, stackCount = 1 };
nestedSource.Things.TryAdd(retainedGear, false);
int sourceIterations = 0;
IEnumerable<Thing> CorpseResults()
{
    sourceIterations++;
    yield return new Thing { def = salvageDef, stackCount = 20 };
    nestedSource.Things.Remove(retainedGear);
    yield return retainedGear;
}
var corpseResults = ((IEnumerable<Thing>)bonusMethod.Invoke(null, new object[] { CorpseResults(), corpseRecipe, bonuses, new List<Thing> { corpseInput, nestedSource } })).ToList();
Check(sourceIterations == 1 && corpseResults[0].stackCount == 40 && corpseResults[1] == retainedGear && retainedGear.stackCount == 1,
    "Native corpse fresh yield is bonused once while existing nested inventory stays unreplicated after detachment");
var specialUnknown = new RecipeDef { defName = "GrechkaNativeTest_Override", workerClass = typeof(RecipeWorker), products = new(), specialProducts = new() { SpecialProductType.Smelted } };
var unknownYield = new Thing { def = salvageDef, stackCount = 20 };
var overridden = ((IEnumerable<Thing>)bonusMethod.Invoke(null, new object[] { new[] { unknownYield }, specialUnknown, bonuses, new List<Thing> { new UnknownSmeltFixture { def = steel } } })).ToList();
Check(overridden.Single().stackCount == 20, "Unknown special-product override does not receive speculative extra output");

var cloth = new ThingDef { defName = "GrechkaNativeTest_Cloth" };
var wood = new ThingDef { defName = "GrechkaNativeTest_Wood" };
var garment = new ThingDef { defName = "GrechkaNativeTest_Garment", thingClass = typeof(ThingWithComps), category = ThingCategory.Item, stackLimit = 1 };
ThingWithComps Garment(ThingDef stuff, QualityCategory quality)
{
    var item = new ThingWithComps { def = garment, stackCount = 1 }; item.SetStuffDirect(stuff);
    var comp = new CompQuality { parent = item, props = new CompProperties(typeof(CompQuality)) };
    AccessTools.Field(typeof(CompQuality), "qualityInt").SetValue(comp, quality);
    AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(item, new List<ThingComp> { comp });
    AccessTools.Field(typeof(ThingWithComps), "compQuality").SetValue(item, comp);
    return item;
}
var signatureRecipe = new RecipeDef { defName = "GrechkaNativeTest_Signature", products = new() { new ThingDefCountClass(garment, 1) }, ingredients = new() };
var signatureBonus = new CompTierBonuses { props = new CompProperties_TierBonuses { outputBonus = .5f } };
Check(signatureBonus.TakeExtra(signatureRecipe, Garment(cloth, QualityCategory.Normal)) == 0
    && signatureBonus.TakeExtra(signatureRecipe, Garment(wood, QualityCategory.Normal)) == 0
    && signatureBonus.TakeExtra(signatureRecipe, Garment(cloth, QualityCategory.Good)) == 0
    && signatureBonus.TakeExtra(signatureRecipe, Garment(cloth, QualityCategory.Normal)) == 1,
    "Fractional output carry never crosses material or quality, but resumes a compatible signature");
var changed = new IngredientCount(); changed.SetBaseCount(2); signatureRecipe.ingredients.Add(changed);
Check(signatureBonus.TakeExtra(signatureRecipe, Garment(wood, QualityCategory.Normal)) == 0,
    "Changing recipe input counts cannot spend the prior recipe revision's fractional carry");
Check(signatureBonus.PreviewExtra(signatureRecipe, garment) == 0,
    "Pending-output preview is a safe lower bound when future material and quality are unknown");
var upgradedBonus = new CompTierBonuses { props = new CompProperties_TierBonuses { outputBonus = .5f } };
AccessTools.Method(typeof(CompTierBonuses), "CopyProgressFrom").Invoke(upgradedBonus, new object[] { signatureBonus });
Check(upgradedBonus.TakeExtra(signatureRecipe, Garment(wood, QualityCategory.Normal)) == 1
    && signatureBonus.TakeExtra(signatureRecipe, Garment(wood, QualityCategory.Normal)) == 1,
    "Upgrade carries compatible fractional progress once into an independent dictionary");

var beforeFailedBatch = signatureBonus.CaptureCarry();
int attemptedBonus = signatureBonus.TakeExtra(signatureRecipe, Garment(cloth, QualityCategory.Good));
signatureBonus.RestoreCarry(beforeFailedBatch);
Check(signatureBonus.CaptureCarry().OrderBy(p => p.Key).SequenceEqual(beforeFailedBatch.OrderBy(p => p.Key)),
    "A failed staged batch can restore its pre-production carry without sharing the snapshot dictionary");
beforeFailedBatch.Clear();
Check(signatureBonus.CaptureCarry().Count > 0, "Restored carry is independent of the caller's snapshot ownership");
var carryState = new CarryFixtureState();
Check(carryState.Bonus.TakeExtra(signatureRecipe, Garment(cloth, QualityCategory.Normal)) == 0,
    "Fixture starts with one real half-item carry");
var legacy = carryState.Bonus.CaptureCarry(); legacy[signatureRecipe.defName + "/" + garment.defName] = .9f;
carryState.Bonus.RestoreCarry(legacy);
string carryPath = Path.Combine(Path.GetTempPath(), "grechka-carry-" + Guid.NewGuid().ToString("N") + ".xml");
try
{
    Scribe.saver.InitSaving(carryPath, "fixture"); Scribe_Deep.Look(ref carryState, "state"); Scribe.saver.FinalizeSaving();
    CarryFixtureState loaded = null; Scribe.loader.InitLoading(carryPath); Scribe_Deep.Look(ref loaded, "state"); Scribe.loader.FinalizeLoading();
    Check(loaded.Bonus.CaptureCarry().ContainsKey(signatureRecipe.defName + "/" + garment.defName)
        && loaded.Bonus.TakeExtra(signatureRecipe, Garment(cloth, QualityCategory.Normal)) == 1,
        "Native Scribe preserves compatible v2 carry and retains legacy data without spending it on an unknown signature");
}
finally { if (File.Exists(carryPath)) File.Delete(carryPath); }
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
BalanceChecks.Run(Check);
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

// Resolve the pawn's cell with the installed game API and compare the byte-exact
// rendered directional PNG to the art drawn with its operator edge on that side.
string artworkRoot = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
int verifiedWorkViews = 0, verifiedStateViews = 0;
foreach (var pair in new[] { ("GrechkaIndustry", "AssemblyBenchT1"), ("GrechkaIndustry", "AssemblyBenchT2"),
    ("GrechkaIndustry", "AssemblyBenchT3"), ("GrechkaIndustry", "AssemblyBenchT4"), ("GrechkaIndustry", "AssemblyBenchT5"),
    ("GrechkaIndustry", "MatterRecyclerT4"), ("GrechkaStorage", "ElectricInscriber") })
{
    (string module, string family) = pair;
    string moduleRoot = Path.Combine(artworkRoot, "mods", module);
    string artRoot = Path.Combine(artworkRoot, "assets", module, "Textures");
    string packaged = Path.Combine(moduleRoot, "Textures");
    string graphicPath = $"Buildings/Map/{family}/{family}";
    var node = Directory.EnumerateFiles(Path.Combine(moduleRoot, "Defs"), "*.xml", SearchOption.AllDirectories)
        .SelectMany(file => System.Xml.Linq.XDocument.Load(file).Root!.Elements())
        .Single(def => (string)def.Element("graphicData")?.Element("texPath") == graphicPath);
    if ((string)node.Element("hasInteractionCell") != "true" || (string)node.Element("interactionCellOffset") != "(0,0,-1)")
        throw new Exception("Changed operator geometry: " + family);
    var workDef = new ThingDef { hasInteractionCell = true, interactionCellOffset = new IntVec3(0, 0, -1) };
    var center = new IntVec3(20, 0, 20);
    string[] names = { "north", "east", "south", "west" };
    foreach (Rot4 rotation in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
    {
        var offset = ThingUtility.InteractionCellWhenAt(workDef, center, rotation, null) - center;
        string operatorSide = offset.x == 1 ? "east" : offset.x == -1 ? "west" : offset.z == -1 ? "south" : "north";
        string slot = names[rotation.AsInt];
        void SamePng(string deployed, string authored)
        {
            if (!File.ReadAllBytes(Path.Combine(packaged, deployed + ".png"))
                .SequenceEqual(File.ReadAllBytes(Path.Combine(artRoot, authored + ".png"))))
                throw new Exception($"Operator faces {operatorSide} for {family}/{slot}, expected {authored}");
        }
        SamePng(graphicPath + "_" + slot, graphicPath + "_" + operatorSide);
        verifiedWorkViews++;
        if (family == "MatterRecyclerT4")
        {
            foreach (string state in new[] { "Off", "Idle", "Active" })
            {
                string stem = $"Buildings/Map/States/{family}/{family}_{state}";
                string authored = stem + "_" + operatorSide;
                if (operatorSide == "west" && !File.Exists(Path.Combine(artRoot, authored + ".png"))) authored = stem;
                SamePng(stem + "_" + slot, authored);
                verifiedStateViews++;
            }
        }
    }
}
Check(verifiedWorkViews == 28 && verifiedStateViews == 12,
    "Packaged operator edges face native interaction cells in all 28 worktable views and 12 recycler states");

var directionalArt = new DirectionFixtureGraphic { drawSize = new UnityEngine.Vector2(2, 2), data = new GraphicData() };
var stateArt = new DirectionFixtureGraphic();
var partialStateArt = new Graphic_OperationalView(directionalArt, stateArt, Rot4.West);
foreach (var direction in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
{
    bool west = direction == Rot4.West;
    Check(ReferenceEquals(partialStateArt.ViewAt(direction), west ? stateArt : directionalArt),
        "Prepared west state preserves the original view at " + direction);
    partialStateArt.MatAt(direction);
    Check(west ? stateArt.singleReads == 1 : directionalArt.lastDirection == direction,
        "Native MatAt selects the correct material source at " + direction);
}
Check(!partialStateArt.ShouldDrawRotated && !partialStateArt.WestFlipped
    && ReferenceEquals(partialStateArt.data, directionalArt.data) && partialStateArt.drawSize == directionalArt.drawSize,
    "Partial state keeps native geometry, shadow/offset data and never mirrors the prepared west view");

var northState = new DirectionFixtureGraphic();
var southState = new DirectionFixtureGraphic();
var westState = new DirectionFixtureGraphic();
var prepared = new Dictionary<string, Graphic> {
    ["State_Active_north"] = northState, ["State_Active_south"] = southState, ["State_Active"] = westState
};
var configuredStates = new CompProperties_OperationalGraphics { directionalStates = true, singleView = 3 };
Graphic LoadState(string path) => prepared.TryGetValue(path, out Graphic value) ? value : null;
var mixedState = (Graphic_OperationalView)CompOperationalGraphics.BuildStateGraphic(directionalArt, "State_Active", configuredStates, LoadState);
Check(ReferenceEquals(mixedState.ViewAt(Rot4.North), northState)
    && ReferenceEquals(mixedState.ViewAt(Rot4.South), southState)
    && ReferenceEquals(mixedState.ViewAt(Rot4.West), westState),
    "Directional state resolves actual north/south and retains legacy unsuffixed west");
Check(ReferenceEquals(mixedState.ViewAt(Rot4.East), directionalArt),
    "An absent east frame falls back only to the original east view, never to west");
prepared["State_Active_west"] = stateArt;
var explicitWest = (Graphic_OperationalView)CompOperationalGraphics.BuildStateGraphic(directionalArt, "State_Active", configuredStates, LoadState);
Check(ReferenceEquals(explicitWest.ViewAt(Rot4.West), stateArt),
    "An explicit west frame takes precedence over the compatible legacy west frame");
Check(ReferenceEquals(CompOperationalGraphics.BuildStateGraphic(directionalArt, "Missing", configuredStates, LoadState), directionalArt),
    "Entirely missing directional state returns the original graphic without bad-texture replacement");
Check(ReferenceEquals(CompOperationalGraphics.BuildStateGraphic(directionalArt, "State_Active",
    new CompProperties_OperationalGraphics(), LoadState), westState),
    "Existing complete single-view state keeps its original loading contract");
Check(ReferenceEquals(CompOperationalGraphics.BuildStateGraphic(directionalArt, null, configuredStates, LoadState), directionalArt),
    "An unspecified state path retains the original graphic");
var isolatedViews = new Graphic[] { northState, null, southState, westState };
var immutableStates = new Graphic_OperationalView(directionalArt, isolatedViews);
isolatedViews[0] = null;
Check(ReferenceEquals(immutableStates.ViewAt(Rot4.North), northState),
    "Caller changes to a prepared frame array cannot corrupt cached operational graphics");
var northOnly = new Graphic_OperationalView(directionalArt, new Graphic[] { northState, null, null, null });
Check(northOnly.WestFlipped && !mixedState.WestFlipped,
    "Original west reflection survives only when a prepared west frame is absent");
foreach (string meshMethod in new[] { "GridPlane", "GridPlaneFlip" })
    harmony.Patch(AccessTools.Method(typeof(MeshPool), meshMethod, new[] { typeof(UnityEngine.Vector2) }),
        prefix: new HarmonyMethod(typeof(DirectionFixtureMesh), nameof(DirectionFixtureMesh.Capture)));
var rectangularBase = new DirectionFixtureGraphic { drawSize = new UnityEngine.Vector2(3, 1) };
var rectangularState = new Graphic_OperationalView(rectangularBase, stateArt, Rot4.West);
rectangularState.MeshAt(Rot4.West);
Check(DirectionFixtureMesh.lastMethod == "GridPlane" && DirectionFixtureMesh.lastSize == new UnityEngine.Vector2(1, 3)
    && rectangularBase.meshReads == 0,
    "Prepared west mesh uses native horizontal footprint without reapplying original reflection");
rectangularState.MeshAt(Rot4.East);
Check(rectangularBase.meshReads == 1,
    "Unprepared directions preserve the original custom mesh implementation");

public sealed class DirectionFixtureGraphic : Graphic
{
    public Rot4 lastDirection;
    public int singleReads;
    public int meshReads;
    public override UnityEngine.Mesh MeshAt(Rot4 rot) { meshReads++; return null; }
    public override bool ShouldDrawRotated => false;
    public override bool WestFlipped => true;
    public override UnityEngine.Material MatSingle { get { singleReads++; return null; } }
    public override UnityEngine.Material MatAt(Rot4 rot, Thing thing = null) { lastDirection = rot; return null; }
}

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

public sealed class UnknownSmeltFixture : Thing
{
    public override IEnumerable<Thing> SmeltProducts(float efficiency) => Enumerable.Empty<Thing>();
}

public sealed class CarryFixtureState : IExposable
{
    public CompTierBonuses Bonus = new CompTierBonuses { props = new CompProperties_TierBonuses { outputBonus = .5f } };
    public void ExposeData() => Bonus.PostExposeData();
}

public sealed class NestedSourceFixture : Thing, IThingHolder
{
    public ThingOwner<Thing> Things;
    public NestedSourceFixture() { Things = new ThingOwner<Thing>(this, false); }
    public ThingOwner GetDirectlyHeldThings() => Things;
    public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, Things);
}

public static class DirectionFixtureMesh
{
    public static string lastMethod;
    public static UnityEngine.Vector2 lastSize;
    public static bool Capture(MethodBase __originalMethod, UnityEngine.Vector2 size, ref UnityEngine.Mesh __result)
    {
        lastMethod = __originalMethod.Name; lastSize = size; __result = null; return false;
    }
}
