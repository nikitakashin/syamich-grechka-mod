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
var industry=Assembly.LoadFrom(Path.GetFullPath("mods/GrechkaIndustry/Assemblies/GrechkaIndustry.dll"));
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

var steel=Fixture.Def("Grechka_AutoTestSteel",75);
var food=Fixture.Def("Grechka_AutoTestProduct",75);
var crafterDef=Fixture.Def("Grechka_AutoTestCrafter",1); crafterDef.category=ThingCategory.Building; crafterDef.building=new BuildingProperties();crafterDef.thingClass=typeof(Building_StorageCrafter);
var crafter=new Building_StorageCrafter{def=crafterDef,thingIDNumber=8871};
var input=(StorageBuffer)AccessTools.Field(typeof(Building_StorageCrafter),"buffer").GetValue(crafter);
var stage=(StorageBuffer)AccessTools.Field(typeof(Building_StorageCrafter),"outputStage").GetValue(crafter);
Thing a=Fixture.Create(steel,5),b=Fixture.Create(steel,6);input.Contents.TryAdd(a,false);input.Contents.TryAdd(b,false);
var recipe=new RecipeDef{defName="Grechka_AutoTestRecipe",workerClass=typeof(RecipeWorker),products=new(){new ThingDefCountClass(food,10)}};
DefDatabase<RecipeDef>.Add(recipe);AccessTools.Field(typeof(Building_StorageCrafter),"recipe").SetValue(crafter,recipe);
Fixture.Output=food;
harmony.Patch(AccessTools.Method(typeof(Building_StorageCrafter),"Worker"),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Worker)));
harmony.Patch(AccessTools.Method(typeof(GenRecipe),nameof(GenRecipe.MakeRecipeProducts)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Products)));
harmony.Patch(AccessTools.Method(typeof(RecipeWorker),nameof(RecipeWorker.ConsumeIngredient)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Consume)));
harmony.Patch(AccessTools.Method(typeof(Log),nameof(Log.Error),new[]{typeof(string)}),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Error)));
AccessTools.Method(typeof(Building_StorageCrafter),"Finish").Invoke(crafter,null);
Check(Fixture.Consumed==2&&a.Destroyed&&!b.Destroyed&&b.stackCount==6,"Injected second-consumption failure preserves actual unconsumed input; no clone rollback");
Check(stage.Contents.Sum(t=>t.stackCount)==10&&crafter.Contents.All(t=>t.def!=food),"Prepared outputs have a real owner but remain quarantined after partial consumption");
Check((bool)AccessTools.Field(typeof(Building_StorageCrafter),"faulted").GetValue(crafter),"Partial-consumption failure stops automatic retry");
string path=Path.Combine(Path.GetTempPath(),"grechka-autocraft-"+Guid.NewGuid().ToString("N")+".xml");
try{
 Scribe.saver.InitSaving(path,"fixture");Scribe_Deep.Look(ref crafter,"crafter");Scribe.saver.FinalizeSaving();
 Building_StorageCrafter restored=null;Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref restored,"crafter");Scribe.loader.FinalizeLoading();
 var held=(StorageBuffer)AccessTools.Field(typeof(Building_StorageCrafter),"outputStage").GetValue(restored);
 Check(held.Contents.Sum(t=>t.stackCount)==10&&restored.Contents.Sum(t=>t.stackCount)==6&&(bool)AccessTools.Field(typeof(Building_StorageCrafter),"faulted").GetValue(restored),"Native save/load preserves quarantined stage and actual remainder, without exposing free output");
 AccessTools.Method(typeof(Building_StorageCrafter),"Cancel").Invoke(restored,null);
 Check(!held.Contents.Any&&restored.Contents.Sum(t=>t.stackCount)==6,"Cancellation destroys unpaid outputs and returns only surviving inputs");
}finally{if(File.Exists(path))File.Delete(path);}
var table=new Building_WorkTable{def=new ThingDef{defName="Grechka_AutoTierTable",comps=new()}};
Type compType=industry.GetType("Grechka.Industry.CompTierBonuses"),propsType=industry.GetType("Grechka.Industry.CompProperties_TierBonuses");
var tier=(ThingComp)Activator.CreateInstance(compType);var props=(CompProperties)Activator.CreateInstance(propsType);AccessTools.Field(propsType,"outputBonus").SetValue(props,.2f);tier.props=props;tier.parent=table;
AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(table,new List<ThingComp>{tier});
var carry=(Dictionary<string,float>)AccessTools.Field(compType,"outputCarry").GetValue(tier);carry[recipe.defName+"/"+food.defName]=.9f;
recipe.products[0].count=1;
var expected=AccessTools.Method(typeof(StorageDisk).Assembly.GetType("Grechka.Storage.CraftDependencies"),"Expected");
Check((int)expected.Invoke(null,new object[]{recipe,table})==1&&carry.Values.Single()==.9f,"Pending output uses a conservative tier bonus without spending another variant fractional carry");
// Production may update Industry carry while staging output; a failed consumption
// must restore that state along with keeping unpaid products quarantined.
var rollbackCrafter=new Building_StorageCrafter{def=crafterDef};
AccessTools.Field(typeof(Building_StorageCrafter),"recipe").SetValue(rollbackCrafter,recipe);
AccessTools.Field(typeof(Building_StorageCrafter),"table").SetValue(rollbackCrafter,table);
var rollbackInput=(StorageBuffer)AccessTools.Field(typeof(Building_StorageCrafter),"buffer").GetValue(rollbackCrafter);
rollbackInput.Contents.TryAdd(Fixture.Create(steel,1),false);
var beforeCarry=new Dictionary<string,float>((Dictionary<string,float>)AccessTools.Field(compType,"outputCarry").GetValue(tier));
Fixture.Consumed=1;
Fixture.OnProducts=product=>AccessTools.Method(compType,"TakeExtra").Invoke(tier,new object[]{recipe,product});
AccessTools.Method(typeof(Building_StorageCrafter),"Finish").Invoke(rollbackCrafter,null);
Fixture.OnProducts=null;
var afterCarry=(Dictionary<string,float>)AccessTools.Field(compType,"outputCarry").GetValue(tier);
Check((bool)AccessTools.Field(typeof(Building_StorageCrafter),"faulted").GetValue(rollbackCrafter)
 && afterCarry.Count==beforeCarry.Count && beforeCarry.All(p=>afterCarry.TryGetValue(p.Key,out float value)&&value==p.Value),
 "Failed input consumption rolls back the actual Industry tier-carry mutation from staged products");
