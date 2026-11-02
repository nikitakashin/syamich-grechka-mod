using System.Collections;
using System.Runtime.CompilerServices;
using Grechka.WorldIntegration;
using HarmonyLib;
using RimWorld;
using Verse;
void Check(bool condition,string text){if(!condition)throw new Exception(text);Console.WriteLine("PASS "+text);}
ushort nextHash=1;
ThingDef Def(string name){var d=new ThingDef{defName=name,label=name,shortHash=nextHash++,category=ThingCategory.Building,thingClass=typeof(Building),building=new(),comps=new(),size=new IntVec2(1,1)};DefDatabase<ThingDef>.Add(d);return d;}
ThingDef original=Def("DeepDrill"),second=Def("GrechkaWorld_DeepDrill_T2_Building"),third=Def("GrechkaWorld_DeepDrill_T3_Building"),other=Def("OtherBench");
ThingDefOf.DeepDrill=original;
var applied=(IDictionary)AccessTools.Field(typeof(WorldProfile),"AppliedTiers").GetValue(null);
foreach(var pair in new[]{(original,1),(second,2),(third,3)}){ProfileVariants.Entries[pair.Item1.defName]=new VariantEntry{sourceName=original.defName,name=pair.Item1.defName,tier=pair.Item2,minimum=1};applied[pair.Item1.defName]=pair.Item2;}
var giver=new WorkGiverDef{defName="FixtureDoBill",fixedBillGiverDefs=new(){original,other}};DefDatabase<WorkGiverDef>.Add(giver);
FamilyWorkDiscovery.Refresh();
Check(giver.fixedBillGiverDefs.Count==4&&giver.fixedBillGiverDefs.Contains(second)&&giver.fixedBillGiverDefs.Contains(other),"Native fixedBillGiverDefs expands original families and retains unrelated providers");
var harmony=new Harmony("grechka.family-work.isolated");
foreach(var t in typeof(WorldProfile).Assembly.GetTypes().Where(t=>t.Name.StartsWith("FamilyWork")&&t.Name.EndsWith("Hook")||t.Name=="FamilyDeepDrillSkipHook"))harmony.CreateClassProcessor(t).Patch();
// These fixture buildings cannot be fish or Odyssey footprints. Avoid booting the real mod loader.
harmony.Patch(AccessTools.Method(typeof(ThingListGroupHelper),nameof(ThingListGroupHelper.Includes)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.ExpansionGroup)));
var lister=new ListerThings(ListerThingsUse.Global);Building B(ThingDef d,int id){var b=new Building{def=d,thingIDNumber=id};AccessTools.Method(typeof(ThingWithComps),"InitializeComps").Invoke(b,null);return b;}
Building baseThing=B(original,2701),cloneThing=B(second,2702),thirdThing=B(third,2703);lister.Add(baseThing);lister.Add(cloneThing);
var request=ThingRequest.ForDef(original);var specific=ThingRequest.ForDef(second);
Check(lister.ThingsMatching(request).SequenceEqual(new[]{baseThing})&&!request.Accepts(cloneThing),"Outside work scope native exact queries and accepts remain exact");
var raw=(Dictionary<ThingDef,List<Thing>>)AccessTools.Field(typeof(ListerThings),"listsByDef").GetValue(lister);var exactRaw=raw[original];
int outer=FamilyWorkDiscovery.Enter();
try{
 Check(lister.ThingsMatching(request).Count==2&&request.Accepts(cloneThing),"Work scope discovers and accepts actual family instances");
 Check(lister.ThingsMatching(specific).SequenceEqual(new[]{cloneThing})&&!specific.Accepts(baseThing),"Variant-specific work requests remain exact");
 Check(ReferenceEquals(raw[original],exactRaw)&&exactRaw.SequenceEqual(new[]{baseThing}),"Merged work views never modify native raw def lists");
 int inner=FamilyWorkDiscovery.Enter();FamilyWorkDiscovery.Leave(inner);Check(FamilyWorkDiscovery.Active,"Leaving nested scope preserves the outer scope");
 lister.Add(thirdThing);Check(lister.ThingsMatching(request).Count==3,"Native Add invalidates the cached family view");
 lister.Remove(cloneThing);Check(lister.ThingsMatching(request).SequenceEqual(new[]{baseThing,thirdThing}),"Native Remove invalidates the cached family view");
 lister.Clear();Check(lister.ThingsMatching(request).Count==0,"Native Clear invalidates all family views");
}finally{FamilyWorkDiscovery.Leave(outer);}
Check(!FamilyWorkDiscovery.Active&&!request.Accepts(cloneThing),"Final scope restore prevents family matching from leaking into inventory queries");
// Native DeepDrill.ShouldSkip retains its original power/demolition tests, with one canonical read.
var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));var map=(Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));AccessTools.Field(typeof(Game),"maps").SetValue(game,new List<Map>{map});Current.Game=game;
map.listerBuildings=(ListerBuildings)RuntimeHelpers.GetUninitializedObject(typeof(ListerBuildings));AccessTools.Field(typeof(ListerBuildings),"allBuildingsColonist").SetValue(map.listerBuildings,new List<Building>{cloneThing});
map.designationManager=(DesignationManager)RuntimeHelpers.GetUninitializedObject(typeof(DesignationManager));
var pawn=new Pawn();foreach(Thing t in new Thing[]{pawn,cloneThing})AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(t,(sbyte)0);
harmony.Patch(AccessTools.Method(typeof(DesignationManager),nameof(DesignationManager.DesignationOn),new[]{typeof(Thing),typeof(DesignationDef)}),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Designation)));
var drillGiver=new WorkGiver_DeepDrill();
Check(!drillGiver.ShouldSkip(pawn),"Real native DeepDrill predicate finds a canonical clone");
Fixture.Uninstall=true;Check(drillGiver.ShouldSkip(pawn),"Native DeepDrill uninstall exclusion remains effective");Fixture.Uninstall=false;
var power=new CompPowerTrader{parent=cloneThing,props=new CompProperties_Power()};AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(cloneThing,new List<ThingComp>{power});
Check(drillGiver.ShouldSkip(pawn),"Native DeepDrill power exclusion remains effective");AccessTools.Field(typeof(CompPowerTrader),"powerOnInt").SetValue(power,true);Check(!drillGiver.ShouldSkip(pawn),"Powered clone is eligible through the unchanged native predicate");
applied.Clear();FamilyWorkDiscovery.Refresh();
Check(giver.fixedBillGiverDefs.SequenceEqual(new[]{original,other}),"Profile restoration removes only derived family providers");
int disabled=FamilyWorkDiscovery.Enter();try{Check(!request.Accepts(cloneThing)&&drillGiver.ShouldSkip(pawn),"Disabled family remains an exact def for native work predicates");}finally{FamilyWorkDiscovery.Leave(disabled);}
Console.WriteLine("Native lister/Harmony predicates; no game controlled.");
static class Fixture{
 public static bool Uninstall;
 public static bool ExpansionGroup(ThingRequestGroup __0,ref bool __result){if(__0!=ThingRequestGroup.SubstructureFootprint&&__0!=ThingRequestGroup.Fish)return true;__result=false;return false;}
 public static bool Designation(ref Designation __result){__result=Uninstall?(Designation)RuntimeHelpers.GetUninitializedObject(typeof(Designation)):null;return false;}
}
