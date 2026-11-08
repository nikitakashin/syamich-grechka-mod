using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Grechka.DestroyItem;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
T Bare<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
var prefs = AccessTools.Field(typeof(Prefs), "data"); prefs.SetValue(null, Activator.CreateInstance(prefs.FieldType));
var language = new LoadedLanguage("English");
Field(language, "workerInt", new LanguageWorker_English()); Field(language, "dataIsLoaded", true);
LanguageDatabase.activeLanguage = language; LanguageDatabase.defaultLanguage = language;
var game = Bare<Game>();
var map = Bare<Map>();
map.info = new MapInfo { Size = new IntVec3(20, 1, 20) };
map.cellIndices = new CellIndices(map);
Field(game, "maps", new List<Map> { map });
Field(game, "components", new List<GameComponent>());
Field(game, "uniqueIDsManager", new UniqueIDsManager());
game.tickManager = new TickManager();
Current.Game = game;
var faction = new Faction { def = new FactionDef { isPlayer = true } };
map.events = new MapEvents(map);
map.reservationManager = new ReservationManager(map);
map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
map.attackTargetReservationManager = new AttackTargetReservationManager(map);

var fixture = new Harmony("grechka.destroyitem.fixture");
fixture.Patch(AccessTools.PropertyGetter(typeof(GenTypes), "AllActiveAssemblies"), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Assemblies)));
// Parse shipping XML with the actual game's field/type loader.
var xml = new XmlDocument();
string root = Path.GetFullPath(args.FirstOrDefault() ?? ".");
xml.Load(Path.Combine(root, "mods/GrechkaDestroyItem/Defs/DestroyItem.xml"));
var designation = DirectXmlToObject.ObjectFromXml<DesignationDef>(xml.SelectSingleNode("/Defs/DesignationDef"), false);
DefDatabase<DesignationDef>.Add(designation); DestroyDefs.Grechka_DestroyItem = designation;
var thought = DirectXmlToObject.ObjectFromXml<ThoughtDef>(xml.SelectSingleNode("/Defs/ThoughtDef"), false);
DefDatabase<ThoughtDef>.Add(thought); DestroyDefs.Grechka_DestroyedItem = thought;
var jobDef = DirectXmlToObject.ObjectFromXml<JobDef>(xml.SelectSingleNode("/Defs/JobDef"), false);
DefDatabase<JobDef>.Add(jobDef);
var giverDef = DirectXmlToObject.ObjectFromXml<WorkGiverDef>(xml.SelectSingleNode("/Defs/WorkGiverDef"), false);
var basicWork = new WorkTypeDef { defName = "BasicWorker" };
DefDatabase<WorkTypeDef>.Add(basicWork);
DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
Check(giverDef.workType == basicWork, "Automatic disintegration resolves to the basic work type");
Check(designation.targetType == TargetType.Thing && designation.designateCancelable, "Native designation targets things and supports standard cancellation");
Check(thought.durationDays == .25f && thought.stackLimit == 1 && thought.stages.Single().baseMoodEffect == -3f, "Shipping memory is −3 mood for 6 hours, limited to one");
Check(jobDef.driverClass == typeof(JobDriver_DestroyItem) && giverDef.giverClass == typeof(WorkGiver_DestroyItem)
    && giverDef.requiredCapacities.NullOrEmpty() && giverDef.workTags == WorkTags.None, "Shipping job/workgiver bind without skill, work-tag or manipulation requirements");
map.designationManager = new DesignationManager(map);

