using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Grechka.Kite;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

int checks = 0;
void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS " + text); }
var fixture = new Harmony("grechka.kite.fixture");
void Patch(MethodBase method, string prefix) => fixture.Patch(method, prefix: new HarmonyMethod(typeof(Fixture), prefix));
Patch(AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Spawned)), nameof(Fixture.Spawned));
Patch(AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Map)), nameof(Fixture.Map));
Patch(AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer)), nameof(Fixture.PlayerFaction));
Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.CombinedDisabledWorkTags)), nameof(Fixture.DisabledWork));
Patch(AccessTools.PropertyGetter(typeof(StunHandler), nameof(StunHandler.Hypnotized)), nameof(Fixture.False));
Patch(AccessTools.Method(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Faction) }), nameof(Fixture.HostileFaction));
Patch(AccessTools.Method(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Thing) }), nameof(Fixture.HostileThing));
// Map/path environment only. The native PatherTick and its production busy gate run.
Patch(AccessTools.Method(typeof(Pawn_PathFollower), "TrySetMovePercent"), nameof(Fixture.Skip));
Patch(AccessTools.Method(typeof(Pawn_PathFollower), "WillCollideWithPawnAt"), nameof(Fixture.False));
Patch(AccessTools.Method(typeof(Pawn_PathFollower), "AtDestinationPosition"), nameof(Fixture.True));
Patch(AccessTools.Method(typeof(Pawn_PathFollower), "PatherArrived"), nameof(Fixture.Arrived));

var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
game.tickManager = new TickManager();
Current.Game = game;
var prefs = AccessTools.Field(typeof(Prefs), "data");
prefs.SetValue(null, Activator.CreateInstance(prefs.FieldType));
JobDefOf.Goto = new JobDef { defName = "Goto" };
JobDefOf.AttackStatic = new JobDef { defName = "AttackStatic" };
JobDefOf.Wait_Combat = new JobDef { defName = "Wait_Combat" };
var work = new Job { def = new JobDef { defName = "HaulToCell" } };
var flee = new Job { def = new JobDef { defName = "Flee" } };
var move = new Job { def = JobDefOf.Goto };
Pawn MakePawn(Faction faction)
{
    var pawn = new Pawn { def = new ThingDef { defName = "FixtureHuman", race = new RaceProperties() }, thingIDNumber = 10 };
    AccessTools.Field(typeof(Thing), "factionInt").SetValue(pawn, faction);
    pawn.health = new Pawn_HealthTracker(pawn);
    pawn.mindState = new Pawn_MindState(pawn);
    pawn.jobs = new Pawn_JobTracker(pawn) { curJob = move };
    pawn.drafter = new Pawn_DraftController(pawn);
    AccessTools.Field(typeof(Pawn_DraftController), "draftedInt").SetValue(pawn.drafter, true);
    AccessTools.Field(typeof(Pawn_DraftController), "fireAtWillInt").SetValue(pawn.drafter, true);
    pawn.stances = new Pawn_StanceTracker(pawn);
    pawn.pather = new Pawn_PathFollower(pawn);
    pawn.equipment = new Pawn_EquipmentTracker(pawn);
    return pawn;
}
var pawn = MakePawn(Fixture.Player);
var enemy = MakePawn(Fixture.Enemy);
var neutral = MakePawn(new Faction());
TestVerb Equip(Pawn owner)
{
var weapon = new ThingWithComps { def = new ThingDef { defName = "FixtureRifle", equipmentType = EquipmentType.Primary } };
var equippable = new CompEquippable { parent = weapon };
AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(weapon, new List<ThingComp> { equippable });
equippable.verbTracker = new VerbTracker(equippable);
var verb = new TestVerb
{
    caster = owner, verbTracker = equippable.verbTracker,
    verbProps = new VerbProperties { verbClass = typeof(Verb_Shoot), isPrimary = true, violent = true, range = 30f }
};
AccessTools.Field(typeof(VerbTracker), "verbs").SetValue(equippable.verbTracker, new List<Verb> { verb });
var container = (ThingOwner<ThingWithComps>)AccessTools.Field(typeof(Pawn_EquipmentTracker), "equipment").GetValue(owner.equipment);
container.InnerListForReading.Add(weapon);
return verb;
}
var verb = Equip(pawn);
var enemyVerb = Equip(enemy);
Check(pawn.equipment.PrimaryEq.PrimaryVerb == verb && verb.EquipmentSource == pawn.equipment.Primary, "Native equipment/verb ownership resolves the primary attack");
Check(new KiteSettings().enemiesFireWhileMoving, "Enemy checkbox defaults on");
Check(MovingFire.Eligible(pawn, move) && MovingFire.Eligible(enemy, move), "Drafted player and hostile combat movement are enabled by default");
MovingFire.Settings.enemiesFireWhileMoving = false;
Check(!MovingFire.Eligible(enemy, move) && MovingFire.Eligible(pawn, move), "Enemy checkbox affects enemies without disabling player moving fire");
MovingFire.Settings.enemiesFireWhileMoving = true;
Check(!MovingFire.Eligible(neutral, move) && !MovingFire.Eligible(null, move), "Neutral visitors and missing pawns never receive moving fire");
AccessTools.Field(typeof(Pawn_DraftController), "draftedInt").SetValue(pawn.drafter, false);
Check(!MovingFire.Eligible(pawn, move), "Undrafted colonists do not fire while working");
AccessTools.Field(typeof(Pawn_DraftController), "draftedInt").SetValue(pawn.drafter, true);
AccessTools.Field(typeof(Pawn_DraftController), "fireAtWillInt").SetValue(pawn.drafter, false);
Check(!MovingFire.Eligible(pawn, move), "Fire at will off blocks player moving fire");
AccessTools.Field(typeof(Pawn_DraftController), "fireAtWillInt").SetValue(pawn.drafter, true);
Check(!MovingFire.Eligible(pawn, work) && !MovingFire.Eligible(enemy, flee), "Work and fleeing jobs retain normal behaviour");
Fixture.Disabled = WorkTags.Violent;
Check(!MovingFire.Eligible(pawn, move), "Incapable of violence is respected");
Fixture.Disabled = WorkTags.None;
Fixture.IsSpawned = false;
Check(!MovingFire.Eligible(pawn, move), "Despawned/world pawns are excluded");
Fixture.IsSpawned = true;
foreach (var state in new[] { PawnHealthState.Down, PawnHealthState.Dead })
{
    AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, state);
    Check(!MovingFire.Eligible(pawn, move), "Health restriction: " + state);
}
AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(pawn.health, PawnHealthState.Mobile);
Check(MovingFire.SupportedVerb(pawn, verb), "Ordinary primary ranged attack is supported");
verb.verbProps.onlyManualCast = true;
Check(!MovingFire.SupportedVerb(pawn, verb), "Manual-only weapons are not auto-fired");
verb.verbProps.onlyManualCast = false;
verb.verbProps.verbClass = typeof(Verb_MeleeAttackDamage);
Check(!MovingFire.SupportedVerb(pawn, verb), "Melee never becomes a moving ranged attack");
verb.verbProps.verbClass = typeof(Verb_Shoot);
Check(!MovingFire.SupportedVerb(pawn, new TestVerb { verbProps = verb.verbProps }), "Unowned/ability attacks are excluded");

