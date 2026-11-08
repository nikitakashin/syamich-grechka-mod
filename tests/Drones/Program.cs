using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using AIRobot;
using Grechka.Drones;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
    Console.WriteLine("PASS " + label);
}
string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
Assembly[] assemblies = { typeof(ThingDef).Assembly, typeof(Drone).Assembly, typeof(X2_AIRobot).Assembly };
Type Resolve(string name) => assemblies.SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.FullName == name || t.Name == name);
void Schema(XElement node, Type type, bool top = false)
{
    if (node.Attribute("Class") is XAttribute attribute)
    {
        Type concrete = Resolve(attribute.Value) ?? throw new Exception("Unknown XML Class " + attribute.Value);
        Check(type.IsAssignableFrom(concrete), "XML class assignable: " + attribute.Value);
        type = concrete;
    }
    if (type == typeof(Type))
    {
        Check(Resolve(node.Value) != null, "XML runtime type exists: " + node.Value);
        return;
    }
    if (!node.HasElements || typeof(Def).IsAssignableFrom(type) && !top) return;
    if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
    {
        Type item = type.GetGenericArguments()[0];
        // RimWorld's custom XML loaders consume these keyed collections.
        if (new[] { typeof(StatModifier), typeof(ThingDefCountClass), typeof(SkillRequirement) }.Contains(item)) return;
        foreach (var child in node.Elements()) Schema(child, item);
        return;
    }
    foreach (XElement child in node.Elements())
    {
        var field = AccessTools.Field(type, child.Name.LocalName)
            ?? throw new Exception("Unknown XML field " + type + "." + child.Name);
        Schema(child, field.FieldType);
    }
}
var defs = Directory.GetFiles(Path.Combine(root, "mods/GrechkaDrones/Defs"), "*.xml")
    .SelectMany(p => XDocument.Load(p).Root.Elements()).ToList();
foreach (var def in defs) Schema(def, Resolve(def.Name.LocalName) ?? throw new Exception("Unknown XML root " + def.Name), true);
Check(typeof(Drone).IsSubclassOf(typeof(X2_AIRobot)) && typeof(DroneStation).IsSubclassOf(typeof(X2_Building_AIRobotRechargeStation)),
    "Units and docks extend the installed Misc Robots runtime classes");
var ids = defs.Where(d => d.Element("defName") != null).Select(d => d.Name + ":" + (string)d.Element("defName")).ToArray();
Check(ids.Length == ids.Distinct().Count(), "Definition IDs are unique within their native type");
XElement D(string type, string id) => defs.Single(d => d.Name == type && (string)d.Element("defName") == id);
var units = defs.Where(d => d.Name == "ThingDef" && (string)d.Attribute("ParentName") == "Grechka_DroneBase").ToList();
var docks = defs.Where(d => d.Name == "ThingDef" && d.Element("spawnThingDef") != null).ToList();
var kinds = defs.Where(d => d.Name == "PawnKindDef").ToList();
Check(units.Count == 15 && docks.Count == 15 && kinds.Count == 15, "Exactly three roles times five tiers, with a matching kind and dock each");
var baseDef = defs.Single(d => (string)d.Attribute("Name") == "Grechka_DroneBase");
Check((string)baseDef.Attribute("Class") == typeof(X2_ThingDef_AIRobot).FullName
    && (string)baseDef.Element("thingClass") == typeof(Drone).FullName
    && (float)baseDef.Element("statBases").Element("Flammability") == 0,
    "All units inherit Misc custom ThingDef, our runtime and zero flammability");
