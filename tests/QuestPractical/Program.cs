using System.Runtime.CompilerServices;
using Grechka.Quests;
using HarmonyLib;
using RimWorld;
using Verse;
using Quest = Grechka.Quests.Quest;
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
var harmony = new Harmony("grechka.practical-quests.isolated");
harmony.Patch(AccessTools.PropertyGetter(typeof(QuestGame), nameof(QuestGame.HasColony)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.HasColony)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.PlayerFaction)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Faction)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Owner)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Dead)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.IsDead)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsPrisoner)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.IsPrisoner)));
var packedDef = new ThingDef { defName="PracticalPacked", category=ThingCategory.Item, thingClass=typeof(MinifiedThing) };
var dockDef = new ThingDef { defName="PracticalDock", category=ThingCategory.Building, thingClass=typeof(Building), minifiedDef=packedDef, building=new() };
var wallDef = new ThingDef { defName="PracticalWall", category=ThingCategory.Building, thingClass=typeof(Building), building=new() };
var pawnDef = new ThingDef { defName="PracticalRobot", category=ThingCategory.Pawn, thingClass=typeof(Pawn), race=new() };
foreach (var def in new[] { packedDef, dockDef, wallDef, pawnDef }) DefDatabase<ThingDef>.Add(def);
Check(QuestGame.ObjectiveProblem(new Objective { type="produce", target=dockDef.defName })==null, "Minifiable robot docks are valid production goals");
Check(QuestGame.ObjectiveProblem(new Objective { type="produce", target=wallDef.defName })!=null, "An ordinary uncraftable wall is not treated as recipe output");
Check(QuestGame.ObjectiveProblem(new Objective { type="have", target=pawnDef.defName })==null, "A deployed player robot is a valid possession goal");
Check(QuestGame.ObjectiveProblem(new Objective { type="have", target=dockDef.defName })!=null, "Possession does not silently accept an arbitrary building");
var goal = new Objective { id="made",type="produce",target=dockDef.defName,count=5 };
var deployed = new Objective { id="deployed",type="have",target=pawnDef.defName,count=1 };
var quest = new Quest { id="robot",objectives=new() { goal,deployed } };
var pack = new QuestPack { id="practical.fixture",quests=new() { quest } };
var component = (QuestGame)RuntimeHelpers.GetUninitializedObject(typeof(QuestGame));component.Pack=pack;
var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game), "maps").SetValue(game,new List<Map>());
AccessTools.Field(typeof(Game), "components").SetValue(game,new List<GameComponent> { component });Current.Game=game;
var pawn = new Pawn { def=pawnDef };
AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(pawn,(sbyte)0);
var dock = new Building { def=dockDef };
var packed = new MinifiedThing { def=packedDef,InnerThing=dock };
var recipe = AccessTools.Method(typeof(QuestGame).Assembly.GetType("Grechka.Quests.RecipeOutputHook"),"Postfix");
QuestProgress State() => (QuestProgress)AccessTools.Property(typeof(QuestGame),"Progress").GetValue(component);
void Produced() => recipe.Invoke(null,new object[] { pawn,new List<Thing> { packed } });
Check(State().EventValue(pack,quest,goal)==0,"Possessing a packed dock gives no production credit");
Produced();Check(State().EventValue(pack,quest,goal)==1,"Completed recipe unwraps and credits the actual dock once");
Fixture.Owned=false;Produced();Check(State().EventValue(pack,quest,goal)==1,"Foreign worker production cannot advance a dock quest");
Fixture.Owned=true;Fixture.Colony=false;Produced();Check(State().EventValue(pack,quest,goal)==1,"A completed dock recipe without a colony gives no credit");
Fixture.Colony=true;
var machine=new Building { def=wallDef };AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(machine,(sbyte)0);
var map=(Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));AccessTools.Field(typeof(Game),"maps").SetValue(game,new List<Map>{map});
component.RecordVerifiedMachineOutput("produce",machine,packed,99);
Check(State().EventValue(pack,quest,goal)==2,"Machine output unwraps a single dock rather than counting a wrapper amount");
var counter=AccessTools.Method(typeof(QuestGame),"CountThing");
int DeployedCount() { var tally=new ObjectiveTally(pack);counter.Invoke(null,new object[]{tally,"have",pawn,null});counter.Invoke(null,new object[]{tally,"have",pawn,null});return tally.Value(pack,quest,deployed); }
Check(DeployedCount()==1,"A living deployed player robot counts once across repeated scans");
Fixture.Owned=false;Check(DeployedCount()==0,"A trader or hostile robot cannot satisfy player deployment");Fixture.Owned=true;
Fixture.Dead=true;Check(DeployedCount()==0,"A dead robot cannot satisfy deployment");Fixture.Dead=false;
Fixture.Prisoner=true;Check(DeployedCount()==0,"A prisoner cannot satisfy player deployment");Fixture.Prisoner=false;
AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(pawn,(sbyte)-1);
Check(DeployedCount()==0,"A contained or unspawned robot cannot satisfy deployment");
Console.WriteLine(checks+" practical native checks passed; no running game was controlled.");
static class Fixture
{
 public static bool Colony=true,Owned=true,Dead,Prisoner;
 static readonly Faction player=new() { def=new FactionDef { isPlayer=true } };
 public static bool HasColony(ref bool __result){__result=Colony;return false;}
 public static bool PlayerFaction(ref Faction __result){__result=player;return false;}
 public static bool Owner(ref Faction __result){__result=Owned?player:null;return false;}
 public static bool IsDead(ref bool __result){__result=Dead;return false;}
 public static bool IsPrisoner(ref bool __result){__result=Prisoner;return false;}
}
