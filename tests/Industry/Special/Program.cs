using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.WorldIntegration;
using HarmonyLib;
using RimWorld;
using Verse;
System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentUICulture=System.Globalization.CultureInfo.InvariantCulture;
var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game),"tickManager").SetValue(game,new TickManager());
AccessTools.Field(typeof(Game),"maps").SetValue(game,new List<Map>());
AccessTools.Field(typeof(Game),"uniqueIDsManager").SetValue(game,new UniqueIDsManager());
Current.Game=game;
var prefs=AccessTools.Field(typeof(Prefs),"data");prefs.SetValue(null,Activator.CreateInstance(prefs.FieldType));
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
// Optional DLLs are read from the installed modpack, never copied into the repository.
AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>
{
 string name=new AssemblyName(e.Name).Name;
 if(name.StartsWith("UnityEngine"))return typeof(UnityEngine.Vector3).Assembly;
 string path=Path.Combine(Environment.GetEnvironmentVariable("SPECIAL_VEF_DIR")??"",name+".dll");
 return File.Exists(path)?Assembly.LoadFrom(path):null;
};
foreach(string name in new[]{"SPECIAL_FORTIFIED_DLL","SPECIAL_FACTORY_DLL","SPECIAL_VNPE_DLL","SPECIAL_PIRATES_DLL","SPECIAL_SPACER_DLL"})
{string path=Environment.GetEnvironmentVariable(name);if(!string.IsNullOrEmpty(path)&&File.Exists(path))Assembly.LoadFrom(path);}
var harmony=new Harmony("grechka.test.special-native");
foreach(var type in typeof(SpecialProduction).Assembly.GetTypes().Where(t=>t.Name.StartsWith("Special")&&t.Name.EndsWith("Hook")))
{harmony.CreateClassProcessor(type).Patch();Console.WriteLine("PASS native/optional hook binds or explicitly skips: "+type.Name);}
var tiers=(Dictionary<string,int>)AccessTools.Field(typeof(WorldProfile),"AppliedTiers").GetValue(null);
T Make<T>(string name,int tier)where T:Thing,new()
{
 var def=new ThingDef{defName=name,thingClass=typeof(T)};
 ProfileVariants.Entries[name]=new VariantEntry{name=name,sourceName=name,tier=tier,minimum=1};tiers[name]=tier;return new T{def=def};
}
var gene=Make<Building_GeneAssembler>("FixtureGenes",5);gene.DoWork(10);
Check((float)AccessTools.Field(typeof(Building_GeneAssembler),"workDone").GetValue(gene)==600,"Real gene assembly work receives the T5 multiplier");
var mech=Make<Building_MechGestator>("FixtureMechs",4);
var bill=(Bill_Mech)RuntimeHelpers.GetUninitializedObject(typeof(Bill_ProductionMech));bill.billStack=new BillStack(mech);bill.recipe=new RecipeDef{workSpeedStat=null};
AccessTools.Field(typeof(Bill_Autonomous),"state").SetValue(bill,FormingState.Forming);
AccessTools.Field(typeof(Bill_Autonomous),"formingTicks").SetValue(bill,1000f);bill.BillTick();
Check((float)AccessTools.Field(typeof(Bill_Autonomous),"formingTicks").GetValue(bill)==985,"Native autonomous gestation clock advances without repeating whole building ticks");
var ingredient = new IngredientCount();ingredient.SetBaseCount(100);bill.recipe.ingredients=new List<IngredientCount>{ingredient};
Check(Math.Abs(bill.recipe.Worker.GetIngredientCount(ingredient,bill)-65)<.0001,"The real mech bill uses the T4 ingredient discount exactly once");
var scanner=Make<Building_SubcoreScanner>("FixtureSubcore",5);
var steel=new ThingDef{defName="FixtureSteel",category=ThingCategory.Item,thingClass=typeof(Thing),stackLimit=75,useHitPoints=false};
var subInput=new IngredientCount();subInput.filter.SetAllow(steel,true);subInput.SetBaseCount(10);
scanner.def.building=new BuildingProperties{subcoreScannerFixedIngredients=new List<IngredientCount>{subInput}};
var held=new ThingOwner<Thing>(scanner,false);
AccessTools.Field(typeof(Building_Enterable),"innerContainer").SetValue(scanner,held);
held.TryAdd(new Thing{def=steel,stackCount=2,thingIDNumber=12345});
Check(scanner.GetRequiredCountOf(steel)==3,"Native subcore delivery subtracts the two loaded items AFTER applying the five-item price");
var barrel=Make<Building_FermentingBarrel>("FixtureBeer",5);
Check(barrel.SpaceLeftForWort==13,"T5 barrel requests only the physical discounted wort amount");
var temperature = new CompTemperatureRuinable {parent=barrel,props=new CompProperties_TemperatureRuinable()};
AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(barrel,new List<ThingComp>{temperature});
barrel.AddWort(13);
Check((int)AccessTools.Field(typeof(Building_FermentingBarrel),"wortCount").GetValue(barrel)==25&&barrel.SpaceLeftForWort==0,"Native barrel is exactly full after consuming thirteen physical units (native capacity 25)");
var deliveredBarrel=Make<Building_FermentingBarrel>("FixtureBeerPhysical",5);
AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(deliveredBarrel,new List<ThingComp>{new CompTemperatureRuinable{parent=deliveredBarrel,props=new CompProperties_TemperatureRuinable()}});
var wort=new Thing{def=steel,stackCount=20,thingIDNumber=12346};
deliveredBarrel.AddWort(wort);
Check(wort.stackCount==7&&(int)AccessTools.Field(typeof(Building_FermentingBarrel),"wortCount").GetValue(deliveredBarrel)==25,"An oversized real wort stack loses only thirteen items when filling a T5 barrel");
var t2=Make<Building>("FixtureFraction",2);
int sum=0;for(int i=0;i<10;i++)sum+=SpecialProduction.Amount(t2,"product",1);
Check(sum==11,"Special production keeps fractional output bonuses across small batches");
var turret=Make<Building_TurretGun>("FixtureTurret",5);
Check(!SpecialTierAdapter.HasPowerBenefit(turret.def),"Turret tiers do not silently increase power or combat characteristics");
Check(SpecialTierAdapter.SupportsXmlClass("Fortified.Building_TurretCapacity")&&SpecialTierAdapter.SupportsXmlClass("Building_GeneExtractor")&&!SpecialTierAdapter.SupportsXmlClass("Unknown.Building_Custom"),"Special variants use the explicit audited class allowlist");
Check(SpecialOptional.FastInterval(400,gene)==7,"VNPE production scheduling has a bounded real interval at T5");
var smallInputs=Make<Building>("FixtureWortCredit",2);
int virtualWort=0;for(int i=0;i<9;i++)virtualWort+=SpecialProduction.InputCredit(smallInputs,"wort",1,100);
Check(virtualWort==10,"Nine small T2 wort deliveries preserve the same discount as one full batch");
Check(SpecialProduction.WholeCost(3,gene)==2,"Indivisible ingredient discounts round upward");
var savedState=new SpecialProductionState();savedState.carry["input"]=.4f;savedState.carry["output"]=.7f;
string savePath=Path.Combine(Path.GetTempPath(),"grechka-special-"+Guid.NewGuid()+".xml");
try
{
 Scribe.saver.InitSaving(savePath,"fixture");Scribe_Deep.Look(ref savedState,"state");Scribe.saver.FinalizeSaving();
 SpecialProductionState restored=null;Scribe.loader.InitLoading(savePath);Scribe_Deep.Look(ref restored,"state");Scribe.loader.FinalizeLoading();
 Check(Math.Abs(restored.carry["input"]-.4)<.00001&&Math.Abs(restored.carry["output"]-.7)<.00001,"Native Scribe preserves both input credits and output remainders");
}
finally{if(File.Exists(savePath))File.Delete(savePath);}
Console.WriteLine("Special machine native tests complete; no running game controlled.");
