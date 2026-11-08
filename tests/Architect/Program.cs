using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
string root=Path.GetFullPath(args.Length>0?args[0]:".");
AccessTools.Field(typeof(Prefs),"data").SetValue(null,new PrefsData());
// Exercise the game's actual XML patch engine for every standalone combination and load order.
string[] modules={"GrechkaIndustry","GrechkaStorage","GrechkaQuests"};
IEnumerable<string[]> Orders(string[] remaining, string[] prefix)
{
 if(prefix.Length>0)yield return prefix;
 foreach(string next in remaining)
  foreach(var order in Orders(remaining.Where(m=>m!=next).ToArray(),prefix.Append(next).ToArray()))
   yield return order;
}
foreach(var order in Orders(modules,Array.Empty<string>()))
{
 var xml=new XmlDocument();
 xml.LoadXml("<Defs><DesignationCategoryDef><defName>Production</defName><label>Production</label></DesignationCategoryDef></Defs>");
 string production=xml.DocumentElement.FirstChild.OuterXml;
 if(order.Contains("GrechkaIndustry"))
 {
  var definitions=new XmlDocument();definitions.Load(Path.Combine(root,"mods/GrechkaIndustry/Defs/DesignationCategories.xml"));
  foreach(XmlNode definition in definitions.DocumentElement.ChildNodes)
   if(definition is XmlElement)xml.DocumentElement.AppendChild(xml.ImportNode(definition,true));
 }
 for(int repeat=0;repeat<2;repeat++)
 foreach(string module in order)
 {
  string relative=module=="GrechkaStorage"?"Patches/ArchitectCategory.xml":module=="GrechkaQuests"?"Patches/RewardMarker.xml":null;
  if(relative==null)continue;
  var patchXml=new XmlDocument();patchXml.Load(Path.Combine(root,"mods",module,relative));
  foreach(XmlNode operation in patchXml.SelectNodes("Patch/Operation"))
  {
   var patch=DirectXmlToObject.ObjectFromXml<PatchOperation>(operation,false);
   Check(patch.Apply(xml),"Native category patch applies: "+module);
  }
 }
 var own=xml.SelectNodes("Defs/DesignationCategoryDef[starts-with(defName,'Grechka_')]");
 own=xml.SelectNodes("Defs/DesignationCategoryDef[defName='Grechka_IndustryCategory']");
 Check(xml.SelectNodes("Defs/DesignationCategoryDef[defName='Grechka_SolarCategory']").Count==(order.Contains("GrechkaIndustry")?1:0),"Solar category is supplied only by Industry");
 Check(own.Count==1 && own[0].SelectSingleNode("label").InnerText=="Гречка"
   && own[0].SelectSingleNode("showPowerGrid").InnerText=="true"
   && own[0].SelectNodes("specialDesignatorClasses/li").Count==(order.Contains("GrechkaQuests")?1:0)
   && xml.DocumentElement.FirstChild.OuterXml==production,
   "One shared category, optional reward marker and unchanged Production: "+string.Join(",",order));
}
var target=AccessTools.Method(typeof(MainTabWindow_Architect),"DoCategoryButton");
foreach(string module in args.Length>1?args.Skip(1):new[]{"GrechkaIndustry","GrechkaStorage","GrechkaQuests","GrechkaWorldIntegration"})
{
 var assembly=Assembly.LoadFrom(Path.Combine(root,"mods",module,"Assemblies",module+".dll"));
 Type hook=assembly.GetType("Grechka.Shared.ArchitectCategoryBranding",true);
 new Harmony("grechka.test.architect."+module).CreateClassProcessor(hook).Patch();
 Type visibility=assembly.GetType("Grechka.Shared.ArchitectEmptyCategoryVisibility",true);
 new Harmony("grechka.test.architect.visibility."+module).CreateClassProcessor(visibility).Patch();
 Check(Harmony.GetPatchInfo(AccessTools.PropertyGetter(typeof(DesignationCategoryDef),nameof(DesignationCategoryDef.Visible))).Postfixes.Count==1,"Optional empty groups use a single visibility patch");
 var empty=new DesignationCategoryDef{defName="Grechka_IndustryCategory"};
 AccessTools.Field(typeof(DesignationCategoryDef),"resolvedDesignators").SetValue(empty,new List<Designator>());
 object[] emptyArgs={empty,true};visibility.GetMethod("Postfix").Invoke(null,emptyArgs);
 Check(!(bool)emptyArgs[1],"Standalone modules hide an empty optional profile category");
 Check(Harmony.GetPatchInfo(target).Transpilers.Count==1,"Standalone module "+module+" keeps exactly one shared Architect branding patch");
 var check=hook.GetMethod("IsBranded");
 Check((bool)check.Invoke(null,new object[]{new DesignationCategoryDef{defName="Grechka_IndustryCategory"}})
   &&!(bool)check.Invoke(null,new object[]{new DesignationCategoryDef{defName="Production"}})
   &&!(bool)check.Invoke(null,new object[]{new DesignationCategoryDef{defName="Grechka_ThirdPartyCategory"}}),"Branding recognizes only the explicit Grechka category registry");
 // Use the category's real content holder without loading graphics or scanning game folders.
 var mod=(ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
 var holder=new ModContentHolder<Texture2D>(mod);
 AccessTools.Field(typeof(ModContentPack),"textures").SetValue(mod,holder);
 var textures=(Dictionary<string,Texture2D>)AccessTools.Field(typeof(ModContentHolder<Texture2D>),"contentList").GetValue(holder);
 var category=new DesignationCategoryDef{defName="Grechka_IndustryCategory",label="Grechka",modContentPack=mod};
 var oldLanguage=LanguageDatabase.activeLanguage;
 try
 {
  var first=(Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
  var reloaded=(Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
  textures["UI/GrechkaBrand/Grain"]=first;
  var english=(LoadedLanguage)RuntimeHelpers.GetUninitializedObject(typeof(LoadedLanguage));
  var russian=(LoadedLanguage)RuntimeHelpers.GetUninitializedObject(typeof(LoadedLanguage));
  english.folderName="English";russian.folderName="Russian";
  LanguageDatabase.activeLanguage=english;
  Check(ReferenceEquals(hook.GetMethod("ResolveIcon").Invoke(null,new object[]{category}),first),"English category resolves the current mod texture");
  textures["UI/GrechkaBrand/Grain"]=reloaded;
  LanguageDatabase.activeLanguage=russian;
  category.label="Гречка";
  Check(ReferenceEquals(hook.GetMethod("ResolveIcon").Invoke(null,new object[]{category}),reloaded)
    &&(bool)check.Invoke(null,new object[]{new DesignationCategoryDef{defName="Grechka_IndustryCategory",label="Гречка"}}),
    "Russian language/content reload uses the new icon without retaining a destroyed texture");
 }
 finally {LanguageDatabase.activeLanguage=oldLanguage;}
}
SolarChecks.Run(root);
Console.WriteLine("Native Architect patch binding/deduplication complete; no graphical game session was controlled.");
