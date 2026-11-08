using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Xml.Linq;
using Grechka.Translations.Rus;
using HarmonyLib;
using RimWorld.IO;
using Verse;

string repo = Path.GetFullPath(args[0]);
string managed = Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR");
string workshop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/Steam/steamapps/common/RimWorld/Mods");
var directories = new[] { managed,
    Path.Combine(repo,"mods/GrechkaTranslationsRus/Assemblies"),
    Path.Combine(workshop,"Syamych_2006605356/1.7/Assemblies"),
    Path.Combine(workshop,"Syamych_3534185281/1.7/Assemblies"),
    Path.Combine(workshop,"Syamych_3286376165/1.7/Assemblies") };
AppDomain.CurrentDomain.AssemblyResolve += (_,e) => {
    string file = directories.Select(d => Path.Combine(d,new AssemblyName(e.Name).Name+".dll")).FirstOrDefault(File.Exists);
    return file == null ? null : Assembly.LoadFrom(file);
};
foreach (string file in new[] { "ColoredMoodBar17.dll", "SettingHelper17.dll", "Build_From_Inventory.dll", "CutPlantsBeforeBuilding.dll" })
    Assembly.LoadFrom(directories.Select(d=>Path.Combine(d,file)).First(File.Exists));

string mod = Path.Combine(repo,"mods/GrechkaTranslationsRus");
var languages = new Dictionary<string,LoadedLanguage>();
var load = AccessTools.Method(typeof(LoadedLanguage),"LoadFromFile_Keyed");
int keyed = 0;
foreach (string name in new[] { "Russian", "English" })
{
    var language = new LoadedLanguage(name);
    languages[name]=language;
    AccessTools.Field(typeof(LoadedLanguage),"dataIsLoaded").SetValue(language,true);
    var seen = new HashSet<string>();
    foreach (string file in Directory.GetFiles(mod,"*.xml",SearchOption.AllDirectories).Where(p=>p.Contains("/Languages/"+name+"/Keyed/")))
    {
        load.Invoke(language,new object[]{new DiskFile(file),File.ReadAllText(file)});
        foreach (var entry in XDocument.Load(file).Root.Elements())
        {
            string key=entry.Name.LocalName;
            if (!seen.Add(key)) throw new Exception("Duplicate native keyed entry: "+key);
            if (!language.TryGetTextFromKey(key,out var value) || value.ToString()!=entry.Value.Replace("\\n","\n"))
                throw new Exception("Native loader mismatch "+name+"/"+key);
            keyed++;
        }
    }
    var errors=(List<string>)AccessTools.Field(typeof(LoadedLanguage),"loadErrors").GetValue(language);
    if(errors.Count!=0) throw new Exception(string.Join("\n",errors));
}
LanguageDatabase.defaultLanguage=languages["English"];
var targets=UiLocalization.ConfigureMappings(Path.Combine(mod,"Translations/UiMappings.xml"));
if(targets.Count!=3)throw new Exception("Expected all three installed UI methods, got "+targets.Count);
int replaced=0;
foreach(var method in targets)
{
    var original=PatchProcessor.GetOriginalInstructions(method);
    var labels=original.SelectMany(i=>i.labels).ToList();
    var blocks=original.SelectMany(i=>i.blocks).ToList();
    int callsBefore=original.Count(i=>i.opcode==OpCodes.Call);
    var translated=UiLocalization.Transpiler(original,method).ToList();
    int calls=translated.Count(i=>i.opcode==OpCodes.Call)-callsBefore;
    if(calls==0)throw new Exception("No UI text matched real installed method "+method);
    if(!translated.SelectMany(i=>i.labels).SequenceEqual(labels) || !translated.SelectMany(i=>i.blocks).SequenceEqual(blocks))
        throw new Exception("Transpiler changed branch/exception metadata");
    foreach(var instruction in translated.Where(i=>i.opcode==OpCodes.Call && Equals(i.operand,typeof(UiLocalization).GetMethod("TranslateKey"))))
    {
        int index=translated.IndexOf(instruction);
        string key=(string)translated[index-1].operand;
        if(!languages["Russian"].TryGetTextFromKey(key,out _)) throw new Exception("Missing Russian UI text: "+key);
        if(!languages["English"].TryGetTextFromKey(key,out _)) throw new Exception("Missing English fallback UI text: "+key);
    }
    replaced+=calls;
    if(method.DeclaringType.FullName=="ColoredMoodBar13.MoodPatchMod")
        foreach(var i in translated.Where(i=>i.opcode==OpCodes.Ldstr && !((string)i.operand).StartsWith("CmColoredMoodBarPatch.") && !((string)i.operand).StartsWith("GrechkaTranslationsRus.")))
            throw new Exception("Untranslated settings literal: "+i.operand);
    Console.WriteLine("PASS real UI method "+method.DeclaringType.FullName+"."+method.Name+": "+calls+" text sites");
}
foreach(var name in new[]{"Russian","English","Russian"})
{
    LanguageDatabase.activeLanguage=languages[name];
    string expected=name=="Russian"?"Вырубка растений перед строительством":"Cut Plants Before Building";
    if(UiLocalization.TranslateKey("GrechkaTranslationsRus.CutPlants.Name")!=expected)throw new Exception("Language reload failed");
}
var harmony=new Harmony("grechka.translations.rus.isolated-check");
try
{
    foreach(var method in targets)harmony.Patch(method,transpiler:new HarmonyMethod(typeof(UiLocalization),nameof(UiLocalization.Transpiler)));
    var category=targets.Single(m=>m.Name=="SettingsCategory");
    var instance=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(category.DeclaringType);
    if((string)category.Invoke(instance,null)!="Вырубка растений перед строительством")throw new Exception("Patched real settings category did not translate");
    Console.WriteLine("PASS Harmony patches all three actual installed methods; real SettingsCategory returns Russian");
}
finally { harmony.UnpatchAll(harmony.Id); }
Console.WriteLine("PASS native keyed loader: "+keyed+" entries; "+replaced+" translated UI sites; RU/EN/RU reload");

sealed class DiskFile : VirtualFile
{
    readonly string path;
    public DiskFile(string path){this.path=path;}
    public override string Name=>Path.GetFileName(path);
    public override string FullPath=>path;
    public override bool Exists=>File.Exists(path);
    public override long Length=>new FileInfo(path).Length;
    public override Stream CreateReadStream()=>File.OpenRead(path);
    public override string ReadAllText()=>File.ReadAllText(path);
    public override string[] ReadAllLines()=>File.ReadAllLines(path);
    public override byte[] ReadAllBytes()=>File.ReadAllBytes(path);
}
