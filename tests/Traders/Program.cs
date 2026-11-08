using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Grechka.Traders;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

// The game sets invariant culture during bootstrap; the isolated harness must do the same.
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var native = typeof(ThingDef).Assembly;
var own = typeof(SpecialistDef).Assembly;
Type Resolve(string name) => new[] { native, own }.SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.Name == name || t.FullName == name);
void Schema(XElement node, Type type, bool top = false)
{
    if (node.Attribute("Class") is XAttribute cls)
    {
        var concrete = Resolve(cls.Value) ?? throw new Exception("Unknown XML Class " + cls.Value);
        Check(type.IsAssignableFrom(concrete), "XML class is assignable: " + cls.Value);
        type = concrete;
    }
    if (!node.HasElements || typeof(Def).IsAssignableFrom(type) && !top) return;
    if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
    { foreach (var child in node.Elements()) Schema(child, type.GetGenericArguments()[0]); return; }
    foreach (var child in node.Elements())
    {
        var field = AccessTools.Field(type, child.Name.LocalName) ?? throw new Exception("Unknown XML field " + type + "." + child.Name);
        Schema(child, field.FieldType);
    }
}
var definitions = Directory.GetFiles(Path.Combine(root, "mods/GrechkaTraders/Defs"), "*.xml").SelectMany(p => XDocument.Load(p).Root.Elements()).ToList();
foreach (var def in definitions) Schema(def, Resolve(def.Name.LocalName) ?? throw new Exception("Unknown XML root " + def.Name), true);
Check(true, "All shipped XML fields and classes exist in native RimWorld 1.7 or the new assembly");
var settings = GrechkaTradersMod.Settings = new TraderSettings();
Check(settings.orbitalFrequency == 1 && settings.caravanFrequency == 1 && !settings.customSlaveCount && settings.discountMultiplier == 1, "Defaults retain native frequency/slaves and apply 15% specialist discount");
settings.orbitalFrequency = float.NaN; settings.caravanFrequency = 8; settings.slavesPerCaravan = 99; settings.discountMultiplier = float.PositiveInfinity;
settings.Normalize();
Check(settings.orbitalFrequency == 1 && settings.caravanFrequency == 5 && settings.slavesPerCaravan == 50 && settings.discountMultiplier == 1, "Settings sanitize nonfinite and excessive values");
settings.orbitalFrequency = settings.caravanFrequency = 1;

ModContentPack Pack(string id)
{
    var pack = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
    AccessTools.Field(typeof(ModContentPack), "packageIdInt").SetValue(pack, id);
    AccessTools.Field(typeof(ModContentPack), "packageIdPlayerFacingInt").SetValue(pack, id);
    return pack;
}
var mod = Pack("grechka.industry");
var otherMod = Pack("other.mod");
ThingDef Item(string name, ModContentPack pack, Type cls = null) => new ThingDef {
    defName = name, thingClass = cls ?? typeof(ThingWithComps), category = ThingCategory.Item,
    modContentPack = pack, tradeability = Tradeability.All, stackLimit = 75, comps = new List<CompProperties>() };
var resource = Item("Fixture_GrechkaResource", mod);
var extra = Item("Fixture_Support", otherMod);
var unsupported = Item("Fixture_Unrelated", otherMod);
var shell = Item("Fixture_Packed", mod, typeof(MinifiedThing));
var unfinished = Item("Fixture_Unfinished", mod, typeof(UnfinishedThing));
var forbidden = Item("Fixture_Forbidden", mod); forbidden.tradeability = Tradeability.None;
var buyOnly = Item("Fixture_PlayerSellsOnly", mod); buyOnly.tradeability = Tradeability.Sellable;
var building = new ThingDef { defName = "Fixture_Machine", thingClass = typeof(TrackedBuilding), category = ThingCategory.Building,
    modContentPack = mod, minifiedDef = shell, tradeability = Tradeability.All, stackLimit = 1, comps = new List<CompProperties>() };