foreach (DroneRole role in Enum.GetValues<DroneRole>())
{
    var family = units.Where(u => (string)u.Element("modExtensions").Element("li").Element("role") == role.ToString())
        .OrderBy(u => (int)u.Element("modExtensions").Element("li").Element("tier")).ToList();
    Check(family.Select(u => (int)u.Element("modExtensions").Element("li").Element("tier")).SequenceEqual(Enumerable.Range(1, 5)), role + " covers each tier exactly once");
    float previousHealth = 0, previousDamage = 0;
    foreach (var unit in family)
    {
        string id = (string)unit.Element("defName");
        var kind = D("PawnKindDef", id);
        var dock = docks.Single(d => (string)d.Element("spawnThingDef") == id);
        Check((string)kind.Element("race") == id && (string)dock.Attribute("Class") == typeof(X2_ThingDef_AIRobot_Building_RechargeStation).FullName,
            "Misc creator can resolve kind, race and station for " + id);
        Check((string)dock.Element("thingClass") == typeof(DroneStation).FullName && (float)dock.Element("statBases").Element("Flammability") == 0,
            "Dock runtime and fireproof configuration for " + id);
        Check((string)dock.Element("minifiedDef") == "AIRobot_MinifiedRechargeStation"
            && dock.Element("recipeMaker").Element("recipeUsers").Elements("li").Any(e => e.Value == "FabricationBench"),
            "Manufacturable native packed dock for " + id);
        float hp = (float)unit.Element("race").Element("baseHealthScale");
        Check(hp > previousHealth, "Increasing native body health scale for " + id);
        previousHealth = hp;
        if (role == DroneRole.Warrior)
        {
            float damage = (float)unit.Element("tools").Element("li").Element("power");
            Check(hp >= 8 && damage > previousDamage && unit.Element("verbs") == null
                && (string)kind.Element("weaponMoney") == "0~0", "Large health pool and tiered melee-only loadout for " + id);
            previousDamage = damage;
        }
        else if (role == DroneRole.Rescue)
        {
            var extension = unit.Element("modExtensions").Element("li");
            Check((float)extension.Element("foamRadius") >= 3 && (int)extension.Element("foamCooldownTicks") > 0,
                "Area foam with finite cooldown for " + id);
        }
    }
}
Check((string)baseDef.Element("race").Element("thinkTreeMain") == "Grechka_DroneMain"
    && D("ThinkTreeDef", "Grechka_DroneMain").Descendants().Any(n => (string)n.Attribute("Class") == typeof(JobGiver_Drone).FullName),
    "Native main tree dispatches our drone behavior");
Check(D("ThinkTreeDef", "Grechka_DroneMain").Descendants().Any(n => (string)n.Attribute("Class") == typeof(JobGiver_DroneRecharge).FullName)
    && !D("ThinkTreeDef", "Grechka_DroneMain").Descendants().Any(n => (string)n.Attribute("Class") == typeof(X2_JobGiver_RechargeEnergy).FullName),
    "Priority tree uses the recharge wrapper that honors Misc's native energy priority");
Check(!D("ThinkTreeDef", "Grechka_DroneConstant").Element("thinkRoot").Element("subNodes").Elements().Any(),
    "Constant tree cannot override retreat with ordinary hostility responses");
foreach (var pair in new[] { ("Grechka_DroneFoam", typeof(JobDriver_Foam)), ("Grechka_DroneRetreat", typeof(JobDriver_Retreat)), ("Grechka_DroneRescueFloor", typeof(JobDriver_RescueFloor)) })
    Check((string)D("JobDef", pair.Item1).Element("driverClass") == pair.Item2.FullName && pair.Item2.IsSubclassOf(typeof(JobDriver)), "Native job driver binding: " + pair.Item1);

Check(!DroneRules.ShouldRetreat(.3001f, false) && DroneRules.ShouldRetreat(.30f, false) && DroneRules.ShouldRetreat(.01f, false),
    "Retreat begins at 30 percent, including the threshold");
Check(DroneRules.ShouldRetreat(.9f, true) && DroneRules.ShouldRetreat(1f, true), "Repair latch prevents premature return to combat");
Check(DroneRules.ShouldPreempt(true, true) && !DroneRules.ShouldPreempt(false, true)
    && !DroneRules.ShouldPreempt(true, false) && !DroneRules.ShouldPreempt(false, false),
    "Own rescue preempts captivity only when both conditions hold");
