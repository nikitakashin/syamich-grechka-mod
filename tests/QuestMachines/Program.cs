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
static class Fixture{
 public static bool Owned=true,Colony=true;static Faction player=new Faction();
 public static bool HasColony(ref bool __result){__result=Colony;return false;}
 public static bool NotHome(ref bool __result){__result=false;return false;}
 public static bool PlayerFaction(ref Faction __result){__result=player;return false;}
 public static bool MachineFaction(ref Faction __result){__result=Owned?player:null;return false;}
}