var harmony = new Harmony("grechka.kite.native-test");
harmony.PatchAll(typeof(GrechkaKiteMod).Assembly);
var pathMethod = AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick));
Check(Harmony.GetPatchInfo(pathMethod).Owners.Contains(harmony.Id), "Production patch binds installed RimWorld 1.7 path follower");
Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.StanceTrackerTick))).Owners.Contains(harmony.Id), "Moving-fire tick binds native stance tracker");
Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))).Owners.Contains(harmony.Id), "Move-order guard binds native StartJob");
Check(Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m).Owners.Contains(harmony.Id)) == 3,
    "Production patches have exactly three targets; verb, accuracy and global busy getter stay native");
var original = PatchProcessor.GetOriginalInstructions(pathMethod);
var rewritten = MovingFirePathPatch.Transpiler(original).ToList();
Check(rewritten.Count == original.Count && original.Zip(rewritten).Count(pair => pair.First.opcode != pair.Second.opcode || !Equals(pair.First.operand, pair.Second.operand)) == 1,
    "Transpiler changes exactly one busy-check call and preserves every other path instruction");
bool rejected = false;
try { MovingFirePathPatch.Transpiler(Array.Empty<CodeInstruction>()).ToList(); } catch (InvalidOperationException) { rejected = true; }
Check(rejected, "Unknown path-follower layout fails explicitly");

