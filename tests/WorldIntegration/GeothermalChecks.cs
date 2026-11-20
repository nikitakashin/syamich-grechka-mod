using System.Runtime.CompilerServices;
using Grechka.WorldIntegration;
using HarmonyLib;
using RimWorld;
using Verse;

internal static class GeothermalChecks
{
    internal static void Run()
    {
        void Check(bool ok, string label)
        { if (!ok) throw new Exception(label); Console.WriteLine("PASS geothermal: " + label); }
        // ListerThings consults DLC flags; keep its native ModsConfig initialization isolated.
        var data = Directory.CreateTempSubdirectory("grechka-geothermal-native-");
        File.WriteAllText(Path.Combine(data.CreateSubdirectory("Config").FullName, "ModsConfig.xml"),
            "<ModsConfigData><version>1.7.4998</version><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>");
        AccessTools.Field(typeof(GenFilePaths), "saveDataPath").SetValue(null, data.FullName);
        AccessTools.Field(typeof(GenFilePaths), "modsFolderPath").SetValue(null, data.CreateSubdirectory("Mods").FullName);
        var official = data.CreateSubdirectory("Data");
        File.WriteAllText(Path.Combine(official.CreateSubdirectory("Core/About").FullName, "About.xml"),
            "<ModMetaData><name>Core</name><author>Ludeon Studios</author><packageId>ludeon.rimworld</packageId><supportedVersions><li>1.7</li></supportedVersions></ModMetaData>");
        AccessTools.Field(typeof(GenFilePaths), "officialModsFolderPath").SetValue(null, official.FullName);
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(60, 1, 60) };
        map.cellIndices = new CellIndices(60, 60);
        map.thingGrid = new ThingGrid(map);
        map.listerThings = new ListerThings(ListerThingsUse.Global);
        var previous = ThingDefOf.SteamGeyser;
        var geyserDef = new ThingDef { defName = "SteamGeyser", size = new IntVec2(2, 2), category = ThingCategory.Building,
            thingClass = typeof(Building_SteamGeyser), building = new BuildingProperties() };
        ThingDefOf.SteamGeyser = geyserDef;
        var generator = new ThingDef { defName = "GeothermalGenerator", size = new IntVec2(6, 6),
            placeWorkers = new() { typeof(PlaceWorker_OnSteamGeyser) } };
        var cell = new IntVec3(30, 0, 30);
        var geyser = new Thing { def = geyserDef, thingIDNumber = 2701 };
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(geyser, cell);
        var native = new PlaceWorker_OnSteamGeyser();
        var patch = new Harmony("grechka.geothermal.native-check");
        try
        {
            patch.CreateClassProcessor(typeof(GeothermalPlacementDiagnostics)).Patch();
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(Designator_Build), nameof(Designator_Build.CanDesignateCell)))
                .Postfixes.Any(p => p.owner == patch.Id), "Diagnostic postfix binds to the installed CanDesignateCell signature");
            Check(!native.AllowsPlacing(generator, cell, Rot4.North, map).Accepted, "Native placement rejects a cell without a geyser");
            Check(!GeothermalPlacementDiagnostics.TryBuildReport(generator, map, cell, Rot4.North, false, out _),
                "Ordinary off-geyser red previews produce no diagnostic report");
            map.thingGrid.Register(geyser); map.listerThings.Add(geyser);
            Check(Enumerable.Range(0, 100).All(_ => native.AllowsPlacing(generator, cell, Rot4.North, map).Accepted),
                "Native ordinary-generator geyser check succeeds on 100 repeated previews without reload");
            Check(!native.AllowsPlacing(generator, cell + new IntVec3(1, 0, 0), Rot4.North, map).Accepted,
                "Native placement requires the exact geyser center even within its 2x2 footprint");
            Check(native.ForceAllowPlaceOver(geyserDef) && !native.ForceAllowPlaceOver(generator),
                "Native geyser overlap permission remains narrow");
            AcceptanceReport failure = "fixture rejection";
            Check(GeothermalPlacementDiagnostics.TryBuildReport(generator, map, cell, Rot4.North, failure, out string report)
                && report.Contains("reason=fixture rejection") && report.Contains("gridGeyser=SteamGeyser2701")
                && report.Contains("listedGeyser=SteamGeyser2701") && report.Contains(nameof(PlaceWorker_OnSteamGeyser))
                && !failure.Accepted && failure.Reason == "fixture rejection",
                "A rejected centered preview records reason, both geyser indexes and workers without changing acceptance");
            Check(!GeothermalPlacementDiagnostics.TryBuildReport(generator, map, cell, Rot4.North, true, out _),
                "Successful placement produces no failure report");
            // Deliberately inconsistent synthetic indexes test capture, not the cause of the user's incident.
            foreach (IntVec3 c in GenAdj.OccupiedRect(cell, Rot4.North, geyserDef.size).Cells)
                map.thingGrid.ThingsListAt(c).Remove(geyser);
            Check(!native.AllowsPlacing(generator, cell, Rot4.North, map).Accepted
                && GeothermalPlacementDiagnostics.TryBuildReport(generator, map, cell, Rot4.North, false, out report)
                && report.Contains("gridGeyser=none") && report.Contains("listedGeyser=SteamGeyser2701"),
                "Diagnostic capture detects a synthetic ThingGrid/ListerThings mismatch");
            foreach (IntVec3 c in GenAdj.OccupiedRect(cell, Rot4.North, geyserDef.size).Cells)
                map.thingGrid.ThingsListAt(c).Add(geyser);
            Check(native.AllowsPlacing(generator, cell, Rot4.North, map).Accepted,
                "Native checker observes current index contents without keeping a stale rejection");
        }
        finally { ThingDefOf.SteamGeyser = previous; patch.UnpatchAll(patch.Id); }
    }
}
