using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.Storage;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

static class StorageAccessChecks
{
    static readonly Faction player = new Faction { def = new FactionDef { defName = "StorageAccessPlayer", isPlayer = true } };
    static StoragePriority sourcePriority = StoragePriority.Low;
    static bool blocked;
    static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    static bool Player(ref Faction __result) { __result = player; return false; }
    static bool False(ref bool __result) { __result = false; return false; }
    static bool Reach(ref bool __result) { __result = !blocked; return false; }
    static bool Priority(ref StoragePriority __result) { __result = sourcePriority; return false; }
    static bool CarrySpace(ref int __result) { __result = 75; return false; }
    static bool CanHaul(ref bool __result) { __result = true; return false; }
    static bool Skip() => false;

    internal static void Run(Action<bool,string> check, string repo)
    {
        var harmony = new Harmony("grechka.storage.external-boundaries");
        var maps = Current.Game.Maps.ToList();
        Current.Game.storyteller=(Storyteller)RuntimeHelpers.GetUninitializedObject(typeof(Storyteller)); Current.Game.storyteller.difficulty=new Difficulty();
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(30,1,30) };
        map.cellIndices = new CellIndices(30,30); map.fogGrid = new FogGrid(map); map.thingGrid = new ThingGrid(map);
        map.listerBuildings = new ListerBuildings(); map.listerThings = new ListerThings(ListerThingsUse.Global);
        Set(map.listerThings,"listsByDef",new Dictionary<ThingDef,List<Thing>>(ReferenceEqualityComparer.Instance));
        map.reservationManager = new ReservationManager(map);
        map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
        map.reachability = (Reachability)RuntimeHelpers.GetUninitializedObject(typeof(Reachability));
        map.designationManager = (DesignationManager)RuntimeHelpers.GetUninitializedObject(typeof(DesignationManager)); map.itemAvailability = new ItemAvailability(map); map.listerHaulables = new ListerHaulables(map); map.haulDestinationManager = new HaulDestinationManager(map); map.resourceCounter = new ResourceCounter(map);
        var manager = new SignalNetworkMap(map);
        map.components = new List<MapComponent> { manager };
        StatDefOf.MaxHitPoints=new StatDef{defName="StorageAccessMaxHP",defaultBaseValue=100,workerClass=typeof(StatWorker)};
        Current.Game.Maps.Clear(); Current.Game.Maps.Add(map);
        void Put(Thing thing, IntVec3 cell)
        {
            Set(thing,"positionInt",cell); Set(thing,"mapIndexOrState",(sbyte)0); Set(thing,"factionInt",player);
            map.thingGrid.Register(thing);
            // Supply only the lister indexes; the standalone host has no DLC/graphics bootstrap.
            var byDef=(Dictionary<ThingDef,List<Thing>>)AccessTools.Field(typeof(ListerThings),"listsByDef").GetValue(map.listerThings);
            if(!byDef.TryGetValue(thing.def,out var list))byDef[thing.def]=list=new(); list.Add(thing);
            var groups=(List<Thing>[])AccessTools.Field(typeof(ListerThings),"listsByGroup").GetValue(map.listerThings);
            foreach(var group in new[]{ThingRequestGroup.Everything, thing is Building ? ThingRequestGroup.BuildingArtificial : ThingRequestGroup.HaulableEver})
            { groups[(int)group] ??= new(); groups[(int)group].Add(thing); }
            if (thing is Building building) map.listerBuildings.allBuildingsColonist.Add(building);
        }
        try
        {
            harmony.Patch(AccessTools.Method(typeof(Building_DiskRack),"RefreshDiskGraphic"),prefix:new HarmonyMethod(typeof(StorageAccessChecks),nameof(Skip)));
            harmony.Patch(AccessTools.Method(typeof(DesignationManager),nameof(DesignationManager.RemoveAllDesignationsOn)),prefix:new HarmonyMethod(typeof(StorageAccessChecks),nameof(Skip)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Faction),nameof(Faction.OfPlayer)),prefix:new HarmonyMethod(typeof(StorageAccessChecks),nameof(Player)));
            harmony.Patch(AccessTools.Method(typeof(ForbidUtility),nameof(ForbidUtility.IsForbidden),new[]{typeof(Thing),typeof(Pawn)}),prefix:new HarmonyMethod(typeof(StorageAccessChecks),nameof(False)));
            harmony.Patch(AccessTools.Method(typeof(FireUtility),nameof(FireUtility.IsBurning),new[]{typeof(Thing)}),prefix:new HarmonyMethod(typeof(StorageAccessChecks),nameof(False)));
            harmony.Patch(AccessTools.Method(typeof(ReservationUtility),nameof(ReservationUtility.CanReserveAndReach)),prefix:new HarmonyMethod(typeof(StorageAccessChecks),nameof(Reach)));
            harmony.Patch(AccessTools.Method(typeof(StoreUtility),nameof(StoreUtility.CurrentStoragePriorityOf)),prefix:new HarmonyMethod(typeof(StorageAccessChecks),nameof(Priority)));
            harmony.Patch(AccessTools.Method(typeof(Pawn_CarryTracker),nameof(Pawn_CarryTracker.AvailableStackSpace)),prefix:new HarmonyMethod(typeof(StorageAccessChecks),nameof(CarrySpace)));
            harmony.Patch(AccessTools.Method(typeof(HaulAIUtility),nameof(HaulAIUtility.PawnCanAutomaticallyHaulFast)),prefix:new HarmonyMethod(typeof(StorageAccessChecks),nameof(CanHaul)));
            var def = Fixture.Def("StorageAccessController",typeof(Building_StorageController),1,new CompProperties_SignalNode{controller=true});
            def.category=ThingCategory.Building; def.building=new BuildingProperties();
            var controller=(Building_StorageController)Fixture.Create(def); Put(controller,new IntVec3(10,0,10));
            manager.Register(controller.Node); Set(controller.Node,"manager",manager);
            var second=(Building_StorageController)Fixture.Create(def); Put(second,new IntVec3(11,0,10));
            manager.Register(second.Node); Set(second.Node,"manager",manager);
            var rackDef=Fixture.Def("StorageAccessRack",typeof(Building_DiskRack),1,new CompProperties_Power{compClass=typeof(CompPowerTrader)});
            rackDef.category=ThingCategory.Building; rackDef.building=new BuildingProperties();
            var rack=(Building_DiskRack)Fixture.Create(rackDef); Put(rack,new IntVec3(10,0,11)); Set(rack.GetComp<CompPowerTrader>(),"powerOnInt",true);
            var holder=new TestHolder(20);
            var disk=(StorageDisk)Fixture.Create(Fixture.DiskDef("StorageAccessDisk",65536,false));
            holder.Contents.TryAdd(disk,false); rack.Install(disk);
            // Native topology sees the rack through its adjacent cable ports.
            check(controller.ActiveDisks().Contains(disk) && second.ActiveDisks().Contains(disk),"Both connected controllers resolve the same installed disk through the real signal graph");
            var steelDef=Fixture.Def("StorageAccessSteel",typeof(ThingWithComps),75); steelDef.resourceReadoutPriority=ResourceCountPriority.Middle; steelDef.alwaysHaulable=true;
            Thing stock=Fixture.Create(steelDef,1000); holder.Contents.TryAdd(stock,false); disk.Accept(stock,1000);
            var access=typeof(StorageDisk).Assembly.GetType("Grechka.Storage.StorageMapAccess");
            var available=AccessTools.Method(access,"Available");
            check(((IEnumerable<Thing>)available.Invoke(null,new object[]{map})).Count(t=>t==stock)==1,"Shared network stock appears exactly once across multiple controllers");
            check(HaulAIUtility.IsInHaulableInventory(stock) && stock.PositionHeld==rack.Position && stock.MapHeld==map && disk.HaulSourceEnabled,
                "Native hauling recognises the real disk owner and resolves pickup to the connected rack");
            var hasDiskType = AccessTools.Method(typeof(Building_StorageController), "HasDiskType");
            bool Has(bool frozen) => (bool)hasDiskType.Invoke(controller, new object[] { frozen });
            check(Has(false) && !Has(true), "Only the regular storage tab is available with a regular disk");
            var freezer = (StorageDisk)Fixture.Create(Fixture.DiskDef("StorageAccessFreezer", 65536, true));
            holder.Contents.TryAdd(freezer, false);
            rack.Install(freezer);
            var meal = DefDatabase<ThingDef>.GetNamed("Grechka_TestMeal");
            var armor = DefDatabase<ThingDef>.GetNamed("Grechka_TestArmor");
            var regular = controller.SettingsFor(false);
            var frozen = controller.SettingsFor(true);
            check(Has(false) && Has(true) && regular.filter.Allows(steelDef) && !regular.filter.Allows(meal)
                && frozen.filter.Allows(meal) && !frozen.filter.Allows(steelDef)
                && !frozen.filter.Allows(armor) && !regular.filter.Allows(armor),
                "Both tabs follow installed disks; default filters separate stackable perishables and durable goods and exclude singletons");
            controller.GetStoreSettings().Priority=StoragePriority.Critical;
            controller.GetStoreSettings().filter.SetDisallowAll(); controller.GetStoreSettings().filter.SetAllow(steelDef,true);
            var pawn=new Pawn { def=new ThingDef{defName="StorageAccessPawn",race=new RaceProperties{intelligence=Intelligence.Humanlike}} };
            pawn.carryTracker=new Pawn_CarryTracker(pawn); Put(pawn,new IntVec3(5,0,5));
            var integration = Assembly.LoadFrom(Path.Combine(repo, "mods", "GrechkaWorldIntegration", "Assemblies", "GrechkaWorldIntegration.dll"));
            var upgradeWork = integration.GetType("Grechka.WorldIntegration.WorkGiver_FactoryUpgrade", true);
            harmony.CreateClassProcessor(typeof(StorageDisk).Assembly.GetType("Grechka.Storage.StorageUpgradeMaterialsPatch", true)).Patch();
            var findMaterials = AccessTools.Method(upgradeWork, "FindMaterials");
            var reach = AccessTools.Method(typeof(ReachabilityUtility), nameof(ReachabilityUtility.CanReach),
                new[] { typeof(Pawn), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(Danger), typeof(bool), typeof(bool), typeof(TraverseMode) });
            harmony.Patch(reach, prefix: new HarmonyMethod(typeof(StorageAccessChecks), nameof(Reach)));
            try
            {
                check(findMaterials.Invoke(null, new object[] { pawn, steelDef, Danger.Deadly, false }) == stock,
                    "Factory upgrade delivery can find the original material stack in the disk with an empty map stockpile");
            }
            finally { harmony.Unpatch(reach, HarmonyPatchType.Prefix, harmony.Id); }
            check(disk.Space.FillFraction == (float)disk.Space.UsedBytes / disk.Capacity,
                "The disk progress bar measures occupied bytes including item-type overhead");
            var jobDef=new JobDef {defName="Grechka_ImportStorage",driverClass=typeof(JobDriver_ImportStorage)};
            DefDatabase<JobDef>.Add(jobDef);
            Thing loose=Fixture.Create(steelDef,100); Put(loose,new IntVec3(7,0,7));
            var auto=AccessTools.Method(typeof(Building_StorageController),"AutomaticImportJob");
            var job=(Job)auto.Invoke(controller,new object[]{pawn});
            check(job?.targetA.Thing==loose && job.targetB.Thing==controller && job.count==75,"Critical Storage requests a carry-sized transfer from a lower-priority stockpile");
            sourcePriority=StoragePriority.Critical;
            check(auto.Invoke(controller,new object[]{pawn})==null,"Equal storage priorities do not shuffle the same stack repeatedly");
            sourcePriority=StoragePriority.Low;
            controller.GetStoreSettings().filter.SetAllow(steelDef,false);
            check(auto.Invoke(controller,new object[]{pawn})==null,"The native storage filter excludes an otherwise haulable resource");
            controller.GetStoreSettings().filter.SetAllow(steelDef,true); blocked=true;
            check(auto.Invoke(controller,new object[]{pawn})==null,"An inaccessible stack cannot generate an automatic import job"); blocked=false;
            var worker=new WorkGiver_StorageHaul();
            check(worker.PotentialWorkThingsGlobal(pawn).Contains(controller) && worker.PotentialWorkThingsGlobal(pawn).Contains(second),"Storage hauling scans controller classes, including tiered definitions");
            var preferred = AccessTools.Method(typeof(Building_StorageController), "PreferredDisk");
            StorageDisk Target(Thing item) => (StorageDisk)preferred.Invoke(controller, new object[] { item, controller.ActiveDisks() });
            var food = Fixture.Create(meal, 8);
            check(Target(food) == freezer && Target(loose) == disk && job.targetC.Thing == disk,
                "Automatic hauling binds each job to a disk permitted by its own filter");
            frozen.filter.SetAllow(steelDef, true);
            frozen.Priority = StoragePriority.Critical;
            regular.Priority = StoragePriority.Important;
            check(Target(loose) == freezer && regular.Priority == StoragePriority.Important,
                "An explicitly enabled item follows the higher of two independent disk priorities");
            frozen.filter.SetAllow(steelDef, false);
            regular.Priority = StoragePriority.Critical;
            holder.Contents.TryAdd(food, false); freezer.Accept(food, 8);
            var moreFood = Fixture.Create(meal, 5); holder.Contents.TryAdd(moreFood, false); freezer.Accept(moreFood, 5);
            freezer.IntakePaused = true;
            check(Target(food) == null && Target(loose) == disk && freezer.Acceptable(food) == 0 && freezer.HaulSourceEnabled,
                "Unloading disables intake for this disk only and leaves withdrawal enabled");
            DefDatabase<JobDef>.Add(new JobDef { defName = "Grechka_UnloadDisk", driverClass = typeof(JobDriver_UnloadDisk) });
            var takeLoad = AccessTools.Method(typeof(JobDriver_UnloadDisk), "TakeLoad");
            int Take(Job load) => (int)takeLoad.Invoke(null, new object[] { pawn, freezer, rack, load.targetB.Thing, load.count });
            int unloaded = 0, trips = 0;
            while (freezer.Items.Any() && trips < 4)
            {
                Job load = worker.JobOnThing(pawn, rack);
                if (load?.targetC.Thing != freezer || load.def.defName != "Grechka_UnloadDisk") break;
                if (trips == 0)
                {
                    freezer.IntakePaused = false;
                    check(Take(load) == 0 && freezer.Space.ItemCount == 13, "Cancelling a pending unload prevents pickup at the rack");
                    freezer.IntakePaused = true;
                }
                unloaded += Take(load);
                var carried = pawn.carryTracker.CarriedThing;
                if (carried == null) break;
                pawn.carryTracker.innerContainer.TryTransferToContainer(carried, holder.Contents, false);
                trips++;
            }
            check(unloaded == 13 && trips == 2 && !freezer.Items.Any() && worker.JobOnThing(pawn, rack) == null
                && freezer.IntakePaused && freezer.Accept(food, 1) == 0 && Target(food) == null,
                "Haulers empty the selected disk over successive jobs without refilling it after the last trip");
            freezer.IntakePaused = false;
            check(Target(food) == freezer && freezer.Accept(food, 1) == 1 && frozen.filter.Allows(meal) && !frozen.filter.Allows(steelDef),
                "Resuming intake reuses the same freezer settings and accepts new items");
            rack.Remove(freezer, holder.Contents);
            check(!Has(true) && Has(false), "Removing the last freezer disk hides only the freezer tab");
            JobDefOf.HaulToContainer=new JobDef{defName="HaulToContainer",driverClass=typeof(JobDriver_HaulToContainer)};
            check(map.itemAvailability.ThingsAvailableAnywhere(steelDef,1100,pawn) && !map.itemAvailability.ThingsAvailableAnywhere(steelDef,1101,pawn),"Construction availability adds digital and physical stock without inventing missing materials");
            StorageTradeChecks.Run(map, pawn, rack, disk, holder, Put, check);
            StorageHaulChecks.Run(map, pawn, rack, disk, holder, Put, check);
            var split=stock.SplitOff(30); holder.Contents.TryAdd(split,false);
            check(split.stackCount==30 && disk.Space.ItemCount==970 && stock.stackCount==970,"Native partial pickup updates disk capacity immediately and preserves the exact total");
            disk.SealedContents=true;
            check(!((IEnumerable<Thing>)available.Invoke(null,new object[]{map})).Any() && !disk.HaulSourceEnabled,"Sealed disks cannot expose their cargo through construction or trade");
            disk.SealedContents=false; manager.Unregister(controller.Node); manager.Unregister(second.Node);
            check(!((IEnumerable<Thing>)available.Invoke(null,new object[]{map})).Any() && !disk.HaulSourceEnabled,"Disconnected disks stop advertising resources to native consumers");
            check(pawn.carryTracker.TryStartCarry(stock,10)==0 && stock.stackCount==970,"A queued native pickup cannot withdraw after the signal network disconnects");
            string path=Path.Combine(Path.GetTempPath(),"storage-settings-"+Guid.NewGuid()+".xml");
            try
            {
                var saved = (Building_StorageController)Fixture.Create(controller.def);
                saved.SettingsFor(false).CopyFrom(controller.SettingsFor(false));
                saved.SettingsFor(true).filter.SetAllow(armor, true);
                saved.SettingsFor(true).Priority = StoragePriority.Low;
                Scribe.saver.InitSaving(path, "fixture"); Scribe_Deep.Look(ref saved, "controller"); Scribe.saver.FinalizeSaving();
                Building_StorageController restored = null;
                Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref restored, "controller"); Scribe.loader.FinalizeLoading();
                check(restored.SettingsFor(false).Priority == StoragePriority.Critical && restored.SettingsFor(false).filter.Allows(steelDef)
                    && restored.SettingsFor(true).Priority == StoragePriority.Low && restored.SettingsFor(true).filter.Allows(armor),
                    "Scribe restores both tab priorities and explicit single-item overrides independently");
                var old = System.Xml.Linq.XDocument.Load(path);
                var node = old.Root.Element("controller");
                node.Element("grechkaRegularStorageSettings").Name = "grechkaStorageSettings";
                node.Element("grechkaFrozenStorageSettings").Remove();
                old.Save(path);
                restored = null;
                Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref restored, "controller"); Scribe.loader.FinalizeLoading();
                check(restored.SettingsFor(false).Priority == StoragePriority.Critical && restored.SettingsFor(true).Priority == StoragePriority.Critical
                    && restored.SettingsFor(false).filter.Allows(steelDef) && !restored.SettingsFor(true).filter.Allows(steelDef)
                    && !restored.SettingsFor(true).filter.Allows(meal),
                    "An old single-tab save splits by disk type while retaining the old priority and exclusions");
            }
            finally { if(File.Exists(path))File.Delete(path); }
        }
        finally { harmony.UnpatchAll(harmony.Id); Current.Game.Maps.Clear(); Current.Game.Maps.AddRange(maps); }
    }
}
