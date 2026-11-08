using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.Storage;
using HarmonyLib;
using RimWorld;
using Verse;

static class MeteorChecks
{
    static Map host;
    static readonly List<Thing> spawned = new();
    static readonly HashSet<Thing> cleared = new();
    static readonly Faction player = new Faction { def = new FactionDef { defName = "MeteorFixturePlayer", isPlayer = true } };
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static bool Player(ref Faction __result) { __result = player; return false; }
    static bool Spawn(Thing __0, IntVec3 __1, Map __2, Rot4 __3, ref Thing __result)
    {
        if (__2 != host) return true;
        Put(__0, __1); spawned.Add(__0); __result = __0; return false;
    }
    static bool Despawn(Thing __instance)
    {
        if (!__instance.Spawned || __instance.Map != host) return true;
        host.thingGrid.Deregister(__instance); Field(__instance, "mapIndexOrState", (sbyte)-1); return false;
    }
    static bool Destroy(Thing __instance)
    {
        if (!__instance.Spawned || __instance.Map != host) return true;
        host.thingGrid.Deregister(__instance); Field(__instance, "mapIndexOrState", (sbyte)-2);
        cleared.Add(__instance); return false;
    }
    static bool Standable(IntVec3 __0, Map __1, ref bool __result)
    {
        if (__1 != host) return true;
        __result = !__0.GetThingList(host).Any(t => t is Plant || t.def.category == ThingCategory.Building);
        return false;
    }
    static bool Message(string __0)
    {
        if (!__0.StartsWith("[GrechkaStorage] Certus meteorite generated:")) return true;
        Console.WriteLine(__0); return false;
    }
    static void Put(Thing thing, IntVec3 cell)
    {
        Field(thing, "positionInt", cell); Field(thing, "mapIndexOrState", (sbyte)0);
        host.thingGrid.Register(thing);
    }
    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("grechka.isolated.certus-impact");
        var maps = (List<Map>)AccessTools.Field(typeof(Game), "maps").GetValue(Current.Game);
        host = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        host.info = new MapInfo { Size = new IntVec3(80, 1, 80) };
        host.cellIndices = new CellIndices(80, 80);
        host.thingGrid = new ThingGrid(host); host.roofGrid = new RoofGrid(host);
        host.terrainGrid = (TerrainGrid)RuntimeHelpers.GetUninitializedObject(typeof(TerrainGrid));
        Field(host.terrainGrid, "map", host);
        var terrain = new TerrainDef { defName = "MeteorFixtureSoil", passability = Traversability.Standable };
        Field(host.terrainGrid, "topGrid", Enumerable.Repeat(terrain, 6400).ToArray());
        foreach (string grid in new[] { "tempGrid", "underGrid", "foundationGrid" })
            Field(host.terrainGrid, grid, new TerrainDef[6400]);
        maps.Add(host);
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer)), prefix: new HarmonyMethod(typeof(MeteorChecks), nameof(Player)));
            var spawn = typeof(GenSpawn).GetMethods().Single(m => m.Name == nameof(GenSpawn.Spawn)
                && m.GetParameters().Length == 7 && m.GetParameters()[0].ParameterType == typeof(Thing));
            harmony.Patch(spawn, prefix: new HarmonyMethod(typeof(MeteorChecks), nameof(Spawn)));
            harmony.Patch(AccessTools.Method(typeof(GenGrid), nameof(GenGrid.Standable)), prefix: new HarmonyMethod(typeof(MeteorChecks), nameof(Standable)));
            harmony.Patch(AccessTools.Method(typeof(Log), nameof(Log.Message), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(MeteorChecks), nameof(Message)));
            foreach (var method in new[] { typeof(Thing), typeof(ThingWithComps), typeof(Building), typeof(Mineable), typeof(Plant) }
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(m => m.Name == nameof(Thing.DeSpawn) && m.GetMethodBody() != null))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(MeteorChecks), nameof(Despawn)));
            foreach (var method in new[] { typeof(Thing), typeof(ThingWithComps), typeof(Building), typeof(Mineable), typeof(Plant) }
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(m => m.Name == nameof(Thing.Destroy) && m.GetMethodBody() != null))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(MeteorChecks), nameof(Destroy)));

            var stone = Fixture.Def("MeteorFixtureGranite", typeof(Mineable), 1);
            stone.category = ThingCategory.Building; stone.building = new BuildingProperties { isNaturalRock = true };
            var ore = Fixture.Def("Grechka_MineableCertus", typeof(Mineable), 1);
            ore.category = ThingCategory.Building; ore.building = new BuildingProperties { isNaturalRock = true, isResourceRock = true };
            var sourceDef = Fixture.Def("Grechka_CertusSource", typeof(Building), 1, new CompProperties_CertusSource());
            sourceDef.category = ThingCategory.Building; sourceDef.building = new BuildingProperties();
            var tree = Fixture.Def("MeteorFixtureTree", typeof(Plant), 1); tree.category = ThingCategory.Plant;
            var trees = new List<Thing>();
            for (int z = 2; z < 78; z += 2)
                for (int x = 2; x < 78; x += 2)
                { var thing = Fixture.Create(tree); Put(thing, new IntVec3(x, 0, z)); trees.Add(thing); }
            for (int z = 7; z < 78; z += 7)
                for (int x = 7; x < 78; x += 7)
                    if (!new IntVec3(x, 0, z).GetThingList(host).Any()) Put(Fixture.Create(stone), new IntVec3(x, 0, z));
            var chunkDef = Fixture.Def("MeteorFixtureLooseChunk", typeof(ThingWithComps), 75);
            Thing chunk = Fixture.Create(chunkDef, 13); Put(chunk, new IntVec3(40, 0, 40));
            var protectedDef = Fixture.Def("MeteorFixtureWall", typeof(Building), 1);
            protectedDef.category = ThingCategory.Building; protectedDef.building = new BuildingProperties();
            Thing wall = Fixture.Create(protectedDef); Put(wall, new IntVec3(15, 0, 15));
            var generator = typeof(CompCertusSource).Assembly.GetType("Grechka.Storage.CertusMeteorGenerator");
            var classify = AccessTools.Method(generator, "Classify");
            check((int)classify.Invoke(null, new object[] { host, new IntVec3(40, 0, 40) }) == 0,
                "Native impact classification accepts a tree and loose loot on the same cell");
            check((int)classify.Invoke(null, new object[] { host, new IntVec3(7, 0, 7) }) == 0,
                "Native impact classification accepts ordinary natural rock instead of requiring standable ground");
            check((int)classify.Invoke(null, new object[] { host, wall.Position }) == -1,
                "Native impact classification protects constructed buildings");
            AccessTools.Method(generator, "Generate").Invoke(null, new object[] { host, stone });
            var rocks = spawned.Where(t => t.def == stone || t.def == ore).ToList();
            var sources = spawned.Where(t => t.def == sourceDef).Cast<ThingWithComps>().ToList();
            check(rocks.Count >= 170 && rocks.Count <= 280 && sources.Count == 3,
                "Actual impact generator creates the full cracked meteor and three sources on a forested rocky map");
            check(Math.Abs(rocks.Count(t => t.def == ore) / (double)rocks.Count - 0.3) < 0.003
                && sources.All(t => t.GetComp<CompCertusSource>().ReadyToHarvest),
                "Actual spawn sequence keeps the 70/30 mixture and makes every central source mature");
            check(chunk.Spawned && chunk.stackCount == 13 && chunk.thingIDNumber > 0
                && chunk.Position != new IntVec3(40, 0, 40) && !cleared.Contains(chunk),
                "Impact relocation preserves the same loose item instance and quantity outside the meteor");
            check(wall.Spawned && !cleared.Contains(wall) && cleared.Any(t => t is Plant)
                && trees.Any(t => t.Spawned), "Impact preparation clears only the natural footprint and preserves surroundings");
        }
        finally { harmony.UnpatchAll(harmony.Id); maps.Remove(host); host = null; }
    }
}