DamageDefOf.Flame = new DamageDef { defName = "Flame" };
DamageDefOf.Burn = new DamageDef { defName = "Burn" };
var blunt = new DamageDef { defName = "Blunt" };
Check(DroneRules.BlocksFire(DamageDefOf.Flame) && DroneRules.BlocksFire(DamageDefOf.Burn)
    && !DroneRules.BlocksFire(blunt) && !DroneRules.BlocksFire(null), "Fire immunity recognizes exact damage defs and leaves other damage intact");
var fixture = new Harmony("grechka.drones.native.fixture");
fixture.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.PreApplyDamage)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Damage)));
var drone = new Drone();
foreach (var fire in new[] { DamageDefOf.Flame, DamageDefOf.Burn })
{
    DamageInfo info = new DamageInfo(fire, 999);
    drone.PreApplyDamage(ref info, out bool absorbed);
    Check(absorbed && Fixture.NativeDamageCalls == 0, "Actual Drone.PreApplyDamage absorbs " + fire.defName + " before native health processing");
}
DamageInfo melee = new DamageInfo(blunt, 12);
drone.PreApplyDamage(ref melee, out bool meleeAbsorbed);
Check(!meleeAbsorbed && Fixture.NativeDamageCalls == 1, "Actual nonfire hit reaches native Pawn damage processing");

// Test our actual preemption transaction against controlled native carry/job boundaries.
// The fixture does not claim to simulate placement or job cleanup on a complete map.
fixture.Patch(AccessTools.PropertyGetter(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.CarriedThing)),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.CarriedThing)));
fixture.Patch(AccessTools.Method(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryDropCarriedThing),
    new[] { typeof(IntVec3), typeof(ThingPlaceMode), typeof(Thing).MakeByRefType(), typeof(Action<Thing, int>) }),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Drop)));
fixture.Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.StartJob)));
drone.carryTracker = new Pawn_CarryTracker(drone);
drone.jobs = new Pawn_JobTracker(drone);
MethodInfo replace = AccessTools.Method(typeof(Drone), "ReplaceJob");
var nextRescue = new Job { def = new JobDef { defName = "FixtureRescue" } };
Fixture.Carried = new Pawn(); Fixture.DropSucceeds = true; Fixture.Events.Clear();
replace.Invoke(drone, new object[] { nextRescue });
Check(Fixture.Events.SequenceEqual(new[] { "drop", "start" }) && Fixture.Started == nextRescue,
    "Actual preemption places the carried enemy down before starting own rescue");
Check(Fixture.EndCondition == JobCondition.InterruptForced && Fixture.KeepCarrying == false,
    "Own rescue force-interrupts the old job and explicitly clears its carried-patient context");
Fixture.DropSucceeds = false; Fixture.Events.Clear(); Fixture.Started = null;
replace.Invoke(drone, new object[] { nextRescue });
Check(Fixture.Events.SequenceEqual(new[] { "drop" }) && Fixture.Started == null,
    "A failed patient drop preserves the old job instead of starting incompatible rescue");
Fixture.Carried = null; Fixture.Events.Clear();
replace.Invoke(drone, new object[] { nextRescue });
Check(Fixture.Events.SequenceEqual(new[] { "start" }), "An empty-handed preemption starts rescue without a phantom drop");
Fixture.Events.Clear();
replace.Invoke(drone, new object[] { null });
Check(Fixture.Events.Count == 0, "An unavailable rescue never drops a carried patient");

// Rescue and Capture use the game's TakeToBed driver. Exercise its reservation transaction,
// retaining native target selection while replacing the map's reservation service boundary.
fixture.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.ClearAllReservations)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.SkipReservationsCleanup)));
fixture.Patch(AccessTools.Method(typeof(ReservationUtility), nameof(ReservationUtility.Reserve)),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Reserve)));
fixture.Patch(AccessTools.PropertyGetter(typeof(Building_Bed), nameof(Building_Bed.SleepingSlotsCount)),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.BedSlots)));
var patient = new Pawn();
var bed = new Building_Bed();
var captureJob = new Job { def = new JobDef { defName = "FixtureCapture", driverClass = typeof(JobDriver_TakeToBed), makeTargetPrisoner = true }, targetA = patient, targetB = bed };
var driver = new JobDriver_TakeToBed { pawn = drone, job = captureJob };
Fixture.Reservations.Clear(); Fixture.ReservationFailure = null;
Check(driver.TryMakePreToilReservations(false) && Fixture.Reservations.Count == 2 && ReferenceEquals(Fixture.Reservations[0], patient) && ReferenceEquals(Fixture.Reservations[1], bed),
    "Native capture reserves both the downed patient and its actual prison bed");
