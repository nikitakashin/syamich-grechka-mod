using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Grechka.Pharmacy;
using HarmonyLib;
using RimWorld;
using Verse;

int checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
string repo=args.Length>0?args[0]:Directory.GetCurrentDirectory();
AccessTools.Method(Type.GetType("Waylith.Rendering.ShaderSourceLoader, Waylith",true),"SetOverrideShaderDirectories").Invoke(null,new object[]{new[]{Path.Combine(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"),"Rendering/Shaders")}});
AccessTools.Field(typeof(UnityData),"mainThreadId").SetValue(null,Environment.CurrentManagedThreadId);
var harmony=new Harmony("grechka.pharmacy.isolated-tests");
foreach(var method in AccessTools.GetDeclaredMethods(typeof(Log)).Where(m=>new[]{"Error","Warning","Message"}.Contains(m.Name)))harmony.Patch(method,prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Log)));
AccessTools.Field(typeof(Prefs),"data").SetValue(null,new PrefsData());
var temp=Path.Combine(Path.GetTempPath(),"grechka-pharmacy-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
AccessTools.Field(typeof(GenFilePaths),"saveDataPath").SetValue(null,temp);
Directory.CreateDirectory(Path.Combine(temp,"Config"));Directory.CreateDirectory(Path.Combine(temp,"Mods"));Directory.CreateDirectory(Path.Combine(temp,"Data"));
File.WriteAllText(Path.Combine(temp,"Config/ModsConfig.xml"),"<ModsConfigData><version>1.7.4998</version><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>");
Directory.CreateDirectory(Path.Combine(temp,"Data/Core/About"));File.WriteAllText(Path.Combine(temp,"Data/Core/About/About.xml"),"<ModMetaData><name>Core</name><author>Ludeon Studios</author><packageId>ludeon.rimworld</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>");
AccessTools.Field(typeof(GenFilePaths),"modsFolderPath").SetValue(null,Path.Combine(temp,"Mods"));AccessTools.Field(typeof(GenFilePaths),"officialModsFolderPath").SetValue(null,Path.Combine(temp,"Data"));
var gameRoot=new DirectoryInfo(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"));
while(!Directory.Exists(Path.Combine(gameRoot.FullName,"Data/Core")))gameRoot=gameRoot.Parent??throw new Exception("Missing game data");
var types=typeof(Pawn).Assembly.GetTypes().Concat(typeof(PharmacyRules).Assembly.GetTypes()).Concat(typeof(Grechka.Tweaks.Mastery).Assembly.GetTypes()).ToList();
Type TypeFor(string name)=>types.FirstOrDefault(t=>t.FullName==name||t.Name==name);
var index=new Dictionary<(Type,string),XElement>();
var allDocuments=new List<XmlDocument>();
foreach(string folder in new[]{"Core","Royalty","Ideology","Biotech","Odyssey","Anomaly"}.Select(n=>Path.Combine(gameRoot.FullName,"Data",n,"Defs")).Concat(Directory.GetDirectories(Path.Combine(repo,"mods")).Select(p=>Path.Combine(p,"Defs"))))
{
 if(!Directory.Exists(folder))continue;
 foreach(string file in Directory.GetFiles(folder,"*.xml",SearchOption.AllDirectories)){
  var doc=new XmlDocument();doc.Load(file);allDocuments.Add(doc);
  foreach(XmlNode node in doc.DocumentElement.ChildNodes){if(node is not XmlElement)continue;string name=node.SelectSingleNode("defName")?.InnerText;var type=TypeFor(node.Name);if(name!=null&&type!=null&&typeof(Def).IsAssignableFrom(type))index[(type,name)]=XElement.Parse(node.OuterXml);}
 }
}
var pack=(ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
AccessTools.Field(typeof(ModContentPack),"packageIdPlayerFacingInt").SetValue(pack,"grechka.pharmacy");
AccessTools.Field(typeof(ModContentPack),"rootDirInt").SetValue(pack,new DirectoryInfo(Path.Combine(repo,"mods/GrechkaPharmacy")));
pack.assetBundles=new ModAssetBundlesHandler(pack);
AccessTools.Field(typeof(ModContentPack),"textures").SetValue(pack,new ModContentHolder<UnityEngine.Texture2D>(pack));
AccessTools.Field(typeof(ModContentPack),"audioClips").SetValue(pack,new ModContentHolder<UnityEngine.AudioClip>(pack));
AccessTools.Field(typeof(ModContentPack),"strings").SetValue(pack,new ModContentHolder<string>(pack));
pack.assemblies=new ModAssemblyHandler(pack);pack.assemblies.loadedAssemblies.Add(typeof(PharmacyRules).Assembly);pack.assemblies.loadedAssemblies.Add(typeof(Grechka.Tweaks.Mastery).Assembly);
LoadedModManager.RunningModsListForReading.Add(pack);GenTypes.ClearCache();XmlInheritance.Clear();
// Represent optional installed modules in the isolated process only. The native
// MayRequire resolver still evaluates its real comma-separated AND semantics.
foreach(string id in new[]{"grechka.treasures","grechka.tweaks"}){
 var metadata=new ModMetaData(Path.Combine(repo,"mods",id.EndsWith("treasures")?"GrechkaTreasures":"GrechkaTweaks"));
 foreach(string field in new[]{"modsByPackageId","modsByPackageIdIgnorePostfix"})
  ((Dictionary<string,List<ModMetaData>>)AccessTools.Field(typeof(ModLister),field).GetValue(null))[id]=new(){metadata};
}
var activeMods=(HashSet<string>)AccessTools.Field(typeof(ModsConfig),"activeModsHashSet").GetValue(null);
for(int mask=0;mask<4;mask++){
 foreach(var (id,bit) in new[]{("grechka.treasures",1),("grechka.tweaks",2)}){
  if((mask&bit)!=0)activeMods.Add(id);else activeMods.Remove(id);
 }
 Check(ModLister.AllModsActiveNoSuffix((IEnumerable<string>)new[]{"grechka.treasures","grechka.tweaks"})==(mask==3),"Native optional trainer requirements need both modules, mask "+mask);
}
foreach(var doc in allDocuments)
 foreach(XmlNode node in doc.DocumentElement.ChildNodes)
  if(node is XmlElement&&node.Attributes?["Name"]!=null&&node.Attributes?["MayRequire"]==null&&node.Attributes?["MayRequireAnyOf"]==null)XmlInheritance.TryRegister(node,pack);
var nodes=new List<XmlNode>();
foreach(string file in Directory.GetFiles(Path.Combine(repo,"mods/GrechkaPharmacy/Defs"),"*.xml")){
 var doc=new XmlDocument();doc.Load(file);
 foreach(XmlNode node in doc.DocumentElement.ChildNodes)if(node is XmlElement){nodes.Add(node);if(node.Attributes?["Name"]==null)XmlInheritance.TryRegister(node,pack);}
}
XmlInheritance.Resolve();
var resolved=nodes.Where(n=>n.SelectSingleNode("defName")!=null).Select(XmlInheritance.GetResolvedNodeFor).ToList();
var refs=new HashSet<(Type,string)>();
void Reference(Type type,string name){Check(index.ContainsKey((type,name)),"Owned data reference "+type.Name+"/"+name);refs.Add((type,name));}
void Schema(XmlNode node,Type type,bool root=false){
 string className=node.Attributes?["Class"]?.Value;if(className!=null){Type concrete=TypeFor(className)??throw new Exception("Unknown class "+className);Check(type.IsAssignableFrom(concrete),"Assignable XML class "+className);type=concrete;}
 if(type==typeof(StatModifier)){Reference(typeof(StatDef),node.Name);return;}
 if(type==typeof(ThingDefCountClass)){Reference(typeof(ThingDef),node.Name);return;}
 if(type==typeof(SkillRequirement)){Reference(typeof(SkillDef),node.Name);return;}
 if(typeof(Def).IsAssignableFrom(type)&&!root){Reference(type,node.InnerText);return;}
 if(type.IsEnum){Enum.Parse(type,node.InnerText);return;}
 var children=node.ChildNodes.Cast<XmlNode>().Where(n=>n is XmlElement).ToList();if(children.Count==0)return;
 if(type.IsGenericType&&typeof(IEnumerable).IsAssignableFrom(type)){
  var generics=type.GetGenericArguments();foreach(var child in children){if(generics.Length==2&&typeof(Def).IsAssignableFrom(generics[0]))Reference(generics[0],child.Name);else Schema(child,generics.Last());}return;
 }
 foreach(var child in children){FieldInfo field=AccessTools.Field(type,child.Name)??throw new Exception("Unknown field "+type.FullName+"."+child.Name);Schema(child,field.FieldType);}
}
foreach(var node in resolved)Schema(node,TypeFor(node.Name)??throw new Exception(node.Name),true);
Check(true,"All inherited XML fields and enum values match installed RimWorld 1.7");
foreach(var (type,name) in refs){var def=(Def)Activator.CreateInstance(type);def.defName=name;typeof(DefDatabase<>).MakeGenericType(type).GetMethod("Add",new[]{type}).Invoke(null,new object[]{def});}
Fixture.Errors.Clear();
var loaded=new List<Def>();
foreach(var node in nodes.Where(n=>n.SelectSingleNode("defName")!=null)){Type type=TypeFor(node.Name);var method=typeof(DirectXmlToObject).GetMethods().Single(m=>m.Name=="ObjectFromXml"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==2);var def=(Def)method.MakeGenericMethod(type).Invoke(null,new object[]{node,false});loaded.Add(def);}
foreach(var group in loaded.GroupBy(d=>d.GetType())){var database=typeof(DefDatabase<>).MakeGenericType(group.Key);database.GetMethod("Clear").Invoke(null,null);foreach(var def in group)database.GetMethod("Add",new[]{group.Key}).Invoke(null,new object[]{def});}
// Restore external seeded references after replacing only the module's native definitions.
foreach(var (type,name) in refs.Where(r=>!r.Item2.StartsWith("GrechkaPharmacy_"))){var database=typeof(DefDatabase<>).MakeGenericType(type);if(database.GetMethod("GetNamedSilentFail").Invoke(null,new object[]{name})!=null)continue;var def=(Def)Activator.CreateInstance(type);def.defName=name;database.GetMethod("Add",new[]{type}).Invoke(null,new object[]{def});}
DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
Check(Fixture.Errors.Count==0,"Native XML parser and cross references report no errors: "+string.Join(";",Fixture.Errors));
var hediffs=loaded.OfType<HediffDef>().ToDictionary(d=>d.defName);
var preparations=loaded.OfType<ThingDef>().Where(d=>d.ingestible?.outcomeDoers?.OfType<IngestionOutcomeDoer_Pharmacy>().Any()==true).ToList();
Check(preparations.Count==12,"Twelve independent preparations load through native XML");
foreach(var item in preparations){Check(item.ingestible.drugCategory==DrugCategory.Medical,"Native medical consumable "+item.defName);Check(item.ingestible.outcomeDoers.OfType<IngestionOutcomeDoer_Pharmacy>().Count()==1,"Single pharmacy outcome "+item.defName);}
Check(loaded.OfType<ThingDef>().Count(t=>t.thingClass==typeof(Building_WorkTable))==3,"Three powered pharmaceutical reactors load");
var language=new LoadedLanguage("English");AccessTools.Field(typeof(LoadedLanguage),"workerInt").SetValue(language,new LanguageWorker_English());AccessTools.Field(typeof(LoadedLanguage),"dataIsLoaded").SetValue(language,true);LanguageDatabase.activeLanguage=language;LanguageDatabase.defaultLanguage=language;
var trainers=loaded.OfType<ThingDef>().Where(d=>d.comps?.OfType<Grechka.Tweaks.CompProperties_InterestTrainer>().Any()==true).ToList();
Check(trainers.Count==5&&trainers.Select(d=>d.comps.OfType<Grechka.Tweaks.CompProperties_InterestTrainer>().Single().grade).Order().SequenceEqual(new[]{2,3,4,5,6}),"Five positive trainer grades load through native XML");
foreach(var trainer in trainers){
 Check(trainer.comps[1] is CompProperties_Usable usable&&usable.useJob.defName=="UseItem"&&usable.useDuration==600,"Inherited trainer usable comp and native job "+trainer.defName);
 var recipe=loaded.OfType<RecipeDef>().Single(r=>r.products?.Any(p=>p.thingDef==trainer)==true);
 Check(recipe.ingredients.All(i=>i.GetBaseCount()>0)&&recipe.recipeUsers.Count>0&&recipe.researchPrerequisites.Count==1,"Resolved trainer production recipe "+recipe.defName);
}
foreach(string lang in new[]{"Russian","English"})foreach(var (kind,type) in new[]{("ThingDef",typeof(ThingDef)),("RecipeDef",typeof(RecipeDef))}){
 var injection=new DefInjectionPackage(type);
 foreach(var entry in XDocument.Load(Path.Combine(repo,"mods/GrechkaPharmacy/Languages",lang,"DefInjected",kind,"InterestTrainers.xml")).Root.Elements()){
  object[] parameters={type,entry.Name.LocalName,entry.Value,typeof(string),true,"Interest trainer translation test",false,null,null,null,null};
  Check((bool)AccessTools.Method(typeof(DefInjectionPackage),"SetDefFieldAtPath").Invoke(injection,parameters),"Native trainer translation "+lang+"/"+entry.Name.LocalName);
 }
 Check(((List<string>)AccessTools.Field(typeof(DefInjectionPackage),"loadErrors").GetValue(injection)).Count==0,"Trainer translations have no native injection errors "+lang+"/"+kind);
}
var administer=(IEnumerable<RecipeDef>)AccessTools.Method(typeof(RecipeDefGenerator),"DrugAdministerDefs").Invoke(null,new object[]{false});
var administration=administer.Where(r=>r.defName.Contains("GrechkaPharmacy_")).ToList();
Check(administration.Count==12,"Native recipe generator creates twelve doctor-administered preparations");
foreach(var recipe in administration)Check(recipe.workerClass==typeof(Recipe_AdministerIngestible)&&recipe.ingredients.Count==1,"Native doctor ingestion recipe "+recipe.defName);
// No map is launched. Native health notifications are the isolated boundary; the actual
// hediffs, treatment clocks, selection rules, Scribe state and healing calculations execute.
harmony.Patch(AccessTools.Method(typeof(Pawn_HealthTracker),nameof(Pawn_HealthTracker.AddHediff),new[]{typeof(HediffDef),typeof(BodyPartRecord),typeof(DamageInfo?),typeof(DamageWorker.DamageResult)}),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Add)));
harmony.Patch(AccessTools.Method(typeof(Pawn_HealthTracker),nameof(Pawn_HealthTracker.RemoveHediff)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Remove)));
harmony.Patch(AccessTools.Method(typeof(Pawn_HealthTracker),nameof(Pawn_HealthTracker.Notify_HediffChanged)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.HealthNotification)));
harmony.Patch(AccessTools.Method(typeof(Pawn_HealthTracker),nameof(Pawn_HealthTracker.RestorePart)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Restore)));
var isolatedGame=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));isolatedGame.tickManager=new TickManager();AccessTools.Field(typeof(Game),"maps").SetValue(isolatedGame,new List<Map>());Current.Game=isolatedGame;
Pawn Patient(bool human=true,bool mech=false){
 var torso=new BodyPartRecord{def=new BodyPartDef{defName="Torso",hitPoints=40}};
 var arm=new BodyPartRecord{def=new BodyPartDef{defName="Arm",hitPoints=30,tags=new(){new BodyPartTagDef{defName="ManipulationLimbCore"}}},parent=torso,depth=BodyPartDepth.Outside};torso.parts.Add(arm);
 var hand=new BodyPartRecord{def=new BodyPartDef{defName="Hand",hitPoints=20,tags=new(){new BodyPartTagDef{defName="ManipulationLimbSegment"}}},parent=arm,depth=BodyPartDepth.Outside};arm.parts.Add(hand);
 var head=new BodyPartRecord{def=new BodyPartDef{defName="Head",hitPoints=25},parent=torso,depth=BodyPartDepth.Outside};torso.parts.Add(head);
 var flesh=new FleshTypeDef{defName=mech?"Mechanoid":"Normal",isOrganic=!mech};FleshTypeDefOf.Mechanoid=new FleshTypeDef{defName="Mechanoid",isOrganic=false};if(mech)flesh=FleshTypeDefOf.Mechanoid;
 var pawn=new Pawn{def=new ThingDef{defName="FixtureRace",race=new RaceProperties{body=new BodyDef{corePart=torso},intelligence=human?Intelligence.Humanlike:Intelligence.Animal,lifeStageAges=new(){new LifeStageAge{minAge=0,def=new LifeStageDef{defName="Child",developmentalStage=DevelopmentalStage.Child}},new LifeStageAge{minAge=13,def=new LifeStageDef{defName="Adult",developmentalStage=DevelopmentalStage.Adult}}}}}};AccessTools.Field(typeof(RaceProperties),"fleshType").SetValue(pawn.RaceProps,flesh);pawn.RaceProps.body.ResolveReferences();pawn.health=new Pawn_HealthTracker(pawn);pawn.ageTracker=new Pawn_AgeTracker(pawn);return pawn;
}
harmony.CreateClassProcessor(typeof(AdministrationAvailability)).Patch();
Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(Recipe_AdministerIngestible),nameof(Recipe_AdministerIngestible.AvailableOnNow))).Postfixes.Any(p=>p.owner==harmony.Id),"Targeted operation availability hook binds native method");
var patient=Patient();Check(PharmacyRules.Eligible(patient)&&PharmacyRules.Eligible(Patient(false)),"Living humans and animals accept treatment");Check(!PharmacyRules.Eligible(null)&&!PharmacyRules.Eligible(Patient(true,true)),"Null and mechanical patients excluded");
HediffWithComps Effect(Pawn p,string name)=>(HediffWithComps)PharmacyRules.AddOrRefresh(p,hediffs[name],60000);
var scarRecipe=administration.Single(r=>r.defName.Contains("ScarSerum"));bool operationAvailable=true;AdministrationAvailability.Postfix(new Recipe_AdministerIngestible{recipe=scarRecipe},patient,ref operationAvailable);Check(!operationAvailable,"Healthy patient is not offered a useless scar operation");
var ordinary=new Recipe_AdministerIngestible{recipe=new RecipeDef{ingredients=new(){new IngredientCount()}}};operationAvailable=true;AdministrationAvailability.Postfix(ordinary,patient,ref operationAvailable);Check(operationAvailable,"Non-Pharmacy medical operations keep native availability");
var hemo=Effect(patient,"GrechkaPharmacy_HemostaticEffect");var clock=hemo.TryGetComp<HediffComp_Disappears>();clock.ticksToDisappear=100;
Check(PharmacyRules.AddOrRefresh(patient,hemo.def,7500)==hemo&&clock.ticksToDisappear==7500&&patient.health.hediffSet.hediffs.Count(h=>h.def==hemo.def)==1,"Repeat dose refreshes one existing effect without severity stacking");
Check(hemo.CurStage.totalBleedFactor==.15f,"Hemostatic native stage multiplies total blood loss by 0.15");
var course=Effect(patient,"GrechkaPharmacy_MechaniteRegenCourse").TryGetComp<HediffComp_Treatment>();course.Advance(2499);Check(course.CompletedCycles==0,"Course waits for its treatment interval");course.Advance(1);Check(course.CompletedCycles==1,"Course runs after exactly one interval");course.Advance(1250);
PharmacyRules.AddOrRefresh(patient,course.parent.def,60000);Check(course.CompletedCycles==1&&course.ElapsedTicks==1250,"Refreshing unfinished course preserves partial progress");
string save=Path.Combine(temp,"course.xml");Scribe.saver.InitSaving(save,"PharmacyTreatment");course.CompExposeData();Scribe.saver.FinalizeSaving();
var restored=new HediffComp_Treatment{parent=course.parent,props=course.props};Scribe.loader.InitLoading(save);restored.CompExposeData();Scribe.loader.FinalizeLoading();
Check(restored.CompletedCycles==course.CompletedCycles&&restored.ElapsedTicks==course.ElapsedTicks,"Native Scribe saves and restores course clock and cycle count");
restored.Advance(60000);Check(restored.CompletedCycles==24,"Large interval respects the bounded treatment cycle count");restored.Advance(60000);Check(restored.CompletedCycles==24,"Completed course does not run extra treatments");restored.RestartCompletedCourse();Check(restored.CompletedCycles==0&&restored.ElapsedTicks==0,"New dose can restart a fully completed course");

