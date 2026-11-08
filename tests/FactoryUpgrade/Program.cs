using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.WorldIntegration;
using HarmonyLib;
using RimWorld;
using Verse;
void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);}
string pipePath=Environment.GetEnvironmentVariable("PIPESYSTEM_ASSEMBLY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library/Application Support/Steam/steamapps/workshop/content/294100/2023507013/1.7/Assemblies/PipeSystem.dll");
var pipes=Assembly.LoadFrom(pipePath);
var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));var map=(Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
AccessTools.Field(typeof(Game),"maps").SetValue(game,new List<Map>{map});AccessTools.Field(typeof(Game),"components").SetValue(game,new List<GameComponent>());AccessTools.Field(typeof(Game),"tickManager").SetValue(game,new TickManager());AccessTools.Field(typeof(Game),"uniqueIDsManager").SetValue(game,new UniqueIDsManager());Current.Game=game;
var pref=AccessTools.Field(typeof(Prefs),"data");pref.SetValue(null,Activator.CreateInstance(pref.FieldType));
var harmony=new Harmony("grechka.factory-upgrade.isolated");
harmony.Patch(AccessTools.Method(typeof(ThingOwnerUtility),nameof(ThingOwnerUtility.ShouldRemoveDesignationsOnAddedThings)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.NoRelics)));
harmony.Patch(AccessTools.Method(typeof(Thing),"RemoveAllReservationsAndDesignationsOnThis"),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.SkipMapCleanup)));
harmony.Patch(AccessTools.Method(typeof(ThingMaker),nameof(ThingMaker.MakeThing)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.MakeThing)));
harmony.Patch(AccessTools.Method(typeof(ReliquaryUtility),nameof(ReliquaryUtility.IsRelic)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.NoRelics)));
harmony.Patch(AccessTools.PropertySetter(typeof(Building),nameof(Building.HitPoints)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.SetHP)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Thing),nameof(Thing.MaxHitPoints)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.MaxHP)));
harmony.Patch(AccessTools.Method(typeof(GenSpawn),nameof(GenSpawn.Spawn),new[]{typeof(Thing),typeof(IntVec3),typeof(Map),typeof(Rot4),typeof(WipeMode),typeof(bool),typeof(bool)}),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Spawn)));
harmony.Patch(AccessTools.Method(typeof(Log),nameof(Log.Error),new[]{typeof(string)}),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Error)));
Type compType=pipes.GetType("PipeSystem.CompAdvancedResourceProcessor"),propsType=pipes.GetType("PipeSystem.CompProperties_AdvancedResourceProcessor"),stackType=pipes.GetType("PipeSystem.ProcessStack"),processType=pipes.GetType("PipeSystem.Process");
CompProperties NativeProps(){var props=(CompProperties)Activator.CreateInstance(propsType);props.compClass=compType;return props;}
var old=Fixture.Def("NativeFactory",typeof(FixtureBuilding));old.category=ThingCategory.Building;old.useHitPoints=true;old.size=new IntVec2(2,2);old.building=new();old.comps=new(){NativeProps(),new CompProperties_FactoryUpgrade()};
var target=Fixture.Def("GrechkaWorld_NativeFactory_T2_Building",typeof(FixtureBuilding));target.category=ThingCategory.Building;target.useHitPoints=true;target.size=old.size;target.building=new();target.comps=new(){NativeProps(),new CompProperties_FactoryUpgrade()};
var building=new FixtureBuilding{def=old,thingIDNumber=41882};building.HitPoints=80;
var native=(ThingComp)RuntimeHelpers.GetUninitializedObject(compType);native.parent=building;native.props=old.comps[0];
var upgrade=new CompFactoryUpgrade{parent=building,props=old.comps[1]};
AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(building,new List<ThingComp>{native,upgrade});
object stack=Activator.CreateInstance(stackType,building);AccessTools.Field(compType,"processStack").SetValue(native,stack);
ProfileVariants.Entries[old.defName]=new VariantEntry{sourceName=old.defName,name=old.defName,tier=1,minimum=1};
var applied=(IDictionary)AccessTools.Field(typeof(WorldProfile),"AppliedTiers").GetValue(null);applied[old.defName]=1;applied[target.defName]=2;
var steel=Fixture.Def("UpgradeSteel",typeof(ThingWithComps));var housing1=Fixture.Def("Grechka_HousingT1_Item",typeof(ThingWithComps));var housing2=Fixture.Def("Grechka_HousingT2_Item",typeof(ThingWithComps));
IngredientCount I(ThingDef d,int n){var i=new IngredientCount();i.filter.SetAllow(d,true);i.SetBaseCount(n);return i;}
var recipe=new RecipeDef{defName="MakeUpgradeHousing",ingredients=new(){I(housing1,1),I(steel,5)},products=new(){new ThingDefCountClass(housing2,1)}};AccessTools.Field(typeof(RecipeDef),"ingredientValueGetterClass").SetValue(recipe,typeof(IngredientValueGetter_Volume));DefDatabase<RecipeDef>.Add(recipe);
Check(upgrade.Costs().Count==1&&upgrade.Costs().Single().thingDef==steel&&upgrade.Costs().Single().count==5,"Upgrade credits the previous housing and derives exact remaining materials");
Current.Game.researchManager = new ResearchManager();
var tierResearch = new ResearchProjectDef { defName = "FixtureTierResearch", baseCost = 100 };
target.researchPrerequisites = new() { tierResearch };
Check(!upgrade.Queue() && !upgrade.Requested && !upgrade.Supplies.Any, "Locked target research prevents direct factory upgrade without reserving or spending materials");
((Dictionary<ResearchProjectDef,float>)AccessTools.Field(typeof(ResearchManager),"progress").GetValue(Find.ResearchManager))[tierResearch] = 100;
Check(upgrade.Queue(),"Applied native family queues a real upgrade");
recipe.ingredients[1].SetBaseCount(99);Check(upgrade.Costs().Single().count==5,"Queued upgrade freezes its cost against later recipe balance edits");recipe.ingredients[1].SetBaseCount(5);
var source=new Holder();Thing material=Fixture.Create(steel,7);source.Things.TryAdd(material,false);
Check(upgrade.Accept(material)==5&&material.stackCount==2&&upgrade.Supplies.Sum(t=>t.stackCount)==5,"Delivery transfers only the deficit between actual owners");
Check(upgrade.Accept(material)==0&&source.Things.Sum(t=>t.stackCount)==2,"Repeated delivery cannot overcharge or duplicate materials");
UpgradeSnapshot.Parent=building;var snapshot=new UpgradeSnapshot{Value=upgrade};string path=Path.Combine(Path.GetTempPath(),"grechka-upgrade-"+Guid.NewGuid().ToString("N")+".xml");
try{
 Scribe.saver.InitSaving(path,"fixture");Scribe_Deep.Look(ref snapshot,"upgrade");Scribe.saver.FinalizeSaving();
 UpgradeSnapshot restored=null;Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref restored,"upgrade");Scribe.loader.FinalizeLoading();
 Check(restored.Value.Requested&&restored.Value.Supplies.Sum(t=>t.stackCount)==5&&restored.Value.Supplies.All(t=>ReferenceEquals(t.holdingOwner.Owner,restored.Value)),"Native Scribe retains requested upgrade and the actual paid materials owner");
}finally{if(File.Exists(path))File.Delete(path);}
var process=RuntimeHelpers.GetUninitializedObject(processType);AccessTools.Field(processType,"parent").SetValue(process,building);AccessTools.Field(processType,"suspended").SetValue(process,true);AccessTools.Field(processType,"id").SetValue(process,"preserved-process");
AccessTools.Field(processType,"spawning").SetValue(process,true);
var ownerType=pipes.GetType("PipeSystem.ThingAndResourceOwner");var owner=Activator.CreateInstance(ownerType);AccessTools.Field(ownerType,"count").SetValue(owner,3);var owners=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ownerType));owners.Add(owner);AccessTools.Field(processType,"ingredientsOwners").SetValue(process,owners);
var processes=(IList)AccessTools.Field(stackType,"processes").GetValue(stack);processes.Add(process);
Fixture.SpawnState(building,true);var complete=AccessTools.Method(typeof(CompFactoryUpgrade),"Complete");
Check(FactoryUpgradeSafety.Problem(building)!=null&&!(bool)complete.Invoke(upgrade,null)&&upgrade.Supplies.Sum(t=>t.stackCount)==5&&building.def==old,"Busy native owners reject upgrade before spending or despawning");
bool decision=true;Check(FactoryUpgradeProcessPauseHook.Prefix(process,ref decision),"Requested upgrade lets an already funded native cycle finish");
AccessTools.Field(ownerType,"count").SetValue(owner,0);decision=true;
Check(!FactoryUpgradeProcessPauseHook.Prefix(process,ref decision)&&!decision&&(bool)AccessTools.Field(processType,"suspended").GetValue(process),"Empty repeat cycle is temporarily masked without changing native suspension/settings");
AccessTools.Field(processType,"pickUpReady").SetValue(process,true);
Check(FactoryUpgradeSafety.Problem(building)!=null,"Undelivered native result blocks upgrade");AccessTools.Field(processType,"pickUpReady").SetValue(process,false);
Check(FactoryUpgradeSafety.Compatible(building,target)&&FactoryUpgradeSafety.Problem(building)==null,"Empty suspended native process permits upgrade");
Check(!FactoryUpgradeSafety.BatchInProgress(process),"Native persistent spawning permission does not make an empty process permanently busy");
AccessTools.Field(processType,"currentResult").SetValue(process,1);
Check(FactoryUpgradeSafety.BatchInProgress(process)&&FactoryUpgradeSafety.Problem(building)!=null,"Partial result delivery blocks upgrade even with zero reported progress");
AccessTools.Field(processType,"currentResult").SetValue(process,0);
FactoryUpgradeSafety.SetDefinition(building,target);
Check(ReferenceEquals(AccessTools.Field(compType,"processStack").GetValue(native),stack)&&ReferenceEquals(AccessTools.Field(processType,"parent").GetValue(process),building)&&ReferenceEquals(owners,AccessTools.Field(processType,"ingredientsOwners").GetValue(process)),"Definition change retains real process, native owner objects and parent identity");
FactoryUpgradeSafety.SetDefinition(building,old);
// Actual native PostDeSpawn WillReplace skips ResetProcess (which would clear these diagnostic counters).
AccessTools.Field(processType,"processCount").SetValue(process,23);native.PostDeSpawn(map,DestroyMode.WillReplace);
Check((int)AccessTools.Field(processType,"processCount").GetValue(process)==23&&ReferenceEquals(stack,AccessTools.Field(compType,"processStack").GetValue(native)),"Native WillReplace despawn retains process stack and counters");
processes.Clear();Fixture.FailSpawn=true;
Check(!(bool)complete.Invoke(upgrade,null)&&building.def==old&&building.Spawned&&building.HitPoints==80&&upgrade.Supplies.Sum(t=>t.stackCount)==5&&ReferenceEquals(native.props,old.comps[0]),"Injected post-despawn spawn failure restores original def/props and retains all paid materials");
Fixture.FailDestroy=steel;
harmony.Patch(AccessTools.Method(typeof(Thing),nameof(Thing.Destroy)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.DestroyFault)));
Check(!(bool)complete.Invoke(upgrade,null)&&ReferenceEquals(building.def,target)&&upgrade.Supplies.Sum(t=>t.stackCount)==5,"Payment failure after successful swap retains the new machine and locks unpaid materials");
Check(upgrade.Cancel()&&ReferenceEquals(building.def,target)&&building.thingIDNumber==41882&&!upgrade.Supplies.Any&&!upgrade.Requested,"Payment retry consumes actual materials once; cancellation cannot refund an applied upgrade");
Console.WriteLine("Native owners and lifecycle checks; spawn topology is a fixture, no game controlled.");
sealed class UpgradeSnapshot:IExposable{public static ThingWithComps Parent;public CompFactoryUpgrade Value=new CompFactoryUpgrade{parent=Parent};public void ExposeData()=>Value.PostExposeData();}
sealed class Holder:IThingHolder{public ThingOwner<Thing> Things;public Holder(){Things=new(this,false);}public IThingHolder ParentHolder=>null;public ThingOwner GetDirectlyHeldThings()=>Things;public void GetChildHolders(List<IThingHolder> c)=>ThingOwnerUtility.AppendThingHoldersFromThings(c,Things);}
public sealed class FixtureBuilding:Building{public override void DeSpawn(DestroyMode mode=DestroyMode.Vanish){foreach(var c in AllComps)c.PostDeSpawn(Map,mode);Fixture.SpawnState(this,false);}}
static class Fixture{
 static int next=80000;public static bool FailSpawn;public static ThingDef FailDestroy;
 public static void DestroyFault(Thing __instance){if(__instance.def!=FailDestroy)return;FailDestroy=null;throw new InvalidOperationException("injected payment failure");}
 public static ThingDef Def(string name,Type type){var d=new ThingDef{defName=name,label=name,category=ThingCategory.Item,thingClass=type,stackLimit=75,useHitPoints=false,comps=new(),thingCategories=new()};DefDatabase<ThingDef>.Add(d);return d;}
 public static Thing Create(ThingDef def,int count=1){var t=new ThingWithComps{def=def,stackCount=count,thingIDNumber=next++};AccessTools.Method(typeof(ThingWithComps),"InitializeComps").Invoke(t,null);return t;}
 public static bool MakeThing(ThingDef __0,ref Thing __result){__result=Create(__0);return false;}
 public static bool SkipMapCleanup()=>false;
 public static bool NoRelics(ref bool __result){__result=false;return false;}
 public static bool SetHP(Building __instance,int __0){AccessTools.Field(typeof(Thing),"hitPointsInt").SetValue(__instance,__0);return false;}
 public static bool MaxHP(ref int __result){__result=100;return false;}
 public static void SpawnState(Thing t,bool spawned)=>AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(t,(sbyte)(spawned?0:-1));
 public static bool Spawn(Thing __0,ref Thing __result){if(FailSpawn){FailSpawn=false;throw new InvalidOperationException("injected spawn failure");}SpawnState(__0,true);__result=__0;return false;}
 public static bool Error(string __0){if(!__0.StartsWith("[Grechka World] Factory upgrade rollback")&&!__0.StartsWith("[Grechka World] Upgrade payment retained"))return true;Console.WriteLine("EXPECTED "+__0.Split('\n')[0]);return false;}
}
