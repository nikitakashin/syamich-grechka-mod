using Grechka.Industry;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}

var cycle = new ProductionCycle();
cycle.Advance(false, 2500, 2500, 5);
Check(cycle.WorkedTicks == 0 && cycle.PendingCount == 0, "Unpowered extractor creates no progress or output");
cycle.Advance(true, 1000, 2500, 5);
cycle.Advance(false, 6000, 2500, 5);
Check(cycle.WorkedTicks == 1000 && cycle.PendingCount == 0, "Power loss preserves partial work without completing it");
// These are exactly the two fields persisted by the game component.
var restored = new ProductionCycle { WorkedTicks = cycle.WorkedTicks, PendingCount = cycle.PendingCount };
restored.Advance(true, 1500, 2500, 5);
Check(restored.WorkedTicks == 0 && restored.PendingCount == 5, "Restored progress finishes exactly one batch");
restored.Advance(true, 10000, 2500, 5);
Check(restored.PendingCount == 5 && restored.WorkedTicks == 0, "Blocked output pauses the next cycle instead of accumulating batches");
restored.RecordDelivery(2);
Check(restored.PendingCount == 3, "Partial placement retains only the undelivered remainder");
var savedPending = new ProductionCycle { WorkedTicks = restored.WorkedTicks, PendingCount = restored.PendingCount };
savedPending.RecordDelivery(3);
savedPending.Advance(true, 2499, 2500, 5);
Check(savedPending.PendingCount == 0 && savedPending.WorkedTicks == 2499, "Restored pending output is not produced again after delivery");
savedPending.Advance(true, 1, 2500, 5);
Check(savedPending.PendingCount == 5, "Production resumes after the previous batch is fully delivered");
bool rejected = false;
try { savedPending.RecordDelivery(6); } catch (ArgumentOutOfRangeException) { rejected = true; }
Check(rejected && savedPending.PendingCount == 5, "Invalid delivery cannot erase or duplicate a batch");

foreach (string prefix in new[] { "", "Blueprint_", "Frame_", "Blueprint_Install_" })
    Check(LegacyDefNames.Canonicalize(prefix + "Grechka_AssemblyBenchT2")
        == prefix + "Grechka_AssemblyBenchT2_Building", "Legacy building and implied def remap: " + prefix);
Check(LegacyDefNames.Canonicalize("Grechka_ComponentT3") == "Grechka_ComponentT3_Item"
    && LegacyDefNames.Canonicalize("Grechka_ComponentT3_Item") == "Grechka_ComponentT3_Item"
    && LegacyDefNames.Canonicalize("Steel") == "Steel",
    "Item aliases are idempotent and leave other mods untouched");
