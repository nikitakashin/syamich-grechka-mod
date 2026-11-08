using System.Runtime.CompilerServices;
using Grechka.LuckyBlocks;
using HarmonyLib;
using RimWorld;
using Verse;
void Check(bool test,string label){if(!test)throw new Exception(label);Console.WriteLine("PASS "+label);}
var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game),"maps").SetValue(game,new List<Map>());
AccessTools.Field(typeof(Game),"components").SetValue(game,new List<GameComponent>());
AccessTools.Field(typeof(Game),"tickManager").SetValue(game,new TickManager());
AccessTools.Field(typeof(Game),"uniqueIDsManager").SetValue(game,new UniqueIDsManager());
Current.Game=game;
var prefs=AccessTools.Field(typeof(Prefs),"data");prefs.SetValue(null,Activator.CreateInstance(prefs.FieldType));
var harmony=new Harmony("grechka.lucky.native-tests");
harmony.CreateClassProcessor(typeof(RaiderLuckyLoot)).Patch();
Check(Harmony.GetAllPatchedMethods().Any(m=>m.DeclaringType==typeof(IncidentWorker_RaidEnemy)&&m.Name=="GenerateRaidLoot"),"Raid loot hook binds to actual 1.7 method");
var blockDef=new ThingDef{defName="Grechka_TestLucky",label="Test block",thingClass=typeof(LuckyBlock),category=ThingCategory.Item,stackLimit=1,useHitPoints=false,comps=new(),modExtensions=new(){new LuckyProperties{pool="missing"}}};
var itemDef=new ThingDef{defName="Grechka_TestReward",label="reward",thingClass=typeof(ThingWithComps),category=ThingCategory.Item,stackLimit=75,useHitPoints=false,comps=new()};
DefDatabase<ThingDef>.Add(blockDef);DefDatabase<ThingDef>.Add(itemDef);
LuckyMod.Config=new LuckyConfig();
var block=new LuckyBlock{def=blockDef,thingIDNumber=4441};
Check(block.Problem()!=null && !block.Open() && !block.Destroyed,"Unavailable pool and unspawned block remain intact");
AccessTools.Field(typeof(LuckyBlock),"outcomeJson").SetValue(block,"{\"id\":\"bad\",\"effect\":\"animals\",\"pawnKind\":\"Cat\",\"pawnCount\":101}");
Check(block.Problem()!=null && !block.Destroyed && block.GetDirectlyHeldThings().Count==0,
 "Malformed saved outcome is reported without consuming the physical block or generating oversized results");
var outcome=new LuckyOutcome{id="chosen",items=new(){new LuckyItem{def=itemDef.defName,count=80}}};
string snapshot=LuckyCodec.EncodeOutcome(outcome);
AccessTools.Field(typeof(LuckyBlock),"outcomeJson").SetValue(block,snapshot);
AccessTools.Field(typeof(LuckyBlock),"prepared").SetValue(block,true);
AccessTools.Field(typeof(LuckyBlock),"itemEntry").SetValue(block,1);
AccessTools.Field(typeof(LuckyBlock),"itemCount").SetValue(block,-5);
Check(block.Problem()!=null && !block.Destroyed,"Negative materialization cursor cannot generate extra rewards after loading");
AccessTools.Field(typeof(LuckyBlock),"itemCount").SetValue(block,0);
AccessTools.Field(typeof(LuckyBlock),"pawnCount").SetValue(block,1);
Check(block.Problem()!=null && !block.Destroyed,"Prepared item-only outcome rejects an impossible pawn materialization state");
AccessTools.Field(typeof(LuckyBlock),"pawnCount").SetValue(block,0);
Check(block.Problem()==null,"Valid prepared state is still resumable after strict cursor checks");
var reward=new ThingWithComps{def=itemDef,stackCount=5,thingIDNumber=4442};
block.GetDirectlyHeldThings().TryAdd(reward,false);
string path=Path.Combine(Path.GetTempPath(),"grechka-lucky-"+Guid.NewGuid().ToString("N")+".xml");
try{
 Scribe.saver.InitSaving(path,"luckyFixture");Scribe_Deep.Look(ref block,"block");Scribe.saver.FinalizeSaving();
 LuckyBlock restored=null;Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref restored,"block");Scribe.loader.FinalizeLoading();
 var remaining=restored.GetDirectlyHeldThings().Single();
 Check(remaining.stackCount==5 && ReferenceEquals(remaining.holdingOwner.Owner,restored),"Real Scribe preserves only remaining rewards and their owner");
 Check((string)AccessTools.Field(typeof(LuckyBlock),"outcomeJson").GetValue(restored)==snapshot && (bool)AccessTools.Field(typeof(LuckyBlock),"prepared").GetValue(restored),"Real Scribe preserves selected outcome and prepared state without reroll");
 Check((int)AccessTools.Field(typeof(LuckyBlock),"itemEntry").GetValue(restored)==1,"Partial materialization cursor survives loading");
 restored.Destroy();Check(remaining.Destroyed,"Destroyed block disposes owned contents once");
}finally{if(File.Exists(path))File.Delete(path);}
Console.WriteLine("Isolated game library checks only; no game process was launched or controlled.");
var valueStat = new StatDef { defName = "LuckyFixtureMarketValue", defaultBaseValue = 0, workerClass = typeof(StatWorker) };
StatDefOf.MarketValue = valueStat;
itemDef.statBases = new() { new StatModifier { stat = valueStat, value = 20 } };
var pricedBlock = new ThingDef { defName = "LuckyPricedFixture", category = ThingCategory.Item,
    thingClass = typeof(LuckyBlock), statBases = new() { new StatModifier { stat = valueStat, value = 9500 } },
    modExtensions = new() { new LuckyProperties { pool = "priced" } } };
