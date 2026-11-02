using System.Reflection;
using System.Xml;
using System.Runtime.CompilerServices;
using Grechka.WorldIntegration;
using HarmonyLib;
using RimWorld;
using Verse;

static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
ModContentPack Pack(string id){var pack=(ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));AccessTools.Field(typeof(ModContentPack),"packageIdPlayerFacingInt").SetValue(pack,id);return pack;}
ThingDef Thing(string id,ModContentPack pack){var d=new ThingDef {defName=id,label=id,modContentPack=pack,category=ThingCategory.Item,stackLimit=75};DefDatabase<ThingDef>.Add(d);return d;}
var core=Pack("ludeon.rimworld");var industry=Pack("grechka.industry");var fakeA=Pack("fixture.copper.a");var fakeB=Pack("fixture.copper.b");
var steel=Thing("Steel",core);var housing=Thing("Grechka_HousingT2_Item",industry);
var machine=Thing("TestMachine",core);machine.costList=new(){new ThingDefCountClass(steel,20)};
var rule=new MachineProfileRuleDef {defName="test_machine_rule",packageId="ludeon.rimworld",targetDefName=machine.defName,minimumTier=2,addHousing=true};DefDatabase<MachineProfileRuleDef>.Add(rule);
var copperA=Thing("Fixture_CopperA",fakeA);var copperB=Thing("Fixture_CopperB",fakeB);var ore=Thing("Fixture_CopperOre",fakeB);
var group=new MaterialGroupDef {defName="fixture.copper.metal",form="metal",members=new(){new MaterialMember{packageId="fixture.copper.a",thingDefName=copperA.defName},new MaterialMember{packageId="fixture.copper.b",thingDefName=copperB.defName}}};DefDatabase<MaterialGroupDef>.Add(group);
var count=new IngredientCount();count.filter.SetAllow(copperA,true);count.SetBaseCount(100);
var recipe=new RecipeDef{defName="TestCopperRecipe",label="test",modContentPack=industry,ingredients=new(){count},fixedIngredientFilter=new ThingFilter(),defaultIngredientFilter=new ThingFilter()};recipe.fixedIngredientFilter.SetAllow(copperA,true);recipe.defaultIngredientFilter.SetAllow(copperA,true);DefDatabase<RecipeDef>.Add(recipe);
var materialRule=new RecipeMaterialRuleDef {defName="test_copper_rule",packageId="grechka.industry",recipeDefName=recipe.defName,ingredientIndex=0,materialGroup=group.defName};DefDatabase<RecipeMaterialRuleDef>.Add(materialRule);
Current.Game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
Current.Game.uniqueIDsManager=new UniqueIDsManager();
Current.Game.storyteller=(Storyteller)RuntimeHelpers.GetUninitializedObject(typeof(Storyteller));
Current.Game.storyteller.difficulty=new Difficulty();
var existingBill=new Bill_Production(recipe,null);
WorldProfile.Capture();
var state=new WorldProfileState(null);
WorldProfile.Apply(state,false);
Check(machine.costList.Count==1 && !recipe.allowMixingIngredients,"Old unapproved save keeps original price and recipe");
var blueprint=new Blueprint_Build{def=new ThingDef{entityDefToBuild=machine}};
Check(blueprint.TotalMaterialCost().Count==1,"Native blueprint initially caches the original raw material cost");
WorldProfile.Apply(state,true);
Check(blueprint.TotalMaterialCost().Any(c=>c.thingDef==housing),"Native blueprint cost cache is invalidated when the housing profile is applied");
Check(machine.costList.Count==2 && machine.costList.Single(c=>c.thingDef==housing).count==1,"Explicit profile application adds exactly one minimum-tier housing");
WorldProfile.Apply(state,false);
Check(machine.costList.Count==2 && machine.costList.Single(c=>c.thingDef==housing).count==1,"Reload/apply is idempotent rather than adding another housing");
var newBill=new Bill_Production(recipe,null);
Check(newBill.ingredientFilter.Allows(copperB)&&!existingBill.ingredientFilter.Allows(copperB),"New bills accept all approved providers while existing player filters remain unchanged");
Check(recipe.allowMixingIngredients && count.filter.Allows(copperA) && count.filter.Allows(copperB) && !count.filter.Allows(ore),"Native mixing filter accepts both explicit metals and rejects ore");
Check(recipe.IngredientValueGetter.ValuePerUnitOf(copperA)==recipe.IngredientValueGetter.ValuePerUnitOf(copperB) && count.GetBaseCount()==100,"Two metal providers share one unchanged 100-unit ingredient requirement");
var third=Thing("Fixture_CopperC",fakeB);group.members.Add(new MaterialMember{packageId="fixture.copper.b",thingDefName=third.defName});
WorldProfile.Apply(state,false);
Check(!count.filter.Allows(third),"A newly installed provider is not silently added to a saved recipe group");
WorldProfile.Apply(state,true);Check(count.filter.Allows(third),"Explicit reapplication accepts the new provider");
Check(!MaterialDictionary.BelongsTo(copperA,"fixture.copper.b"),"Same spelling cannot bypass exact package identity");
var newMachine=Thing("NewMachine",core);newMachine.costList=new(){new ThingDefCountClass(steel,12)};
DefDatabase<MachineProfileRuleDef>.Add(new MachineProfileRuleDef{defName="new_rule",packageId="ludeon.rimworld",targetDefName=newMachine.defName,minimumTier=2,addHousing=true});WorldProfile.Capture();WorldProfile.Apply(state,false);
Check(newMachine.costList.Count==1,"A newly added machine rule waits for explicit approval in an existing save");
WorldProfile.RestoreBaseline();Check(machine.costList.Count==1 && !recipe.allowMixingIngredients && !count.filter.Allows(copperB),"Returning to another unapproved game restores baseline defs");
var harmony=new Harmony("grechka.world.isolated-check");
foreach(Type hook in new[]{typeof(ProfileVariantXmlHook),typeof(ProfileVariantVisibilityHook),typeof(ProfileVariantPlacementHook),typeof(ProfileUpgradeAvailabilityHook),typeof(ProfileUpgradeCompletionHook),typeof(ProfileIngredientDiscountHook),typeof(ProfileProductBonusHook),typeof(ProfilePowerHook)})
    harmony.CreateClassProcessor(hook).Patch();
