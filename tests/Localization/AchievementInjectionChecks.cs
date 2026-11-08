using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;
using Verse;

internal static class AchievementInjectionChecks
{
    internal static void Run(string repo)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("AchievementsExpanded.AchievementDef"))
            .First(t => t != null);
        FieldInfo description = type.GetField("description", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        if (description == null) throw new Exception("VAE derived description field is missing");
        MethodInfo add = typeof(DefDatabase<>).MakeGenericType(type).GetMethod("Add", new[] { type });
        MethodInfo inject = AccessTools.Method(typeof(DefInjectionPackage), "SetDefFieldAtPath");
        var package = new DefInjectionPackage(type);
        var definitions = new Dictionary<string, Def>();
        int checks = 0;
        foreach (string language in new[] { "English", "Russian" })
        {
            string folder = Path.Combine(repo, "mods", "GrechkaAchivements", "Languages", language, "DefInjected", type.FullName);
            foreach (var entry in Directory.GetFiles(folder, "*.xml").SelectMany(file => XDocument.Load(file).Root.Elements())
                         .Where(node => node.Name.LocalName.EndsWith(".description", StringComparison.Ordinal)))
            {
                string name = entry.Name.LocalName[..^".description".Length];
                if (!definitions.TryGetValue(name, out Def definition))
                {
                    definition = (Def)Activator.CreateInstance(type);
                    definition.defName = name;
                    definition.description = "base field sentinel";
                    description.SetValue(definition, "derived field sentinel");
                    add.Invoke(null, new object[] { definition });
                    definitions.Add(name, definition);
                }
                string expected = entry.Value.Replace("\\n", "\n");
                object[] parameters = { type, entry.Name.LocalName, expected, typeof(string), true,
                    "Grechka achievement localization test", false, null, null, null, null };
                bool success = (bool)inject.Invoke(package, parameters);
                if (!success || (string)description.GetValue(definition) != expected || definition.description != "base field sentinel")
                    throw new Exception($"Native injection failed for {language}/{entry.Name}");
                checks++;
            }
        }
        var errors = (List<string>)AccessTools.Field(typeof(DefInjectionPackage), "loadErrors").GetValue(package);
        if (checks == 0 || errors.Count != 0) throw new Exception("VAE injection errors: " + string.Join("\n", errors));
        Console.WriteLine($"PASS {checks} VAE derived description translations applied through native SetDefFieldAtPath");
    }
}
