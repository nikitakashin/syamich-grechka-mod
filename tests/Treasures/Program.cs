using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using HarmonyLib;
using Grechka.Quests;
using RimWorld;
using Verse;

int checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
string repo=args.Length>0?args[0]:Directory.GetCurrentDirectory();
AccessTools.Method(Type.GetType("Waylith.Rendering.ShaderSourceLoader, Waylith",true),"SetOverrideShaderDirectories").Invoke(null,new object[]{new[]{Path.Combine(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"),"Rendering/Shaders")}});
AccessTools.Field(typeof(UnityData),"mainThreadId").SetValue(null,Environment.CurrentManagedThreadId);
var harmony=new Harmony("grechka.treasures.isolated-tests");
foreach(var method in AccessTools.GetDeclaredMethods(typeof(Log)).Where(m=>new[]{"Error","Warning","Message"}.Contains(m.Name)))harmony.Patch(method,prefix:new HarmonyMethod(typeof(Fixture),nameof(Fixture.Log)));
AccessTools.Field(typeof(Prefs),"data").SetValue(null,new PrefsData());
var temp=Path.Combine(Path.GetTempPath(),"grechka-treasures-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
AccessTools.Field(typeof(GenFilePaths),"saveDataPath").SetValue(null,temp);
Directory.CreateDirectory(Path.Combine(temp,"Config"));Directory.CreateDirectory(Path.Combine(temp,"Mods"));Directory.CreateDirectory(Path.Combine(temp,"Data"));
File.WriteAllText(Path.Combine(temp,"Config/ModsConfig.xml"),"<ModsConfigData><version>1.7.4998</version><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>");
Directory.CreateDirectory(Path.Combine(temp,"Data/Core/About"));File.WriteAllText(Path.Combine(temp,"Data/Core/About/About.xml"),"<ModMetaData><name>Core</name><author>Ludeon Studios</author><packageId>ludeon.rimworld</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>");
AccessTools.Field(typeof(GenFilePaths),"modsFolderPath").SetValue(null,Path.Combine(temp,"Mods"));AccessTools.Field(typeof(GenFilePaths),"officialModsFolderPath").SetValue(null,Path.Combine(temp,"Data"));
var gameRoot=new DirectoryInfo(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"));
while(!Directory.Exists(Path.Combine(gameRoot.FullName,"Data/Core")))gameRoot=gameRoot.Parent??throw new Exception("Missing game data");
var moduleAssembly=Assembly.LoadFrom(Path.Combine(repo,"mods/GrechkaTreasures/Assemblies/GrechkaTreasures.dll"));
var types=typeof(Pawn).Assembly.GetTypes().Concat(moduleAssembly.GetTypes()).ToList();
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
AccessTools.Field(typeof(ModContentPack),"packageIdPlayerFacingInt").SetValue(pack,"grechka.treasures");
AccessTools.Field(typeof(ModContentPack),"rootDirInt").SetValue(pack,new DirectoryInfo(Path.Combine(repo,"mods/GrechkaTreasures")));
pack.assetBundles=new ModAssetBundlesHandler(pack);
AccessTools.Field(typeof(ModContentPack),"textures").SetValue(pack,new ModContentHolder<UnityEngine.Texture2D>(pack));
AccessTools.Field(typeof(ModContentPack),"audioClips").SetValue(pack,new ModContentHolder<UnityEngine.AudioClip>(pack));
AccessTools.Field(typeof(ModContentPack),"strings").SetValue(pack,new ModContentHolder<string>(pack));
pack.assemblies=new ModAssemblyHandler(pack);pack.assemblies.loadedAssemblies.Add(moduleAssembly);
LoadedModManager.RunningModsListForReading.Add(pack);GenTypes.ClearCache();XmlInheritance.Clear();
foreach(var doc in allDocuments)
 foreach(XmlNode node in doc.DocumentElement.ChildNodes)
  if(node is XmlElement&&node.Attributes?["Name"]!=null&&node.Attributes?["MayRequire"]==null&&node.Attributes?["MayRequireAnyOf"]==null)XmlInheritance.TryRegister(node,pack);
var nodes=new List<XmlNode>();
foreach(string file in Directory.GetFiles(Path.Combine(repo,"mods/GrechkaTreasures/Defs"),"*.xml")){
 var doc=new XmlDocument();doc.Load(file);
 foreach(XmlNode node in doc.DocumentElement.ChildNodes)if(node is XmlElement){nodes.Add(node);if(node.Attributes?["Name"]==null)XmlInheritance.TryRegister(node,pack);}
}
XmlInheritance.Resolve();
var resolved=nodes.Where(n=>n.SelectSingleNode("defName")!=null).Select(XmlInheritance.GetResolvedNodeFor).ToList();
var categoryDocument=new XmlDocument();categoryDocument.Load(Path.Combine(repo,"mods/GrechkaTreasures/Patches/ArchitectCategory.xml"));
var category=categoryDocument.SelectSingleNode("Patch/Operation/nomatch/value/DesignationCategoryDef");
index[(typeof(DesignationCategoryDef),"Grechka_IndustryCategory")]=XElement.Parse(category.OuterXml);
var refs=new HashSet<(Type,string)>();
void Reference(Type type,string name){Check(index.ContainsKey((type,name)),"Owned data reference "+type.Name+"/"+name);refs.Add((type,name));}
void Schema(XmlNode node,Type type,bool root=false){
 string className=node.Attributes?["Class"]?.Value;if(className!=null){Type concrete=TypeFor(className)??throw new Exception("Unknown class "+className);Check(type.IsAssignableFrom(concrete),"Assignable XML class "+className);type=concrete;}
 if(type==typeof(StorageSettings)){foreach(XmlNode child in node.ChildNodes){if(child is not XmlElement)continue;if(child.Name=="priority")Enum.Parse<StoragePriority>(child.InnerText);else if(child.Name=="filter")Schema(child,typeof(ThingFilter));else throw new Exception("Unknown custom StorageSettings field "+child.Name);}return;}
 if(type==typeof(StatModifier)){Reference(typeof(StatDef),node.Name);return;}
 if(type==typeof(ThingDefCountClass)){Reference(typeof(ThingDef),node.Name);return;}
 if(type==typeof(SkillRequirement)){Reference(typeof(SkillDef),node.Name);return;}
 if(typeof(Def).IsAssignableFrom(type)&&!root){Reference(type,node.InnerText);return;}
 if(type.IsEnum){Enum.Parse(type,node.ChildNodes.Cast<XmlNode>().Any(c=>c is XmlElement)?string.Join(",",node.ChildNodes.Cast<XmlNode>().Where(c=>c is XmlElement).Select(c=>c.InnerText)):node.InnerText);return;}
 var children=node.ChildNodes.Cast<XmlNode>().Where(n=>n is XmlElement).ToList();if(children.Count==0)return;
 if(type.IsGenericType&&typeof(IEnumerable).IsAssignableFrom(type)){
  var generics=type.GetGenericArguments();foreach(var child in children){if(generics.Length==2&&typeof(Def).IsAssignableFrom(generics[0]))Reference(generics[0],child.Name);else Schema(child,generics.Last());}return;
 }
 foreach(var child in children){FieldInfo field=AccessTools.Field(type,child.Name)??throw new Exception("Unknown field "+type.FullName+"."+child.Name);Schema(child,field.FieldType);}
}
foreach(var node in resolved)Schema(node,TypeFor(node.Name)??throw new Exception(node.Name),true);
Check(true,"All inherited XML fields and enum values match installed RimWorld 1.7");
foreach(string name in new[]{"SculptureSmall","SculptureLarge","SculptureGrand"})Reference(typeof(ThingDef),name);
foreach(var (type,name) in refs){var def=(Def)Activator.CreateInstance(type);def.defName=name;typeof(DefDatabase<>).MakeGenericType(type).GetMethod("Add",new[]{type}).Invoke(null,new object[]{def});}
Fixture.Errors.Clear();
var loaded=new List<Def>();
foreach(var node in nodes.Where(n=>n.SelectSingleNode("defName")!=null)){Type type=TypeFor(node.Name);var method=typeof(DirectXmlToObject).GetMethods().Single(m=>m.Name=="ObjectFromXml"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==2);var def=(Def)method.MakeGenericMethod(type).Invoke(null,new object[]{node,false});loaded.Add(def);}
foreach(var group in loaded.GroupBy(d=>d.GetType())){var database=typeof(DefDatabase<>).MakeGenericType(group.Key);database.GetMethod("Clear").Invoke(null,null);foreach(var def in group)database.GetMethod("Add",new[]{group.Key}).Invoke(null,new object[]{def});}
// Restore external seeded references after replacing only the module's native definitions.
foreach(var (type,name) in refs.Where(r=>!r.Item2.StartsWith("GrechkaTreasures_"))){var database=typeof(DefDatabase<>).MakeGenericType(type);if(database.GetMethod("GetNamedSilentFail").Invoke(null,new object[]{name})!=null)continue;var def=(Def)Activator.CreateInstance(type);def.defName=name;database.GetMethod("Add",new[]{type}).Invoke(null,new object[]{def});}
DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
Check(Fixture.Errors.Count==0,"Native XML parser and cross references report no errors: "+string.Join(";",Fixture.Errors));

var things=loaded.OfType<ThingDef>().ToDictionary(t=>t.defName);
var recipes=loaded.OfType<RecipeDef>().ToList();
var gems=new[]{"Diamond","Ruby","Sapphire","Emerald","Amethyst","Topaz"};
foreach(string gem in gems){
 foreach(string form in new[]{"Raw","Cut","Monolith","Ore"})Check(things.ContainsKey("GrechkaTreasures_"+gem+form),gem+" has "+form);
 foreach(string weapon in new[]{"Knife","Sword","Spear","Mace"}){
  var def=things["GrechkaTreasures_"+gem+weapon];
  Check(def.tools!=null&&def.tools.Count>0&&def.tools.All(t=>t.power>0&&t.cooldownTime>0),"Native melee tools "+def.defName);
  Check(def.comps.Any(c=>c.compClass==typeof(CompQuality)),"Native weapon quality "+def.defName);
 }
 foreach(string decor in new[]{"Statue","Obelisk","Vase","Throne","Table","Bed","Sarcophagus","Lamp"}){
  var def=things["GrechkaTreasures_"+gem+decor];
  if(decor!="Statue"&&decor!="Vase")Check(def.designationCategory?.defName=="Grechka_IndustryCategory","Shared build category "+def.defName);
  Check(def.costList!=null&&def.costList.Count>0&&def.costList.All(c=>c.count>0),"Native construction cost "+def.defName);
  if(decor!="Statue"&&decor!="Vase")Check(def.designatorDropdown!=null,"Grouped jewel colours "+def.defName);
 }
}
foreach(var recipe in recipes){
 Check(recipe.ingredients.Count>0&&recipe.ingredients.All(i=>i.GetBaseCount()>0),"Positive physical ingredients "+recipe.defName);
 Check((recipe.workerClass?.FullName=="Grechka.Treasures.RecipeWorker_CutAnyGem" || recipe.products.Count>0)&&recipe.products.All(p=>p.thingDef!=null&&p.count>0),"Resolved recipe products "+recipe.defName);
 Check(recipe.recipeUsers!=null&&recipe.recipeUsers.Count>0,"Accessible workstation "+recipe.defName);
}
var language=new LoadedLanguage("English");AccessTools.Field(typeof(LoadedLanguage),"workerInt").SetValue(language,new LanguageWorker_English());AccessTools.Field(typeof(LoadedLanguage),"dataIsLoaded").SetValue(language,true);LanguageDatabase.activeLanguage=language;LanguageDatabase.defaultLanguage=language;
var makeable=loaded.OfType<ThingDef>().Where(t=>t.recipeMaker!=null).ToList();
Check(makeable.Count==36,"Exactly 24 weapons and 12 sculptures are makeable");
foreach(var t in makeable)Check(!t.MadeFromStuff&&t.costStuffCount==0,"No implied Stuff requirement "+t.defName);
Console.WriteLine("Invoking native recipe makers");
var implied=(IEnumerable<RecipeDef>)AccessTools.Method(typeof(RecipeDefGenerator),"DefsFromRecipeMakers").Invoke(null,new object[]{false});
var made=implied.Where(r=>r.defName.StartsWith("Make_GrechkaTreasures_")).ToList();
Check(made.Count==36,"Native generator creates all 36 expected recipes");
foreach(var t in makeable){
 var r=made.Single(r=>r.defName=="Make_"+t.defName);
 Check(r.products.Count==1&&r.products[0].thingDef==t&&r.products[0].count==1,"One exact product "+r.defName);
 Check(r.ingredients.Count==t.costList.Count&&r.ingredients.All(i=>!i.IsFixedIngredient||i.GetBaseCount()>0),"Fixed ingredient count "+r.defName);
 Check(r.recipeUsers.Count==1&&r.recipeUsers[0].defName=="GrechkaTreasures_JewelersBench","Jeweler availability "+r.defName);
 DefDatabase<RecipeDef>.Add(r);
}
var bench=loaded.OfType<ThingDef>().Single(t=>t.defName=="GrechkaTreasures_JewelersBench");
Check(bench.AllRecipes.Count(r=>r.defName.StartsWith("Make_GrechkaTreasures_"))==36,"All 36 generated recipes available through bench.AllRecipes");
Check(Fixture.Errors.Count==0,"No logged native errors after recipe generation: "+string.Join(";",Fixture.Errors));

var workGiver=loaded.OfType<WorkGiverDef>().Single(w=>w.defName=="GrechkaTreasures_DoBillsJeweler");
Check(workGiver.giverClass==typeof(WorkGiver_DoBill)&&workGiver.fixedBillGiverDefs.Contains(bench),"Native bill scanner includes the jeweler workbench");
Check(bench.AllRecipes.Count(r=>r.defName.StartsWith("GrechkaTreasures_"))==13,"Twelve specific recipes and one general cutting bill available on bench");

Current.Game ??= (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
Current.Game.uniqueIDsManager ??= new UniqueIDsManager();
var anyGem=recipes.Single(r=>r.defName=="GrechkaTreasures_CutAnyGem");
anyGem.fixedIngredientFilter.ResolveReferences();
foreach(var ingredient in anyGem.ingredients)ingredient.filter.ResolveReferences();
anyGem.defaultIngredientFilter = new ThingFilter();
anyGem.defaultIngredientFilter.CopyAllowancesFrom(anyGem.fixedIngredientFilter);
var gemCutting=moduleAssembly.GetType("Grechka.Treasures.GemCutting",true);
RuntimeHelpers.RunClassConstructor(gemCutting.TypeHandle);
foreach(string gem in new[]{"Diamond","Ruby","Sapphire","Emerald","Amethyst","Topaz"}){
 var raw=DefDatabase<ThingDef>.GetNamed("GrechkaTreasures_"+gem+"Raw");
 var cut=DefDatabase<ThingDef>.GetNamed("GrechkaTreasures_"+gem+"Cut");
 raw.category=ThingCategory.Item;cut.category=ThingCategory.Item;cut.thingClass=typeof(ThingWithComps);cut.stackLimit=75;cut.useHitPoints=false;
 Check(anyGem.fixedIngredientFilter.Allows(raw)&&anyGem.ingredients.Single().filter.Allows(raw),"General cutting filter accepts "+gem);
 var item=new ThingWithComps{def=raw,stackCount=1};
 var products=GenRecipe.MakeRecipeProducts(anyGem,null,new List<Thing>{item},item,null).ToList();
 Check(products.Count==1&&products[0].def==cut&&products[0].stackCount==1&&item.stackCount==1&&!item.Destroyed,"Native general recipe returns matching gem without pre-consuming input: "+gem);
 var bill=(Bill_Production)anyGem.MakeNewBill();bill.ingredientFilter.SetAllow(raw,false);
 Check(!bill.IsFixedOrAllowedIngredient(raw),"General bill respects disabled raw type: "+gem);
}
Check(!anyGem.WorkerCounter.CanCountProducts((Bill_Production)anyGem.MakeNewBill()),"Variable products use repeat-count/forever bills without an incorrect single-gem stock target");

var worldBook=PackCodec.Decode(File.ReadAllText(Path.Combine(repo,"mods/GrechkaQuests/QuestPacks/grechka-world.json")));
var learning=worldBook.quests.Where(q=>q.id.StartsWith("treasures.")).ToList();
Check(learning.Count==12,"Twelve learning quests decode through the actual Quests codec");
// The content fixture seeds external references without their fields. Learning
// quests also use Core supplies: retain their actual inherited ThingCategory.
var bases=allDocuments.SelectMany(d=>d.DocumentElement.ChildNodes.Cast<XmlNode>())
 .Where(n=>n is XmlElement&&n.Attributes?["Name"]!=null)
 .GroupBy(n=>n.Attributes["Name"].Value).ToDictionary(g=>g.Key,g=>XElement.Parse(g.Last().OuterXml));
foreach(string name in learning.SelectMany(q=>q.objectives).Select(o=>o.target).Where(n=>n!=null&&!n.StartsWith("GrechkaTreasures_")).Distinct()){
 Check(index.TryGetValue((typeof(ThingDef),name),out var source),"Core learning supply exists "+name);
 var seen=new HashSet<string>();
 while(source.Element("category")==null&&source.Attribute("ParentName") is { } parent&&seen.Add(parent.Value))
  source=bases[parent.Value];
 var external=DefDatabase<ThingDef>.GetNamedSilentFail(name);
 if(external==null){external=new ThingDef{defName=name};DefDatabase<ThingDef>.Add(external);}
 external.category=Enum.Parse<ThingCategory>(source.Element("category").Value);
}
foreach(var quest in learning){
 foreach(var objective in quest.objectives)
  Check(QuestGame.ObjectiveProblem(objective)==null,"Actual quest objective accepts installed target types "+quest.id+"/"+objective.id+": "+QuestGame.ObjectiveProblem(objective));
 foreach(var reward in quest.rewards.Where(r=>r.def.StartsWith("GrechkaTreasures_")))
  Check(QuestGame.RewardProblem(reward)==null,"Actual reward validator accepts physical learning reward "+quest.id+"/"+reward.def);
}

// Use the game's actual patch engine to verify standalone fallback is repeatable.
var categoryXml=new XmlDocument();categoryXml.LoadXml("<Defs/>");
var patch=DirectXmlToObject.ObjectFromXml<PatchOperation>(categoryDocument.SelectSingleNode("Patch/Operation"),false);
Check(patch.Apply(categoryXml)&&patch.Apply(categoryXml)&&categoryXml.SelectNodes("Defs/DesignationCategoryDef").Count==1,"Standalone category patch is idempotent");
Check(Fixture.Errors.Count==0,"Native validation finished without logged errors: "+string.Join(";",Fixture.Errors));
Console.WriteLine($"Treasures: {checks} installed-library XML/content checks passed. No game launched.");
static class Fixture{
 public static List<string> Errors=new();
 public static bool Log(MethodBase __originalMethod,object[] __args){if(__originalMethod.Name=="Error")Errors.Add(__args.FirstOrDefault()?.ToString()??"error");return false;}
}
