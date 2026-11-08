using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.Storage;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);}
string pipePath=Environment.GetEnvironmentVariable("PIPESYSTEM_ASSEMBLY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library/Application Support/Steam/steamapps/workshop/content/294100/2023507013/1.7/Assemblies/PipeSystem.dll");
var pipes=Assembly.LoadFrom(pipePath);
var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game),"maps").SetValue(game,new List<Map>());
AccessTools.Field(typeof(Game),"components").SetValue(game,new List<GameComponent>{new StorageDiskClock(game)});
AccessTools.Field(typeof(Game),"tickManager").SetValue(game,new TickManager());
AccessTools.Field(typeof(Game),"uniqueIDsManager").SetValue(game,new UniqueIDsManager());
var world=(World)RuntimeHelpers.GetUninitializedObject(typeof(World)); world.factionManager=new FactionManager(); AccessTools.Field(typeof(Game),"worldInt").SetValue(game,world);
Current.Game=game;
var prefs=AccessTools.Field(typeof(Prefs),"data");prefs.SetValue(null,Activator.CreateInstance(prefs.FieldType));
var harmony=new Harmony("grechka.factory.isolated-tests");
harmony.Patch(AccessTools.Method(typeof(ThingMaker),nameof(ThingMaker.MakeThing)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.MakeThing)));
harmony.Patch(AccessTools.Method(typeof(ReliquaryUtility),nameof(ReliquaryUtility.IsRelic)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.NoRelics)));
Check(FactoryCraftAdapter.Status.StartsWith("PipeSystem:"),"Optional adapter binds against actual PipeSystem 1.7 DLL");
Check(Harmony.GetAllPatchedMethods().Any(m=>m.DeclaringType?.FullName=="PipeSystem.Process"&&m.Name=="TrySpawnAtCell"),"Real process output hook is installed");
Check(Harmony.GetAllPatchedMethods().Any(m=>m.DeclaringType==typeof(GenPlace)&&m.Name==nameof(GenPlace.TryPlaceThing)),"Real split/merge placement callback hook is installed");
var steel=Fixture.Def("Grechka_FactoryTestSteel",75);
var holder=new Holder();Thing existing=Fixture.Create(steel,70);holder.Things.TryAdd(existing,false);
var task=new FactoryTask();
var capture=AccessTools.Method(typeof(FactoryCraftAdapter),"CaptureOutput");
bool moved=(bool)capture.Invoke(null,new object[]{task,existing,10});
Check(moved&&existing.stackCount==60&&task.Buffer.Contents.Sum(t=>t.stackCount)==10,"Output capture moves exact delta from merged stack, preserving earlier stock");
Check(task.Buffer.Contents.All(t=>ReferenceEquals(t.holdingOwner.Owner,task.Buffer)),"Actual output has one owning buffer");
var rollback=new FactoryTask();Fixture.RejectOwner=rollback.Buffer.Contents;
var method=AccessTools.Method(typeof(ThingOwner),nameof(ThingOwner.TryAddOrTransfer),new[]{typeof(Thing),typeof(bool)});
harmony.Patch(method,prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.RejectTransfer)));
int before=holder.Things.Sum(t=>t.stackCount);moved=(bool)capture.Invoke(null,new object[]{rollback,existing,5});
Check(!moved&&holder.Things.Sum(t=>t.stackCount)==before&&!rollback.Buffer.Contents.Any,"Rejected output transfer returns actual split to its previous owner without loss or cloning");
Fixture.RejectOwner=null;
// Native PipeSystem ingredient ownership consumes real Things; no substitute RecipeDef or own synthetic yield.
Type ingredientType=pipes.GetType("PipeSystem.ProcessDef+Ingredient",true),processType=pipes.GetType("PipeSystem.Process",true),defType=pipes.GetType("PipeSystem.ProcessDef",true);
object ingredient=Activator.CreateInstance(ingredientType);AccessTools.Field(ingredientType,"thing").SetValue(ingredient,steel);AccessTools.Field(ingredientType,"countNeeded").SetValue(ingredient,7f);
object definition=Activator.CreateInstance(defType);var ingredients=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ingredientType));ingredients.Add(ingredient);AccessTools.Field(defType,"ingredients").SetValue(definition,ingredients);
object process=RuntimeHelpers.GetUninitializedObject(processType);AccessTools.Field(processType,"def").SetValue(process,definition);AccessTools.Method(processType,"Setup").Invoke(process,null);
object owner=((IEnumerable)AccessTools.Property(processType,"IngredientsOwners").GetValue(process)).Cast<object>().Single();
Check((int)AccessTools.Property(owner.GetType(),"Required").GetValue(owner)==7,"Native Setup determines ingredient requirements");
Thing material=Fixture.Create(steel,10);holder.Things.TryAdd(material,false);
object[] nativeArgs={material,0};AccessTools.Method(owner.GetType(),"AddFromThing").Invoke(owner,nativeArgs);
Check((int)nativeArgs[1]==7&&material.stackCount==3&&(int)AccessTools.Property(owner.GetType(),"Count").GetValue(owner)==7,"Native AddFromThing consumes exactly required items and retains surplus");
// Task and buffer persistence belongs to the map; removing the crafter must first drop these contents.
AccessTools.Field(typeof(FactoryTask),nameof(FactoryTask.ProcessId)).SetValue(task,"real-process-id");task.OrderId=9;task.Fed=true;task.Completed=true;task.Finished=true;task.Produced=10;
string path=Path.Combine(Path.GetTempPath(),"grechka-factory-"+Guid.NewGuid().ToString("N")+".xml");
try{
 Scribe.saver.InitSaving(path,"factoryFixture");Scribe_Deep.Look(ref task,"task");Scribe.saver.FinalizeSaving();
 FactoryTask restored=null;Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref restored,"task");Scribe.loader.FinalizeLoading();
 Check(restored.ProcessId=="real-process-id"&&restored.Fed&&restored.Completed&&restored.Finished&&restored.Produced==10,"Native Scribe preserves completion ledger and process identity");
 Check(restored.Buffer.Contents.Sum(t=>t.stackCount)==10&&restored.Buffer.Contents.All(t=>ReferenceEquals(t.holdingOwner.Owner,restored.Buffer)),"Native Scribe restores actual buffered outputs exactly once");
}finally{if(File.Exists(path))File.Delete(path);}
// Large T5 output may exceed Int32 while each physical stack still fits it.
task.Produced=2L*int.MaxValue;
try{
 Scribe.saver.InitSaving(path,"factoryLargeFixture");Scribe_Deep.Look(ref task,"task");Scribe.saver.FinalizeSaving();
 FactoryTask restored=null;Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref restored,"task");Scribe.loader.FinalizeLoading();
 Check(restored.Produced==4294967294L,"Factory completion ledger preserves output above Int32 across native save/load");
}finally{if(File.Exists(path))File.Delete(path);}
// Actual native owner requirements, with selection performed before any disk withdrawal.
var materialCategory=new ThingCategoryDef{defName="Grechka_FactoryMaterial"};
var woodA=Fixture.Def("Grechka_FactoryWoodA",75);var woodB=Fixture.Def("Grechka_FactoryWoodB",75);
woodA.thingCategories.Add(materialCategory);woodB.thingCategories.Add(materialCategory);
object categoryIngredient=Activator.CreateInstance(ingredientType);
AccessTools.Field(ingredientType,"thingCategory").SetValue(categoryIngredient,materialCategory);
AccessTools.Field(ingredientType,"countNeeded").SetValue(categoryIngredient,5f);
var categoryIngredients=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ingredientType)); categoryIngredients.Add(categoryIngredient);
object categoryDef=Activator.CreateInstance(defType);AccessTools.Field(defType,"ingredients").SetValue(categoryDef,categoryIngredients);
object categoryProcess=RuntimeHelpers.GetUninitializedObject(processType);AccessTools.Field(processType,"def").SetValue(categoryProcess,categoryDef);AccessTools.Method(processType,"Setup").Invoke(categoryProcess,null);
var realOwners=((IEnumerable)AccessTools.Property(processType,"IngredientsOwners").GetValue(categoryProcess)).Cast<object>().ToList();
var planner=AccessTools.Method(typeof(StorageDisk).Assembly.GetType("Grechka.Storage.FactoryInputPlanner"),"Select");
List<(StorageDisk disk,Thing item,int count)> Plan(List<(StorageDisk disk,Thing item)> input,bool noMix,List<object> ownerList=null){
 object[] args={new List<object>{categoryIngredient},ownerList??realOwners,input,noMix,-1,0};
 return (List<(StorageDisk,Thing,int)>)planner.Invoke(null,args);
}
var small=Fixture.Create(woodA,2);var enough=Fixture.Create(woodB,5);
var picked=Plan(new(){(null,small),(null,enough)},true);
Check(picked!=null&&picked.Sum(t=>t.count)==5&&picked.All(t=>t.item==enough)&&small.stackCount==2&&enough.stackCount==5,
 "Factory NoMix chooses the sufficient kind instead of locking onto the first small pile");
