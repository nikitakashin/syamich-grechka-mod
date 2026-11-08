using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using HarmonyLib;
using Verse;

internal static class DefInjectionChecks
{
    internal static void Run(string repo)
    {
        Console.WriteLine("DefInjected checks: resolving native types");
        var dlls = Directory.GetFiles(Path.Combine(repo, "mods"), "*.dll", SearchOption.AllDirectories)
            .Where(p => p.Contains("/Assemblies/")).ToList();
        var eager = new List<string>(dlls);
        foreach (string variable in new[] { "RIMWORLD_MANAGED_DIR", "HARMONY_ASSEMBLY", "MISC_ROBOTS_ASSEMBLY", "ACHIEVEMENTS_ASSEMBLY" })
        {
            string path = Environment.GetEnvironmentVariable(variable);
            if (Directory.Exists(path)) dlls.AddRange(Directory.GetFiles(path, "*.dll"));
            else if (File.Exists(path)) { dlls.Add(path); eager.Add(path); }
        }
        Assembly Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            string path = dlls.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name.Name);
            return path == null ? null : context.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            foreach (string path in eager)
                if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == Path.GetFileNameWithoutExtension(path)))
                    AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
            Console.WriteLine("DefInjected checks: assemblies loaded");
            var types = AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes).ToList();
            var fullNames = types.Where(t => t.FullName != null).GroupBy(t => t.FullName).ToDictionary(g => g.Key, g => g.First());
            var shortNames = types.GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());
            Type FindType(string name)
            {
                if (name == null) return null;
                return fullNames.TryGetValue(name, out Type found) ? found : shortNames.GetValueOrDefault(name);
            }
            var nodes = new List<XElement>();
            foreach (string path in Directory.GetFiles(Path.Combine(repo, "mods"), "*.xml", SearchOption.AllDirectories)
                         .Where(p => p.Contains("/Defs/") || p.Contains("/Patches/")))
            {
                var document = XDocument.Load(path);
                nodes.AddRange(document.Descendants().Where(e => e.Element("defName") != null));
                nodes.AddRange(document.Descendants().Where(e => e.Attribute("Name") != null && e.Parent?.Name.LocalName == "Defs"));
            }
            string managed = Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR");
            for (DirectoryInfo parent = Directory.Exists(managed) ? new DirectoryInfo(managed) : null; parent != null; parent = parent.Parent)
            {
                string core = Path.Combine(parent.FullName, "Data", "Core", "Defs");
                if (!Directory.Exists(core)) continue;
                foreach (string file in Directory.GetFiles(core, "*.xml", SearchOption.AllDirectories))
                    nodes.AddRange(XDocument.Load(file).Root.Elements());
                break;
            }
            var named = nodes.Where(n => n.Attribute("Name") != null).GroupBy(n => (string)n.Attribute("Name"))
                .ToDictionary(g => g.Key, g => g.First());
            Console.WriteLine("DefInjected checks: validating paths");
            int checks = 0;
            var errors = new List<string>();
            foreach (string file in Directory.GetFiles(Path.Combine(repo, "mods"), "*.xml", SearchOption.AllDirectories)
                         .Where(p => p.Contains("/DefInjected/")
                             // These ignored external catalogues refer to definitions outside this repository.
                             && !p.Contains("/GrechkaTranslationsRus/Translations/")))
            {
                string folder = Path.GetFileName(Path.GetDirectoryName(file));
                Type rootType = FindType(folder);
                if (rootType == null || !typeof(Def).IsAssignableFrom(rootType))
                { errors.Add($"{file}: unknown Def type {folder}"); continue; }
                foreach (XElement entry in XDocument.Load(file).Root.Elements())
                {
                    string[] parts = entry.Name.LocalName.Split('.');
                    XElement source = nodes.FirstOrDefault(n => (string)n.Element("defName") == parts[0]
                        && rootType.IsAssignableFrom(FindType((string)n.Attribute("Class") ?? n.Name.LocalName) ?? typeof(object)));
                    if (source == null) { errors.Add($"{folder}/{entry.Name}: target definition missing"); continue; }
                    source = ResolveXml(source, named, new HashSet<XElement>());
                    Type type = FindType((string)source.Attribute("Class")) ?? rootType;
                    FieldInfo field = null;
                    bool valid = true;
                    foreach (string part in parts.Skip(1))
                    {
                        if (int.TryParse(part, out int index))
                        {
                            Type element = type.IsArray ? type.GetElementType() : type.IsGenericType ? type.GetGenericArguments().FirstOrDefault() : null;
                            XElement item = source?.Elements("li").ElementAtOrDefault(index);
                            if (element == null || item == null) { errors.Add($"{folder}/{entry.Name}: invalid list index {part}"); valid = false; break; }
                            type = FindType((string)item.Attribute("Class")) ?? element;
                            source = item;
                            continue;
                        }
                        field = AccessTools.Field(type, part);
                        if (field == null) { errors.Add($"{folder}/{entry.Name}: missing native field {type.FullName}.{part}"); valid = false; break; }
                        XElement child = FindElement(source, part, named);
                        if (child == null) { errors.Add($"{folder}/{entry.Name}: XML path does not exist at {part}"); valid = false; break; }
                        type = FindType((string)child.Attribute("Class")) ?? field.FieldType;
                        source = child;
                    }
                    if (!valid) continue;
                    // VAE hides Def.description without [MustTranslate]. Native SetDefFieldAtPath
                    // still permits this field, verified for every entry by AchievementInjectionChecks.
                    bool nativeVaeDescription = field?.DeclaringType?.FullName == "AchievementsExpanded.AchievementDef"
                        && field.Name == "description";
                    if (type != typeof(string) || field == null || (!nativeVaeDescription && !field.GetCustomAttributes(true).Any(a => a.GetType().Name is "MustTranslateAttribute" or "MustTranslate")))
                        errors.Add($"{folder}/{entry.Name}: leaf is not a native [MustTranslate] string");
                    else checks++;
                }
            }
            if (errors.Count != 0) throw new Exception("DefInjected path validation failed:\n" + string.Join("\n", errors.Distinct()));
            Console.WriteLine($"PASS {checks} DefInjected paths resolve to existing XML targets and native translatable strings");
        }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
    }

    private static XElement ResolveXml(XElement node, Dictionary<string, XElement> named, HashSet<XElement> visited)
    {
        if (!visited.Add(node)) throw new Exception("XML inheritance cycle at " + node.Name);
        string parent = (string)node.Attribute("ParentName");
        XElement resolved = parent != null && named.TryGetValue(parent, out XElement inherited)
            ? MergeXml(ResolveXml(inherited, named, visited), node) : new XElement(node);
        visited.Remove(node);
        return resolved;
    }

    private static XElement MergeXml(XElement inherited, XElement child)
    {
        if (string.Equals((string)child.Attribute("Inherit"), "false", StringComparison.OrdinalIgnoreCase)
            || !child.HasElements) return new XElement(child);
        XElement result = new XElement(inherited);
        foreach (XAttribute attribute in child.Attributes()) result.SetAttributeValue(attribute.Name, attribute.Value);
        foreach (XElement element in child.Elements())
        {
            XElement original = element.Name.LocalName == "li" ? null : result.Element(element.Name);
            if (original == null) result.Add(new XElement(element));
            else original.ReplaceWith(MergeXml(original, element));
        }
        return result;
    }

    private static XElement FindElement(XElement node, string field, Dictionary<string, XElement> named)
    {
        var visited = new HashSet<XElement>();
        while (node != null && visited.Add(node))
        {
            XElement child = node.Element(field);
            if (child != null) return child;
            string parent = (string)node.Attribute("ParentName");
            node = parent != null && named.TryGetValue(parent, out XElement inherited) ? inherited : null;
        }
        return null;
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error) { return error.Types.Where(t => t != null); }
    }
}
