using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.Storage;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

static class CertusChecks
{
    static readonly Faction player = (Faction)RuntimeHelpers.GetUninitializedObject(typeof(Faction));
    static int output, placementLimit = int.MaxValue;
    static bool freeOutput = true;
    static bool Player(ref Faction __result) { __result = player; return false; }
    static bool Dirty() => false;
    static bool OutputCell(ref IntVec3 __3, ref bool __result)
    { __3 = new IntVec3(10, 0, 10); __result = freeOutput; return false; }
    static bool Place(Thing __0, Action<Thing, int> __4, ref bool __result)
    {
        int amount = Math.Min(__0.stackCount, placementLimit);
        output += amount; __4?.Invoke(__0, amount);
        // Host placement is supplied by this fixture; production, pending counts and the
        // callback/reporting path are the actual source code, including partial delivery.
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(__0, (sbyte)0);
        __result = amount == __0.stackCount;
        return false;
    }
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static int Progress(CompCertusSource source) => (int)AccessTools.Field(typeof(CompCertusSource), "progress").GetValue(source);
    static int Harvest(CompCertusSource source) => (int)AccessTools.Method(typeof(CompCertusSource), "Harvest").Invoke(source, null);
    static void SpawnAt(Thing thing, IntVec3 cell, Map map)
    {
        Field(thing, "positionInt", cell); Field(thing, "mapIndexOrState", (sbyte)0);
        Field(thing, "factionInt", player); map.thingGrid.Register(thing);
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("grechka.isolated.certus-growth");
        var maps = (List<Map>)AccessTools.Field(typeof(Game), "maps").GetValue(Current.Game);
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(30, 1, 30) };
        map.cellIndices = new CellIndices(30, 30);
        map.thingGrid = new ThingGrid(map);
        map.mapDrawer = (MapDrawer)RuntimeHelpers.GetUninitializedObject(typeof(MapDrawer));
        maps.Add(map);
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer)), prefix: new HarmonyMethod(typeof(CertusChecks), nameof(Player)));
            foreach (var method in typeof(MapDrawer).GetMethods().Where(m => m.Name == nameof(MapDrawer.MapMeshDirty)))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(CertusChecks), nameof(Dirty)));
            harmony.Patch(AccessTools.Method(typeof(CompSpawner), nameof(CompSpawner.TryFindSpawnCell)), prefix: new HarmonyMethod(typeof(CertusChecks), nameof(OutputCell)));
            var place = typeof(GenPlace).GetMethods().Single(m => m.Name == nameof(GenPlace.TryPlaceThing)
                && m.GetParameters()[4].ParameterType == typeof(Action<Thing, int>));
            harmony.Patch(place, prefix: new HarmonyMethod(typeof(CertusChecks), nameof(Place)));
            var product = Fixture.Def("Grechka_CertusQuartz", typeof(ThingWithComps), 75);
            var sourceDef = Fixture.Def("Grechka_TestCertusSource", typeof(Building), 1, new CompProperties_CertusSource());
            sourceDef.category = ThingCategory.Building;
            sourceDef.building = new BuildingProperties { alwaysUninstallable = true };
            var packedDef = Fixture.Def("Grechka_TestPackedCertus", typeof(MinifiedThing), 1);
            sourceDef.minifiedDef = packedDef;
            var building = (Building)Fixture.Create(sourceDef);
            var source = building.GetComp<CompCertusSource>();
            SpawnAt(building, new IntVec3(15, 0, 15), map);
            for (int i = 0; i < 59; i++) source.CompTickRare();
            check(Progress(source) == 14750 && source.GrowthStage == 3 && !source.ReadyToHarvest,
                "Source grows through its real six-hour cycle without producing loose crystals");
            source.CompTickRare();
            for (int i = 0; i < 20; i++) source.CompTickRare();
            check(source.ReadyToHarvest && source.GrowthStage == 4 && Progress(source) == 0 && output == 0,
                "Ripe fifth stage persists without automatic output or another growth cycle");
            freeOutput = false;
            check(Harvest(source) == 0 && source.ReadyToHarvest, "Blocked harvest space preserves the entire ripe yield");
            freeOutput = true; placementLimit = 2;
            check(Harvest(source) == 2 && source.ReadyToHarvest && source.GrowthStage == 4,
                "Partial harvest preserves the unplaced remainder on the ripe stage");
            placementLimit = int.MaxValue;
            check(Harvest(source) == 2 && !source.ReadyToHarvest && source.GrowthStage == 0 && output == 4,
                "Finishing a reserved harvest produces exactly four crystals and resets to empty");
            check(Harvest(source) == 0 && output == 4, "A second harvest cannot duplicate an already collected yield");

            var accelDef = Fixture.Def("Grechka_CertusAccelerator", typeof(Building), 1,
                new CompProperties_Power { compClass = typeof(CompPowerTrader) });
            accelDef.category = ThingCategory.Building; accelDef.building = new BuildingProperties();
            var accelerators = new List<Building>();
            foreach (var cell in GenAdj.CellsAdjacentCardinal(building))
            {
                var accelerator = (Building)Fixture.Create(accelDef);
                SpawnAt(accelerator, cell, map);
                Field(accelerator.GetComp<CompPowerTrader>(), "powerOnInt", true);
                accelerators.Add(accelerator);
            }
            source.CompTickRare();
            check(Progress(source) == 1250, "Four powered cardinal accelerators supply five times the base growth");
            Field(accelerators[0].GetComp<CompPowerTrader>(), "powerOnInt", false);
            source.CompTickRare();
            check(Progress(source) == 2250, "An unpowered accelerator contributes no growth bonus");
            foreach (var accelerator in accelerators) Field(accelerator.GetComp<CompPowerTrader>(), "powerOnInt", false);
            var diagonal = (Building)Fixture.Create(accelDef);
            SpawnAt(diagonal, new IntVec3(16, 0, 16), map); Field(diagonal.GetComp<CompPowerTrader>(), "powerOnInt", true);
            source.CompTickRare();
            check(Progress(source) == 2500, "Diagonal accelerators do not accelerate crystal growth");

            // Unspawned packing follows the real minification path, retaining the same source.
            map.thingGrid.Deregister(building); Field(building, "mapIndexOrState", (sbyte)-1);
            // This synthetic map has no blueprints/designations. Remove the host-only
            // fixture before vanilla searches the loaded-map list while packing.
            maps.Remove(map);
            MinifiedThing packed = MinifyUtility.MakeMinified(building);
            check(ReferenceEquals(packed.InnerThing, building) && Progress(source) == 2500,
                "Native minification keeps the original source instance and its growth progress");
            source.CompTickRare();
            check(Progress(source) == 2500, "Packed source pauses growth while being transported");
            string path = Path.Combine(Path.GetTempPath(), "grechka-certus-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Scribe.saver.InitSaving(path, "certusFixture"); Scribe_Deep.Look(ref packed, "source"); Scribe.saver.FinalizeSaving();
                MinifiedThing loaded = null;
                Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "source"); Scribe.loader.FinalizeLoading();
                var restored = ((ThingWithComps)loaded.InnerThing).GetComp<CompCertusSource>();
                check(Progress(restored) == 2500 && !restored.ReadyToHarvest && loaded.InnerThing.thingIDNumber == building.thingIDNumber,
                    "Real Scribe restores a transported source's identity and in-progress cycle");
                AccessTools.Method(typeof(CompCertusSource), "Mature").Invoke(restored, null);
                Scribe.saver.InitSaving(path, "certusFixture"); Scribe_Deep.Look(ref loaded, "source"); Scribe.saver.FinalizeSaving();
                MinifiedThing ripe = null;
                Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref ripe, "source"); Scribe.loader.FinalizeLoading();
                var ripeComp = ((ThingWithComps)ripe.InnerThing).GetComp<CompCertusSource>();
                check(ripeComp.ReadyToHarvest && ripeComp.GrowthStage == 4,
                    "Ripe crystals and their fifth frame survive packing and real save/load");
            }
            finally { if (File.Exists(path)) File.Delete(path); }

            var jobDef = new JobDef { defName = "Grechka_HarvestCertus", driverClass = typeof(JobDriver_HarvestCertus) };
            DefDatabase<JobDef>.Add(jobDef);
            Job job = new WorkGiver_HarvestCertus().JobOnThing(null, building);
            check(job.def == jobDef && job.targetA.Thing == building && job.def.driverClass == typeof(JobDriver_HarvestCertus),
                "Harvest work creates the registered pawn job targeting the actual source");
        }
        finally { harmony.UnpatchAll(harmony.Id); maps.Remove(map); }
    }
}
