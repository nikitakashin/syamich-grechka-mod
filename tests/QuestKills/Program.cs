using System.Runtime.CompilerServices;
using Grechka.Quests;
using HarmonyLib;
using RimWorld;
using Verse;
using Quest=Grechka.Quests.Quest;
int checks=0;
void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game),"maps").SetValue(game,new List<Map>());Current.Game=game;
var quests=(QuestGame)RuntimeHelpers.GetUninitializedObject(typeof(QuestGame));
foreach(var field in typeof(QuestGame).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic))
 if(field.FieldType.IsGenericType&&field.FieldType.GetGenericTypeDefinition()==typeof(List<>))field.SetValue(quests,Activator.CreateInstance(field.FieldType));
AccessTools.Field(typeof(Game),"components").SetValue(game,new List<GameComponent>{quests});
var prefs=AccessTools.Field(typeof(Prefs),"data");prefs.SetValue(null,Activator.CreateInstance(prefs.FieldType));
var cow=new ThingDef{defName="Cow",label="cow",category=ThingCategory.Pawn,thingClass=typeof(Pawn),race=new RaceProperties()};
var muffalo=new ThingDef{defName="Muffalo",label="muffalo",category=ThingCategory.Pawn,thingClass=typeof(Pawn),race=new RaceProperties()};
var steel=new ThingDef{defName="Steel",label="steel",category=ThingCategory.Item,thingClass=typeof(Thing)};
foreach(var d in new[]{cow,muffalo,steel})DefDatabase<ThingDef>.Add(d);
var goal=new Objective{id="cows",type="kill",target="Cow",count=10};
var first=new Quest{id="intro",objectives=new()};var q=new Quest{id="slaughter",requires=new(){"intro"},objectives=new(){goal}};
quests.Pack=new QuestPack{id="kill.fixture",chapters=new(){new Chapter()},quests=new(){first,q}};
var pack=PackCodec.Decode(PackCodec.Encode(quests.Pack));Check(pack.quests[1].objectives[0].type=="kill"&&pack.quests[1].objectives[0].id=="cows","Kill JSON roundtrip preserves stable fields");
goal.stuff="Steel";Check(PackCodec.Validate(quests.Pack).Count>0,"Kill objectives reject materials");goal.stuff=null;
goal.minQuality="Normal";Check(PackCodec.Validate(quests.Pack).Count>0,"Kill objectives reject quality");goal.minQuality=null;
Check(QuestGame.ObjectiveProblem(goal)==null,"Native race ThingDef is a valid kill target");goal.target="Steel";Check(QuestGame.ObjectiveProblem(goal)!=null,"Items cannot become kill targets");goal.target="Cow";
var harmony=new Harmony("grechka.kill-objectives.isolated");
harmony.Patch(AccessTools.PropertyGetter(typeof(QuestGame),nameof(QuestGame.HasColony)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.HasColony)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Faction),nameof(Faction.OfPlayer)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.PlayerFaction)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Thing),nameof(Thing.Faction)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Faction)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Pawn),nameof(Pawn.IsPrisoner)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Prisoner)));
// Bind the real 1.7 Pawn.Kill hook, but do not execute its map/corpse/game managers here.
harmony.CreateClassProcessor(typeof(KillEventHook)).Patch();
Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(Pawn),nameof(Pawn.Kill))).Owners.Contains(harmony.Id),"Death hook binds to actual RimWorld 1.7 Pawn.Kill");
Pawn Animal(ThingDef d){var pawn=new Pawn{def=d};pawn.health=(Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));AccessTools.Field(typeof(Pawn_HealthTracker),"pawn").SetValue(pawn.health,pawn);AccessTools.Field(typeof(Pawn_HealthTracker),"healthState").SetValue(pawn.health,PawnHealthState.Mobile);return pawn;}
var killer=Animal(cow);Fixture.Owned.Add(killer);
var enemy=Animal(cow);
var damageDef=new DamageDef{defName="FixtureCut"};
DamageInfo Damage(Thing actor)=>new DamageInfo(damageDef,10,instigator:actor);
void Death(Pawn victim,DamageInfo? damage,bool finishes=true){KillEventHook.Prefix(victim,damage,out var attempt);if(finishes&&!victim.Dead)victim.health.SetDead();KillEventHook.Postfix(victim,attempt);}
QuestProgress Progress()=> (QuestProgress)AccessTools.Property(typeof(QuestGame),"Progress").GetValue(quests);
int Count()=>quests.Value(q,goal);
var victim=Animal(cow);Death(victim,Damage(killer));Check(Count()==1&&!Progress().Unlocked(quests.Pack,q),"Owned killing advances a locked quest immediately");
Death(victim,Damage(killer));Check(Count()==1,"A previously dead pawn cannot count again");
Death(Animal(cow),Damage(killer),false);Check(Count()==1,"A prevented death cannot count");
Death(Animal(cow),Damage(enemy));Death(Animal(cow),null);Check(Count()==1,"Enemy kills and unattributed natural deaths do not count");
Death(Animal(muffalo),Damage(killer));Check(Count()==1,"Different pawn races do not count for Cow");
Fixture.PrisonerPawn=killer;Death(Animal(cow),Damage(killer));Fixture.PrisonerPawn=null;Check(Count()==1,"Prisoner instigators do not impersonate colony workers");
var turret=new Building();Fixture.Owned.Add(turret);Death(Animal(cow),Damage(turret));Check(Count()==2,"A player-owned combat building counts");
Fixture.Colony=false;Death(Animal(cow),Damage(killer));Fixture.Colony=true;Check(Count()==2,"No-colony rule prevents new kill progress");
for(int i=0;i<8;i++)Death(Animal(cow),Damage(killer));Check(Count()==10&&Progress().Achieved(quests.Pack,q,goal)&&!Progress().Ready(quests.Pack,q,true),"Ten kills finish the goal before its reward chain is unlocked");
Progress().RecordClaim(quests.Pack,first);Check(Progress().Ready(quests.Pack,q,true),"Claiming predecessor unlocks the already completed kill reward");
Progress().RecordClaim(quests.Pack,q);
string save=Path.Combine(Path.GetTempPath(),"grechka-kills-"+Guid.NewGuid().ToString("N")+".xml");
try{
 Scribe.saver.InitSaving(save,"kills");quests.ExposeData();Scribe.saver.FinalizeSaving();
 var restored=(QuestGame)RuntimeHelpers.GetUninitializedObject(typeof(QuestGame));
 Scribe.loader.InitLoading(save);restored.ExposeData();Scribe.loader.FinalizeLoading();
 Scribe.mode=LoadSaveMode.PostLoadInit;restored.ExposeData();Scribe.mode=LoadSaveMode.Inactive;
 var savedQuest=restored.Pack.quests[1];Check(restored.LoadError==null&&restored.Value(savedQuest,savedQuest.objectives[0])==10&&restored.Claimed(savedQuest),"Real Scribe preserves kill counters and claimed reward history");
}finally{Scribe.mode=LoadSaveMode.Inactive;if(File.Exists(save))File.Delete(save);}
var preview=new QuestPreview(quests.Pack);var previewQuest=preview.Pack.quests[1];preview.Mark(previewQuest,previewQuest.objectives[0]);Check(preview.Achieved(previewQuest,previewQuest.objectives[0])&&Count()==10,"Editor preview supports kill goals without changing colony counters");
var edited=new Objective{id="existing",type="have",target="Steel",stuff="Steel",minQuality="Normal"};QuestEditor.ChangeObjective(edited,"kill","Cow");Check(edited.type=="kill"&&edited.stuff==null&&edited.minQuality==null&&edited.id!="existing","Editor changes semantic goal identity and clears item-only filters");
QuestSnapshotRegression.Run(quests, args, Check);
Console.WriteLine(checks+" isolated checks; death transition simulated around native SetDead, full Pawn.Kill side effects not run.");
static class Fixture{
 public static bool Colony=true;public static Pawn PrisonerPawn;public static HashSet<Thing> Owned=new(ReferenceEqualityComparer.Instance);static Faction player=new Faction();
 public static bool HasColony(ref bool __result){__result=Colony;return false;}
 public static bool PlayerFaction(ref Faction __result){__result=player;return false;}
 public static bool Faction(Thing __instance,ref RimWorld.Faction __result){__result=Owned.Contains(__instance)?player:null;return false;}
 public static bool Prisoner(Pawn __instance,ref bool __result){__result=ReferenceEquals(__instance,PrisonerPawn);return false;}
}
