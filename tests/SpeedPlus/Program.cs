using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Grechka.SpeedPlus;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

int checks = 0;
void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS " + text); }
var manager = (TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));
manager.slower = new TimeSlower();
var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
game.tickManager = manager;
var maps = new List<Map>();
AccessTools.Field(typeof(Game), "maps").SetValue(game, maps);
Current.Game = game;
var speed = AccessTools.Field(typeof(TickManager), "curTimeSpeed");
var until = AccessTools.Field(typeof(TimeSlower), "forceNormalSpeedUntil");
var fixture = new Harmony("grechka.speedplus.fixture");
fixture.Patch(AccessTools.PropertyGetter(typeof(TickManager), nameof(TickManager.ForcePaused)),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.ForcePause)));
fixture.Patch(AccessTools.PropertyGetter(typeof(TickManager), "PlayerCanControl"),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.CanControl)));
fixture.Patch(AccessTools.Method(typeof(TickManager), "NothingHappeningInGame"),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.NothingHappening)));
var harmony = new Harmony("grechka.speedplus.native-test");
harmony.PatchAll(typeof(GrechkaSpeedPlusMod).Assembly);
Check(Harmony.GetPatchInfo(AccessTools.PropertyGetter(typeof(TickManager), nameof(TickManager.TickRateMultiplier))).Owners.Contains(harmony.Id), "Speed patch binds to installed RimWorld 1.7 getter");
Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))).Owners.Contains(harmony.Id), "Button/input patch binds to installed time controls");

foreach (var (mode, expected) in new[] { (TimeSpeed.Paused, 0f), (TimeSpeed.Normal, 1f), (TimeSpeed.Fast, 3f), (TimeSpeed.Superfast, 18f) })
{
    speed.SetValue(manager, mode);
    Check(manager.TickRateMultiplier == expected, "Native rate retained: " + mode);
}
speed.SetValue(manager, TimeSpeed.Ultrafast);
Check(manager.TickRateMultiplier == 150f, "Fourth speed uses native ×150 without maps, faster than third's ×18");
maps.Add((Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)));
Check(manager.TickRateMultiplier == 15f, "Fourth speed uses native ×15 on a map");
speed.SetValue(manager, TimeSpeed.Superfast);
Check(manager.TickRateMultiplier == 6f, "Active-map third speed remains native ×6");
Fixture.Idle = true;
Check(manager.TickRateMultiplier == 12f, "Idle-map third speed remains native ×12");
speed.SetValue(manager, TimeSpeed.Ultrafast);
Check(manager.TickRateMultiplier > 12f, "Fourth speed remains faster than third even in an idle colony");
Fixture.Idle = false;
AccessTools.Field(typeof(TickManager), "UltraSpeedBoost").SetValue(null, true);
Check(manager.TickRateMultiplier == 15f, "Debug ultra boost cannot change fourth speed into 20000×");
AccessTools.Field(typeof(TickManager), "UltraSpeedBoost").SetValue(null, false);
until.SetValue(manager.slower, 100);
Check(manager.TickRateMultiplier == 1f, "Native forced slowdown takes precedence over ultrafast");
speed.SetValue(manager, TimeSpeed.Paused);
Check(manager.TickRateMultiplier == 0f, "Forced slowdown never unpauses the game");
until.SetValue(manager.slower, 0);
Check(FourthSpeedControls.TrySelect(manager, true) && manager.TickRateMultiplier == 15f, "Fourth button/key selection uses native speed setter");
manager.TogglePaused();
Check(manager.CurTimeSpeed == TimeSpeed.Paused && manager.TickRateMultiplier == 0f, "Native pause works from ultrafast");
manager.TogglePaused();
Check(manager.CurTimeSpeed == TimeSpeed.Ultrafast && manager.TickRateMultiplier == 15f, "Native resume returns to ultrafast");
speed.SetValue(manager, TimeSpeed.Superfast);
Check(!FourthSpeedControls.TrySelect(manager, false) && manager.CurTimeSpeed == TimeSpeed.Superfast, "Unrequested selection leaves current speed intact");
Fixture.Paused = true;
Check(!FourthSpeedControls.TrySelect(manager, true) && manager.CurTimeSpeed == TimeSpeed.Superfast, "Forced pause blocks fourth-speed selection");
Fixture.Paused = false;
Fixture.AllowControl = false;
Check(!FourthSpeedControls.TrySelect(manager, true) && manager.CurTimeSpeed == TimeSpeed.Superfast, "Native player-control restriction is retained");
Fixture.AllowControl = true;

var original = new Rect(1000f, 500f, 150f, 24f);
Rect expanded = FourthSpeedControls.ExpandedRect(original), button = FourthSpeedControls.ButtonRect(expanded);
Check(button.x == expanded.x + 4f * 32f && button.width == 32f && button.height == 24f, "Fourth button follows pause and three native speeds at native dimensions");
Check(button.xMax == original.x + 128f && button.xMax <= expanded.xMax && expanded.x >= 0f, "Fifth button fits in the row without spilling off the right screen edge");

string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var native = new XmlDocument();
native.Load(Path.Combine(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR"), "../../../Data/Core/Defs/Misc/KeyBindings/KeyBindings.xml"));
// Apply the exact shipped XML operations against this installation's actual key definitions.
var patch = XDocument.Load(Path.Combine(root, "mods/GrechkaSpeedPlus/Patches/FourthSpeedKey.xml"));
foreach (var operation in patch.Root.Elements("Operation"))
{
    var nodes = native.SelectNodes(operation.Element("xpath").Value);
    Check(nodes.Count == 1, "Patch target exists uniquely: " + operation.Element("xpath").Value);
    foreach (XmlNode node in nodes)
    {
        var replacement = native.CreateElement(node.Name);
        replacement.InnerText = operation.Element("value").Elements().Single().Value;
        node.ParentNode.ReplaceChild(replacement, node);
    }
}
var key = native.SelectSingleNode("Defs/KeyBindingDef[defName='TimeSpeed_Ultrafast']");
Check(key["defaultKeyCodeA"].InnerText == "Alpha4" && key["devModeOnly"].InnerText == "false", "Digit 4 is available outside developer mode, with the same remappable binding ID");
Check(key["label"].InnerText == "game speed: ultrafast (4th)", "Binding label describes fourth mode without claiming a ×4 multiplier");
var active = XDocument.Load(Path.Combine(root, "profiles/GrechkaWorld/ModsConfig.xml")).Descendants("activeMods").Elements("li").Select(n => n.Value).ToList();
Check(active.Count(p => p == "grechka.speedplus") == 1, "Collection profile includes the module exactly once");
Console.WriteLine($"PASS {checks} SpeedPlus checks against installed libraries. Game UI and live tick throughput remain untested; no game launched or controlled.");

static class Fixture
{
    public static bool Paused;
    public static bool AllowControl = true;
    public static bool Idle;
    public static bool NothingHappening(ref bool __result) { __result = Idle; return false; }
    public static bool ForcePause(ref bool __result) { __result = Paused; return false; }
    public static bool CanControl(ref AcceptanceReport __result) { __result = AllowControl; return false; }
}
