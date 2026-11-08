using System.Runtime.CompilerServices;
using Grechka.Quests;
using HarmonyLib;
using RimWorld;
using Verse;
using Quest=Grechka.Quests.Quest;
void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));var map=(Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));AccessTools.Field(typeof(Game),"maps").SetValue(game,new List<Map>{map});Current.Game=game;
var quests=(QuestGame)RuntimeHelpers.GetUninitializedObject(typeof(QuestGame));AccessTools.Field(typeof(QuestGame),"claimed").SetValue(quests,new List<string>());
var steel=new ThingDef{defName="QuestMachineSteel",label="steel",category=ThingCategory.Item,thingClass=typeof(Thing),stackLimit=75};DefDatabase<ThingDef>.Add(steel);
var goal=new Objective{id="produce",type="produce",target=steel.defName,count=100};var q=new Quest{id="machine",objectives=new(){goal}};quests.Pack=new QuestPack{id="fixture",quests=new(){q}};
var machine=new Building{def=new ThingDef{defName="QuestMachine",category=ThingCategory.Building,building=new()}};AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(machine,(sbyte)0);
var harmony=new Harmony("grechka.machine-objectives.isolated");
harmony.Patch(AccessTools.PropertyGetter(typeof(QuestGame),nameof(QuestGame.HasColony)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.HasColony)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Map),nameof(Map.IsPlayerHome)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.NotHome)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Faction),nameof(Faction.OfPlayer)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.PlayerFaction)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Thing),nameof(Thing.Faction)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.MachineFaction)));
void Record(int n)=>quests.RecordVerifiedMachineOutput("produce",machine,steel,null,null,n);
int Count()=>((QuestProgress)AccessTools.Property(typeof(QuestGame),"Progress").GetValue(quests)).EventValue(quests.Pack,q,goal);
Record(7);Check(!map.IsPlayerHome&&Count()==7,"Owned machine production on a temporary map counts while a colony exists");
Fixture.Owned=false;Record(5);Check(Count()==7,"Foreign machine output cannot advance player objectives");
Fixture.Owned=true;Fixture.Colony=false;Record(5);Check(Count()==7,"No-colony rule still blocks progress despite an owned spawned machine");
Fixture.Colony=true;AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(machine,(sbyte)-1);Record(5);Check(Count()==7,"Unspawned machine cannot claim verified production");
// Deep drills have no native QuestManager production notification. The adapter must
// count only each successful placement callback, not the size of a merged stack.
AccessTools.Field(typeof(Game),"components").SetValue(game,new List<GameComponent>{quests});
var mined=new Objective{id="mine",type="mine",target=steel.defName,count=100};q.objectives.Add(mined);
AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(machine,(sbyte)0);
var drill=new CompDeepDrill{parent=machine};
var hook=typeof(QuestGame).Assembly.GetType("Grechka.Quests.DeepDrillEventHook",true);
harmony.CreateClassProcessor(hook).Patch();
Check(Harmony.GetAllPatchedMethods().Any(m=>m.DeclaringType==typeof(CompDeepDrill)&&m.Name=="TryProducePortion"),"Deep-drill adapter binds the installed native production method");
var place=AccessTools.Method(typeof(GenPlace),nameof(GenPlace.TryPlaceThing),new[]{typeof(Thing),typeof(IntVec3),typeof(Map),typeof(ThingPlaceMode),typeof(Action<Thing,int>),typeof(Predicate<IntVec3>),typeof(Rot4?),typeof(int)});
harmony.Patch(place,prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Place)));
var output=new Thing{def=steel,stackCount=100};
bool DrillPlace()=> (bool)AccessTools.Method(hook,"PlaceProduced").Invoke(null,new object[]{output,new IntVec3(0,0,0),map,ThingPlaceMode.Near,null,null,null,1,drill,null});
int Mined()=>((QuestProgress)AccessTools.Property(typeof(QuestGame),"Progress").GetValue(quests)).EventValue(quests.Pack,q,mined);
Fixture.Placements=new[]{3,4};Fixture.Placed=false;
Check(!DrillPlace()&&Mined()==7&&Count()==7,"Partial deep-drill placement counts exactly seven mined units despite a merged 100-item stack and no full-placement success");
Fixture.Placements=Array.Empty<int>();DrillPlace();Check(Mined()==7,"Failed placement cannot invent deep-drill output");
Fixture.Placements=new[]{5};Fixture.Owned=false;DrillPlace();Check(Mined()==7,"Foreign deep-drill output is not credited");
Fixture.Owned=true;GenPlace.TryPlaceThing(output,new IntVec3(0,0,0),map,ThingPlaceMode.Near);Check(Mined()==7,"Unrelated placement or transfer outside the drill cycle cannot count as mining");
static class Fixture{
 public static bool Owned=true,Colony=true;static Faction player=new Faction();
 public static bool HasColony(ref bool __result){__result=Colony;return false;}
 public static bool NotHome(ref bool __result){__result=false;return false;}
 public static bool PlayerFaction(ref Faction __result){__result=player;return false;}
 public static bool MachineFaction(ref Faction __result){__result=Owned?player:null;return false;}
 public static int[] Placements=Array.Empty<int>();public static bool Placed;
 public static bool Place(Thing __0,Action<Thing,int> __4,ref bool __result){foreach(int amount in Placements)__4?.Invoke(__0,amount);__result=Placed;return false;}
}