Fixture.Reservations.Clear(); Fixture.ReservationFailure = patient;
Check(!driver.TryMakePreToilReservations(false) && Fixture.Reservations.Count == 1 && ReferenceEquals(Fixture.Reservations[0], patient),
    "Native capture cannot proceed when another rescuer has reserved the patient");
Fixture.Reservations.Clear(); Fixture.ReservationFailure = bed;
Check(!driver.TryMakePreToilReservations(false) && Fixture.Reservations.Count == 2,
    "Native capture cannot proceed if its selected bed becomes unavailable");

fixture.Patch(AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.PlayerFaction)));
fixture.Patch(AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayerSilentFail)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.PlayerFaction)));
var player = Fixture.Player = new Faction { def = new FactionDef { defName = "FixturePlayer", isPlayer = true } };
var enemy = new Faction { def = new FactionDef { defName = "FixtureEnemy" } };
var ownPawn = new Pawn(); AccessTools.Field(typeof(Thing), "factionInt").SetValue(ownPawn, player);
var enemyPawn = new Pawn(); AccessTools.Field(typeof(Thing), "factionInt").SetValue(enemyPawn, enemy);
var neutralPawn = new Pawn();
Check(DroneRules.IsOwn(ownPawn) && !DroneRules.IsOwn(enemyPawn) && !DroneRules.IsOwn(neutralPawn) && !DroneRules.IsOwn(null),
    "Own rescue selects player faction and excludes enemies, factionless and null patients");
ownPawn.guest = new Pawn_GuestTracker(ownPawn) { guestStatusInt = GuestStatus.Prisoner };
Check(!DroneRules.IsOwn(ownPawn), "Prisoner status is excluded from own-rescue priority");
Fixture.Player = null;
Check(!DroneRules.IsOwn(neutralPawn), "A missing player faction never makes factionless pawns our own");
Fixture.Player = player;

drone.def = new ThingDef { defName = "FixtureDrone", race = new RaceProperties() };
drone.health = new Pawn_HealthTracker(drone);
var station = new DroneStation { robot = drone, robotSpawnedOnce = true };
drone.rechargeStation = station;
Check(!drone.CaptureEnemies && !station.captureEnemies, "Enemy capture defaults off on both unit and dock");
drone.CaptureEnemies = true;
Check(station.captureEnemies && drone.captureEnemies && drone.CaptureEnemies, "Unit toggle synchronizes station and local fallback");
station.captureEnemies = false;
Check(!drone.CaptureEnemies, "Station toggle is authoritative for its active unit");
drone.rechargeStation = null;
Check(drone.CaptureEnemies, "Unit retains its fallback toggle if the dock is unavailable");
drone.rechargeStation = station;
drone.repairing = true;
Check(station.HoldingForRepair && !HoldRepairHook.Prefix(station), "Manual deployment of our repairing robot is blocked");
var nativeStation = new X2_Building_AIRobotRechargeStation { robot = drone, robotSpawnedOnce = true };
Check(HoldRepairHook.Prefix(nativeStation), "The same repair flag cannot alter an unrelated Misc station");
drone.repairing = false;
Check(HoldRepairHook.Prefix(station) && HoldRepairHook.Prefix(new DroneStation()), "Healthy and never-spawned own stations retain normal deployment");
drone.repairing = true;
AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(drone.health, PawnHealthState.Dead);
Check(!station.HoldingForRepair, "A dead robot cannot be held as a living repair patient");
AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(drone.health, PawnHealthState.Mobile);
var production = new Harmony("grechka.drones.native.checks");
production.PatchAll(typeof(Drone).Assembly);
var spawnMethod = AccessTools.Method(typeof(X2_Building_AIRobotRechargeStation), "Button_SpawnBot");
Check(Harmony.GetPatchInfo(spawnMethod).Prefixes.Any(p => p.owner == production.Id), "Repair guard binds the actual private Misc deployment method");
var allowedHookTypes = new[] { typeof(X2_Building_AIRobotRechargeStation), typeof(Pawn_NeedsTracker), typeof(Need),
    typeof(Need_Food), typeof(Need_Rest), typeof(MassUtility), typeof(Pawn_CarryTracker) };