var order=new CraftOrder{Id=91,ParentKey="f:77",Ancestors=new(){"f:ProcessA","v:RecipeB"},Recipe=recipe,Remaining=6};
try{
 Scribe.saver.InitSaving(path,"fixture");Scribe_Deep.Look(ref order,"order");Scribe.saver.FinalizeSaving();
 CraftOrder restored=null;Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref restored,"order");Scribe.loader.FinalizeLoading();
 Check(restored.Id==91&&restored.ParentKey=="f:77"&&restored.Ancestors.SequenceEqual(order.Ancestors),"Cross-family child IDs and cycle ancestry survive native save/load");
}finally{if(File.Exists(path))File.Delete(path);}

var spoiledCrafter=new Building_StorageCrafter{def=crafterDef};
AccessTools.Field(typeof(Building_StorageCrafter),"recipe").SetValue(spoiledCrafter,recipe);
AccessTools.Field(typeof(Building_StorageCrafter),"reservedQuantity").SetValue(spoiledCrafter,5L);
int consumedBefore=Fixture.Consumed;AccessTools.Method(typeof(Building_StorageCrafter),"Finish").Invoke(spoiledCrafter,null);
Check(Fixture.Consumed==consumedBefore&&!((StorageBuffer)AccessTools.Field(typeof(Building_StorageCrafter),"outputStage").GetValue(spoiledCrafter)).Contents.Any&&(bool)AccessTools.Field(typeof(Building_StorageCrafter),"faulted").GetValue(spoiledCrafter),"Lost or spoiled reserved inputs cannot generate a free finished product");
// Routing fixtures replace only topology/catalogue. Orders, ancestry, cancellation and quantities are real implementations.
var map=(Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));var manager=new CraftMap(map);var factory=new FactoryCraftMap(map);
AccessTools.Field(typeof(Map),"components").SetValue(map,new List<MapComponent>{manager,factory});
var controller=new Building_StorageController();Fixture.Catalogue=new CraftCatalogue();Fixture.Catalogue.Products[food]=new(){recipe};
recipe.ingredients=new();
harmony.Patch(AccessTools.Method(typeof(CraftMap),"SameNetwork"),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.SameNetwork)));
harmony.Patch(AccessTools.Method(typeof(CraftMap),nameof(CraftMap.Catalogue)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.GetCatalogue)));
harmony.Patch(AccessTools.Method(typeof(FactoryCraftMap),nameof(FactoryCraftMap.Catalog)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.FactoryCatalogue)));
var request=AccessTools.Method(typeof(StorageDisk).Assembly.GetType("Grechka.Storage.CraftDependencies"),"Request");
bool Request(ThingDef product,int count,string parent,List<string> history)=>(bool)request.Invoke(null,new object[]{map,controller,product,count,parent,history});
Check(Request(food,8,"f:77",new(){"f:ProcessA"}),"Factory parent creates a real vanilla intermediate order");
var child=manager.Orders(controller).Single();Check(child.ParentKey=="f:77"&&child.Remaining==8,"Intermediate reserves requested deficit with stable parent identity");
Check(Request(food,8,"f:77",new(){"f:ProcessA"})&&manager.Orders(controller).Count==1,"Repeated planning deduplicates the live child instead of multiplying orders");
var processDef=(Def)Activator.CreateInstance(pipes.GetType("PipeSystem.ProcessDef"));processDef.defName="ProcessB";
Fixture.FactoryRecipes.Add(new FactoryRecipe{Def=processDef,Primary=steel,Quantity=3,Title="Native process"});
Check(Request(steel,6,"v:"+child.Id,new(){"f:ProcessA","v:"+recipe.defName})&&factory.Orders(controller).Single().Remaining==6,"Vanilla intermediate creates a real Factory child order in output units");
Check(!Request(food,1,"f:99",new(){"v:"+recipe.defName}),"Cross-family ancestry refuses a recipe cycle");
manager.Cancel(child);Check(manager.Orders(controller).Count==0&&factory.Orders(controller).Count==0,"Cancelling a parent recursively cancels unstarted cross-family children");
// Fixed metal reinforcement must not change the chosen material of a made-from-stuff result.
var wood=Fixture.Def("Grechka_AutoWood",75);var otherWood=Fixture.Def("Grechka_AutoOtherWood",75);
var metal=new StuffCategoryDef{defName="Grechka_AutoStuff"};wood.stuffProps=new StuffProperties{categories=new(){metal}};otherWood.stuffProps=new StuffProperties{categories=new(){metal}};steel.stuffProps=new StuffProperties{categories=new(){metal}};
var furniture=Fixture.Def("Grechka_AutoFurniture",1);furniture.stuffCategories=new(){metal};
var diskDef=Fixture.Def("Grechka_AutoDisk",1);diskDef.modExtensions=new(){new StorageDiskProperties{capacityBytes=1024}};var disk=new StorageDisk{def=diskDef};
var materials=new Holder();foreach(var t in new[]{Fixture.Create(steel,2),Fixture.Create(wood,5)}){materials.Things.TryAdd(t,false);disk.Accept(t,t.stackCount);}
Fixture.Disks.Add(disk);
harmony.Patch(AccessTools.Method(typeof(Building_StorageController),nameof(Building_StorageController.ActiveDisks)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.ActiveDisks)));
harmony.Patch(AccessTools.Method(typeof(ThingFilter),nameof(ThingFilter.Allows),new[]{typeof(Thing)}),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.FilterDef)));
var fixedMetal=new IngredientCount();fixedMetal.SetBaseCount(2);fixedMetal.filter.SetAllow(steel,true);
var variable=new IngredientCount();variable.SetBaseCount(5);variable.filter.SetAllow(wood,true);variable.filter.SetAllow(otherWood,true);
var furnitureRecipe=new RecipeDef{defName="Grechka_AutoFurnitureRecipe",workerClass=typeof(RecipeWorker),ingredients=new(){variable,fixedMetal},products=new(){new ThingDefCountClass(furniture,1)},fixedIngredientFilter=new ThingFilter(),defaultIngredientFilter=new ThingFilter()};
foreach(var d in new[]{steel,wood,otherWood})furnitureRecipe.fixedIngredientFilter.SetAllow(d,true);
AccessTools.Field(typeof(RecipeDef),"ingredientValueGetterClass").SetValue(furnitureRecipe,typeof(IngredientValueGetter_Volume));
var select=AccessTools.Method(typeof(StorageDisk).Assembly.GetType("Grechka.Storage.CraftRecipes"),"SelectDetailed");object[] selectionArgs={controller,table,furnitureRecipe,null,null,0d,null};
var selection=(List<(StorageDisk disk,Thing item,int count)>)select.Invoke(null,selectionArgs);
Check(selection!=null&&selection.Sum(t=>t.count)==7&&ReferenceEquals(selectionArgs[6],wood),"Fixed metal and variable wooden material both selected; result explicitly keeps wood");