var healingPatient=Patient();var healingPart=healingPatient.RaceProps.body.corePart.parts[0];
Hediff_Injury Injury(Pawn p,float severity,bool permanent=false){
 var injury=new Hediff_Injury{pawn=p,Part=p.RaceProps.body.corePart.parts[0],def=new HediffDef{defName="Cut",maxSeverity=100,comps=new(){new HediffCompProperties_GetsPermanent()}}};
 AccessTools.Method(typeof(HediffWithComps),"InitializeComps").Invoke(injury,null);if(permanent)AccessTools.Field(typeof(HediffComp_GetsPermanent),"isPermanentInt").SetValue(injury.TryGetComp<HediffComp_GetsPermanent>(),true);injury.Severity=severity;p.health.hediffSet.hediffs.Add(injury);return injury;
}
var fresh=Injury(healingPatient,12);var scar=Injury(healingPatient,20,true);
Check(PharmacyRules.HealFreshInjury(healingPatient,2)&&fresh.Severity==10&&scar.Severity==20,"Regeneration heals the fresh injury and preserves older scars");
Check(PharmacyRules.HealScar(healingPatient)&&!healingPatient.health.hediffSet.hediffs.Contains(scar)&&healingPatient.health.hediffSet.hediffs.Contains(fresh),"Scar serum removes exactly one permanent injury");
var implant=new Hediff_AddedPart{pawn=healingPatient,Part=healingPart,def=new HediffDef{defName="BionicArm"}};healingPatient.health.hediffSet.hediffs.Add(implant);
Check(!PharmacyRules.HealFreshInjury(healingPatient,2)&&fresh.Severity==10,"Regeneration preserves artificial limb anatomy");healingPatient.health.hediffSet.hediffs.Remove(implant);
var gene=new Hediff{pawn=healingPatient,def=new HediffDef{defName="ChemicalDependency"}};var pregnancy=new Hediff{pawn=healingPatient,def=new HediffDef{defName="Pregnant"}};var chronic=new Hediff{pawn=healingPatient,def=new HediffDef{defName="Cataract"}};
healingPatient.health.hediffSet.hediffs.AddRange(new[]{gene,pregnancy,chronic});
var oldScar=Injury(healingPatient,5,true);var comprehensive=Effect(healingPatient,"GrechkaPharmacy_GrechkaCourse").TryGetComp<HediffComp_Treatment>();comprehensive.Advance(5000);
Check(!healingPatient.health.hediffSet.hediffs.Contains(oldScar)&&healingPatient.health.hediffSet.hediffs.Contains(chronic),"Comprehensive course cures one old scar, without also curing chronic conditions");
Check(healingPatient.health.hediffSet.hediffs.Contains(gene)&&healingPatient.health.hediffSet.hediffs.Contains(pregnancy),"Comprehensive treatment preserves gene and pregnancy conditions");
harmony.Patch(AccessTools.PropertyGetter(typeof(Hediff_Addiction),nameof(Hediff_Addiction.CurStageIndex)),prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.AddictionStage)));
var detoxPatient=Patient();
Hediff_Addiction Addiction(string chemical,string defName){var def=new HediffDef{defName=defName,maxSeverity=1,initialSeverity=1};DefDatabase<ChemicalDef>.Add(new ChemicalDef{defName=chemical,addictionHediff=def});var h=new Hediff_Addiction{pawn=detoxPatient,def=def};h.Severity=1;detoxPatient.health.hediffSet.hediffs.Add(h);return h;}
var alcohol=Addiction("Alcohol","AlcoholAddiction");var psychite=Addiction("Psychite","PsychiteAddiction");var luciferium=Addiction("Luciferium","LuciferiumAddiction");
Check(PharmacyRules.IsOrdinaryAddiction(alcohol)&&!PharmacyRules.IsOrdinaryAddiction(luciferium),"Detox explicitly accepts ordinary chemicals and excludes luciferium");
var detox=Effect(detoxPatient,"GrechkaPharmacy_DetoxCourse").TryGetComp<HediffComp_Treatment>();detox.Advance(2500);Check(alcohol.Severity<1&&psychite.Severity==1&&luciferium.Severity==1,"One detox course reduces one selected addiction only");
string detoxSave=Path.Combine(temp,"detox.xml");Scribe.saver.InitSaving(detoxSave,"PharmacyTreatment");detox.CompExposeData();Scribe.saver.FinalizeSaving();
var loadedDetox=new HediffComp_Treatment{parent=detox.parent,props=detox.props};Scribe.loader.InitLoading(detoxSave);loadedDetox.CompExposeData();Scribe.loader.FinalizeLoading();
Check(loadedDetox.TargetAddiction=="AlcoholAddiction"&&loadedDetox.CompletedCycles==1,"Detox target and progress survive native Scribe save/load");
loadedDetox.Advance(60000);Check(!detoxPatient.health.hediffSet.hediffs.Contains(alcohol)&&psychite.Severity==1&&luciferium.Severity==1,"Detox finishes its target without spilling into another addiction");
var boosted=Effect(patient,"GrechkaPharmacy_WorkEffect");boosted.TryGetComp<HediffComp_Disappears>().ticksToDisappear=100;
patient.health.RemoveHediff(boosted);Check(!patient.health.hediffSet.hediffs.Any(h=>h.def==hediffs["GrechkaPharmacy_FatigueEffect"]),"Early effect removal does not create a stimulant crash");
boosted=Effect(patient,"GrechkaPharmacy_WorkEffect");boosted.TryGetComp<HediffComp_Disappears>().ticksToDisappear=0;patient.health.RemoveHediff(boosted);
var fatigue=patient.health.hediffSet.GetFirstHediffOfDef(hediffs["GrechkaPharmacy_FatigueEffect"]);Check(fatigue!=null&&fatigue.TryGetComp<HediffComp_Disappears>().ticksToDisappear==15000,"Natural stimulant expiry applies one six-hour fatigue effect");
var armPart=patient.RaceProps.body.corePart.parts[0];var handPart=armPart.parts[0];var headPart=patient.RaceProps.body.corePart.parts[1];
Hediff Missing(BodyPartRecord p){var h=new Hediff_MissingPart{pawn=patient,Part=p,def=new HediffDef{defName="MissingBodyPart"}};patient.health.hediffSet.hediffs.Add(h);return h;}
var missingArm=Missing(armPart);Missing(handPart);Missing(headPart);Check(PharmacyRules.LimbCandidate(patient)==armPart,"Largest eligible missing external limb is selected, never head");
var added=new Hediff_AddedPart{pawn=patient,Part=armPart,def=new HediffDef{defName="BionicArm"}};patient.health.hediffSet.hediffs.Add(added);Check(PharmacyRules.LimbCandidate(patient)==null,"Added parent limb protects all nested anatomy");patient.health.hediffSet.hediffs.Remove(added);
var nestedImplant=new Hediff_Implant{pawn=patient,Part=handPart,def=new HediffDef{defName="GrechkaStructuralImplant"}};patient.health.hediffSet.hediffs.Add(nestedImplant);Check(PharmacyRules.LimbCandidate(patient)==null,"A nested Hediff_Implant blocks parent regeneration and cannot be deleted");patient.health.hediffSet.hediffs.Remove(nestedImplant);
var sacrificed=new Hediff{pawn=patient,Part=armPart,def=new HediffDef{defName="VPE_Sacrificed"}};patient.health.hediffSet.hediffs.Add(sacrificed);Check(PharmacyRules.LimbCandidate(patient)==null,"VPE sacrificed anatomy is never regenerated");patient.health.hediffSet.hediffs.Remove(sacrificed);
Check(PharmacyRules.RestoreLimb(patient)&&Fixture.Restored==armPart,"Regeneration invokes native restoration boundary for one selected limb");
Check(!PharmacyRules.Rejuvenate(patient,5),"Infant age cannot be rejuvenated");
AccessTools.Field(typeof(Pawn_AgeTracker),"ageBiologicalTicksInt").SetValue(patient.ageTracker,40*PharmacyRules.TicksPerYear);
AccessTools.Field(typeof(Pawn_AgeTracker),"cachedLifeStageIndex").SetValue(patient.ageTracker,1);
long birth=(long)AccessTools.Field(typeof(Pawn_AgeTracker),"birthAbsTicksInt").GetValue(patient.ageTracker);
Check(PharmacyRules.Rejuvenate(patient,5)&&patient.ageTracker.AgeBiologicalTicks==35*PharmacyRules.TicksPerYear,"Youth serum removes five biological years");Check((long)AccessTools.Field(typeof(Pawn_AgeTracker),"birthAbsTicksInt").GetValue(patient.ageTracker)==birth,"Rejuvenation preserves chronological birth time");
foreach(int age in new[]{0,10,20,25,26,29,30,80}){long result=PharmacyRules.RejuvenatedAge(age*PharmacyRules.TicksPerYear,5);Check(result==(age<=25?age:Math.Max(25,age-5))*PharmacyRules.TicksPerYear,"Age floor protects patient at "+age);}
Check(PharmacyRules.RejuvenatedAge(40*PharmacyRules.TicksPerYear,float.NaN)==40*PharmacyRules.TicksPerYear,"Invalid age amount ignored");
Console.WriteLine($"Pharmacy: {checks} checks passed. Isolated process; no game launched.");

static class Fixture{
 public static List<string> Errors=new();public static BodyPartRecord Restored;
 public static bool Log(MethodBase __originalMethod,object[] __args){if(__originalMethod.Name=="Error")Errors.Add(__args.FirstOrDefault()?.ToString()??"error");return false;}
 public static bool Add(Pawn_HealthTracker __instance,HediffDef def,BodyPartRecord part,ref Hediff __result){var pawn=(Pawn)AccessTools.Field(typeof(Pawn_HealthTracker),"pawn").GetValue(__instance);var h=new HediffWithComps{def=def,pawn=pawn,Part=part};AccessTools.Method(typeof(HediffWithComps),"InitializeComps").Invoke(h,null);h.Severity=def.initialSeverity;__instance.hediffSet.hediffs.Add(h);__result=h;return false;}
 public static bool Remove(Pawn_HealthTracker __instance,Hediff hediff){__instance.hediffSet.hediffs.Remove(hediff);hediff.PostRemoved();return false;}
 public static bool HealthNotification()=>false;
 public static bool AddictionStage(ref int __result){__result=0;return false;}
 public static bool Restore(BodyPartRecord part){Restored=part;return false;}
}