// Replace only services requiring a rendered map; orders, native designation indexing,
// destruction, leavings policy, memory merging and toil setup below execute for real.
void Patch(MethodBase method, string prefix) => fixture.Patch(method, prefix: new HarmonyMethod(typeof(Fixture), prefix));
Patch(AccessTools.Method(typeof(FleckMaker), nameof(FleckMaker.ThrowMetaPuffs), new[] { typeof(TargetInfo) }), nameof(Fixture.Noop));
Patch(AccessTools.Method(typeof(ReservationUtility), nameof(ReservationUtility.CanReserveAndReach)), nameof(Fixture.Reachable));
Patch(AccessTools.Method(typeof(ReachabilityImmediate), nameof(ReachabilityImmediate.CanReachImmediate), new[] { typeof(Pawn), typeof(LocalTargetInfo), typeof(PathEndMode) }), nameof(Fixture.Touch));
Patch(AccessTools.Method(typeof(ThoughtUtility), nameof(ThoughtUtility.CanGetThought)), nameof(Fixture.True));
Patch(AccessTools.Method(typeof(Thing), nameof(Thing.DeSpawn)), nameof(Fixture.Despawn));
Patch(AccessTools.Method(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), new[] { typeof(Thing), typeof(Pawn) }), nameof(Fixture.Forbidden));
Patch(AccessTools.Method(typeof(ContentFinder<Texture2D>), "Get"), nameof(Fixture.Icon));

var pawn = new Pawn { def = new ThingDef { defName = "FixtureHuman", category = ThingCategory.Pawn, race = new RaceProperties { intelligence = Intelligence.Humanlike } } };
Field(pawn, "mapIndexOrState", (sbyte)0); Field(pawn, "factionInt", faction);
pawn.health = new Pawn_HealthTracker(pawn);
pawn.jobs = new Pawn_JobTracker(pawn);
pawn.needs = Bare<Pawn_NeedsTracker>();
pawn.needs.mood = Bare<Need_Mood>();
pawn.needs.mood.thoughts = Bare<ThoughtHandler>();
pawn.needs.mood.thoughts.memories = new MemoryThoughtHandler(pawn);
var memories = pawn.needs.mood.thoughts.memories;
var itemDef = new ThingDef { defName = "FixtureItem", label = "fixture item", useHitPoints = false, category = ThingCategory.Item, thingClass = typeof(Thing),
    destroyable = true, stackLimit = 75, size = new IntVec2(1, 1) };
Thing Item(int count = 75) { var t = new Thing { def = itemDef, stackCount = count }; Field(t, "mapIndexOrState", (sbyte)0); return t; }
var item = Item();
Check(DestroyOrders.IsTarget(item), "Loose item stack is a target");
var corpse = new Corpse { def = new ThingDef { category = ThingCategory.Item, destroyable = true } };
Field(corpse, "mapIndexOrState", (sbyte)0);
Check(DestroyOrders.IsTarget(corpse), "Corpses are targets");
var packed = new MinifiedThing { def = itemDef }; Field(packed, "mapIndexOrState", (sbyte)0);
Check(DestroyOrders.IsTarget(packed), "Packed items are targets");
Check(!DestroyOrders.IsTarget(pawn) && !DestroyOrders.IsTarget(new Building { def = new ThingDef { category = ThingCategory.Building } })
    && !DestroyOrders.IsTarget(new Thing { def = itemDef }) && !DestroyOrders.IsTarget(null), "Living pawns, buildings, inventory/unspawned things and null are excluded");
Check(!DestroyOrders.Complete(pawn, item) && !item.Destroyed && memories.Memories.Count == 0, "Unmarked item cannot disappear or penalize mood");
DestroyOrders.Mark(item); DestroyOrders.Mark(item);
Check(DestroyOrders.IsMarked(item) && map.designationManager.AllDesignations.Count == 1, "Marking twice uses one native designation");
var giver = new WorkGiver_DestroyItem { def = giverDef };
Check(!giver.ShouldSkip(pawn) && giver.PotentialWorkThingsGlobal(pawn).Single() == item, "AI scans marked objects only");
Check(giver.HasJobOnThing(pawn, item) && giver.JobOnThing(pawn, item).def == jobDef, "Any suitable player pawn gets the destruction job");
Fixture.CanReach = false;
Check(!giver.HasJobOnThing(pawn, item) && giver.JobOnThing(pawn, item) == null, "Unreachable or reserved object is not assigned");
Fixture.CanReach = true; Fixture.IsForbidden = true;
Check(!giver.HasJobOnThing(pawn, item) && giver.HasJobOnThing(pawn, item, true), "Automatic work respects forbid; explicit order may override it");
Check(!Fixture.LastIgnoredReservation, "Even forced work respects another pawn's reservation");
Fixture.IsForbidden = false;
DestroyOrders.Cancel(item);
Check(!DestroyOrders.IsMarked(item) && giver.ShouldSkip(pawn) && !DestroyOrders.Complete(pawn, item)
    && !item.Destroyed && memories.Memories.Count == 0, "Cancel removes pending work without destroying or penalizing");