void Inventory(params (ThingDef def,int count)[] items)
{
 Fixture.Disks.Clear();
 foreach(var item in items)
 {
  var inventoryDisk=new StorageDisk{def=diskDef};var owner=new Holder();var thing=Fixture.Create(item.def,item.count);
  owner.Things.TryAdd(thing,false);Check(inventoryDisk.Accept(thing,item.count)==item.count,"Fixture stores actual ingredient stack");Fixture.Disks.Add(inventoryDisk);
 }
}
List<(StorageDisk disk,Thing item,int count)> Select(RecipeDef r,out object[] args)
{
 args=new object[]{controller,table,r,null,null,0d,null};
 return (List<(StorageDisk disk,Thing item,int count)>)select.Invoke(null,args);
}
Inventory((steel,2),(wood,2),(otherWood,5));
selection=Select(furnitureRecipe,out selectionArgs);
Check(selection!=null&&ReferenceEquals(selectionArgs[6],otherWood)&&selection.All(t=>t.item.def!=wood),
 "A small first material pile cannot block another material with enough stock for the whole item");
Inventory((steel,2),(wood,3),(otherWood,3));
furnitureRecipe.allowMixingIngredients=true;
Check(Select(furnitureRecipe,out selectionArgs)==null&&(double)selectionArgs[5]==2,
 "Made-from-stuff result refuses mixed wood types and reports the single-material deficit");

