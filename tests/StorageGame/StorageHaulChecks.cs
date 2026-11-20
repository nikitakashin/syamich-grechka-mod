using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.Storage;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

static class StorageHaulChecks
{
    static Action<Thing, IntVec3> register;
    static LocalTargetInfo pathDestination;
    static Thing unreachableTarget;
    static bool ReachTarget(LocalTargetInfo dest, ref bool __result) { __result = dest.Thing != unreachableTarget || unreachableTarget == null; return false; }
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static bool Skip() => false;
    static bool True(ref bool __result) { __result = true; return false; }
    static bool False(ref bool __result) { __result = false; return false; }
    public sealed class FixtureStatWorker : StatWorker
    {
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true) => stat.defaultBaseValue;
        public override void FinalizeValue(StatRequest req, ref float val, bool applyPostProcess) { }
    }
    static StatDef Stat(string name, float value) => new StatDef { defName = name, workerClass = typeof(FixtureStatWorker), defaultBaseValue = value };
    static bool Path(LocalTargetInfo dest) { pathDestination = dest; return false; }
    static bool Spawn(Thing newThing, IntVec3 loc, ref Thing __result)
    {
        register(newThing, loc); __result = newThing; return false;
    }
    static bool DestroyOnMap(Thing __instance)
    {
        if (!__instance.Spawned || !(__instance is Blueprint || __instance is Frame)) return true;
        __instance.Map.thingGrid.Deregister(__instance);
        Field(__instance, "mapIndexOrState", (sbyte)-2);
        return false;
    }
    static bool MakeWithStuff(ThingDef def, ThingDef stuff, ref Thing __result)
    {
        if (stuff == null) return true;
        __result = Fixture.Create(def);
        Field(__result, "stuffInt", stuff);
        return false;
    }
    static List<Toil> Toils(JobDriver driver)
    {
        driver.pawn.jobs.curDriver = driver;
        driver.pawn.jobs.curJob = driver.job;
        var steps = ((IEnumerable<Toil>)AccessTools.Method(driver.GetType(), "MakeNewToils").Invoke(driver, null)).ToList();
        foreach (Toil step in steps) step.actor = driver.pawn;
        return steps;
    }
    static void Step(List<Toil> steps, string name) => steps.First(t => t.debugName == name).initAction();

    internal static void Run(Map map, Pawn pawn, Building_DiskRack rack, StorageDisk disk, TestHolder holder,
        Action<Thing, IntVec3> put, Action<bool,string> check)
    {
        var fixture = new Harmony("grechka.storage.haul-jobs");
        void Patch(MethodBase method, string name) => fixture.Patch(method, prefix: new HarmonyMethod(typeof(StorageHaulChecks), name) { priority = Priority.First });
        register = put;
        var rootField = AccessTools.Field(typeof(Current), "rootInt");
        var oldRoot = rootField.GetValue(null);
        try
        {
            Patch(typeof(Frame).TypeInitializer, nameof(Skip));
            Patch(typeof(Dialog_FormCaravan).TypeInitializer, nameof(Skip));
            Patch(typeof(CaravanUIUtility).TypeInitializer, nameof(Skip));
            var root = (Root_Play)RuntimeHelpers.GetUninitializedObject(typeof(Root_Play));
            var ui = (UIRoot_Play)RuntimeHelpers.GetUninitializedObject(typeof(UIRoot_Play));
            ui.mapUI = (MapInterface)RuntimeHelpers.GetUninitializedObject(typeof(MapInterface));
            ui.mapUI.selector = new Selector(); root.uiRoot = ui; rootField.SetValue(null, root);
            Patch(AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath)), nameof(Path));
            Patch(AccessTools.Method(typeof(MassUtility), nameof(MassUtility.IsOverEncumbered)), nameof(False));
            map.enrouteManager = new EnrouteManager(map); map.events = new MapEvents(map);
            map.lordManager = new LordManager(map); map.edificeGrid = new EdificeGrid(map); map.planManager = new PlanManager(map);
            map.pathing = new Pathing(map);
            map.reachability = new Reachability(map);
            pawn.health = new Pawn_HealthTracker(pawn); pawn.mindState = new Pawn_MindState(pawn);
            pawn.jobs = new Pawn_JobTracker(pawn); pawn.inventory = new Pawn_InventoryTracker(pawn);
            pawn.pather = new Pawn_PathFollower(pawn);
            pawn.rotationTracker = new Pawn_RotationTracker(pawn);
            StatDefOf.CarryingCapacity = Stat("FixtureCarryCapacity", 75);
            StatDefOf.ConstructionSpeed = Stat("FixtureConstructionSpeed", 1);
            StatDefOf.ConstructionSpeedFactor = Stat("FixtureConstructionSpeedFactor", 1);
            StatDefOf.ConstructSuccessChance = Stat("FixtureConstructSuccess", 1);
            StatDefOf.WorkToBuild = Stat("FixtureWorkToBuild", 100);
            StatDefOf.MaxHitPoints = Stat("FixtureMaxHP", 100);
            RecordDefOf.ThingsHauled = new RecordDef { defName = "HaulFixtureHauled", type = RecordType.Int };
            RecordDefOf.ThingsConstructed = new RecordDef { defName = "HaulFixtureBuilt", type = RecordType.Int };
            DefDatabase<RecordDef>.Add(RecordDefOf.ThingsHauled); DefDatabase<RecordDef>.Add(RecordDefOf.ThingsConstructed);
            pawn.records = new Pawn_RecordsTracker(pawn);
            Field(pawn, "positionInt", rack.Position + IntVec3.East);
            var steel = Fixture.Def("HaulFixtureSteel", typeof(ThingWithComps), 75);
            steel.alwaysHaulable = true;
            steel.stuffProps = new StuffProperties { categories = new() { new StuffCategoryDef { defName = "HaulFixtureMetallic" } } };
            var stock = Fixture.Create(steel, 100); holder.Contents.TryAdd(stock, false); disk.Accept(stock, 100);
            check(map.listerThings.ThingsOfDef(steel).Count == 0 && stock.ParentHolder == disk, "All 100 units of the fixture steel are on disk");
            var dialog = (Dialog_FormCaravan)RuntimeHelpers.GetUninitializedObject(typeof(Dialog_FormCaravan));
            Field(dialog, "map", map); var list = new List<TransferableOneWay>(); Field(dialog, "transferables", list);
            AccessTools.Method(typeof(Dialog_FormCaravan), "AddItemsToTransferables").Invoke(dialog, null);
            var line = list.Single(t => t.ThingDef == steel);
            check(line.things.Single() == stock && line.MaxCount == 100
                && AccessTools.Method(typeof(CaravanUIUtility), "GetTransferableCategory").Invoke(null, new object[]{line}).ToString() == "Item",
                "The caravan dialog lists all disk steel once under Items");
            line.AdjustTo(30);
            var lordJob = new LordJob_FormAndSendCaravan { transferables = new() { line } };
            var lord = new Lord { lordManager = map.lordManager }; Field(lord, "curJob", lordJob);
            lordJob.lord = lord; pawn.lord = lord; lord.ownedPawns.Add(pawn); map.lordManager.lords.Add(lord);
            JobDefOf.PrepareCaravan_GatherItems = new JobDef { defName = "PrepareCaravan_GatherItems", driverClass = typeof(JobDriver_PrepareCaravan_GatherItems) };
            // A job that started on loose cargo keeps its floor-only pickup toils across loops.
            var ground = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).First(t => t.Spawned && t.def.category == ThingCategory.Item);
            var groundJob = JobMaker.MakeJob(JobDefOf.PrepareCaravan_GatherItems, ground);
            groundJob.lord = lord;
            var groundSteps = Toils(new JobDriver_PrepareCaravan_GatherItems { pawn = pawn, job = groundJob });
            groundJob.targetA = stock;
            var gotoGround = groundSteps.First(t => t.debugName == "GotoThing");
            check(gotoGround.endConditions.Any(condition => condition() == JobCondition.Incompletable)
                && GatherItemsForCaravanUtility.FindThingToHaul(pawn, lord) == null,
                "A packing loop started on the floor cannot switch to disk steel using floor-only pickup toils");
            pawn.jobs.curJob = null;
            pawn.jobs.curDriver = null;
            var power = rack.GetComp<CompPowerTrader>();
            Field(power, "powerOnInt", false);
            check(GatherItemsForCaravanUtility.FindThingToHaul(pawn, lord) == null && stock.stackCount == 100,
                "Power loss after selection prevents a new pickup and preserves all disk steel");
            Field(power, "powerOnInt", true);
            Patch(AccessTools.Method(typeof(PawnCapacitiesHandler), nameof(PawnCapacitiesHandler.CapableOf)), nameof(True));
            var job = (Job)AccessTools.Method(typeof(JobGiver_PrepareCaravan_GatherItems), "TryGiveJob")
                .Invoke(new JobGiver_PrepareCaravan_GatherItems(), new object[]{pawn});
            check(job?.targetA.Thing == stock && job.lord == lord, "The next caravan job finds the selected stack in the rack");
            var gather = new JobDriver_PrepareCaravan_GatherItems { pawn = pawn, job = job };
            var steps = Toils(gather);
            check(gather.TryMakePreToilReservations(false), "The caravan job reserves the real disk stack");
            Step(steps, "GotoThing");
            check(pathDestination.Thing == rack, "The caravan job targets the spawned rack for walking");
            Step(steps, "DetermineNumToHaul"); Step(steps, "StartCarryThing"); Step(steps, "AddCarriedThingToTransferables");
            Step(steps, "HaulCaravanItemInInventory");
            Step(steps, "FindCarrier"); Step(steps, "AddHauledItemsToCarrier");
            check(pawn.inventory.innerContainer.TotalStackCountOfDef(steel) == 30 && stock.stackCount == 70
                && line.CountToTransfer == 0 && !pawn.inventory.HasAnyUnpackedCaravanItems,
                "Native caravan packing puts exactly the selected 30 steel in the carrier's final inventory and leaves 70 on disk");
            pawn.lord = null; pawn.jobs.curDriver = null; pawn.jobs.curJob = null;
            pawn.inventory.innerContainer.TryTransferAllToContainer(holder.Contents);
            BuildMaterials(map, rack, disk, holder, check);
            Surgery(map, pawn, rack, disk, holder, Patch, check);
            CraftAndRepair(map, pawn, rack, disk, holder, fixture, check);
            Wall(map, pawn, steel, stock, Patch, check);
            // The enclosing access suite checks absolute disk counts for its own unrelated stock.
            disk.Withdraw(stock, stock.stackCount, holder.Contents);
        }
        finally { fixture.UnpatchAll(fixture.Id); rootField.SetValue(null, oldRoot); register = null; }
    }

    static Map architectMap;
    static List<FloatMenuOption> buildOptions;
    static bool CurrentArchitectMap(ref Map __result) { __result = architectMap; return false; }
    static bool MaterialLabel(ThingDef stuffDef, ref string __result) { __result = stuffDef.label; return false; }
    static bool CaptureMenu(List<FloatMenuOption> options) { buildOptions = options; return false; }

    static void BuildMaterials(Map map, Building_DiskRack rack, StorageDisk disk, TestHolder holder, Action<bool, string> check)
    {
        var fixture = new Harmony("grechka.storage.architect-menu");
        void Patch(MethodBase method, string name) => fixture.Patch(method, prefix: new HarmonyMethod(typeof(StorageHaulChecks), name));
        var category = new StuffCategoryDef { defName = "ArchitectFixtureLeathery" };
        var pigskin = Fixture.Def("ArchitectFixturePigskin", typeof(ThingWithComps), 75);
        var lightLeather = Fixture.Def("ArchitectFixtureLightLeather", typeof(ThingWithComps), 75);
        foreach (ThingDef leather in new[] { pigskin, lightLeather })
            leather.stuffProps = new StuffProperties { categories = new() { category } };
        var bedroll = new ThingDef { defName = "ArchitectFixtureDoubleBedroll", label = "double bedroll",
            stuffCategories = new() { category }, costStuffCount = 85 };
        var designator = (Designator_Build)RuntimeHelpers.GetUninitializedObject(typeof(Designator_Build));
        Field(designator, "entDef", bedroll);
        var stock = Fixture.Create(pigskin, 186);
        holder.Contents.TryAdd(stock, false);
        disk.Accept(stock, 186);
        var power = rack.GetComp<CompPowerTrader>();
        try
        {
            architectMap = map;
            Patch(AccessTools.PropertyGetter(typeof(Find), nameof(Find.CurrentMap)), nameof(CurrentArchitectMap));
            Patch(AccessTools.Method(typeof(Designator), "CheckCanInteract"), nameof(True));
            Patch(AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.BaseMarketValue)), nameof(Skip));
            Patch(AccessTools.Method(typeof(GenLabel), nameof(GenLabel.ThingLabel), new[] { typeof(BuildableDef), typeof(ThingDef), typeof(int) }), nameof(MaterialLabel));
            Patch(AccessTools.Method(typeof(FloatMenuOption), nameof(FloatMenuOption.SetSizeMode)), nameof(Skip));
            Patch(AccessTools.Constructor(typeof(FloatMenu), new[] { typeof(List<FloatMenuOption>) }), nameof(CaptureMenu));
            Patch(AccessTools.Method(typeof(WindowStack), nameof(WindowStack.Add)), nameof(Skip));
            Patch(AccessTools.Method(typeof(DesignatorManager), nameof(DesignatorManager.Select)), nameof(Skip));
            var ui = (UIRoot_Play)Current.Root.uiRoot;
            ui.windows = (WindowStack)RuntimeHelpers.GetUninitializedObject(typeof(WindowStack));
            ui.mapUI.designatorManager = (DesignatorManager)RuntimeHelpers.GetUninitializedObject(typeof(DesignatorManager));
            designator.ProcessInput(null);
            string tag = "SelectStuff-" + bedroll.defName + "-";
            var selected = buildOptions.Single();
            check(selected.tutorTag == tag + pigskin.defName && stock.ParentHolder == disk && stock.stackCount == 186,
                "Architect offers pigskin for an 85-leather double bedroll when all 186 pigskin are on disk and no leather is loose");
            selected.action();
            check(designator.StuffDefRaw == pigskin, "Selecting the disk leather sets the native build designator's material");
            register(Fixture.Create(lightLeather, 1), rack.Position + IntVec3.South);
            map.resourceCounter.AllCountedAmounts[lightLeather] = 1;
            map.resourceCounter.AllCountedAmounts[pigskin] = 186;
            designator.ProcessInput(null);
            check(buildOptions.Count == 2 && buildOptions.Any(o => o.tutorTag == tag + lightLeather.defName)
                && buildOptions.Any(o => o.tutorTag == tag + pigskin.defName),
                "Loose and disk materials appear once each after the resource counter includes disk stock");
            Field(power, "powerOnInt", false);
            buildOptions = null;
            designator.ProcessInput(null);
            check(buildOptions.Single().tutorTag == tag + lightLeather.defName,
                "Disconnecting the disk removes its material on the next click even with a stale resource count");
        }
        finally
        {
            fixture.UnpatchAll(fixture.Id);
            Field(power, "powerOnInt", true);
            disk.Withdraw(stock, stock.stackCount, holder.Contents);
            architectMap = null;
            buildOptions = null;
        }
    }

    static void Surgery(Map map, Pawn surgeon, Building_DiskRack rack, StorageDisk disk, TestHolder holder,
        Action<MethodBase, string> patch, Action<bool, string> check)
    {
        var implant = Fixture.Def("SurgeryFixtureImplant", typeof(ThingWithComps), 1);
        implant.isTechHediff = true;
        var required = new IngredientCount(); required.SetBaseCount(1); required.filter.SetAllow(implant, true);
        var race = new ThingDef { category = ThingCategory.Pawn, defName = "StorageFixturePatient", race = new RaceProperties() };
        var patient = new Pawn { def = race };
        register(patient, new IntVec3(15, 0, 10));
        var region = Region.MakeNewUnfilled(patient.Position, map);
        region.type = RegionType.Normal;
        map.regionGrid.SetRegionAt(patient.Position, region);
        var recipe = new RecipeDef { defName = "InstallFixtureImplant", recipeUsers = new() { race }, ingredients = new() { required } };
        recipe.fixedIngredientFilter.SetAllow(implant, true);
        var part = Fixture.Create(implant);
        holder.Contents.TryAdd(part, false); disk.Accept(part, 1);
        var power = rack.GetComp<CompPowerTrader>();
        Field(power, "powerOnInt", false);
        check(recipe.PotentiallyMissingIngredients(null, map).Contains(implant), "The operation menu flags a prosthesis on an offline disk as missing");
        Field(power, "powerOnInt", true);
        check(!recipe.PotentiallyMissingIngredients(null, map).Any() && disk.Items.Contains(part),
            "Health menu ingredient checks recognise the installed disk's prosthesis without consuming it");
        recipe.defaultIngredientFilter = recipe.fixedIngredientFilter;
        var bill = new Bill_Medical(recipe, null);
        var chosen = new List<ThingCount>();
        var search = AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients");
        bool FindIngredients() => (bool)search.Invoke(null, new object[] { bill, surgeon, patient, chosen, null });
        patch(AccessTools.Method(typeof(Reachability), nameof(Reachability.CanReach), new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) }), nameof(ReachTarget));
        check(FindIngredients() && chosen.Count == 1 && chosen[0].Thing == part && chosen[0].Count == 1,
            "The native medical bill chooses exactly one real prosthesis from the rack");
        // No terrain or loose cargo at the patient in the headless fixture.
        patch(AccessTools.Method(typeof(WorkGiverUtility), nameof(WorkGiverUtility.HaulStuffOffBillGiverJob)), nameof(Skip));
        JobDefOf.DoBill = new JobDef { defName = "FixtureDoBill", driverClass = typeof(JobDriver_DoBill) };
        var job = WorkGiver_DoBill.TryStartNewDoBillJob(surgeon, bill, patient, chosen, out _);
        var driver = new JobDriver_DoBill { pawn = surgeon, job = job };
        surgeon.jobs.curJob = job; surgeon.jobs.curDriver = driver;
        var steps = JobDriver_DoBill.CollectIngredientsToils(TargetIndex.B, TargetIndex.A, TargetIndex.C, false, true).ToList();
        foreach (var step in steps) step.actor = surgeon;
        check(driver.TryMakePreToilReservations(false), "The surgical job reserves its patient and disk ingredient");
        Step(steps, "ExtractNextTargetFromQueue"); Step(steps, "GotoThing");
        check(pathDestination.Thing == rack, "The doctor's native ingredient pickup routes to the rack");
        Step(steps, "StartCarryThing");
        check(surgeon.carryTracker.CarriedThing == part && !disk.Items.Contains(part),
            "The doctor physically takes the original prosthesis from the disk for the assigned operation");
        surgeon.carryTracker.innerContainer.TryTransferAllToContainer(holder.Contents);
        map.reservationManager.ReleaseAllForTarget(patient);
        surgeon.jobs.curJob = null; surgeon.jobs.curDriver = null;
    }

    static void CraftAndRepair(Map map, Pawn pawn, Building_DiskRack rack, StorageDisk disk, TestHolder holder, Harmony fixture, Action<bool,string> check)
    {
        var recipe = new RecipeDef { defName = "StorageFixtureAdvancedHousing", allowMixingIngredients = true };
        var costs = new[] { 1, 20, 10, 2 };
        var defs = new List<ThingDef>();
        foreach (int count in costs)
        {
            var def = Fixture.Def("HousingIngredient" + count, typeof(ThingWithComps), 75);
            defs.Add(def);
            var i = new IngredientCount(); i.filter.SetAllow(def, true); i.SetBaseCount(count);
            recipe.ingredients.Add(i); recipe.fixedIngredientFilter.SetAllow(def, true);
            var stock = Fixture.Create(def, count + 3); holder.Contents.TryAdd(stock, false); disk.Accept(stock, stock.stackCount);
        }
        var benchDef = new ThingDef { defName = "StorageFixtureBench", category = ThingCategory.Building,
            building = new BuildingProperties(), hasInteractionCell = true, interactionCellOffset = IntVec3.South };
        var bench = new Building_WorkTable { def = benchDef };
        register(bench, new IntVec3(16, 0, 11));
        var region = Region.MakeNewUnfilled(bench.InteractionCell, map); region.type = RegionType.Normal;
        map.regionGrid.SetRegionAt(bench.InteractionCell, region);
        recipe.defaultIngredientFilter = recipe.fixedIngredientFilter;
        check(!recipe.PotentiallyMissingIngredients(pawn, map).Any(), "The workbench menu reports all four housing ingredients available from disk");
        var bill = new Bill_Production(recipe, null); var chosen = new List<ThingCount>();
        var search = AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients");
        check((bool)search.Invoke(null, new object[]{bill,pawn,bench,chosen,null}) && chosen.Count == 4
            && chosen.All(tc => tc.Thing.ParentHolder == disk && tc.Count == costs[defs.IndexOf(tc.Thing.def)]),
            "A production bench selects the four disk ingredients for the housing bill in quantities 1, 20, 10 and 2");
        var craft = WorkGiver_DoBill.TryStartNewDoBillJob(pawn, bill, bench, chosen, out _);
        var driver = new JobDriver_DoBill { pawn = pawn, job = craft }; pawn.jobs.curJob = craft; pawn.jobs.curDriver = driver;
        var steps = JobDriver_DoBill.CollectIngredientsToils(TargetIndex.B, TargetIndex.A, TargetIndex.C, false, true).ToList();
        foreach (var step in steps) step.actor = pawn;
        foreach (var ingredient in chosen)
        {
            Step(steps, "ExtractNextTargetFromQueue"); Step(steps, "GotoThing"); Step(steps, "StartCarryThing");
            check(pathDestination.Thing == rack && pawn.carryTracker.CarriedThing.def == ingredient.Thing.def
                && pawn.carryTracker.CarriedThing.stackCount == ingredient.Count, "Crafter physically collects " + ingredient.Count + " units from the rack");
            pawn.carryTracker.innerContainer.TryTransferAllToContainer(holder.Contents);
        }
        pawn.jobs.curJob = null; pawn.jobs.curDriver = null;

        var dll = Environment.GetEnvironmentVariable("MISC_ROBOTS_ASSEMBLY");
        if (!string.IsNullOrEmpty(dll))
        {
            var robots = Assembly.LoadFrom(dll);
            var helper = robots.GetType("AIRobot.AIRobot_Helper", true);
            var adapter = typeof(StorageDisk).Assembly.GetType("Grechka.Storage.StorageRobotRepair");
            AccessTools.Method(adapter, "Install").Invoke(null, new object[]{fixture});
            var stationType = robots.GetType("AIRobot.X2_Building_AIRobotRechargeStation", true);
            var station = (Building)RuntimeHelpers.GetUninitializedObject(stationType);
            station.def = benchDef; register(station, new IntVec3(18,0,12));
            var jobType = robots.GetType("AIRobot.X2_JobDriver_RepairStationRobot", true);
            DefDatabase<JobDef>.Add(new JobDef { defName = "AIRobot_RepairStationRobot", driverClass = jobType });
            var wanted = new Dictionary<ThingDef,int> { [defs[1]] = 2 };
            var absent = (List<string>)helper.GetMethod("GetStationRepairJobMissingThingStrings").Invoke(null, new object[]{wanted,pawn});
            var repair = (Job)helper.GetMethod("GetStationRepairJob").Invoke(null, new object[]{pawn,station,wanted});
            check(absent.Count == 0 && repair.targetQueueB.Single().Thing.ParentHolder == disk && repair.countQueue.Single() == 2,
                "Installed Misc. Robots enables the repair and queues the exact disk resources");
            var repairDriver = (JobDriver)Activator.CreateInstance(jobType); repairDriver.pawn = pawn; repairDriver.job = repair;
            var robotSteps = Toils(repairDriver);
            Step(robotSteps,"ExtractNextTargetFromQueue");
            var pickup = robotSteps.First(t => t.debugName == "GotoThing");
            check(pickup.endConditions.All(f => f() == JobCondition.Ongoing), "The robot repair driver accepts a held ingredient at pickup");
            pickup.initAction(); Step(robotSteps,"StartCarryThing");
            check(pathDestination.Thing == rack && pawn.carryTracker.CarriedThing?.stackCount == 2,
                "The native robot repair job walks to the rack and picks up two units");
            pawn.carryTracker.innerContainer.TryTransferAllToContainer(holder.Contents);
            pawn.jobs.curJob = null; pawn.jobs.curDriver = null;
        }
        foreach (var item in disk.Items.Where(t => defs.Contains(t.def)).ToList()) disk.Withdraw(item, item.stackCount, holder.Contents);
    }

    static void Wall(Map map, Pawn pawn, ThingDef steel, Thing stock, Action<MethodBase,string> patch, Action<bool,string> check)
    {
        // The fixture map supplies an unobstructed site and updates its thing grid for spawn/despawn.
        // Material costing, task selection, blueprint replacement, delivery, and building completion use game code.
        patch(AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.Spawn),
            new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) }), nameof(Spawn));
        foreach (var type in new[]{typeof(ThingWithComps), typeof(Building), typeof(Frame)})
            if (AccessTools.DeclaredMethod(type, nameof(Thing.Destroy)) is MethodInfo destroy)
                patch(destroy, nameof(DestroyOnMap));
        patch(AccessTools.Method(typeof(ThingMaker), nameof(ThingMaker.MakeThing)), nameof(MakeWithStuff));
        patch(AccessTools.Method(typeof(GenConstruct), nameof(GenConstruct.CanTouchTargetFromValidCell)), nameof(True));
        patch(AccessTools.Method(typeof(GenConstruct), nameof(GenConstruct.CanConstruct),
            new[]{typeof(Thing),typeof(Pawn),typeof(bool),typeof(bool),typeof(JobDef)}), nameof(True));
        patch(AccessTools.Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.WorkIsActive)), nameof(True));
        pawn.workSettings = new Pawn_WorkSettings(pawn);
        var work = WorkTypeDefOf.Construction = new WorkTypeDef { defName = "HaulFixtureConstruction" };
        var wall = Fixture.Def("HaulFixtureWall", typeof(Building), 1); wall.category = ThingCategory.Building;
        wall.building = new BuildingProperties(); wall.costStuffCount = 5; wall.stuffCategories = steel.stuffProps.categories;
        var blueDef = wall.blueprintDef = Fixture.Def("HaulFixtureWallBlueprint", typeof(Blueprint_Build), 1);
        blueDef.category = ThingCategory.Ethereal; blueDef.entityDefToBuild = wall;
        var frameDef = wall.frameDef = Fixture.Def("HaulFixtureWallFrame", typeof(Frame), 1);
        frameDef.building = new BuildingProperties(); frameDef.category = ThingCategory.Building;
        frameDef.entityDefToBuild = wall; frameDef.stuffCategories = wall.stuffCategories; frameDef.building.isEdifice = true;
        var site = new IntVec3(16,0,15);
        var blue = GenConstruct.PlaceBlueprintForBuild(wall, site, map, Rot4.North, pawn.Faction, steel);
        var giver = new WorkGiver_ConstructDeliverResourcesToBlueprints { def = new WorkGiverDef { workType = work } };
        var component = Fixture.Def("WallFixtureComponent", typeof(ThingWithComps), 50);
        wall.costList = new List<ThingDefCountClass> { new ThingDefCountClass(component, 3) };
        var initialCost = blue.TotalMaterialCost();
        check(initialCost.Any(c => c.thingDef == component) && giver.HasJobOnThing(pawn, blue),
            "A blueprint can start accepting disk steel while its second material is still missing");
        unreachableTarget = stock.SpawnedParentOrMe;
        check(blue.ThingCountNeeded(steel) == 5 && giver.HasJobOnThing(pawn, blue),
            "Automatic construction finds materials for a five-steel wall when the map has no loose steel");
        Job haul = giver.JobOnThing(pawn, blue);
        check(haul?.targetA.Thing == stock && haul.count == 5, "The builder requests exactly five steel from the disk");
        var driver = new JobDriver_HaulToContainer { pawn = pawn, job = haul };
        var steps = Toils(driver);
        check(driver.TryMakePreToilReservations(false), "The wall delivery reserves the stored material and the blueprint's incoming quantity");
        Step(steps, "GotoThing");
        check(pathDestination.Thing is Building_StorageController, "The builder walks to the online controller when the rack is unreachable");
        unreachableTarget = null;
        Step(steps, "StartCarryThing");
        Step(steps, "MakeSolidThingFromBlueprintIfNecessary");
        var frame = (Frame)haul.targetB.Thing; Step(steps, "DepositHauledThingInContainer");
        check(blue.Destroyed && frame.Spawned && !frame.IsCompleted() && frame.resourceContainer.TotalStackCountOfDef(steel) == 5
            && stock.stackCount == 65 && pawn.carryTracker.CarriedThing == null, "Five steel move from disk into the wall frame through the native delivery job");
        var remaining = Fixture.Create(component, 31);
        var tempHolder = new TestHolder(20); tempHolder.Contents.TryAdd(remaining, false);
        var sourceDisk = (StorageDisk)stock.ParentHolder;
        sourceDisk.Accept(remaining, 31);
        var frameGiver = new WorkGiver_ConstructDeliverResourcesToFrames { def = giver.def };
        check(frameGiver.HasJobOnThing(pawn, frame) && frameGiver.HasJobOnThing(pawn, frame, true),
            "An existing frame with delivered steel accepts a component-only disk delivery for automatic and forced work");
        var next = frameGiver.JobOnThing(pawn, frame);
        var nextDriver = new JobDriver_HaulToContainer { pawn = pawn, job = next };
        var nextSteps = Toils(nextDriver);
        check(next.count == 3 && nextDriver.TryMakePreToilReservations(false), "The builder reserves only the three remaining components");
        Step(nextSteps, "GotoThing"); Step(nextSteps, "StartCarryThing"); Step(nextSteps, "DepositHauledThingInContainer");
        check(frame.IsCompleted() && remaining.stackCount == 28, "Three components from disk complete the existing frame's material supply");
        sourceDisk.Withdraw(remaining, 28, tempHolder.Contents);
        JobDefOf.FinishFrame = new JobDef { defName = "FinishFrame", driverClass = typeof(JobDriver_ConstructFinishFrame) };
        var finish = new WorkGiver_ConstructFinishFrames().JobOnThing(pawn, frame);
        check(finish?.targetA.Thing == frame && finish.def == JobDefOf.FinishFrame, "The builder receives the finish-wall job for the supplied frame");
        var finishDriver = new JobDriver_ConstructFinishFrame { pawn = pawn, job = finish };
        var buildStep = Toils(finishDriver).Single(t => t.debugName == "MakeNewToils");
        // The harness advances the work tick directly and owns advancement to the next toil.
        patch(AccessTools.Method(typeof(JobDriver), nameof(JobDriver.ReadyForNextToil)), nameof(Skip));
        patch(AccessTools.Method(typeof(Pawn_RotationTracker), nameof(Pawn_RotationTracker.FaceTarget)), nameof(Skip));
        buildStep.tickIntervalAction(60);
        var built = site.GetThingList(map).Single(t => t.def == wall);
        check(built.Spawned && built.Stuff == steel && frame.Destroyed && !frame.resourceContainer.Any && stock.stackCount == 65,
            "Construction consumes the five delivered steel and spawns the finished steel wall on the blueprint cell");
    }
}
