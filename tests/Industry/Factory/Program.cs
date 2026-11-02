using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.WorldIntegration;
using HarmonyLib;
using PipeSystem;
using Verse;
using Process = PipeSystem.Process;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
var prefsField=AccessTools.Field(typeof(Prefs),"data");prefsField.SetValue(null,Activator.CreateInstance(prefsField.FieldType));
var harmony=new Harmony("grechka.test.factory-tier.native");
foreach(var type in typeof(FactoryTierAdapter).Assembly.GetTypes().Where(t=>t.Name.StartsWith("FactoryTier")&&t.Name.EndsWith("Hook")))
{harmony.CreateClassProcessor(type).Patch();Console.WriteLine("PASS native hook binds: "+type.Name);}
var def=new ThingDef{defName="GrechkaFactoryFixture",comps=new List<CompProperties>{new CompProperties{compClass=typeof(CompAdvancedResourceProcessor)}}};
ProfileVariants.Entries[def.defName]=new VariantEntry{sourceName=def.defName,name=def.defName,tier=5,minimum=2};
var applied=(Dictionary<string,int>)AccessTools.Field(typeof(WorldProfile),"AppliedTiers").GetValue(null);applied[def.defName]=5;
var machine=new ThingWithComps{def=def};
var result=new ProcessDef.Result{count=5};
var processDef=new ProcessDef{defName="FactoryFixtureProcess",worksInSpace=true,results=new List<ProcessDef.Result>{result}};
var process=(Process)RuntimeHelpers.GetUninitializedObject(typeof(Process));
void Set(string field,object value)=>AccessTools.Field(typeof(Process),field).SetValue(process,value);
Set("parent",machine);Set("def",processDef);Set("tickLeft",100000);Set("ticksOrQualityTicks",100000);
var input=new ThingAndResourceOwner(null,null,100,null);
Set("ingredientsOwners",new List<ThingAndResourceOwner>{input});
var adjust=AccessTools.Method(typeof(FactoryTierAdapter),"SetInputs");adjust.Invoke(null,new object[]{process,true});
Check(input.Required==50,"T5 physical owner required amount is halved before hauling");
adjust.Invoke(null,new object[]{process,false});Check(input.Required==50,"Repeated application never discounts an already discounted requirement");
AccessTools.Field(typeof(ThingAndResourceOwner),"count").SetValue(input,50);
Check(!input.Require,"The native owner considers actual discounted materials complete");
Set("ingredientsCompResources",new List<CompResource>{null});
process.Tick(10);
Check(process.TickLeft==99400,"Native Process.Tick consumes 600 work ticks for 10 real ticks at T5");
Check(result.GetCount(process)==10&&result.GetCount(process)==10,"Native Result.GetCount shows doubled quantity without consuming carry");
applied[def.defName]=2;result.count=1;
var commit=AccessTools.Method(typeof(FactoryTierAdapter),"CommitOutput");
int total=0;
for(int i=0;i<10;i++)
{
 int count=result.GetCount(process);
 Check(count==result.GetCount(process),"Preview and failed retries preserve the same bonus quote");
 total+=count;commit.Invoke(null,new object[]{process});
}
Check(total==11,"Ten committed T2 outputs preserve their one fractional bonus item");
var before=result.GetCount(process);
Check(before==1,"Output carry is advanced only by delivery success");
var state=(FactoryBatchState)AccessTools.Method(typeof(FactoryTierAdapter),"State").Invoke(null,new object[]{process});
string path=Path.Combine(Path.GetTempPath(),"factory-tier-"+Guid.NewGuid()+".xml");
try
{
 Scribe.saver.InitSaving(path,"fixture");Scribe_Deep.Look(ref state,"state");Scribe.saver.FinalizeSaving();
 FactoryBatchState read=null;Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref read,"state");Scribe.loader.FinalizeLoading();
 Check(read.baseline.Single()==100&&Math.Abs(read.carry[0]-state.carry[0])<.0001,"Native Scribe retains original input count and output remainder");
}
finally{if(File.Exists(path))File.Delete(path);}
applied.Remove(def.defName);Check(result.GetCount(process)==1,"Unapplied machines receive no output bonus");
Console.WriteLine("Factory tier native adapter checks complete; game process untouched.");