Check(true,"All world variant hooks bind to actual RimWorld 1.7 signatures");
var xml=new XmlDocument();
xml.LoadXml("<Defs><ThingDef Name='FixtureBench' Abstract='True'><thingClass>Building_WorkTable</thingClass><tickerType>Normal</tickerType><size>(2,1)</size><costList><Steel>30</Steel></costList></ThingDef><ThingDef ParentName='FixtureBench'><defName>FixtureBenchReal</defName><label>fixture bench</label></ThingDef><Grechka.WorldIntegration.MachineProfileRuleDef><defName>fixture_variant_rule</defName><packageId>ludeon.rimworld</packageId><targetDefName>FixtureBenchReal</targetDefName><minimumTier>2</minimumTier><addHousing>true</addHousing></Grechka.WorldIntegration.MachineProfileRuleDef></Defs>");
var owners=new Dictionary<XmlNode,LoadableXmlAsset>();
LoadableXmlAsset Owner(ModContentPack pack){var asset=(LoadableXmlAsset)RuntimeHelpers.GetUninitializedObject(typeof(LoadableXmlAsset));AccessTools.Field(typeof(LoadableXmlAsset),"mod").SetValue(asset,pack);return asset;}
foreach(XmlNode node in xml.DocumentElement.ChildNodes)owners[node]=Owner(core);
ProfileVariantXmlHook.EntriesFromXml(xml,owners);
Check(ProfileVariants.Entries.Count==0,"Missing Industry creates no tier variants");
for(int tier=1;tier<=5;tier++)
{
    var item=xml.CreateElement("ThingDef");item.InnerXml="<defName>Grechka_HousingT"+tier+"_Item</defName>";xml.DocumentElement.AppendChild(item);owners[item]=Owner(industry);
    if(tier<=2)continue;
    var r=xml.CreateElement("RecipeDef");r.InnerXml="<defName>FixtureHousingRecipe"+tier+"</defName><ingredients><li><filter><thingDefs><li>Grechka_HousingT"+(tier-1)+"_Item</li></thingDefs></filter><count>1</count></li><li><filter><thingDefs><li>Steel</li></thingDefs></filter><count>20</count></li></ingredients><products><Grechka_HousingT"+tier+"_Item>1</Grechka_HousingT"+tier+"_Item></products>";xml.DocumentElement.AppendChild(r);owners[r]=Owner(industry);
}
ProfileVariantXmlHook.EntriesFromXml(xml,owners);
Check(ProfileVariants.Entries.Count==4&&!ProfileVariants.Entries.Keys.Any(n=>n.Contains("T1_")),"T2-native machine exposes only T2–T5 variants");
int nodeCount=xml.DocumentElement.ChildNodes.Count;
ProfileVariantXmlHook.EntriesFromXml(xml,owners);
Check(xml.DocumentElement.ChildNodes.Count==nodeCount,"Repeated XML processing does not duplicate variants, dropdown or upgrades");
AccessTools.Field(typeof(Prefs),"data").SetValue(null,new PrefsData());
XmlInheritance.Clear();
foreach(XmlNode node in xml.DocumentElement.ChildNodes)XmlInheritance.TryRegister(node,owners[node].mod);
XmlInheritance.Resolve();
var clone=xml.DocumentElement.SelectSingleNode("ThingDef[defName='GrechkaWorld_FixtureBenchReal_T5_Building']");
var resolved=XmlInheritance.GetResolvedNodeFor(clone);
Check(resolved.SelectSingleNode("thingClass").InnerText=="Building_WorkTable"&&resolved.SelectSingleNode("size").InnerText=="(2,1)","Native XML inheritance preserves machine class and footprint");
Check(resolved.SelectNodes("comps/li[@Class='Grechka.WorldIntegration.CompProperties_ProfileUpgrade']").Count==1,"Variant inherits exactly one upgrade/bonus component");
Check(resolved.SelectSingleNode("designatorDropdown").InnerText=="GrechkaWorld_Group_FixtureBenchReal","Tier machines share one native build dropdown");
var upgradeRecipe=xml.DocumentElement.SelectSingleNode("RecipeDef[defName='GrechkaWorld_Upgrade_FixtureBenchReal_T3']");
Check(upgradeRecipe.SelectNodes("ingredients/li").Count==1&&upgradeRecipe.SelectSingleNode("ingredients/li/filter/thingDefs/li").InnerText=="Steel","In-place upgrade credits the previous housing rather than consuming it twice");
WorldProfile.AppliedTiers.Clear();
var inactiveDef=new ThingDef{defName="GrechkaWorld_FixtureBenchReal_T5_Building"};
var designator=(Designator_Build)RuntimeHelpers.GetUninitializedObject(typeof(Designator_Build));
AccessTools.Field(typeof(Designator_Build),"entDef").SetValue(designator,inactiveDef);
AcceptanceReport report=true;
Check(!ProfileVariantPlacementHook.Prefix(designator,ref report)&&!report.Accepted,"Unapproved generated machine cannot be placed even through a retained designator");
void AddXml(string text,ModContentPack pack)
{
    var d=new XmlDocument();d.LoadXml(text);var node=xml.ImportNode(d.DocumentElement,true);xml.DocumentElement.AppendChild(node);owners[node]=Owner(pack);
}
AddXml("<ThingDef Name='FactoryFixtureBase' Abstract='True'><thingClass>Building</thingClass><comps><li Class='PipeSystem.CompProperties_AdvancedResourceProcessor'/></comps></ThingDef>",core);
AddXml("<ThingDef ParentName='FactoryFixtureBase'><defName>FixtureFactory</defName><label>factory fixture</label><comps><li Class='CompProperties_Flickable'/></comps></ThingDef>",core);
AddXml("<Grechka.WorldIntegration.MachineProfileRuleDef><defName>fixture_factory_rule</defName><packageId>ludeon.rimworld</packageId><targetDefName>FixtureFactory</targetDefName><minimumTier>3</minimumTier><addHousing>true</addHousing></Grechka.WorldIntegration.MachineProfileRuleDef>",core);
ProfileVariantXmlHook.EntriesFromXml(xml,owners);
Check(ProfileVariants.Entries.Values.Count(e=>e.sourceName=="FixtureFactory")==3,"Native Factory processor inherited through base comps produces T3–T5 direct variants");
Check(ProfileVariants.Entries["GrechkaWorld_FixtureFactory_T5_Building"].worktable==false&&xml.DocumentElement.SelectSingleNode("RecipeDef[defName='GrechkaWorld_Upgrade_FixtureFactory_T4']")==null,"Factory variants do not receive a fake BillStack upgrade recipe");
Check(WorldProfile.OriginalDefName(new ThingDef{defName="GrechkaWorld_FixtureFactory_T5_Building"})=="FixtureFactory","Optional quest adapters can resolve a generated variant to its original machine");
StatDefOf.WorkTableWorkSpeedFactor=new StatDef{defName="FixtureWorkTableSpeed",defaultBaseValue=1,workerClass=typeof(StatWorker)};
var realBench=Thing("FixtureBenchReal",core);realBench.thingClass=typeof(Building_WorkTable);realBench.category=ThingCategory.Building;
realBench.statBases=new(){new StatModifier{stat=StatDefOf.WorkTableWorkSpeedFactor,value=1.25f}};
realBench.costList=new(){new ThingDefCountClass(steel,30)};
DefDatabase<MachineProfileRuleDef>.Add(new MachineProfileRuleDef{defName="fixture_variant_rule",packageId="ludeon.rimworld",targetDefName=realBench.defName,minimumTier=2,addHousing=true});
foreach(int tier in new[]{3,4,5})
{
    Thing("Grechka_HousingT"+tier+"_Item",industry);
    var d=Thing("GrechkaWorld_FixtureBenchReal_T"+tier+"_Building",core);d.thingClass=typeof(Building_WorkTable);d.category=ThingCategory.Building;d.statBases=new(){new StatModifier{stat=StatDefOf.WorkTableWorkSpeedFactor,value=1.25f}};
}
WorldProfile.Capture();var tierState=new WorldProfileState(null);WorldProfile.Apply(tierState,true);
var t3=DefDatabase<ThingDef>.GetNamed("GrechkaWorld_FixtureBenchReal_T3_Building");
Check(t3.statBases.Single(s=>s.stat==StatDefOf.WorkTableWorkSpeedFactor).value==6.25f,"T3 speed multiplies the original machine factor once");
WorldProfile.Apply(tierState,false);
Check(t3.statBases.Single(s=>s.stat==StatDefOf.WorkTableWorkSpeedFactor).value==6.25f&&realBench.statBases.Single(s=>s.stat==StatDefOf.WorkTableWorkSpeedFactor).value==2.5f,"Reload never compounds speed bonuses across base and derived tiers");
Check(t3.costList.Single(c=>c.thingDef==steel).count==30&&t3.costList.Count==2,"Direct variant price retains original raw cost plus exactly its own housing");
var gatedUpgrade=new RecipeDef{defName="GrechkaWorld_Upgrade_FixtureBenchReal_T4",recipeUsers=new(){t3}};
WorldProfile.AppliedTiers.Remove("GrechkaWorld_FixtureBenchReal_T4_Building");bool available=true;
ProfileUpgradeAvailabilityHook.Postfix(gatedUpgrade,ref available);
Check(!available,"Upgrade ingredients cannot be consumed before the target tier is explicitly approved");
WorldProfile.AppliedTiers["GrechkaWorld_FixtureBenchReal_T4_Building"]=4;
var bench=new Building_WorkTable{def=t3};var bonus=new CompProfileUpgrade{parent=bench};
AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(bench,new List<ThingComp>{bonus});
var product=new Thing{def=copperA,stackCount=1};
Check(Enumerable.Range(0,5).Sum(_=>bonus.Extra(recipe,product))==1,"T3 output carry awards one extra item after five one-item crafts");
var bill=new Bill_Production(recipe,null);bench.BillStack.AddBill(bill);
float required=new RecipeWorker().GetIngredientCount(count,bill);
Check(Math.Abs(required-80)<.001,"Native recipe ingredient count applies the T3 discount to the combined 100-unit requirement");
var power=new CompPowerTrader{parent=bench};float draw=-100;ProfilePowerHook.Postfix(power,ref draw);
Check(draw==-600,"Adapted T3 consumer draws six times its original power");
var unsupported=new Building{def=machine};power.parent=unsupported;draw=-100;ProfilePowerHook.Postfix(power,ref draw);
Check(draw==-100,"Unsupported catalogue machine keeps its original power until a benefit adapter exists");
WorldProfile.RestoreBaseline();
Check(realBench.statBases.Single(s=>s.stat==StatDefOf.WorkTableWorkSpeedFactor).value==1.25f,"Switching to an unapproved world restores original work speed");
SpecialRecipeChecks.Run(core,steel);
UtilityChecks.Run();
Console.WriteLine("World profile checks complete. No game process or real save used.");
