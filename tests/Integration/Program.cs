using System.Reflection;
using System.Xml.Linq;
using System.Collections;
using HarmonyLib;
using Verse;
using RimWorld;
var modules = new[] { "GrechkaIndustry", "GrechkaStorage", "GrechkaEquipment", "GrechkaLuckyBlocks", "GrechkaQuests", "GrechkaWorldIntegration" };
var optional = new List<Assembly>();
string pipes = Environment.GetEnvironmentVariable("PIPESYSTEM_ASSEMBLY");
if (!string.IsNullOrEmpty(pipes) && File.Exists(pipes)) optional.Add(Assembly.LoadFrom(pipes));
var assemblies = modules.Select(Assembly.Load).Prepend(typeof(ThingDef).Assembly).Concat(optional).Distinct().ToArray();
Type Resolve(string name) => assemblies.SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.FullName == name || t.Name == name);
void Schema(XElement node, Type type, bool root = false)
{
    string specific = (string)node.Attribute("Class");
    if (specific != null)
    {
        Type concrete = Resolve(specific);
        if (concrete == null || !type.IsAssignableFrom(concrete)) throw new Exception($"Invalid Class={specific} for {type}");
        type = concrete;
    }
    if (!node.HasElements) return;
    if (type.IsEnum)
    {
        foreach (var item in node.Elements())
            if (item.Name != "li" || !Enum.TryParse(type, item.Value, out _)) throw new Exception("Invalid enum flag " + item);
        return;
    }
    if (typeof(Def).IsAssignableFrom(type) && !root) return; // def reference
    if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
    {
        Type itemType = type.GetGenericArguments()[0];
        if (new[] {typeof(StatModifier),typeof(ThingDefCountClass),typeof(SkillRequirement)}.Contains(itemType)) return;
        foreach (var item in node.Elements()) Schema(item,itemType);
        return;
    }
    foreach (XElement child in node.Elements())
    {
        FieldInfo f = AccessTools.Field(type,child.Name.LocalName);
        if(f == null) throw new Exception($"Unknown XML field {type.Name}.{child.Name} in {node.Name}");
        Schema(child, f.FieldType);
    }
}

string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var identities = new HashSet<string>();
int count = 0;
foreach (string module in modules)
{
    foreach (string file in Directory.GetFiles(Path.Combine(root, "mods", module, "Defs"), "*.xml", SearchOption.AllDirectories))
    foreach (XElement node in XDocument.Load(file).Root.Elements())
    {
        Type type = Resolve(node.Name.LocalName);
        if (type == null && node.Attribute("MayRequire") != null) continue;
        if (type == null) throw new Exception("Unknown required Def type: " + node.Name + " in " + file);
        try { Schema(node, type, true); }
        catch (Exception error) { throw new Exception(file + ": " + error.Message, error); }
        string name = (string)node.Element("defName");
        if (type == typeof(ThingDef) && name != null && char.IsDigit(name[^1])) throw new Exception("ThingDef name ends with a digit: " + name);
        if (name != null && !identities.Add(type.FullName + ":" + name)) throw new Exception("Duplicate definition " + name);
        count++;
    }
}
Console.WriteLine("PASS " + count + " XML definitions across six modules match installed game API and have unique identities.");
Console.WriteLine("No game launched; XML inheritance/rendering and live play remain separate acceptance.");