Check(Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains(production.Id))
    .All(m => allowedHookTypes.Contains(m.DeclaringType)),
    "Production hooks are limited to dock deployment and carrier needs/cargo, without global pawn tick or combat patches");

// Keep native GetPriority and actual Need_Rest values. Only the final job-producing callback
// is a boundary fixture, so a healthy-energy regression cannot hide behind a fake priority.
fixture.Patch(AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Map)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Map)));
fixture.Patch(AccessTools.Method(typeof(X2_JobGiver_RechargeEnergy), "TryGiveJob"),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.RechargeJob)));
Fixture.DroneMap = Fixture.StationMap = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
AccessTools.Field(typeof(Thing), "positionInt").SetValue(station, new IntVec3(10, 0, 10));
AccessTools.Field(typeof(Thing), "positionInt").SetValue(drone, new IntVec3(11, 0, 10));
drone.needs = new Pawn_NeedsTracker(drone);
drone.needs.rest = (Need_Rest)RuntimeHelpers.GetUninitializedObject(typeof(Need_Rest));
void Energy(float amount) => AccessTools.Field(typeof(Need), "curLevelInt").SetValue(drone.needs.rest, amount);
var recharge = new JobGiver_DroneRecharge();
MethodInfo giveRecharge = AccessTools.Method(typeof(JobGiver_DroneRecharge), "TryGiveJob");
Energy(1f); Fixture.RechargeCalls = 0;
Check(recharge.GetPriority(drone) == 0 && giveRecharge.Invoke(recharge, new object[] { drone }) == null && Fixture.RechargeCalls == 0,
    "Full-energy drone bypasses native recharge job production and can reach its profession node");
Energy(.2f);
Check(recharge.GetPriority(drone) > 0 && ReferenceEquals(giveRecharge.Invoke(recharge, new object[] { drone }), Fixture.RechargeSentinel) && Fixture.RechargeCalls == 1,
    "Low-energy drone enters native recharge through the real Misc priority gate");
drone.recharging = false;
Energy(.35f); Fixture.RechargeCalls = 0;
Check(recharge.GetPriority(drone) == 0 && giveRecharge.Invoke(recharge, new object[] { drone }) == null,
    "Moderate energy near the dock preserves native continue-working behavior");
AccessTools.Field(typeof(Thing), "positionInt").SetValue(drone, new IntVec3(50, 0, 10));
Check(recharge.GetPriority(drone) > 0 && ReferenceEquals(giveRecharge.Invoke(recharge, new object[] { drone }), Fixture.RechargeSentinel),
    "The same energy far from the dock triggers native early return for charge");
Fixture.StationMap = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)); Energy(.1f); Fixture.RechargeCalls = 0;
Check(recharge.GetPriority(drone) == 0 && giveRecharge.Invoke(recharge, new object[] { drone }) == null && Fixture.RechargeCalls == 0,
    "A dock on another map cannot produce a recharge job");
Fixture.StationMap = Fixture.DroneMap;
drone.rechargeStation = null;
Check(recharge.GetPriority(drone) == 0 && giveRecharge.Invoke(recharge, new object[] { drone }) == null,
    "A missing dock cannot force an invalid recharge job");
drone.rechargeStation = station;

