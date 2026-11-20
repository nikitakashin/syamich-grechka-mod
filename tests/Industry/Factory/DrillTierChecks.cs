using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Grechka.WorldIntegration;
using HarmonyLib;
using PipeSystem;
using RimWorld;
using Verse;
using Process = PipeSystem.Process;

internal static class DrillTierChecks
{
    private static bool acceptPlacement;
    private static int delivered;
    private static int nextId = 92300;

    internal static void Run()
    {
        var product = new ThingDef
        {
            defName = "GrechkaDrillTierTestSteel", label = "steel", category = ThingCategory.Item,
            thingClass = typeof(ThingWithComps), stackLimit = 750, comps = new()
        };
        DefDatabase<ThingDef>.Add(product);
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(10, 1, 10) };
        map.cellIndices = new CellIndices(map);
        map.thingGrid = new ThingGrid(map);
        var harmony = new Harmony("grechka.test.factory-tier.placement");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(Process), "TrySpawnAtCell"),
                transpiler: new HarmonyMethod(typeof(DrillTierChecks), nameof(PlacementBoundary)));
            harmony.Patch(AccessTools.Method(typeof(ThingMaker), nameof(ThingMaker.MakeThing)),
                prefix: new HarmonyMethod(typeof(DrillTierChecks), nameof(MakeThing)));
            var placement = AccessTools.Method(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
                new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Thing).MakeByRefType(),
                    typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int) });
            harmony.Patch(placement, prefix: new HarmonyMethod(typeof(DrillTierChecks), nameof(Place)));

            foreach (int tier in new[] { 2, 3, 4, 5 })
            {
                var definition = new ThingDef
                {
                    defName = "GrechkaDrillTierTestT" + tier,
                    comps = new() { new CompProperties { compClass = typeof(CompAdvancedResourceProcessor) } }
                };
                ProfileVariants.Entries[definition.defName] = new VariantEntry
                    { sourceName = definition.defName, name = definition.defName, tier = tier, minimum = 2, factory = true };
                WorldProfile.AppliedTiers[definition.defName] = tier;
                var machine = new ThingWithComps { def = definition };
                var result = new ProcessDef.Result { thing = product, count = 20 };
                var recipe = new ProcessDef
                {
                    defName = "GrechkaDrillTierTestProcessT" + tier, worksInSpace = true, results = new() { result }
                };
                DefDatabase<ProcessDef>.Add(recipe);
                Process process = MakeProcess(machine, recipe);

                process.Tick(250);
                int advance = 100000 - process.TickLeft;
                Check(advance == 250 * ProfileVariants.Speed[tier],
                    "T" + tier + " native drill clock advances " + advance + " work ticks per 250 real ticks");

                int expectedBatch = (int)(20 * (1 + ProfileVariants.Output[tier]) + .00001f);
                acceptPlacement = false;
                Check(!Spawn(process, result, map) && result.GetCount(process) == expectedBatch,
                    "T" + tier + " blocked native output retains its bonus quote");
                acceptPlacement = true;
                delivered = 0;
                Check(Spawn(process, result, map) && delivered == expectedBatch,
                    "T" + tier + " physically delivers " + expectedBatch + " steel through native TrySpawnAtCell");

                result.count = 1;
                delivered = 0;
                for (int batch = 0; batch < 10; batch++)
                {
                    int quote = result.GetCount(process);
                    acceptPlacement = false;
                    Check(!Spawn(process, result, map) && result.GetCount(process) == quote,
                        "T" + tier + " retry preserves fractional yield");
                    acceptPlacement = true;
                    Check(Spawn(process, result, map), "T" + tier + " native delivery succeeds");
                }
                Check(delivered == 10 + (int)Math.Round(10 * ProfileVariants.Output[tier]),
                    "T" + tier + " committed physical batches retain their full fractional bonus");
                CheckNativeSave(process, machine, recipe, result);
                WorldProfile.AppliedTiers.Remove(definition.defName);
                ProfileVariants.Entries.Remove(definition.defName);
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private static Process MakeProcess(ThingWithComps machine, ProcessDef definition)
    {
        var process = (Process)RuntimeHelpers.GetUninitializedObject(typeof(Process));
        AccessTools.Field(typeof(Process), "parent").SetValue(process, machine);
        AccessTools.Field(typeof(Process), "def").SetValue(process, definition);
        AccessTools.Field(typeof(Process), "ingredientsOwners").SetValue(process, new List<ThingAndResourceOwner>());
        AccessTools.Field(typeof(Process), "ingredientsCompResources").SetValue(process, new List<CompResource>());
        var processor = (CompAdvancedResourceProcessor)RuntimeHelpers.GetUninitializedObject(typeof(CompAdvancedResourceProcessor));
        processor.cachedIngredients = new();
        process.advancedProcessor = processor;
        process.tickLeft = 100000;
        process.ticksOrQualityTicks = 100000;
        return process;
    }

    private static bool Spawn(Process process, ProcessDef.Result result, Map map)
    {
        // Legacy PipeSystem advances a cursor after this method; newer builds commit at placement.
        AccessTools.Field(typeof(Process), "currentResult")?.SetValue(process, 0);
        object[] args = { result, new IntVec3(3, 0, 3), map, null };
        bool success = (bool)AccessTools.Method(typeof(Process), "TrySpawnAtCell").Invoke(process, args);
        if (success && AccessTools.Field(typeof(Process), "currentResult") != null)
            AccessTools.Method(typeof(FactoryTierAdapter), "CommitResult").Invoke(null, new object[] { process, result });
        return success;
    }

    private static void CheckNativeSave(Process process, ThingWithComps machine, ProcessDef recipe, ProcessDef.Result result)
    {
        string path = Path.Combine(Path.GetTempPath(), "grechka-drill-tier-" + Guid.NewGuid() + ".xml");
        try
        {
            // Create an uncommitted quote. Saving must retain only completed output carry.
            int quote = result.GetCount(process);
            Scribe.saver.InitSaving(path, "fixture");
            Scribe_Deep.Look(ref process, "process");
            Scribe.saver.FinalizeSaving();
            var xml = XDocument.Load(path);
            Check(xml.Root.Element("process")?.Element("grechkaFactoryTiers") != null,
                "Native process save includes the tier bonus state");
            Process restored = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref restored, "process");
            Scribe.loader.FinalizeLoading();
            AccessTools.Field(typeof(Process), "parent").SetValue(restored, machine);
            Check(result.GetCount(restored) == quote && restored.TickLeft == process.TickLeft,
                "Native process save/load retains progress and committed fractional yield");

            xml.Root.Element("process").Element("grechkaFactoryTiers").Remove();
            xml.Save(path);
            restored = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref restored, "process");
            Scribe.loader.FinalizeLoading();
            AccessTools.Field(typeof(Process), "parent").SetValue(restored, machine);
            Check(result.GetCount(restored) >= 1 && restored.TickLeft == process.TickLeft,
                "Existing cursorless saves without bonus data load with their progress intact");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    public static IEnumerable<CodeInstruction> PlacementBoundary(IEnumerable<CodeInstruction> instructions)
    {
        var native = AccessTools.Method(typeof(GenGrid), nameof(GenGrid.Walkable));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(native))
                instruction.operand = AccessTools.Method(typeof(DrillTierChecks), nameof(Walkable));
            yield return instruction;
        }
    }

    public static bool Walkable(IntVec3 cell, Map map) => true;

    public static bool MakeThing(ThingDef __0, ref Thing __result)
    {
        __result = new ThingWithComps { def = __0, thingIDNumber = nextId++ };
        return false;
    }

    public static bool Place(Thing __0, ref Thing __4, ref bool __result)
    {
        __result = acceptPlacement;
        __4 = acceptPlacement ? __0 : null;
        if (acceptPlacement) delivered += __0.stackCount;
        return false;
    }
}
