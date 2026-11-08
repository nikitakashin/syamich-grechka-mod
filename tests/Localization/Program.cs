using System.Xml.Linq;
using HarmonyLib;
using RimWorld.IO;
using Verse;

string repo = Path.GetFullPath(args.FirstOrDefault() ?? ".");
int checks = 0;
var languages = new Dictionary<string, LoadedLanguage>();
var load = AccessTools.Method(typeof(LoadedLanguage), "LoadFromFile_Keyed");
foreach (string name in new[] { "Russian", "English" })
{
    var language = new LoadedLanguage(name);
    languages.Add(name, language);
    AccessTools.Field(typeof(LoadedLanguage), "dataIsLoaded").SetValue(language, true);
    foreach (string file in Directory.GetFiles(Path.Combine(repo, "mods"), "*.xml", SearchOption.AllDirectories)
                 .Where(p => p.Contains($"/Languages/{name}/Keyed/")
                     // Private third-party catalogues have a separate installed-mod native suite.
                     && !p.Contains("/GrechkaTranslationsRus/Translations/")))
    {
        load.Invoke(language, new object[] { new DiskFile(file), File.ReadAllText(file) });
        foreach (var entry in XDocument.Load(file).Root.Elements())
        {
            string key = entry.Name.LocalName;
            if (!language.TryGetTextFromKey(key, out var translated))
                throw new Exception($"Native language loader lost {name}/{key}");
            // Native loader expands escaped newlines in language data.
            string expected = entry.Value.Replace("\\n", "\n");
            if (translated.ToString() != expected)
                throw new Exception($"Native translation differs for {name}/{key}");
            checks++;
        }
    }
    var errors = (List<string>)AccessTools.Field(typeof(LoadedLanguage), "loadErrors").GetValue(language);
    if (errors.Count != 0) throw new Exception(string.Join("\n", errors));
}
foreach (string name in new[] { "Russian", "English", "Russian" })
{
    LanguageDatabase.activeLanguage = languages[name];
    LanguageDatabase.defaultLanguage = languages["English"];
    string expected = name == "Russian" ? "Антисклонность" : "Aversion";
    if ("GrechkaTweaks_Grade0".Translate().ToString() != expected)
        throw new Exception("Translate did not follow the active language " + name);
    checks++;
}
DefInjectionChecks.Run(repo);
AchievementInjectionChecks.Run(repo);
Console.WriteLine($"PASS {checks} keyed entries loaded and resolved through RimWorld LoadedLanguage in both languages");

sealed class DiskFile(string path) : VirtualFile
{
    public override string Name => Path.GetFileName(path);
    public override string FullPath => path;
    public override bool Exists => File.Exists(path);
    public override long Length => new FileInfo(path).Length;
    public override Stream CreateReadStream() => File.OpenRead(path);
    public override string ReadAllText() => File.ReadAllText(path);
    public override string[] ReadAllLines() => File.ReadAllLines(path);
    public override byte[] ReadAllBytes() => File.ReadAllBytes(path);
}