// Actual recharge controller + role selection; map searches and body health are fixture boundaries.
fixture.Patch(AccessTools.PropertyGetter(typeof(Drone), nameof(Drone.HealthFraction)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.HealthFraction)));
fixture.Patch(AccessTools.Method(typeof(DroneJobs), nameof(DroneJobs.Defend)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.DefendJob)));
fixture.Patch(AccessTools.Method(typeof(DroneJobs), nameof(DroneJobs.RescueOwn)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.RescueJob)));
fixture.Patch(AccessTools.Method(typeof(DroneJobs), nameof(DroneJobs.Emergency)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.EmergencyJob)));
var properties = new DroneProperties { role = DroneRole.Warrior };
drone.def.modExtensions = new List<DefModExtension> { properties };
drone.repairing = false;
drone.recharging = false;
station.isRechargeActive = false;
AccessTools.Field(typeof(Thing), "positionInt").SetValue(drone, new IntVec3(11, 0, 10));
Energy(.2f);
Check(ReferenceEquals(giveRecharge.Invoke(recharge, new object[] { drone }), Fixture.RechargeSentinel) && drone.recharging,
    "Low charge starts a persistent recharge session");
foreach (float level in new[] { .30f, .31f, .35f, .99f })
{
    Energy(level);
    Check(recharge.GetPriority(drone) == 0 && ReferenceEquals(giveRecharge.Invoke(recharge, new object[] { drone }), Fixture.RechargeSentinel) && drone.recharging,
        $"Idle warrior continues charging at {level:P0} instead of receiving a patrol");
}
Energy(1f); station.isRechargeActive = true;
Check(giveRecharge.Invoke(recharge, new object[] { drone }) == null && !drone.recharging && !station.isRechargeActive,
    "Exactly full charge releases the drone and clears both session flags");
Energy(.31f); station.isRechargeActive = true;
Check(ReferenceEquals(giveRecharge.Invoke(recharge, new object[] { drone }), Fixture.RechargeSentinel) && drone.recharging,
    "An already active Misc recharge resumes the session even without the new saved flag");
Fixture.WarriorWork = new Job();
Energy(.29f);
Check(ReferenceEquals(giveRecharge.Invoke(recharge, new object[] { drone }), Fixture.RechargeSentinel),
    "Work cannot release a charging drone below the safe 30 percent threshold");
Energy(.31f);
Check(ReferenceEquals(giveRecharge.Invoke(recharge, new object[] { drone }), Fixture.WarriorWork) && !drone.recharging && !station.isRechargeActive,
    "An actual defense job interrupts charging above 30 percent and clears the dock flag");
Fixture.WarriorWork = null;
properties.role = DroneRole.Rescue;
drone.recharging = true; station.isRechargeActive = true;
Check(ReferenceEquals(giveRecharge.Invoke(recharge, new object[] { drone }), Fixture.RechargeSentinel),
    "Idle rescue drone also stays on charge");
Fixture.OwnRescueWork = new Job(); Fixture.EmergencyWork = new Job();
Check(ReferenceEquals(giveRecharge.Invoke(recharge, new object[] { drone }), Fixture.OwnRescueWork) && !drone.recharging && !station.isRechargeActive,
    "Own rescue interrupts charging before emergency work");
Fixture.OwnRescueWork = null;
drone.recharging = true;
Check(ReferenceEquals(giveRecharge.Invoke(recharge, new object[] { drone }), Fixture.EmergencyWork) && !drone.recharging,
    "Available fire or capture work can interrupt the rescue drone's recharge");
Fixture.EmergencyWork = null;
drone.recharging = true; Fixture.RechargeCalls = 0;
Fixture.StationMap = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
Check(giveRecharge.Invoke(recharge, new object[] { drone }) == null && Fixture.RechargeCalls == 0,
    "A latched session cannot request charging from a dock on another map");
Fixture.StationMap = Fixture.DroneMap;
drone.rechargeStation = null;
Check(giveRecharge.Invoke(recharge, new object[] { drone }) == null,
    "A missing dock is safe even during a latched session");
drone.rechargeStation = station;
properties.role = DroneRole.Warrior; drone.repairing = true;
Check(giveRecharge.Invoke(recharge, new object[] { drone }) == null,
    "Charging cannot take priority over a warrior's latched retreat for repair");
drone.repairing = false; drone.recharging = false;