void SetStance(Stance stance) { pawn.stances.SetStance(stance); Fixture.Arrivals = 0; }
void PathGate(bool passes, string text)
{
    AccessTools.Field(typeof(Pawn_PathFollower), "moving").SetValue(pawn.pather, true);
    pawn.pather.PatherTick();
    Check((Fixture.Arrivals == 1) == passes, "Native PatherTick: " + text);
}
SetStance(new Stance_Warmup { ticksLeft = 30, verb = verb });
PathGate(true, "warmup permits movement");
Check(pawn.stances.FullBodyBusy, "Other systems still see the genuine busy stance");
SetStance(new Stance_Cooldown { ticksLeft = 3, verb = verb });
PathGate(true, "burst/cooldown permits movement");
var cooldown = (Stance_Cooldown)pawn.stances.curStance;
cooldown.StanceTick(); cooldown.StanceTick();
Check(cooldown.ticksLeft == 1 && pawn.stances.curStance == cooldown, "Native cooldown counts down without Kite resetting or shortening it");
cooldown.StanceTick();
Check(pawn.stances.curStance is Stance_Mobile, "Native cooldown expires to the normal mobile stance");
SetStance(new Stance_Cooldown { ticksLeft = 30, verb = verb });
AccessTools.Field(typeof(StunHandler), "stunTicksLeft").SetValue(pawn.stances.stunner, 10);
PathGate(false, "stun still prevents movement");
AccessTools.Field(typeof(StunHandler), "stunTicksLeft").SetValue(pawn.stances.stunner, 0);
SetStance(new Stance_Cooldown { ticksLeft = 30, verb = verb });
verb.verbProps.onlyManualCast = true;
PathGate(false, "manual-only attacks keep native movement blocking");
verb.verbProps.onlyManualCast = false;
SetStance(new OtherBusyStance { ticksLeft = 30, verb = verb });
PathGate(false, "unrelated busy stance remains blocking");
SetStance(new Stance_Cooldown { ticksLeft = 30, verb = verb });
bool cancel = true;
MovingFireOrderPatch.Prefix(pawn, move, ref cancel);
Check(!cancel && ((Stance_Busy)pawn.stances.curStance).ticksLeft == 30, "Repeated movement orders preserve aim/burst/cooldown");
cancel = true; MovingFireOrderPatch.Prefix(pawn, work, ref cancel);
Check(cancel, "Switching to work retains native stance cancellation");
cancel = true; MovingFireOrderPatch.Prefix(pawn, new Job { def = JobDefOf.AttackStatic, targetA = enemy }, ref cancel);
Check(cancel, "An explicit attack order retains native target switching and stance cancellation");

enemy.stances.SetStance(new Stance_Warmup { ticksLeft = 30, verb = enemyVerb });
AccessTools.Field(typeof(Pawn_PathFollower), "moving").SetValue(enemy.pather, true);
Fixture.Arrivals = 0; enemy.pather.PatherTick();
Check(Fixture.Arrivals == 1, "Native enemy PatherTick permits armed movement with default checkbox");
MovingFire.Settings.enemiesFireWhileMoving = false;
Fixture.Arrivals = 0; enemy.pather.PatherTick();
Check(Fixture.Arrivals == 0, "Disabling the checkbox restores native enemy warmup movement blocking");
MovingFire.Settings.enemiesFireWhileMoving = true;

pawn.mindState.enemyTarget = enemy;
SetStance(new Stance_Mobile());
void ScanCycle()
{
    for (int i = 0; i < MovingFire.ScanInterval; i++)
    {
        AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(game.tickManager, i);
        MovingFire.Tick(pawn);
    }
}
ScanCycle();
Check(verb.Starts == 1 && verb.LastTarget == enemy && verb.PreventFriendlyFire, "Moving pawn delegates a hostile shot to its equipped verb, once per scan interval");
enemy.stances.SetStance(new Stance_Mobile()); enemy.mindState.enemyTarget = pawn;
void EnemyScanCycle()
{
    for (int i = 0; i < MovingFire.ScanInterval; i++)
    {
        AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(game.tickManager, i);
        MovingFire.Tick(enemy);
    }
}
EnemyScanCycle();
Check(enemyVerb.Starts == 1 && enemyVerb.LastTarget == pawn, "Armed enemies initiate moving fire when the checkbox is enabled");
MovingFire.Settings.enemiesFireWhileMoving = false;
EnemyScanCycle();
Check(enemyVerb.Starts == 1, "Disabling the checkbox immediately prevents new enemy moving attacks");
MovingFire.Settings.enemiesFireWhileMoving = true;
verb.Hittable = false;
// Avoid constructing a world target cache for this negative fixture; direct target validation
// must reject blocked or out-of-range targets before any cast is attempted.
Check(!MovingFire.ValidTarget(pawn, verb, enemy), "Line-of-sight/range rejection is delegated to the native weapon API");
verb.Hittable = true;
Check(!MovingFire.ValidTarget(pawn, verb, neutral), "Neutral targets are rejected");
AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(enemy.health, PawnHealthState.Down);
Check(!MovingFire.ValidTarget(pawn, verb, enemy), "Downed enemies are not automatically finished off");
AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(enemy.health, PawnHealthState.Mobile);
SetStance(new Stance_Cooldown { ticksLeft = 30, verb = verb });
ScanCycle(); Check(verb.Starts == 1, "A busy weapon cannot start a second shot");
SetStance(new Stance_Mobile());
verb.Ready = false; ScanCycle(); Check(verb.Starts == 1, "Unavailable/reloading weapon cannot start a shot");
verb.Ready = true;
AccessTools.Field(typeof(Pawn_PathFollower), "moving").SetValue(pawn.pather, false);
ScanCycle(); Check(verb.Starts == 1, "Standing fire is left to the game's normal jobs");