foreach (var def in new[] {resource, extra, unsupported, shell, unfinished, forbidden, buyOnly, building}) DefDatabase<ThingDef>.Add(def);
var specialist = new SpecialistDef { defName = "Fixture_Specialist", packageIds = new() { "GRECHKA.INDUSTRY" }, extraThingDefs = new() { extra.defName }, offers = 12 };
Check(specialist.Owns(resource) && !specialist.Owns(extra) && specialist.Matches(extra), "Ownership is case-insensitive; supporting supply matching is separate");
var curated = new SpecialistDef { defName = "Fixture_Curated", packageIds = new() { "missing.fixture" },
    curatedPackageIds = new() { "GRECHKA.INDUSTRY" }, extraThingDefs = new() { resource.defName } };
Check(curated.Owns(resource) && curated.Matches(resource) && !curated.Owns(building) && !curated.Matches(building),
    "Curated package ownership selects only its explicit thematic items without annexing the module catalogue");
Check(SpecialistStock.Discount(curated, resource) == .15f && SpecialistStock.Discount(curated, building) == 0,
    "Curated specialty discount applies only to selected goods from their actual owning package");
Check(SpecialistStock.Safe(resource) && SpecialistStock.Safe(building), "Resources and genuinely minifiable buildings are candidates");
Check(!SpecialistStock.Safe(shell) && !SpecialistStock.Safe(unfinished) && !SpecialistStock.Safe(forbidden) && !SpecialistStock.Safe(buyOnly), "Empty shells, unfinished, forbidden and non-trader-sellable defs cannot be generated");
var candidates = SpecialistStock.Candidates(specialist);
Check(candidates.Count == 3 && candidates.Contains(building) && !candidates.Contains(unsupported), "Stock pool contains only safe themed goods and explicit supporting supplies");
var limited = new SpecialistDef { defName = "Fixture_TwoOffers", packageIds = new() { "grechka.industry" },
    extraThingDefs = new() { extra.defName }, offers = 2 };
Rand.PushState(77);
var selected = SpecialistStock.SelectOffers(limited, new[] { resource, building, extra });
Rand.PopState();
Check(selected.Count == 2 && selected.Contains(extra) && selected.Any(limited.Owns),
    "Themed catalogues cannot crowd every supporting-supply offer out of a small caravan");
var generator = new StockGenerator_Specialist { specialist = specialist };
var trader = new TraderKindDef {defName = "Caravan_Fixture", stockGenerators = new() { generator }, modExtensions = new() { new SpecialistExtension { specialist = specialist } } };
generator.ResolveReferences(trader);
Check(generator.HandlesThingDef(shell) && trader.WillTrade(shell), "Packed building shells remain tradable in the native trade-window predicate");
Check(!generator.HandlesThingDef(unsupported), "Unrelated loose goods are outside the specialist buy list");
Check(SpecialistStock.Discount(specialist, resource) == .15f && SpecialistStock.Discount(specialist, extra) == 0, "15% discount covers mod goods, supporting supplies use normal prices");
settings.discountMultiplier = 2;
Check(SpecialistStock.Discount(specialist, building) == .3f, "Maximum discount is 30%, including packed-building inner defs");
settings.discountMultiplier = 1;

var testHarmony = new Harmony("grechka.traders.native.fixture");
testHarmony.Patch(AccessTools.Method(typeof(ModsConfig), nameof(ModsConfig.IsActive), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Active)));
testHarmony.Patch(AccessTools.Method(typeof(ThingMaker), nameof(ThingMaker.MakeThing)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.MakeThing)));
testHarmony.Patch(AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.BaseMarketValue)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.MarketValue)));
Fixture.ActiveOnly = "grechka.industry";
Check(curated.Active, "A curated module activates its specialist even when the broad owner modules are absent");
Fixture.ActiveOnly = null;
var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map>());
AccessTools.Field(typeof(Game), "uniqueIDsManager").SetValue(game, new UniqueIDsManager());
Current.Game = game;
var prefs = AccessTools.Field(typeof(Prefs), "data");
prefs.SetValue(null, Activator.CreateInstance(prefs.FieldType));
Rand.PushState(417);
var generated = generator.GenerateThings(PlanetTile.Invalid).ToList();
Rand.PopState();
Check(generated.Count == 3 && generated.All(t => t.stackCount > 0 && t.stackCount <= t.def.stackLimit), "Native stock utility produces finite, nonempty, bounded themed stock");
var packed = generated.OfType<MinifiedThing>().Single();
Check(packed.InnerThing?.def == building && ((TrackedBuilding)packed.InnerThing).finalized == 1, "Native minification keeps a real building and finalizes the inner building once");
Check(generated.Select(t => t is MinifiedThing m ? m.InnerThing.def : t.def).Distinct().Count() == 3, "Each offer uses a different candidate; no empty shell or unrelated stock");
Fixture.ActiveMods = false;
Check(!generator.GenerateThings(PlanetTile.Invalid).Any(), "Unavailable optional mod disables stock generation");
float commonality = .15f;
SpecialistAvailability.Postfix(trader, ref commonality);
Check(commonality == 0f, "Unavailable specialist has zero selection commonality");
Fixture.ActiveMods = true;
settings.specialistsEnabled = false; commonality = .15f;
SpecialistAvailability.Postfix(trader, ref commonality);
Check(commonality == 0f, "Settings can stop new specialist selection");
settings.specialistsEnabled = true;

