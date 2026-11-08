using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.Treasures;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

int checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
string repo=args.Length>0?args[0]:Directory.GetCurrentDirectory();
AccessTools.Method(Type.GetType("Waylith.Rendering.ShaderSourceLoader, Waylith",true),"SetOverrideShaderDirectories").Invoke(null,new object[]{new[]{Path.Combine(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"),"Rendering/Shaders")}});
AccessTools.Field(typeof(UnityData),"mainThreadId").SetValue(null,Environment.CurrentManagedThreadId);
var pack=(ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
AccessTools.Field(typeof(ModContentPack),"packageIdPlayerFacingInt").SetValue(pack,"grechka.treasures.tests");
AccessTools.Field(typeof(ModContentPack),"rootDirInt").SetValue(pack,new DirectoryInfo(Path.Combine(repo,"mods/GrechkaTreasures")));
pack.assetBundles=new ModAssetBundlesHandler(pack);
AccessTools.Field(typeof(ModContentPack),"textures").SetValue(pack,new ModContentHolder<UnityEngine.Texture2D>(pack));
AccessTools.Field(typeof(ModContentPack),"audioClips").SetValue(pack,new ModContentHolder<UnityEngine.AudioClip>(pack));
AccessTools.Field(typeof(ModContentPack),"strings").SetValue(pack,new ModContentHolder<string>(pack));
pack.assemblies=new ModAssemblyHandler(pack);pack.assemblies.loadedAssemblies.Add(typeof(TreasureSite).Assembly);pack.assemblies.loadedAssemblies.Add(typeof(SavedChest).Assembly);
LoadedModManager.RunningModsListForReading.Add(pack);GenTypes.ClearCache();
var harmony=new Harmony("grechka.treasures.expedition-tests");
foreach(var method in AccessTools.GetDeclaredMethods(typeof(Log)).Where(m=>new[]{"Error","Warning","Message"}.Contains(m.Name)))harmony.Patch(method,prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Log)));
var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game),"maps").SetValue(game,new List<Map>());
AccessTools.Field(typeof(Game),"components").SetValue(game,new List<GameComponent>());
AccessTools.Field(typeof(Game),"tickManager").SetValue(game,new TickManager());
AccessTools.Field(typeof(Game),"uniqueIDsManager").SetValue(game,new UniqueIDsManager());
Current.Game=game;
harmony.Patch(AccessTools.PropertyGetter(typeof(Find),nameof(Find.FactionManager)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.FactionManager)));
var pref=AccessTools.Field(typeof(Prefs),"data");pref.SetValue(null,Activator.CreateInstance(pref.FieldType));
TreasureCatalog.Load(Path.Combine(repo,"mods/GrechkaTreasures/Config/Sites"));
Check(Fixture.Errors.Count==0,"Prepared catalog has no rejected layouts: "+string.Join(";",Fixture.Errors));
Check(TreasureCatalog.Layouts.Count==26,"All 26 prepared XML layouts load and validate");
Check(TreasureCatalog.Layouts.Select(l=>string.Join("\n",l.Rows)).Distinct().Count()==26,"Each encounter has an independently stored distinct footprint");
Check(TreasureCatalog.Layouts.Select(l=>l.Tier).Distinct().Order().SequenceEqual(new[]{1,2,3,4,5}),"Prepared sites span danger tiers 1–5");
foreach(var l in TreasureCatalog.Layouts){
 l.Validate();
 foreach(var pos in l.Rows.SelectMany((row,z)=>row.Select((c,x)=>(c,x,z))).Where(p=>p.c=='S'||p.c=='B')){
  var anchor=new IntVec3(pos.x,0,l.Height-pos.z-1);var size=pos.c=='S'?new IntVec2(2,2):new IntVec2(1,2);
  var footprint=GenAdj.OccupiedRect(anchor,Rot4.North,size);
  Check(footprint.All(c=>c.x>=0&&c.x<l.Width&&c.z>=0&&c.z<l.Height&&(c==anchor||".,".Contains(l.Rows[l.Height-c.z-1][c.x]))),"Native north-facing furniture footprint clear: "+l.Id+"/"+pos.x+","+pos.z);
 }
}
for(int seed=0;seed<100;seed++){
 var a=TreasureCatalog.Choose(seed,2);var b=TreasureCatalog.Choose(seed,2);
 Check(a.Id==b.Id&&a.Tier<=2,"Seeded layout selection respects early-colony danger cap "+seed);
 var first=TreasureLoot.Plan(seed,5,"Ruby",false);var second=TreasureLoot.Plan(seed,5,"Ruby",false);
 Check(first.Select(v=>(v.DefName,v.Count)).SequenceEqual(second.Select(v=>(v.DefName,v.Count))),"Chest loot snapshot deterministic "+seed);
 Check(first.All(e=>e.Count>0)&&first.Any(e=>e.DefName=="GrechkaTreasures_RubyCut"),"Real loot has positive quantities and promised gemstone "+seed);
}
Check(TreasureLoot.Plan(42,5,"Diamond",true).All(e=>e.DefName=="Steel"||e.DefName=="Gold"),"Gilded decoys cannot grant real gemstone loot");
Check(TreasureLoot.Plan(42,5,"Diamond",false,true).Single(e=>e.DefName.EndsWith("Cut")).Count==1,"Plundered chest retains one genuine gemstone");
var invalid=new TreasureLayout{Id="invalid",Tier=1,Gem="Ruby",Enemy="pirates",Rows=new[]{"         "," ####### "," #C....# "," #.....# "," #.....# "," ####### ","         ","    W    ","         "}};
bool rejected=false;try{invalid.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Sealed unreachable mandatory treasure is rejected");
// Fixture substitutes rendering-free item construction and placement boundaries only. The game's
// ThingOwner, stacking/ownership, comp save/load and the actual chest methods run unchanged.
harmony.Patch(AccessTools.Method(typeof(ReliquaryUtility),nameof(ReliquaryUtility.IsRelic)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.NoRelics)));
harmony.Patch(AccessTools.Method(typeof(ThingMaker),nameof(ThingMaker.MakeThing)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.MakeThing)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Thing),nameof(Thing.Spawned)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Spawned)));
harmony.Patch(AccessTools.PropertyGetter(typeof(Thing),nameof(Thing.Map)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Map)));
harmony.Patch(AccessTools.Method(typeof(ThingOwner),nameof(ThingOwner.TryDropAll)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Drop)));
foreach(string name in TreasureLoot.Gems.SelectMany(g=>new[]{"GrechkaTreasures_"+g+"Raw","GrechkaTreasures_"+g+"Cut","GrechkaTreasures_"+g+"Monolith"}).Concat(new[]{"Silver","Gold","Steel","GrechkaTreasures_TreasureMap"})){
 DefDatabase<ThingDef>.Add(new ThingDef{defName=name,label=name,thingClass=typeof(ThingWithComps),category=ThingCategory.Item,stackLimit=75,useHitPoints=false,comps=new()});
}
var state=new SavedChest();Fixture.Parent=state.chest.parent;
state.chest.InitializeLoot(375,5,"Ruby",false,false);
var original=state.chest.GetDirectlyHeldThings().Select(t=>(t.def.defName,t.stackCount)).ToArray();
Check(original.Sum(t=>t.stackCount)>75,"Loot materializes into real stack-limited owned things");
Check(state.chest.GetDirectlyHeldThings().All(t=>t.stackCount<=t.def.stackLimit&&ReferenceEquals(t.holdingOwner.Owner,state.chest)),"Every real item belongs to exactly this chest");
state.chest.InitializeLoot(123,1,"Diamond",true,true);
Check(original.SequenceEqual(state.chest.GetDirectlyHeldThings().Select(t=>(t.def.defName,t.stackCount))),"Repeated initialization cannot replace or duplicate contents");
Fixture.SpawnState=false;Check(!state.chest.Open()&&!state.chest.Opened,"Unspawned chest cannot grant loot or change its seal");
Fixture.SpawnState=true;Fixture.AllowDelivery=false;Check(state.chest.Open()&&state.chest.Opened&&state.chest.HasContents,"Failed placement opens chest but preserves all undelivered physical contents");
string path=Path.Combine(Path.GetTempPath(),"treasures-chest-"+Guid.NewGuid().ToString("N")+".xml");
try{
 Scribe.saver.InitSaving(path,"treasuresFixture");Scribe_Deep.Look(ref state,"chest");Scribe.saver.FinalizeSaving();
 SavedChest restored=null;Scribe.loader.InitLoading(path);Scribe_Deep.Look(ref restored,"chest");Scribe.loader.FinalizeLoading();
 Fixture.Parent=restored.chest.parent;
 Check(restored.chest.Initialized&&restored.chest.Opened&&restored.chest.HasContents,"Real Scribe preserves initialized/opened state and pending rewards");
 Check(original.SequenceEqual(restored.chest.GetDirectlyHeldThings().Select(t=>(t.def.defName,t.stackCount))),"Real Scribe round-trip preserves exact gemstone types and quantities");
 Check(restored.chest.GetDirectlyHeldThings().All(t=>ReferenceEquals(t.holdingOwner.Owner,restored.chest)),"Loaded physical contents have the restored chest as their sole owner");
 Fixture.AllowDelivery=true;int count=restored.chest.GetDirectlyHeldThings().Sum(t=>t.stackCount);
 Check(restored.chest.Open()&&!restored.chest.HasContents&&Fixture.Delivered==count,"Retry delivers only remaining physical contents");
 Check(!restored.chest.Open()&&Fixture.Delivered==count,"Empty opened chest cannot deliver rewards a second time");
 restored.chest.InitializeLoot(999,5,"Diamond",false,false);
 Check(!restored.chest.HasContents,"Reinitializing a loaded empty chest cannot reroll loot");
}finally{if(File.Exists(path))File.Delete(path);}
var siteDef=new WorldObjectDef{defName="GrechkaTreasures_TestSite",worldObjectClass=typeof(TreasureSite),comps=new()};
DefDatabase<WorldObjectDef>.Add(siteDef);
var savedSite=new TreasureSite{def=siteDef,ID=7441,layoutId="ruby_palace",treasureSeed=93519,scenario=2,layoutGenerated=true,forceRemoveWorldObjectWhenMapRemoved=true,customLabel="Persistent palace"};
AccessTools.Field(typeof(WorldObject),"tile").SetValue(savedSite,new PlanetTile(45,0));
string sitePath=Path.Combine(Path.GetTempPath(),"treasures-site-"+Guid.NewGuid().ToString("N")+".xml");
try{
 Scribe.saver.InitSaving(sitePath,"treasureSiteFixture");Scribe_Deep.Look(ref savedSite,"site");Scribe.saver.FinalizeSaving();
 TreasureSite restoredSite=null;Scribe.loader.InitLoading(sitePath);Scribe_Deep.Look(ref restoredSite,"site");Scribe.loader.FinalizeLoading();
 Check(restoredSite.ID==7441&&restoredSite.layoutId=="ruby_palace"&&restoredSite.treasureSeed==93519&&restoredSite.scenario==2,"Real native Site Scribe preserves unique ID, fixed layout, seed and occupancy scenario");
 Check(restoredSite.layoutGenerated&&restoredSite.forceRemoveWorldObjectWhenMapRemoved&&restoredSite.customLabel=="Persistent palace","Real native Site Scribe preserves generated flag, temporary-map lifecycle and custom label");
}finally{if(File.Exists(sitePath))File.Delete(sitePath);}
// This isolated fixture loads no Core rendering assets. These exact built-in UI placeholders are
// unrelated to serialization; all other game errors, including unresolved refs/classes, remain fatal.
var serializationErrors=Fixture.Errors.Where(e=>!Fixture.HeadlessTextureMessage(e)).ToList();
Check(serializationErrors.Count==0,"Native chest/site serialization has no data or cross-reference errors: "+string.Join(";",serializationErrors));
Console.WriteLine("Headless fixture omitted built-in UI textures ("+Fixture.Errors.Count(Fixture.HeadlessTextureMessage)+" expected resource messages); rendering is checked separately.");
Console.WriteLine(checks+" expedition checks passed. Isolated libraries only; no game process controlled.");

