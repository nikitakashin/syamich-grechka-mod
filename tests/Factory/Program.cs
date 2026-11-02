using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.Storage;
using HarmonyLib;
using RimWorld;
using Verse;
void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);}
string pipePath=Environment.GetEnvironmentVariable("PIPESYSTEM_ASSEMBLY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library/Application Support/Steam/steamapps/workshop/content/294100/2023507013/1.7/Assemblies/PipeSystem.dll");
var pipes=Assembly.LoadFrom(pipePath);
var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game),"maps").SetValue(game,new List<Map>());
AccessTools.Field(typeof(Game),"components").SetValue(game,new List<GameComponent>{new StorageDiskClock(game)});
AccessTools.Field(typeof(Game),"tickManager").SetValue(game,new TickManager());
AccessTools.Field(typeof(Game),"uniqueIDsManager").SetValue(game,new UniqueIDsManager());
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