var halfA=Fixture.Create(woodA,3);var halfB=Fixture.Create(woodB,3);
Check(Plan(new(){(null,halfA),(null,halfB)},true)==null,"Factory NoMix cannot combine two individually insufficient material kinds");
Check(Plan(new(){(null,halfA),(null,halfB)},false).Sum(t=>t.count)==5,"Mixing-enabled Factory still combines legal materials");
var moreA=Fixture.Create(woodA,2);
Check(Plan(new(){(null,halfA),(null,moreA)},true).Sum(t=>t.count)==5,"One Factory material kind can span several physical stacks");
Check(Plan(new(){(null,enough)},true,new List<object>())==null,"Changed native owner schema fails before any partial ingredient transfer");
Console.WriteLine("Isolated API/ownership checks; no running game controlled.");
sealed class Holder:IThingHolder{
 public ThingOwner<Thing> Things;public Holder(){Things=new ThingOwner<Thing>(this,false);}public IThingHolder ParentHolder=>null;
 public ThingOwner GetDirectlyHeldThings()=>Things;public void GetChildHolders(List<IThingHolder> children)=>ThingOwnerUtility.AppendThingHoldersFromThings(children,Things);
}
static class Fixture{
 static int next=77600;public static ThingOwner RejectOwner;
 public static ThingDef Def(string name,int stack){var d=new ThingDef{defName=name,label=name,category=ThingCategory.Item,thingClass=typeof(ThingWithComps),stackLimit=stack,useHitPoints=false,comps=new(),thingCategories=new()};DefDatabase<ThingDef>.Add(d);return d;}
 public static Thing Create(ThingDef def,int count=1){var t=new ThingWithComps{def=def,stackCount=count,thingIDNumber=next++};AccessTools.Method(typeof(ThingWithComps),"InitializeComps").Invoke(t,null);return t;}
 public static bool MakeThing(ThingDef __0,ref Thing __result){__result=Create(__0);return false;}
 public static bool NoRelics(ref bool __result){__result=false;return false;}
 public static bool RejectTransfer(ThingOwner __instance,ref bool __result){if(__instance!=RejectOwner)return true;__result=false;return false;}
}