var mixedRecipe=new RecipeDef{defName="Grechka_AutoMixRecipe",workerClass=typeof(RecipeWorker),ingredients=new(){variable},
 products=new(){new ThingDefCountClass(food,1)},fixedIngredientFilter=new ThingFilter(),defaultIngredientFilter=new ThingFilter()};
mixedRecipe.fixedIngredientFilter.SetAllow(wood,true);mixedRecipe.fixedIngredientFilter.SetAllow(otherWood,true);
AccessTools.Field(typeof(RecipeDef),"ingredientValueGetterClass").SetValue(mixedRecipe,typeof(IngredientValueGetter_Volume));
Check(Select(mixedRecipe,out _) == null,"Native allowMixingIngredients=false is respected even for a fixed product");
mixedRecipe.allowMixingIngredients=true;
selection=Select(mixedRecipe,out _);
Check(selection!=null&&selection.Sum(t=>t.count)==5&&selection.Select(t=>t.item.def).Distinct().Count()==2,
 "Native mixing-enabled recipe still combines actual stacks from different ingredient definitions");
mixedRecipe.allowMixingIngredients=false;
Inventory((wood,3),(wood,3));
selection=Select(mixedRecipe,out _);
Check(selection!=null&&selection.Sum(t=>t.count)==5&&selection.Count==2,
 "NoMix may span two disks/stacks of the same ThingDef without overspending either stack");

var invalidStuff=Fixture.Def("Grechka_AutoInvalidStuff",75);
invalidStuff.stuffProps=new StuffProperties{categories=new(){new StuffCategoryDef{defName="Grechka_AutoWrongCategory"}}};
variable.filter.SetAllow(invalidStuff,true);furnitureRecipe.fixedIngredientFilter.SetAllow(invalidStuff,true);
Inventory((steel,2),(invalidStuff,10),(wood,5));
selection=Select(furnitureRecipe,out selectionArgs);
Check(selection!=null&&ReferenceEquals(selectionArgs[6],wood)&&selection.All(t=>t.item.def!=invalidStuff),
 "Broad ingredient filters cannot manufacture an item from a material its native stuff categories reject");