TradeSession.trader = new TestTrader(trader);
float price = 100f; SpecialistPrice.Postfix(resource.MakeThingForTest(), ref price);
Check(Math.Abs(price - 85f) < .001f, "Trade price applies a single 15% discount to matching loose goods");
price = 100f; SpecialistPrice.Postfix(packed, ref price);
Check(Math.Abs(price - 85f) < .001f, "Packed machine price uses its actual inner mod ownership");
price = 100f; SpecialistPrice.Postfix(extra.MakeThingForTest(), ref price);
Check(price == 100f, "Supporting supplies retain their native price");
TradeSession.trader = null;
price = 100f; SpecialistPrice.Postfix(resource.MakeThingForTest(), ref price);
Check(price == 100f, "Price calls outside a trade session retain native value");

var slaveGenerator = new StockGenerator_Slaves { trader = trader, countRange = new IntRange(2, 4) };
settings.customSlaveCount = true; settings.slavesPerCaravan = 17;
var copy = CaravanSlaveCount.ConfiguredCopy(slaveGenerator);
Check(!ReferenceEquals(copy, slaveGenerator) && copy.countRange.min == 17 && copy.countRange.max == 17 && slaveGenerator.countRange.max == 4, "Slave override uses a private copy and preserves shared stock XML");
Check(!(bool)AccessTools.Field(typeof(StockGenerator_Slaves), "respectPopulationIntent").GetValue(copy), "Explicit count is independent of colony population suppression");
var harmony = new Harmony("grechka.traders.native.checks");
harmony.PatchAll(own);
Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(TradeUtility), nameof(TradeUtility.GetPricePlayerBuy))).Owners.Contains(harmony.Id), "Discount hook binds to actual native buy-price method");
Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(StockGenerator_Slaves), nameof(StockGenerator_Slaves.GenerateThings))).Owners.Contains(harmony.Id), "Slave iterator hook binds to actual native generator");
var iterator = slaveGenerator.GenerateThings(PlanetTile.Invalid);
var owner = (StockGenerator_Slaves)AccessTools.Field(iterator.GetType(), "<>4__this").GetValue(iterator);
Check(!ReferenceEquals(owner, slaveGenerator) && owner.countRange.min == 17 && owner.countRange.max == 17, "Native lazy slave iterator captures the private configured generator");
settings.slavesPerCaravan = 0;
Check(!slaveGenerator.GenerateThings(PlanetTile.Invalid).Any(), "Zero-count native slave generation returns no pawns");
trader.orbital = true;
Check(!CaravanSlaveCount.ShouldOverride(slaveGenerator), "Orbital slave stock is unaffected");
trader.orbital = false; trader.defName = "Base_Fixture";
Check(!CaravanSlaveCount.ShouldOverride(slaveGenerator), "Settlement slave stock is unaffected");
settings.customSlaveCount = false; trader.defName = "Caravan_Fixture";
Check(!CaravanSlaveCount.ShouldOverride(slaveGenerator), "Default slave setting retains the native iterator owner");