DefDatabase<ThingDef>.Add(pricedBlock);
LuckyMod.Config = new LuckyConfig { pools = new() { new LuckyPool { id = "priced", outcomes = new() {
    new LuckyOutcome { id = "valid", items = new() { new LuckyItem { def = itemDef.defName, count = 3 } } },
    new LuckyOutcome { id = "unavailable", weight = 99, items = new() { new LuckyItem { def = "MissingOptionalReward", count = 1000 } } }
} } } };
Rand.PushState(3912); float nextRandom = Rand.Value; Rand.PopState();
Rand.PushState(3912); LuckyMarketValues.Refresh(); float afterPrice = Rand.Value; Rand.PopState();
Check(pricedBlock.statBases.Single(s => s.stat == valueStat).value == 60,
    "Unopened block price uses actual available rewards rather than a universal tier price or unavailable optional outcomes");
Check(nextRandom == afterPrice && (string)AccessTools.Field(typeof(LuckyBlock), "outcomeJson").GetValue(block) == snapshot,
    "Price refresh consumes no random roll and never changes a saved block outcome");
itemDef.statBases.Single(s => s.stat == valueStat).value = 30;
valueStat.Worker.DeleteStatCache(); LuckyMarketValues.Refresh();
Check(pricedBlock.statBases.Single(s => s.stat == valueStat).value == 90,
    "Repricing follows changed material market values and is idempotent");
var weighted = new[] {
    new LuckyOutcome { id = "goods", weight = 1, items = new() { new LuckyItem { count = 3 } } },
    new LuckyOutcome { id = "animals", weight = 3, effect = "animals", pawnCount = 2 },
    new LuckyOutcome { id = "hostile", weight = 2, effect = "hostileMechs", pawnCount = 12 }
};
Check(LuckyMarketValues.ExpectedValue(weighted, _ => 10, _ => 100) == 105,
    "Weighted valuation includes owned animals and gives hostile mechs no saleable pawn value");
LuckyMarketValues.Refresh();
Check(pricedBlock.statBases.Single(s => s.stat == valueStat).value == 90,
    "Repeated refresh does not compound the block value");

var fixedBlock = new ThingDef { defName = "LuckyFixedPriceFixture", category = ThingCategory.Item,
    thingClass = typeof(LuckyBlock), statBases = new() { new StatModifier { stat = valueStat, value = 3000 } },
    modExtensions = new() { new LuckyProperties { pool = "fixed", priceFromPool = false } } };
DefDatabase<ThingDef>.Add(fixedBlock);
LuckyMod.Config.pools.Add(new LuckyPool { id = "fixed", outcomes = new() {
    new LuckyOutcome { id = "goods", items = new() { new LuckyItem { def = itemDef.defName, count = 1 } } }
} });
LuckyMarketValues.Refresh();
Check(fixedBlock.statBases.Single(s => s.stat == valueStat).value == 3000,
    "General block keeps its agreed authored tier price when thematic prices refresh");
LuckyMod.Config.pools.Single(p => p.id == "priced").outcomes = new() {
    new LuckyOutcome { id = "nested", items = new() { new LuckyItem { def = fixedBlock.defName, count = 2 } } }
};
LuckyMarketValues.Refresh();
Check(pricedBlock.statBases.Single(s => s.stat == valueStat).value == 6000,
    "A thematic pool values nested general blocks at their actual fixed price");
FleshTypeDefOf.Mechanoid = new FleshTypeDef { defName = "LuckyMechanoidFleshFixture" };
var mechKind = new PawnKindDef { defName = "LuckyHostileMechFixture", race = new ThingDef {
    defName = "LuckyHostileRaceFixture", race = new RaceProperties()
} };
AccessTools.Field(typeof(RaceProperties), "fleshType").SetValue(mechKind.race.race, FleshTypeDefOf.Mechanoid);
DefDatabase<PawnKindDef>.Add(mechKind);
LuckyMod.Config.pools.Single(p => p.id == "priced").outcomes = new() {
    new LuckyOutcome { id = "goods", items = new() { new LuckyItem { def = itemDef.defName, count = 2 } } },
    new LuckyOutcome { id = "risk", effect = "hostileMechs", pawnKind = mechKind.defName }
};
var previousGame = Current.Game;
try
{
    Current.Game = null;
    LuckyMarketValues.Refresh();
    Check(pricedBlock.statBases.Single(s => s.stat == valueStat).value == 30,
        "Startup valuation needs no world and retains hostile-mech risk in the lottery weights");
}
finally { Current.Game = previousGame; }