// Safety must see nearby hostiles even when the worker's allowed area excludes that enemy.
// Map spawn and hostility are native-service boundaries; threat eligibility stays actual code.
fixture.Patch(AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Spawned)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Spawned)));
fixture.Patch(AccessTools.Method(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Thing) }),
    prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Hostile)));
fixture.Patch(AccessTools.Method(typeof(DroneJobs), nameof(DroneJobs.Allowed)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.Disallowed)));
var threat = new Pawn { def = new ThingDef { defName = "FixtureThreat", race = new RaceProperties() } };
threat.health = new Pawn_HealthTracker(threat);
Fixture.AllowedCalls = 0;
Check(DroneJobs.IsThreatToColony(drone, threat) && Fixture.AllowedCalls == 0,
    "Colony safety detects a hostile without consulting the drone's restricted movement area");
Check(!DroneJobs.IsThreat(drone, threat) && Fixture.AllowedCalls == 1,
    "Combat pursuit separately respects the movement area for the very same hostile");
AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(threat.health, PawnHealthState.Down);
Check(!DroneJobs.IsThreatToColony(drone, threat), "Incapacitated hostile is excluded from the rescue safety threat check");
fixture.Patch(AccessTools.PropertyGetter(typeof(RaceProperties), nameof(RaceProperties.IsAnomalyEntity)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.NoAnomaly)));
Fixture.IsHostile = false;
FleshTypeDefOf.Normal = new FleshTypeDef { defName = "FixtureFlesh", isOrganic = true };
var predator = new Pawn
{
    def = new ThingDef { defName = "FixturePredator", race = new RaceProperties
    { intelligence = Intelligence.Animal } }
};
predator.health = new Pawn_HealthTracker(predator);
predator.jobs = new Pawn_JobTracker(predator);
var ownPrey = new Pawn();
AccessTools.Field(typeof(Thing), "factionInt").SetValue(ownPrey, player);
JobDefOf.PredatorHunt = new JobDef { defName = "PredatorHunt" };
predator.jobs.curJob = new Job { def = JobDefOf.PredatorHunt, targetA = ownPrey };
Check(DroneJobs.IsThreatToColony(drone, predator),
    "Neutral predator hunting a player pawn is a threat even without mindState.enemyTarget");
predator.jobs.curJob.targetA = new Pawn();
Check(!DroneJobs.IsThreatToColony(drone, predator),
    "Neutral predator hunting wildlife is not a colony threat");
predator.jobs.curJob = null;
Check(!DroneJobs.IsThreatToColony(drone, predator), "Peaceful neutral wildlife is never attacked preemptively");
Fixture.IsHostile = true;

// Exercise actual Scribe values exposed by our two classes; the extensive native base-pawn and
// base-station graphs are fixture boundaries, not a pretend full save-game roundtrip.
fixture.Patch(AccessTools.Method(typeof(X2_AIRobot), nameof(X2_AIRobot.ExposeData)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.SkipBaseScribe)));
fixture.Patch(AccessTools.Method(typeof(X2_Building_AIRobotRechargeStation), nameof(X2_Building_AIRobotRechargeStation.ExposeData)), prefix: new HarmonyMethod(typeof(Fixture), nameof(Fixture.SkipBaseScribe)));
var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map>());
AccessTools.Field(typeof(Game), "uniqueIDsManager").SetValue(game, new UniqueIDsManager());
Current.Game = game;
var prefs = AccessTools.Field(typeof(Prefs), "data");
prefs.SetValue(null, Activator.CreateInstance(prefs.FieldType));
string path = Path.Combine(Path.GetTempPath(), "grechka-drones-scribe-" + Guid.NewGuid().ToString("N") + ".xml");
try
{
    var saved = new SavedDrones
    {
        drone = new Drone { repairing = true, recharging = true, captureEnemies = true, nextFoamTick = 456789 },
        station = new DroneStation { captureEnemies = true }
    };
    Scribe.saver.InitSaving(path, "drones"); Scribe_Deep.Look(ref saved, "state"); Scribe.saver.FinalizeSaving();
    SavedDrones loaded = null;
    Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "state"); Scribe.loader.FinalizeLoading();
    Check(loaded.drone.repairing && loaded.drone.recharging && loaded.drone.captureEnemies && loaded.drone.nextFoamTick == 456789 && loaded.station.captureEnemies,
        "Native Scribe roundtrip preserves recharge and retreat latches, capture controls and absolute foam cooldown");
    File.WriteAllText(path, File.ReadAllText(path).Replace("<grechkaRecharging>True</grechkaRecharging>", ""));
    loaded = null;
    Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "state"); Scribe.loader.FinalizeLoading();
    Check(!loaded.drone.recharging && loaded.drone.repairing, "Older saves default the absent recharge flag without changing the repair latch");
    Check(!new Drone().captureEnemies && !new Drone().repairing && !new Drone().recharging && new Drone().nextFoamTick == 0,
        "Fresh drones start without capture, retreat or a stale cooldown");
}
finally { if (File.Exists(path)) File.Delete(path); }
checks += CaravanChecks.Run(fixture, root);
Console.WriteLine($"PASS {checks} GrechkaDrones native/schema checks. No game launched or controlled. Full maps, rescue pathing and complete save graphs require in-game acceptance.");