DefDatabase<TraderKindDef>.Add(trader);
var nativeTrader = new TraderKindDef { defName = "Caravan_FixtureNative" };
var sharedList = new List<TraderKindDef> { nativeTrader };
var friendlyFaction = new FactionDef { defName = "Fixture_Outlanders", humanlikeFaction = true, techLevel = TechLevel.Industrial, caravanTraderKinds = sharedList };
var tribalFaction = new FactionDef { defName = "Fixture_Tribe", humanlikeFaction = true, techLevel = TechLevel.Neolithic, caravanTraderKinds = sharedList };
var hostileFaction = new FactionDef { defName = "Fixture_Pirates", humanlikeFaction = true, permanentEnemy = true, techLevel = TechLevel.Industrial, caravanTraderKinds = sharedList };
foreach (var def in new[] { friendlyFaction, tribalFaction, hostileFaction }) DefDatabase<FactionDef>.Add(def);
TraderRegistration.Register(); TraderRegistration.Register();
Check(friendlyFaction.caravanTraderKinds.Count(t => t == trader) == 1 && friendlyFaction.caravanTraderKinds.Contains(nativeTrader), "Registration preserves native traders and adds the specialist exactly once");
Check(sharedList.Count == 1 && !tribalFaction.caravanTraderKinds.Contains(trader) && !hostileFaction.caravanTraderKinds.Contains(trader), "Registration copies inherited collections and preserves tribal/hostile faction roles");
settings.customSlaveCount = true; trader.defName = "ThirdParty_CustomMerchant";
Check(CaravanSlaveCount.ShouldOverride(slaveGenerator), "Caravan-role lookup supports modded names outside native Caravan prefix");

string path = Path.Combine(Path.GetTempPath(), "grechka-traders-settings-" + Guid.NewGuid().ToString("N") + ".xml");
try
{
    TraderSettings saved = new TraderSettings { orbitalFrequency = 2.5f, caravanFrequency = .4f, customSlaveCount = true, slavesPerCaravan = 23, discountMultiplier = 2f, specialistsEnabled = false };
    Scribe.saver.InitSaving(path, "settings"); Scribe_Deep.Look(ref saved, "traders"); Scribe.saver.FinalizeSaving();
    TraderSettings loaded = null;
    Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "traders"); Scribe.loader.FinalizeLoading();
    Check(loaded.orbitalFrequency == 2.5f && loaded.caravanFrequency == .4f && loaded.customSlaveCount && loaded.slavesPerCaravan == 23 && loaded.discountMultiplier == 2f && !loaded.specialistsEnabled,
        "Native Scribe save/load preserves every settings control");
}
finally { if (File.Exists(path)) File.Delete(path); }
Console.WriteLine($"PASS {checks} GrechkaTraders native/schema checks. No game launched or controlled.");

public sealed class TrackedBuilding : Building
{
    public int finalized;
    public override void PostGeneratedForTrader(TraderKindDef trader, PlanetTile tile, Faction faction) { finalized++; }
}
static class Fixture
{
    public static bool ActiveMods = true;
    public static string ActiveOnly;
    public static bool Active(string __0, ref bool __result) { __result = ActiveMods && (ActiveOnly == null || string.Equals(__0, ActiveOnly, StringComparison.OrdinalIgnoreCase)); return false; }
    public static bool MarketValue(ref float __result) { __result = 100f; return false; }
    public static bool MakeThing(ThingDef def, ref Thing __result) { __result = (Thing)Activator.CreateInstance(def.thingClass); __result.def = def; __result.stackCount = 1; return false; }
}
static class ThingFixtureExtension
{
    public static Thing MakeThingForTest(this ThingDef def) => new ThingWithComps { def = def, stackCount = 1 };
}
sealed class TestTrader(TraderKindDef kind) : ITrader
{
    public TraderKindDef TraderKind => kind;
    public IEnumerable<Thing> Goods => Enumerable.Empty<Thing>();
    public int RandomPriceFactorSeed => 0;
    public string TraderName => "fixture";
    public bool CanTradeNow => true;
    public float TradePriceImprovementOffsetForPlayer => 0;
    public Faction Faction => null;
    public TradeCurrency TradeCurrency => TradeCurrency.Silver;
    public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn pawn) => Enumerable.Empty<Thing>();
    public void GiveSoldThingToTrader(Thing thing, int count, Pawn pawn) { }
    public void GiveSoldThingToPlayer(Thing thing, int count, Pawn pawn) { }
}
