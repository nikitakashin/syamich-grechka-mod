using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.Quests;
using HarmonyLib;
using UnityEngine;
using Verse;
using Quest = Grechka.Quests.Quest;
int checks = 0;
void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS " + text); }
var window = (QuestWindow)RuntimeHelpers.GetUninitializedObject(typeof(QuestWindow));
foreach (var size in new[] { (800, 600), (1280, 720), (1920, 1080), (2560, 1440) })
{
    UI.screenWidth = size.Item1; UI.screenHeight = size.Item2;
    AccessTools.Method(typeof(QuestWindow), "SetInitialSizeAndPosition").Invoke(window, null);
    Check(window.windowRect.x == 0 && window.windowRect.y == 0 && window.windowRect.width == size.Item1 && window.windowRect.height == size.Item2,
        "Book covers the complete " + size.Item1 + "x" + size.Item2 + " UI viewport");
}
var game = (QuestGame)RuntimeHelpers.GetUninitializedObject(typeof(QuestGame));
foreach (var field in typeof(QuestGame).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
    if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(List<>)) field.SetValue(game, Activator.CreateInstance(field.FieldType));
Check(!game.HasGraphView("book", "chapter"), "A new chapter can be fitted without confusing the default with a saved view");
game.SetGraphView("book", "chapter", new Vector2(-157, 283), .75f);
game.GraphView("book", "chapter", out Vector2 pan, out float zoom);
Check(game.HasGraphView("book", "chapter") && pan.x == -157 && pan.y == 283 && zoom == .75f,
    "Previously chosen chapter view remains distinguishable and exact");
Check(!game.HasGraphView("other", "chapter") && !game.HasGraphView("book", "other"), "View presence is scoped to book and chapter");
foreach (var (id, label) in new[] { ("BaseMachine", "base machine"), ("TierMachine", "upgraded press"), ("Gift", "gold reward"), ("ChoiceGift", "silver choice") })
    DefDatabase<ThingDef>.Add(new ThingDef { defName = id, label = label, category = ThingCategory.Item, thingClass = typeof(Thing) });
var quest = new Quest { title = "Following housing", description = "Test", objectives = new() { new Objective { target = "BaseMachine", variants = new() { "TierMachine" } } },
    rewards = new() { new Reward { def = "Gift" } }, alternatives = new() { new RewardOption { items = new() { new Reward { def = "ChoiceGift" } } } } };
string search = (string)AccessTools.Method(typeof(QuestWindow), "SearchableQuest").Invoke(null, new object[] { quest });
Check(search.Contains("upgraded press", StringComparison.OrdinalIgnoreCase) && search.Contains("gold reward", StringComparison.OrdinalIgnoreCase) && search.Contains("silver choice", StringComparison.OrdinalIgnoreCase),
    "Search finds allowed tier variants and both guaranteed and choice rewards");
var visuals = typeof(QuestWindow).Assembly.GetType("Grechka.Quests.QuestVisuals", true);
Check((string)AccessTools.Method(visuals, "QuestIcon").Invoke(null, new object[] { quest }) == "BaseMachine", "Unspecified node icon resolves to actual target, not vague objective text");
quest.icon = "Gift";
Check((string)AccessTools.Method(visuals, "QuestIcon").Invoke(null, new object[] { quest }) == "Gift", "Explicit author icon is respected");
var pack = PackCodec.Decode(File.ReadAllText(Path.Combine(args[0], "mods/GrechkaQuests/QuestPacks/grechka-world.json")));
var nodeMethod = AccessTools.Method(typeof(QuestWindow), "GraphNodeRect");
foreach (var chapter in pack.chapters)
{
    var nodes = pack.quests.Where(q => q.chapter == chapter.id).Select((q, i) => (q, rect: (Rect)nodeMethod.Invoke(null, new object[] { q, i, Vector2.zero, 1f }))).ToList();
    for (int i = 0; i < nodes.Count; i++) for (int j = i + 1; j < nodes.Count; j++)
        if (nodes[i].rect.Overlaps(nodes[j].rect)) throw new Exception("Overlapping quest nodes: " + nodes[i].q.id + "/" + nodes[j].q.id);
}
Check(true, "All shipped chapter nodes fit their real clickable rectangles without overlap");
Console.WriteLine(checks + " native presentation checks; rendering and mouse interaction in the game are not simulated.");