public sealed class SavedDrones : IExposable
{
    public Drone drone;
    public DroneStation station;
    public void ExposeData() { Scribe_Deep.Look(ref drone, "drone"); Scribe_Deep.Look(ref station, "station"); }
}
static class Fixture
{
    public static int NativeDamageCalls;
    public static Faction Player;
    public static Thing Carried;
    public static bool DropSucceeds;
    public static readonly List<string> Events = new();
    public static Job Started;
    public static JobCondition EndCondition;
    public static bool? KeepCarrying;
    public static readonly List<Thing> Reservations = new();
    public static Thing ReservationFailure;
    public static Map DroneMap, StationMap;
    public static int RechargeCalls, AllowedCalls;
    public static bool IsHostile = true;
    public static readonly Job RechargeSentinel = new Job();
    public static Job WarriorWork, OwnRescueWork, EmergencyWork;
    public static bool HealthFraction(ref float __result) { __result = 1f; return false; }
    public static bool DefendJob(ref Job __result) { __result = WarriorWork; return false; }
    public static bool RescueJob(ref Job __result) { __result = OwnRescueWork; return false; }
    public static bool EmergencyJob(ref Job __result) { __result = EmergencyWork; return false; }
    public static bool Damage(out bool absorbed) { NativeDamageCalls++; absorbed = false; return false; }
    public static bool PlayerFaction(ref Faction __result) { __result = Player; return false; }
    public static bool CarriedThing(ref Thing __result) { __result = Carried; return false; }
    public static bool Drop(out Thing resultingThing, ref bool __result)
    { Events.Add("drop"); resultingThing = Carried; __result = DropSucceeds; return false; }
    public static bool StartJob(Job newJob, JobCondition lastJobEndCondition, bool? keepCarryingThingOverride)
    { Events.Add("start"); Started = newJob; EndCondition = lastJobEndCondition; KeepCarrying = keepCarryingThingOverride; return false; }
    public static bool SkipReservationsCleanup() => false;
    public static bool Reserve(LocalTargetInfo target, ref bool __result)
    { Reservations.Add(target.Thing); __result = target.Thing != ReservationFailure; return false; }
    public static bool BedSlots(ref int __result) { __result = 1; return false; }
    public static bool Map(Thing __instance, ref Map __result)
    { __result = __instance is X2_Building_AIRobotRechargeStation ? StationMap : DroneMap; return false; }
    public static bool RechargeJob(ref Job __result) { RechargeCalls++; __result = RechargeSentinel; return false; }
    public static bool Spawned(ref bool __result) { __result = true; return false; }
    public static bool Hostile(ref bool __result) { __result = IsHostile; return false; }
    public static bool NoAnomaly(ref bool __result) { __result = false; return false; }
    public static bool Disallowed(ref bool __result) { AllowedCalls++; __result = false; return false; }
    public static bool SkipBaseScribe() => false;
}