var secondPart=Fixture.Def("Grechka_AutoSecondPart",75);
var partsIngredient=new IngredientCount();partsIngredient.SetBaseCount(2);partsIngredient.filter.SetAllow(food,true);partsIngredient.filter.SetAllow(secondPart,true);
furnitureRecipe.ingredients.Add(partsIngredient);furnitureRecipe.fixedIngredientFilter.SetAllow(food,true);furnitureRecipe.fixedIngredientFilter.SetAllow(secondPart,true);
Inventory((steel,2),(wood,5),(food,1),(secondPart,1));
selection=Select(furnitureRecipe,out selectionArgs);
Check(selection!=null&&selection.Sum(t=>t.count)==9&&ReferenceEquals(selectionArgs[6],wood),
 "A separate mixable non-stuff ingredient stays valid in a made-from-stuff recipe");

var supported=AccessTools.Method(typeof(StorageDisk).Assembly.GetType("Grechka.Storage.CraftRecipes"),"Supported");
bool Supported(RecipeDef r)=>(bool)supported.Invoke(null,new object[]{r});
var operation=new RecipeDef{defName="Grechka_AutoUnsafeOperation",workerClass=typeof(Recipe_Surgery),ingredients=new(),products=new(){new ThingDefCountClass(food,1)}};
Check(!Supported(operation)&&Supported(mixedRecipe),"Production allowlist never turns pawn surgery into ordinary crafting, even with a fixed product");
mixedRecipe.ignoreIngredientCountTakeEntireStacks=true;
Check(!Supported(mixedRecipe),"Full-stack destruction recipes are rejected rather than silently executing partial-stack semantics");
mixedRecipe.ignoreIngredientCountTakeEntireStacks=false;
mixedRecipe.specialProducts=new(){SpecialProductType.Smelted};
Check(!Supported(mixedRecipe),"Dynamic salvage output cannot masquerade as a predictable fixed-product recipe");

Console.WriteLine("Autocraft fault injection / actual game ownership and Scribe checks; no game controlled.");
sealed class Holder:IThingHolder{
 public ThingOwner<Thing> Things;public Holder(){Things=new ThingOwner<Thing>(this,false);}public IThingHolder ParentHolder=>null;
 public ThingOwner GetDirectlyHeldThings()=>Things;public void GetChildHolders(List<IThingHolder> children)=>ThingOwnerUtility.AppendThingHoldersFromThings(children,Things);
}
static class Fixture{
 static int next=77600;public static ThingOwner RejectOwner=null; public static CraftCatalogue Catalogue;public static List<FactoryRecipe> FactoryRecipes=new();public static List<StorageDisk> Disks=new();
 public static bool SameNetwork(ref bool __result){__result=true;return false;}
 public static bool GetCatalogue(ref CraftCatalogue __result){__result=Catalogue;return false;}
 public static bool FactoryCatalogue(ref IEnumerable<FactoryRecipe> __result){__result=FactoryRecipes;return false;}
 public static bool ActiveDisks(ref List<StorageDisk> __result){__result=Disks;return false;}
 public static bool FilterDef(ThingFilter __instance,Thing t,ref bool __result){__result=__instance.Allows(t.def);return false;} public static ThingDef Output;public static int Consumed; public static Action<Thing> OnProducts;
 public static bool Worker(ref Pawn __result){__result=null;return false;}
 public static bool Products(ref IEnumerable<Thing> __result){var product=Create(Output,10);OnProducts?.Invoke(product);__result=new[]{product};return false;}
 public static bool Consume(Thing ingredient){Consumed++;if(Consumed==2)throw new InvalidOperationException("injected consumption fault");ingredient.Destroy();return false;}
 public static bool Error(string text)=>!text.StartsWith("[Storage] Crafter paused");
 public static ThingDef Def(string name,int stack){var d=new ThingDef{defName=name,label=name,category=ThingCategory.Item,thingClass=typeof(ThingWithComps),stackLimit=stack,useHitPoints=false,comps=new(),thingCategories=new()};DefDatabase<ThingDef>.Add(d);return d;}
 public static Thing Create(ThingDef def,int count=1){var t=new ThingWithComps{def=def,stackCount=count,thingIDNumber=next++};AccessTools.Method(typeof(ThingWithComps),"InitializeComps").Invoke(t,null);return t;}
 public static bool MakeThing(ThingDef __0,ref Thing __result){__result=Create(__0);return false;}
 public static bool NoRelics(ref bool __result){__result=false;return false;}
 public static bool RejectTransfer(ThingOwner __instance,ref bool __result){if(__instance!=RejectOwner)return true;__result=false;return false;}
}