public sealed class SavedChest:IExposable{
 public CompTreasureChest chest;
 public SavedChest(){chest=new CompTreasureChest{parent=new ThingWithComps{def=new ThingDef{defName="FixtureChest",category=ThingCategory.Building,thingClass=typeof(ThingWithComps),comps=new()},thingIDNumber=98444},props=new CompProperties_TreasureChest()};}
 public void ExposeData()=>chest.PostExposeData();
}
public static class Fixture{
 public static List<string> Errors=new();public static Thing Parent;public static bool SpawnState;public static bool AllowDelivery;public static int Delivered;public static int ID=400;
 public static bool HeadlessTextureMessage(string text)=>text=="MatFrom with null sourceTex."
  || (text.StartsWith("Could not load Texture2D at '")&&new[]{"UI/Misc/BadTexture","PlaceholderImage","UI/Commands/ViewQuest"}.Any(p=>text.StartsWith("Could not load Texture2D at '"+p+"'")));
 public static bool Log(MethodBase __originalMethod,object[] __args){if(__originalMethod.Name=="Error")Errors.Add(__args.FirstOrDefault()?.ToString()??"");return false;}
 public static bool MakeThing(ThingDef def,ThingDef stuff,ref Thing __result){__result=new ThingWithComps{def=def,thingIDNumber=ID++,stackCount=1};return false;}
 public static readonly FactionManager Factions=new FactionManager();
 public static bool FactionManager(ref FactionManager __result){__result=Factions;return false;}
 public static bool NoRelics(ref bool __result){__result=false;return false;}
 public static bool Spawned(Thing __instance,ref bool __result){if(!ReferenceEquals(__instance,Parent))return true;__result=SpawnState;return false;}
 public static bool Map(Thing __instance,ref Map __result){if(!ReferenceEquals(__instance,Parent))return true;__result=null;return false;}
 public static bool Drop(ThingOwner __instance,ref bool __result){if(!AllowDelivery){__result=false;return false;}Delivered+=__instance.Sum(t=>t.stackCount);__instance.Clear();__result=true;return false;}
}
