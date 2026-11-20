using Grechka.WorldIntegration;
using HarmonyLib;
using RimWorld.IO;
using Verse;

static class NamingChecks
{
    public static void Run()
    {
        var savedLanguage=LanguageDatabase.activeLanguage; var savedDefault=LanguageDatabase.defaultLanguage;
        var type=typeof(WorldProfile).Assembly.GetType("Grechka.WorldIntegration.VariantLocalization");
        var refresh=AccessTools.Method(type,"Refresh");
        var source=new ThingDef {defName="NamingBattery",label="large battery"}; DefDatabase<ThingDef>.Add(source);
        var variants=new List<ThingDef>();
        for(int tier=2;tier<=5;tier++)
        {
            var def=tier==2?source:new ThingDef{defName="NamingBatteryT"+tier};
            if(tier!=2)DefDatabase<ThingDef>.Add(def); variants.Add(def);
            ProfileVariants.Entries[def.defName]=new VariantEntry{sourceName=source.defName,name=def.defName,tier=tier,minimum=2};
        }
        var advanced=new ThingDef{defName="NamingAdvancedBattery",label="large advanced battery"}; DefDatabase<ThingDef>.Add(advanced);
        ProfileVariants.Entries[advanced.defName]=new VariantEntry{sourceName=advanced.defName,name=advanced.defName,tier=3,minimum=3};
        try
        {
            foreach(string languageName in new[]{"English","Russian","English"})
            {
                var language=new LoadedLanguage(languageName);
                AccessTools.Field(typeof(LoadedLanguage),"dataIsLoaded").SetValue(language,true);
                var path="mods/GrechkaWorldIntegration/Languages/"+languageName+"/Keyed/Grechka.xml";
                AccessTools.Method(typeof(LoadedLanguage),"LoadFromFile_Keyed").Invoke(language,new object[]{new FileSource(path),File.ReadAllText(path)});
                LanguageDatabase.activeLanguage=language; LanguageDatabase.defaultLanguage=language;
                source.label=languageName=="Russian"?"большая батарея":"large battery";
                advanced.label=languageName=="Russian"?"большая улучшенная батарея":"large advanced battery";
                refresh.Invoke(null,null);
                string[] expected=languageName=="Russian"?new[]{"большая батарея","меха-большая батарея (T3)","архо-большая батарея (T4)","гречневая большая батарея (T5)"}
                    :new[]{"large battery","mecha large battery (T3)","archo large battery (T4)","grechka large battery (T5)"};
                if(!variants.Select(v=>v.label).SequenceEqual(expected))throw new Exception("Tier label mismatch: "+string.Join("; ",variants.Select(v=>v.label)));
                string before=advanced.label; refresh.Invoke(null,null);
                if(advanced.label!=before||!variants.Select(v=>v.label).SequenceEqual(expected))throw new Exception("Repeated refresh duplicated a tier theme");
                Console.WriteLine("PASS Generated names and original T3 batteries use the " + languageName + " tier themes without duplicating prefixes on refresh");
            }
        }
        finally
        {
            LanguageDatabase.activeLanguage=savedLanguage; LanguageDatabase.defaultLanguage=savedDefault;
            foreach(var def in variants.Append(advanced))ProfileVariants.Entries.Remove(def.defName);
        }
    }
    sealed class FileSource(string path):VirtualFile
    {
        public override string Name=>Path.GetFileName(path); public override string FullPath=>Path.GetFullPath(path);
        public override bool Exists=>File.Exists(path);public override long Length=>new FileInfo(path).Length;
        public override Stream CreateReadStream()=>File.OpenRead(path);public override string ReadAllText()=>File.ReadAllText(path);
        public override string[] ReadAllLines()=>File.ReadAllLines(path);public override byte[] ReadAllBytes()=>File.ReadAllBytes(path);
    }
}
