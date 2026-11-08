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

// A room need not be rectangular. Cells across a wall must never inherit light.
var lighting = new RoomLightingMask();
Check(lighting.Replace(new[] { 10, 11, 20 }).SetEquals(new[] { 10, 11, 20 })
    && lighting.Contains(20) && !lighting.Contains(21), "Light covers the real room shape, not its bounding rectangle");
Check(lighting.Replace(new[] { 10, 11, 21 }).SetEquals(new[] { 20, 21 })
    && !lighting.Contains(20) && lighting.Contains(21), "A wall change with the same area invalidates both old and new cells");
Check(lighting.Replace(new[] { 10, 11, 21, 21, 30, 31 }).SetEquals(new[] { 30, 31 }),
    "Overlapping appliance coverage does not duplicate lighting or redraws");
Check(lighting.Replace(new[] { 30, 31 }).SetEquals(new[] { 10, 11, 21 })
    && lighting.Contains(30), "Losing one powered room preserves another room's light");
Check(lighting.Replace(new[] { 31, 30 }).Count == 0,
    "An unchanged room does not schedule unnecessary lighting redraws");
var lastDirty = lighting.Replace(Array.Empty<int>());
Check(lastDirty.SetEquals(new[] { 30, 31 }) && lighting.IsEmpty && !lighting.Contains(31),
    "Removing the last appliance removes light and redraws the formerly lit cells");
lastDirty.Add(99);
Check(!lighting.Contains(99), "A caller cannot mutate the snapshot used by render readers");


Check(MatterEconomy.SynthesisCost(13.01, 10) == 131 && MatterEconomy.SynthesisCost(double.NaN, 10) == 0,
    "Matter synthesis rounds up and rejects undefined free recipes");
foreach (double price in new[] { 1.9, 10, 100, 1300, 34000 })
{
    int cost = (int)Math.Ceiling(MatterEconomy.SynthesisCost(price, 10) * .5);
    int returned = MatterEconomy.RecycleYield(price * 2, 10, .05) * 2;
    Check(returned <= cost, "T5 discounts and double outputs cannot create a positive matter cycle at value " + price);
}
Check(MatterEconomy.Work(1, 1000, 100, 1000, 15) == 90000,
    "Matter work enforces the larger energy budget at configured speed");
Check(MatterEconomy.Work(1000, 1, 100, 1000, 15) == 100000,
    "Matter work keeps the processing time when energy would finish earlier");
Check(MatterEconomy.RecycleYield(50, 10, .05) < MatterEconomy.RecycleYield(100, 10, .05),
    "A lower real input value cannot improve recycling output");
Check(MatterEconomy.SynthesisCost(-5,-10)==0 && MatterEconomy.RecycleYield(-5,-10,.05)==0,
    "Two invalid negative inputs cannot multiply into a valid conversion");
foreach(double bad in new[]{double.NaN,double.PositiveInfinity,0,-1})
    Check(MatterEconomy.Work(bad,1000,1,1000,1)==float.MaxValue
        && MatterEconomy.Work(1,1000,1,1000,bad)==float.MaxValue,
        "Invalid conversion clock fails closed: "+bad);
Check(MatterEconomy.Work(1,1e15,1,1,1)>=6e16,
    "Large finite electrical budgets are not silently truncated at one trillion work");
foreach(double value in new[]{.0001,.001,.01,.1,1,1.9,10,34000})
 foreach(double discount in new[]{.35,.5})
 {
    int batch=MatterEconomy.RecycleBatch(value,10,.05,discount);
    Check(batch>0 && MatterEconomy.RecycleYield(value*Math.Ceiling(batch*(1-discount)),10,.05)>0,
        "Cheap item recycling has a real nonzero post-discount yield: "+value+" / "+discount);
 }
Check(MatterEconomy.RecycleBatch(double.Epsilon,10,.05,.5)==0
    && MatterEconomy.RecycleBatch(1e50,10,.05,.5)==0,
    "Unrepresentable recycling batches fail promptly without overflowing or looping");
for(int priceStep=1;priceStep<=10000;priceStep++)
{
    double price=priceStep/100d;
    int spent=(int)Math.Ceiling(MatterEconomy.SynthesisCost(price,10)*.5);
    int recovered=2*MatterEconomy.RecycleYield(price*2,10,.06249);
    if(recovered>spent) throw new Exception("Direct T5 matter arbitrage at "+price);
}
Check(true,"10,000 price points below the strict configured efficiency boundary cannot create direct T5 matter arbitrage");