DestroyOrders.Mark(item); Fixture.CanTouch = false;
Check(!DestroyOrders.Complete(pawn, item) && !item.Destroyed && memories.Memories.Count == 0, "Completion cannot delete an object at a distance");
Fixture.CanTouch = true;
Check(DestroyOrders.Complete(pawn, item) && item.Destroyed && item.stackCount == 0 && !DestroyOrders.IsMarked(item), "Native Vanish removes the entire stack and its designation");
Check(memories.Memories.Count == 1 && memories.Memories[0].def == thought, "Only the actual worker gains the memory after successful destruction");
memories.Memories[0].age = 3000;
var second = Item(10); DestroyOrders.Mark(second);
Check(DestroyOrders.Complete(pawn, second) && memories.Memories.Count == 1 && memories.Memories[0].age == 0, "Second destruction renews the native memory without stacking");
Check(!DestroyOrders.Complete(pawn, second) && memories.Memories.Count == 1, "Repeated completion cannot destroy or penalize twice");
var third = Item(1); DestroyOrders.Mark(third); pawn.needs = null;
Check(DestroyOrders.Complete(pawn, third) && third.Destroyed, "Pawn without mood can execute without a null-reference failure");

var driver = new JobDriver_DestroyItem { pawn = pawn, job = JobMaker.MakeJob(jobDef, Item()) };
Check(driver.TryMakePreToilReservations(false), "Native reservation claims the whole target stack");
var otherPawn = new Pawn { def = pawn.def, health = new Pawn_HealthTracker(pawn) };
Field(otherPawn, "mapIndexOrState", (sbyte)0); Field(otherPawn, "factionInt", faction);
var competing = new JobDriver_DestroyItem { pawn = otherPawn, job = JobMaker.MakeJob(jobDef, driver.job.targetA) };
Check(!competing.TryMakePreToilReservations(false), "Second worker cannot reserve the same stack concurrently");
AccessTools.Method(typeof(JobDriver), "SetupToils").Invoke(driver, null);
var toils = (List<Toil>)AccessTools.Field(typeof(JobDriver), "toils").GetValue(driver);
Check(toils.Count == 3 && toils[1].defaultDuration == 120 && toils[1].defaultCompleteMode == ToilCompleteMode.Delay,
    "Native job setup produces approach, 2-second progress and completion toils");
var fail = (List<Func<JobCondition>>)AccessTools.Field(typeof(JobDriver), "globalFailConditions").GetValue(driver);
Check(fail.Last()() == JobCondition.Incompletable, "Cancellation/missing designation invalidates an in-progress job");

Patch(AccessTools.Method(typeof(FloatMenuOption), nameof(FloatMenuOption.SetSizeMode)), nameof(Fixture.Noop));
var provider = new FloatMenuOptionProvider_DestroyItem();
var context = Bare<FloatMenuContext>();
context.allSelectedPawns = new List<Pawn> { pawn }; context.map = map;
var optionMethod = AccessTools.Method(typeof(FloatMenuOptionProvider_DestroyItem), "GetSingleOptionFor", new[] { typeof(Thing), typeof(FloatMenuContext) });
Patch(AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.LabelCap)), nameof(Fixture.Label));
var manual = Item();
FloatMenuOption Option() => (FloatMenuOption)optionMethod.Invoke(provider, new object[] { manual, context });
Fixture.CanReach = false;
Check(Option().action == null && !DestroyOrders.IsMarked(manual), "Unavailable right-click action cannot mark or delete the target");
Fixture.CanReach = true;
Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob)), nameof(Fixture.OrderedJob));
Option().action();
Check(DestroyOrders.IsMarked(manual) && Fixture.Ordered.def == jobDef && Fixture.Ordered.targetA.Thing == manual
    && !manual.Destroyed, "Right-click marks the target and queues a real job without instant destruction");
