using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.Quests;
using HarmonyLib;
using System.Xml.Linq;
using AchievementsExpanded;
using Grechka.Achivements;
using Grechka.LuckyBlocks;
using Verse;

Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
Current.Game.uniqueIDsManager = new RimWorld.UniqueIDsManager();
var modules = Path.Combine(args[0], "mods");
var folder = Path.Combine(modules, "GrechkaAchivements");
var xml = XDocument.Load(Path.Combine(folder, "Defs/Achievements.xml")).Root;
var tab = xml.Element("AchievementsExpanded.AchievementTabDef").Element("defName").Value;
var ids = new HashSet<string>();
var icons = new HashSet<string>();
var targets = Directory.GetDirectories(modules)
    .Where(dir => File.Exists(Path.Combine(dir, "About/About.xml")) && Directory.Exists(Path.Combine(dir, "Defs")))
    .ToDictionary(dir => XDocument.Load(Path.Combine(dir, "About/About.xml")).Root.Element("packageId").Value,
        dir => Directory.EnumerateFiles(Path.Combine(dir, "Defs"), "*.xml", SearchOption.AllDirectories)
            .SelectMany(f => XDocument.Load(f).Root.Elements()).Where(d => d.Element("defName") != null)
            .ToLookup(d => d.Element("defName").Value, d => d.Name.LocalName));

foreach (var card in xml.Elements("AchievementsExpanded.AchievementDef"))
{
    string id = card.Element("defName").Value;
    void Check(bool pass, string what) { if (!pass) throw new Exception(id + ": " + what); }
    Check(ids.Add(id) && card.Element("tab").Value == tab && (int)card.Element("points") > 0, "card metadata");
    string icon = card.Element("texPath").Value;
    Check(icons.Add(icon) && File.Exists(Path.Combine(folder, "Textures", icon + ".png")), "unique icon");
    var trackerXml = card.Element("tracker");
    string name = trackerXml.Attribute("Class").Value;
    var type = typeof(TrackerBase).Assembly.GetType(name) ?? typeof(GrechkaTracker).Assembly.GetType(name, true);
    var tracker = (TrackerBase)Activator.CreateInstance(type);
    foreach (var entry in trackerXml.Elements())
    {
        var field = type.GetField(entry.Name.LocalName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Check(field != null, "missing tracker field " + entry.Name);
        if (typeof(Def).IsAssignableFrom(field.FieldType))
        {
            Check(targets[card.Attribute("MayRequire").Value][entry.Value].Contains(field.FieldType.Name), "unresolved " + entry.Value);
            var target = (Def)Activator.CreateInstance(field.FieldType);
            target.defName = entry.Value;
            field.SetValue(tracker, target);
        }
        else if (field.FieldType.IsEnum) field.SetValue(tracker, Enum.Parse(field.FieldType, entry.Value));
        else field.SetValue(tracker, int.Parse(entry.Value));
    }
    var copy = (TrackerBase)Activator.CreateInstance(type, tracker);
    Check(copy.Key == tracker.Key && !string.IsNullOrEmpty(copy.Key), "tracker copy");
    if (copy.AttachToLongTick == null)
        Check(copy.MethodHook != null && copy.PatchMethod != null, "event hook missing from installed API");
    if (copy is MilestoneTracker m)
        Check(m.series.ToString() == trackerXml.Element("series").Value && m.target == (int)trackerXml.Element("target"), "milestone copy");
    if (copy is OpenBlockTracker b)
    {
        Check(b.block == ((OpenBlockTracker)tracker).block && b.MethodHook == typeof(LuckyBlock).GetMethod("Open")
            && b.PatchType == PatchType.Postfix, "lucky block copy and hook");
        var other = new ThingDef { defName = "OtherBlock" };
        Check(b.Accepts(other) == (b.block == null) && b.Accepts(b.block ?? other), "wrong lucky block filter");
    }
}
var book = (QuestGame)RuntimeHelpers.GetUninitializedObject(typeof(QuestGame));
var quest = new Quest { id = "test.reward" };
book.Pack = new QuestPack { quests = new() { quest } };
AccessTools.Field(typeof(Game), "components").SetValue(Current.Game, new List<GameComponent> { book });
var progress = new QuestProgress(null, null);
AccessTools.Field(typeof(QuestGame), "progress").SetValue(book, progress);
var reward = new QuestRewardTracker();
if (reward.Trigger()) throw new Exception("Unclaimed reward counted");
progress.RecordClaim(book.Pack, quest);
if (!reward.Trigger()) throw new Exception("Claimed reward missed");
OpenBlockTracker.AfterOpen(null, false); // Partial drops and rejected attempts never reach VAE.
MilestoneChecks.Run(Current.Game, book, progress, quest);
if (ids.Count != 42) throw new Exception("Missing achievement cards");
Console.WriteLine($"PASS {ids.Count} VAE tracker configurations, copy constructors, event bindings, targets and icons.");