string path = Path.Combine(Path.GetTempPath(), "grechka-kite-settings-" + Guid.NewGuid().ToString("N") + ".xml");
try
{
    foreach (bool enabled in new[] { false, true })
    {
        KiteSettings saved = new KiteSettings { enemiesFireWhileMoving = enabled };
        Scribe.saver.InitSaving(path, "settings"); Scribe_Deep.Look(ref saved, "kite"); Scribe.saver.FinalizeSaving();
        KiteSettings loaded = null;
        Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "kite"); Scribe.loader.FinalizeLoading();
        Check(loaded.enemiesFireWhileMoving == enabled, "Native Scribe preserves enemy checkbox: " + enabled);
    }
    File.WriteAllText(path, "<settings><kite Class=\"Grechka.Kite.KiteSettings\" /></settings>");
    KiteSettings legacy = null;
    Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref legacy, "kite"); Scribe.loader.FinalizeLoading();
    Check(legacy.enemiesFireWhileMoving, "Missing setting in old configuration defaults on");
}
finally { if (File.Exists(path)) File.Delete(path); }
string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var active = XDocument.Load(Path.Combine(root, "profiles/GrechkaWorld/ModsConfig.xml")).Descendants("activeMods").Elements("li").Select(n => n.Value).ToList();
Check(active.Count(p => p == "grechka.kite") == 1, "Collection profile includes Kite exactly once");
harmony.UnpatchAll(harmony.Id);
Check(!Harmony.GetPatchInfo(pathMethod).Owners.Contains(harmony.Id), "Production patches can be removed cleanly");
Console.WriteLine($"PASS {checks} Kite checks against installed libraries. Real map combat/UI acceptance remains open; no game launched or controlled.");

public sealed class OtherBusyStance : Stance_Busy { }

public sealed class TestVerb : Verb
{
    public bool Hittable = true, Ready = true, PreventFriendlyFire;
    public int Starts;
    public Thing LastTarget;
    public override bool Available() => Ready;
    public override bool CanHitTarget(LocalTargetInfo target) => Hittable;
    protected override bool TryCastShot() => true;
    public override bool TryStartCastOn(LocalTargetInfo castTarg, LocalTargetInfo destTarg, bool surpriseAttack = false,
        bool canHitNonTargetPawns = true, bool preventFriendlyFire = false, bool nonInterruptingSelfCast = false)
    {
        Starts++; LastTarget = castTarg.Thing; PreventFriendlyFire = preventFriendlyFire; return true;
    }
}

static class Fixture
{
    public static Faction Player = new Faction(), Enemy = new Faction();
    public static Map FakeMap = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
    public static bool IsSpawned = true;
    public static WorkTags Disabled;
    public static int Arrivals;
    public static bool Spawned(ref bool __result) { __result = IsSpawned; return false; }
    public static bool Map(ref Map __result) { __result = FakeMap; return false; }
    public static bool PlayerFaction(ref Faction __result) { __result = Player; return false; }
    public static bool DisabledWork(ref WorkTags __result) { __result = Disabled; return false; }
    public static bool HostileFaction(Thing t, Faction fac, ref bool __result) { __result = (t.Faction == Player && fac == Enemy) || (t.Faction == Enemy && fac == Player); return false; }
    public static bool HostileThing(Thing a, Thing b, ref bool __result) => HostileFaction(a, b.Faction, ref __result);
    public static bool Skip() => false;
    public static bool False(ref bool __result) { __result = false; return false; }
    public static bool True(ref bool __result) { __result = true; return false; }
    public static bool Arrived() { Arrivals++; return false; }
}
