using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using AIRobot;
using Grechka.Drones;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

static class CaravanChecks
{
    static int pawnTicks, miscTicks;
    static Thing meal;
    static Pawn foodOwner;
    static Faction player;
    static IReadOnlyList<Pawn> sendableCandidates;
    public static int Run(Harmony previousFixture, string repo)
    {
        previousFixture.UnpatchAll(previousFixture.Id);
        var fixture = new Harmony("grechka.caravan-drone.fixture");
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new Exception(label);
            checks++; Console.WriteLine("PASS caravan drone: " + label);
        }
        void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
        fixture.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Suspended)), prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(False)));
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        game.tickManager = new TickManager(); game.uniqueIDsManager = new UniqueIDsManager();
        Field(game, "maps", new List<Map>()); Current.Game = game;
        NeedDefOf.Food = new NeedDef { defName = "Food", needClass = typeof(Need_Food) };
        NeedDefOf.Rest = new NeedDef { defName = "Rest", needClass = typeof(Need_Rest) };
        StatDefOf.MaxNutrition = new StatDef { defName = "MaxNutrition", workerClass = typeof(StatWorker), defaultBaseValue = 2 };
        StatDefOf.Mass = new StatDef { defName = "Mass", defaultBaseValue = 1 };
        CaravanDrone Make(int tier)
        {
            var drone = new CaravanDrone { def = new X2_ThingDef_AIRobot { defName = "FixtureCaravanDroneT" + tier,
                label = "caravan drone", race = new RaceProperties { intelligence = Intelligence.ToolUser, allowedOnCaravan = true, packAnimal = true },
                modExtensions = new List<DefModExtension> { new DroneProperties { role = DroneRole.Caravan, tier = tier } } } };
            drone.health = new Pawn_HealthTracker(drone);
            drone.mindState = new Pawn_MindState(drone);
            drone.needs = new Pawn_NeedsTracker(drone);
            drone.needs.food = new Need_Food(drone) { def = NeedDefOf.Food };
            drone.needs.rest = new Need_Rest(drone) { def = NeedDefOf.Rest };
            Field(drone.needs, "needs", new List<Need> { drone.needs.food, drone.needs.rest });
            drone.needs.food.CurLevel = 2f;
            return drone;
        }
        var shouldHave = AccessTools.Method(typeof(Pawn_NeedsTracker), "ShouldHaveNeed");
        var drone = Make(1);
        fixture.Patch(AccessTools.Method(typeof(X2_Building_AIRobotRechargeStation), nameof(Thing.Destroy)),
            prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(Skip)));
        var dock = new DroneStation { robot = drone };
        drone.rechargeStation = dock;
        drone.inventory = new Pawn_InventoryTracker(drone);
        var cargo = new Thing { def = new ThingDef { defName = "FixtureCaravanCargo", category = ThingCategory.Item, stackLimit = 75 }, stackCount = 7 };
        drone.inventory.innerContainer.TryAdd(cargo, false);
        dock.Destroy();
        Check(dock.robot == null && drone.rechargeStation == null && !drone.Destroyed
            && drone.inventory.innerContainer.Contains(cargo) && cargo.stackCount == 7,
            "destroying a deployed carrier dock detaches its travelling drone without deleting cargo");
        Check((bool)shouldHave.Invoke(drone.needs, new object[] { NeedDefOf.Food })
            && (bool)shouldHave.Invoke(drone.needs, new object[] { NeedDefOf.Rest })
            && !(bool)shouldHave.Invoke(drone.needs, new object[] { new NeedDef { defName = "MechEnergy" } }),
            "only food fuel and the shared dock battery are created, without Biotech energy or mood");
        // The map list, faction singleton and quest/lord services are fixture boundaries.
        // AllSendablePawns' real race, ownership and downed eligibility checks execute.
        fixture.Patch(AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.AllPawnsSpawned)), prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(Candidates)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer)), prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(Player)));
        fixture.Patch(AccessTools.Method(typeof(QuestUtility), nameof(QuestUtility.IsQuestHelper), new[] { typeof(Pawn) }), prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(False)));
        fixture.Patch(AccessTools.Method(typeof(QuestUtility), nameof(QuestUtility.IsQuestLodger), new[] { typeof(Pawn) }), prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(False)));
        fixture.Patch(AccessTools.Method(typeof(LordUtility), nameof(LordUtility.GetLord), new[] { typeof(Pawn) }), prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(NoLord)));
        player = new Faction { def = new FactionDef { defName = "FixtureCaravanPlayer", isPlayer = true } };
        Field(drone, "factionInt", player);
        var excluded = new Pawn { def = new ThingDef { defName = "FixtureExcluded", race = new RaceProperties { allowedOnCaravan = false } } };
        excluded.health = new Pawn_HealthTracker(excluded); excluded.mindState = new Pawn_MindState(excluded);
        Field(excluded, "factionInt", player);
        sendableCandidates = new Pawn[] { drone, excluded };
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.mapPawns = (MapPawns)RuntimeHelpers.GetUninitializedObject(typeof(MapPawns));
        Check(CaravanFormingUtility.AllSendablePawns(map).SequenceEqual(new[] { drone }),
            "real caravan selection lists the carrier and excludes a race disallowed on caravans");
        Field(drone.health, "healthState", PawnHealthState.Down);
        Check(CaravanFormingUtility.AllSendablePawns(map).Count == 0
            && CaravanFormingUtility.AllSendablePawns(map, allowEvenIfDowned: true).Contains(drone),
            "real caravan selection retains normal downed-pawn rules");
        Field(drone.health, "healthState", PawnHealthState.Mobile);
        Field(drone, "factionInt", new Faction { def = new FactionDef { defName = "FixtureForeign" } });
        Check(CaravanFormingUtility.AllSendablePawns(map).Count == 0,
            "foreign carriers are not exposed as the player's caravan members");
        Field(drone, "factionInt", player);
        for (int tier = 1; tier <= 5; tier++)
        {
            var carrier = Make(tier);
            Check(MassUtility.Capacity(carrier) == 50f * tier, "native caravan capacity equals " + (50 * tier) + " kg");
            var goods = new ThingDef { defName = "FixtureHeavyGoods", stackLimit = 750,
                statBases = new List<StatModifier> { new StatModifier { stat = StatDefOf.Mass, value = 3f } } };
            var tracker = new Pawn_CarryTracker(carrier);
            Check(tracker.MaxStackSpaceEver(goods) == (int)Math.Floor(50f * tier / 3f),
                "native map carry count observes actual 3 kg item mass at tier " + tier);
        }
        Check(drone.needs.food.MaxLevel == 2 && drone.needs.rest.CurLevel == 1,
            "a full 2-nutrition food tank and dock battery represent the same charge");
        drone.needs.food.CurLevel = .8f;
        Check(Math.Abs(drone.needs.rest.CurLevel - .4f) < .0001f, "eating/fuel changes update dock energy immediately");
        drone.needs.rest.CurLevel = .75f;
        Check(Math.Abs(drone.needs.food.CurLevel - 1.5f) < .0001f, "powered dock charge updates the food fuel tank immediately");
        Check(Math.Abs(drone.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed) * 60000 - 2f) < .0001f
            && Math.Abs(drone.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Starving) * 60000 - 2f) < .0001f,
            "native food forecasts see a constant 2 nutrition per day instead of biological starvation multipliers");
        drone.needs.food.CurLevel = 2f;
        for (int i = 0; i < 200; i++) { drone.needs.food.NeedInterval(); drone.needs.rest.NeedInterval(); }
        Check(Math.Abs(drone.needs.food.CurLevel - 1f) < .0001f && Math.Abs(drone.needs.rest.CurLevel - .5f) < .0001f,
            "half a day drains exactly half the single battery without a second rest drain");
        float before = drone.needs.food.CurLevel;
        drone.needs.rest.TickResting(10f);
        Check(drone.needs.food.CurLevel == before, "resting/camping does not generate free energy");
        for (int i = 0; i < 250; i++) drone.needs.food.NeedInterval();
        Check(drone.needs.food.CurLevel == 0 && drone.needs.rest.CurLevel == 0 && !drone.Destroyed && !drone.Dead
            && MassUtility.Capacity(drone) == 0 && drone.health.hediffSet.hediffs.Count == 0,
            "flat battery removes caravan carrying contribution without starvation damage or destroying the pawn");
        Check(!new JobGiver_CaravanHaul().TryIssueJobPackage(drone, default).IsValid,
            "flat battery cannot issue ordinary hauling work");
        var foodDef = new ThingDef { defName = "FixtureMeal", category = ThingCategory.Item, stackLimit = 750,
            statBases = new List<StatModifier> { new StatModifier { stat = StatDefOf.Mass, value = .3f } } };
        Check(new Pawn_CarryTracker(drone).MaxStackSpaceEver(foodDef) > 0,
            "reserve handling still permits picking up food for recovery from zero");

        // Real caravan feeding dispatch; map/world and ingestion effects are isolated service boundaries.
        fixture.Patch(AccessTools.Method(typeof(ChildcareUtility), nameof(ChildcareUtility.CanSuckle)), prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(NoSuckle)));
        fixture.Patch(AccessTools.Method(typeof(VirtualPlantsUtility), nameof(VirtualPlantsUtility.CanEatVirtualPlantsNow)), prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(False)));
        fixture.Patch(AccessTools.Method(typeof(CaravanInventoryUtility), nameof(CaravanInventoryUtility.TryGetBestFood)), prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(BestFood)));
        fixture.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.Ingested)), prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(Ingest)));
        meal = new Thing { def = foodDef, stackCount = 3 }; foodOwner = drone;
        var caravan = (Caravan)RuntimeHelpers.GetUninitializedObject(typeof(Caravan)); caravan.notifiedOutOfFood = true;
        var needs = new Caravan_NeedsTracker(caravan);
        AccessTools.Method(typeof(Caravan_NeedsTracker), "TrySatisfyFoodNeed").Invoke(needs, new object[] { drone, drone.needs.food, 150 });
        Check(meal.stackCount == 2 && Math.Abs(drone.needs.food.CurLevel - .9f) < .0001f
            && Math.Abs(drone.needs.rest.CurLevel - .45f) < .0001f && MassUtility.Capacity(drone) == 50,
            "native caravan feeding consumes one real ration and restores charge and cargo contribution");

        fixture.Patch(AccessTools.Method(typeof(Pawn), "Tick"), prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(PawnTick)));
        fixture.Patch(AccessTools.Method(typeof(X2_AIRobot), "Tick"), prefix: new HarmonyMethod(typeof(CaravanChecks), nameof(MiscTick)));
        drone.needs.food.CurLevel = 0;
        AccessTools.Method(typeof(CaravanDrone), "Tick").Invoke(drone, null);
        Check(pawnTicks == 1 && miscTicks == 0 && !drone.Destroyed,
            "actual caravan drone tick reaches Pawn hooks and bypasses Misc auto-food, recall and battery destruction");

        string path = Path.Combine(Path.GetTempPath(), "caravan-fuel-" + Guid.NewGuid() + ".xml");
        try
        {
            drone.needs.food.CurLevel = .73f;
            Scribe.saver.InitSaving(path, "fuel"); drone.needs.food.ExposeData(); Scribe.saver.FinalizeSaving();
            var restored = Make(4);
            Scribe.loader.InitLoading(path); restored.needs.food.ExposeData(); Scribe.loader.FinalizeLoading();
            CaravanEnergy.SyncFromFood(restored);
            Check(Math.Abs(restored.needs.food.CurLevel - .73f) < .0001f && Math.Abs(restored.needs.rest.CurLevel - .365f) < .0001f,
                "native need Scribe preserves fractional fuel and restores its dock mirror");
        }
        finally { Scribe.mode = LoadSaveMode.Inactive; File.Delete(path); }
        var defs = XDocument.Load(Path.Combine(repo, "mods/GrechkaDrones/Defs/CaravanDrones.xml")).Root.Elements().ToList();
        foreach (var unit in defs.Where(d => d.Element("thingClass")?.Value == typeof(CaravanDrone).FullName))
        {
            Check(unit.Element("race").Element("allowedOnCaravan").Value == "true"
                && unit.Element("robotWorkTypes").Elements().Single().Element("workTypeDef").Value == "Hauling",
                "shipping race is caravan-enabled and only performs hauling: " + unit.Element("defName").Value);
        }
        return checks;
    }
    public static bool PawnTick() { pawnTicks++; return false; }
    public static bool Skip() => false;
    public static bool Candidates(ref IReadOnlyList<Pawn> __result) { __result = sendableCandidates; return false; }
    public static bool Player(ref Faction __result) { __result = player; return false; }
    public static bool NoLord(ref Lord __result) { __result = null; return false; }
    public static bool MiscTick() { miscTicks++; return false; }
    public static bool False(ref bool __result) { __result = false; return false; }
    public static bool NoSuckle(ref bool __result) { __result = false; return false; }
    public static bool BestFood(ref Thing food, ref Pawn owner, ref bool __result)
    { food = meal; owner = foodOwner; __result = meal.stackCount > 0; return false; }
    public static bool Ingest(Thing __instance, ref float __result)
    { __instance.stackCount--; __result = .9f; return false; }
}