DestroyOrders.Cancel(manual);
var flags = BindingFlags.Instance | BindingFlags.NonPublic;
Check(!(bool)typeof(FloatMenuOptionProvider_DestroyItem).GetProperty("RequiresManipulation", flags).GetValue(provider)
    && (bool)typeof(FloatMenuOptionProvider_DestroyItem).GetProperty("Drafted", flags).GetValue(provider)
    && (bool)typeof(FloatMenuOptionProvider_DestroyItem).GetProperty("MechanoidCanDo", flags).GetValue(provider),
    "Manual provider supports drafted pawns and mechs without manipulation or work-type restrictions");

var harmony = new Harmony("grechka.destroyitem.native-tests"); harmony.PatchAll(typeof(ItemGizmos).Assembly);
foreach (var method in ItemGizmos.TargetMethods())
    Check(Harmony.GetPatchInfo(method).Owners.Contains(harmony.Id), "Gizmo patch binds: " + method.DeclaringType.FullName);
var overrideItem = new ItemWithoutBase { def = itemDef }; Field(overrideItem, "mapIndexOrState", (sbyte)0);
Check(overrideItem.GetGizmos().OfType<DestroyCommand>().Count() == 1, "Mod item overriding GetGizmos without base still has Destroy");
var nestedItem = new ItemWithBase { def = itemDef }; Field(nestedItem, "mapIndexOrState", (sbyte)0);
Check(nestedItem.GetGizmos().OfType<DestroyCommand>().Count() == 1, "Nested patched base calls never duplicate the command");

// Native memory save/load keeps the penalty and age using stable authored Def identity.
var saved = memories.Memories[0]; saved.age = 777;
string path = Path.Combine(Path.GetTempPath(), "grechka-destroy-memory-" + Guid.NewGuid().ToString("N") + ".xml");
Thought_Memory restored = null;
try {
    Scribe.saver.InitSaving(path, "fixture"); Scribe_Deep.Look(ref saved, "memory"); Scribe.saver.FinalizeSaving();
    Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref restored, "memory"); Scribe.loader.FinalizeLoading();
} finally { if (File.Exists(path)) File.Delete(path); }
Check(restored.def == thought && restored.age == 777, "Penalty identity and remaining duration survive native Scribe roundtrip");
Console.WriteLine($"PASS {checks} DestroyItem native checks. No game launched or controlled; real-map UI/pathing remain untested.");

public class ItemWithoutBase : Thing { public override IEnumerable<Gizmo> GetGizmos() { yield break; } }
public class ItemWithBase : ItemWithoutBase { public override IEnumerable<Gizmo> GetGizmos() { foreach (var g in base.GetGizmos()) yield return g; } }
static class Fixture
{
    public static bool CanReach = true, CanTouch = true, IsForbidden, LastIgnoredReservation;
    public static Job Ordered;
    public static bool Assemblies(ref IEnumerable<Assembly> __result) { __result = new[] { typeof(Thing).Assembly, typeof(DestroyOrders).Assembly, typeof(Fixture).Assembly }; return false; }
    public static bool Label(ref string __result) { __result = "fixture item"; return false; }
    public static bool Noop() => false;
    public static bool OrderedJob(Job job, ref bool __result) { Ordered = job; __result = true; return false; }
    public static bool True(ref bool __result) { __result = true; return false; }
    public static bool Reachable(bool ignoreOtherReservations, ref bool __result) { LastIgnoredReservation = ignoreOtherReservations; __result = CanReach; return false; }
    public static bool Touch(ref bool __result) { __result = CanTouch; return false; }
    public static bool Forbidden(ref bool __result) { __result = IsForbidden; return false; }
    public static bool Icon(ref Texture2D __result) { __result = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D)); return false; }
    public static bool Despawn(Thing __instance) { AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(__instance, (sbyte)-1); return false; }
}
