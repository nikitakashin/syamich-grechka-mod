using Grechka.Storage;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}

var graph = new SignalTopology(10, 10);
graph.SetSource(1, new[] { 9 });
graph.SetSource(2, new[] { 10 });
graph.SetSource(3, new[] { 20 });
Check(graph.NetworkAt(9) != graph.NetworkAt(10) && graph.NetworkAt(10) == graph.NetworkAt(20),
    "Cardinal wires connect without row wrapping or diagonal shortcuts");
int remoteId = graph.NetworkAt(9);
var changed = new HashSet<int>();
graph.NetworkChanged += id => changed.Add(id);
graph.SetSource(4, new[] { 30, 31, 32 });
graph.SetSource(5, new[] { 22 });
Check(graph.NetworkAt(20) == graph.NetworkAt(22) && !changed.Contains(remoteId),
    "A bridge merges branches and leaves an unrelated network untouched");
graph.RemoveSource(4);
Check(graph.NetworkAt(20) != graph.NetworkAt(22) && graph.NetworkAt(30) == 0,
    "Removing a bridge separates the surviving cable branches");
graph.SetSource(6, new[] { 20, 21, 30, 31 });
graph.SetSource(7, new[] { 21 });
graph.RemoveSource(6);
Check(graph.NetworkAt(21) != 0 && graph.NetworkAt(31) == 0,
    "Removing a controller footprint preserves overlapping physical cable");
long before = graph.Revision;
graph.SetSource(7, new[] { 21, 21 });
Check(graph.Revision == before, "Repeated registration does not rebuild an unchanged network");
bool rejected = false;
try { graph.SetSource(7, new[] { 21, 100 }); } catch (ArgumentOutOfRangeException) { rejected = true; }
Check(rejected && graph.NetworkAt(21) != 0 && graph.Revision == before,
    "Invalid footprint is rejected before mutating the installed network");
graph.SetSource(8, new[] { 88 });
int[] ports = { 88, 9, 21, 22, 21 };
Check(graph.NetworksAt(ports).Select(graph.MinimumCell).SequenceEqual(graph.NetworksAt(ports.Reverse()).Select(graph.MinimumCell)),
    "Multi-port attachment order is deterministic and counts each branch once");

var endpoints = new SignalTopology(10, 10);
endpoints.SetSource(1, new[] { 11, 12 });
endpoints.SetSource(2, new[] { 31, 32 });
int lower = endpoints.NetworkAt(11), upper = endpoints.NetworkAt(31);
int[] machinePorts = { 12, 32 };
Check(endpoints.EndpointNetwork(machinePorts, new HashSet<int> { upper }) == upper,
    "An orphan cable fragment cannot steal an endpoint from a branch with a controller");
Check(endpoints.EndpointNetwork(machinePorts, new HashSet<int> { upper, lower }) == lower
    && endpoints.EndpointNetwork(machinePorts.Reverse(), new HashSet<int> { lower, upper }) == lower,
    "Two controllers resolve a shared multi-cell endpoint to exactly one network");
endpoints.SetSource(3, new[] { 22 });
Check(endpoints.EndpointNetwork(machinePorts, new HashSet<int> { endpoints.NetworkAt(11) }) == endpoints.NetworkAt(31),
    "Joining the two branches exposes one endpoint, without duplicating the machine");
endpoints.RemoveSource(3);
Check(endpoints.NetworksAt(machinePorts).Count == 2
    && endpoints.EndpointNetwork(machinePorts, new HashSet<int> { endpoints.NetworkAt(31) }) == endpoints.NetworkAt(31),
    "Split networks reassign endpoints from current physical connections");

// An independent flood fill catches split/merge/overlap errors across changing
// controller footprints, rather than merely checking the algorithm's own fields.
var random = new Random(8361);
var actual = new SignalTopology(15, 12);
var sources = new Dictionary<int, HashSet<int>>();
for (int iteration = 0; iteration < 800; iteration++)
{
    int id = random.Next(1, 40);
    var footprint = Enumerable.Range(0, random.Next(0, 7)).Select(_ => random.Next(180)).ToHashSet();
    sources[id] = footprint;
    actual.SetSource(id, footprint);
    var occupied = sources.Values.SelectMany(s => s).ToHashSet();
    var remaining = new HashSet<int>(occupied);
    int expectedCount = 0;
    while (remaining.Count > 0)
    {
        int seed = remaining.First();
        remaining.Remove(seed);
        int component = actual.NetworkAt(seed);
        if (component == 0) throw new Exception("Lost occupied cable at " + iteration);
        var expectedCells = new HashSet<int> { seed };
        var queue = new Queue<int>(); queue.Enqueue(seed);
        while (queue.Count > 0)
        {
            int c = queue.Dequeue(); int x = c % 15, z = c / 15;
            foreach (var (nx, nz) in new[] { (x-1,z), (x+1,z), (x,z-1), (x,z+1) })
            {
                int next = nx + nz * 15;
                if (nx < 0 || nx >= 15 || nz < 0 || nz >= 12 || !remaining.Remove(next)) continue;
                expectedCells.Add(next); queue.Enqueue(next);
            }
        }
        if (!actual.Cells(component).ToHashSet().SetEquals(expectedCells)
            || expectedCells.Any(c => actual.NetworkAt(c) != component)
            || actual.MinimumCell(component) != expectedCells.Min())
            throw new Exception("Signal partition differs from flood fill at " + iteration);
        expectedCount++;
    }
    if (actual.NetworkCount != expectedCount || Enumerable.Range(0, 180).Any(c => !occupied.Contains(c) && actual.NetworkAt(c) != 0))
        throw new Exception("Phantom network at " + iteration);
}
Console.WriteLine("PASS 800 changing multi-source networks match independent flood-fill partitions");

var steel = new DiskItemKey("Steel", null, -1);
foreach (long capacity in new long[] { 1024, 4096, 16384, 65536, 262144, 524288, 1048576, 2097152,
    4194304, 8388608, 16777216, 67108864, 268435456 })
{
    var space = new DiskSpace(capacity);
    Check(!space.IsFull, "Empty disk accepts items at capacity " + capacity);
    long maximum = (capacity - capacity / 128) * 8;
    Check(space.MaxAccept(steel) == maximum, "AE2 one-type capacity for " + capacity + " bytes");
    space.Add(steel, maximum);
    if (space.UsedBytes != capacity || space.MaxAccept(steel) != 0) throw new Exception("Capacity exceeded");
    Check(space.IsFull, "Fully occupied disk reports full at capacity " + capacity);
    space.SetCount(steel, maximum - 1);
    Check(space.UsedBytes == capacity && !space.IsFull && space.MaxAccept(steel) == 1,
        "Last allocated byte can still hold one item at capacity " + capacity);
}
var small = new DiskSpace(1024);
for (int i = 0; i < 63; i++) small.Add(new DiskItemKey("Item" + i, null, -1), 1);
Check(small.Types == 63 && small.UsedBytes == 63 * 9 && small.MaxAccept(steel) == 0,
    "64th type is rejected despite remaining bytes");
Check(!small.IsFull, "63 occupied types do not imply that the whole disk is full");
small.Add(new DiskItemKey("Item0", null, -1), 7);
Check(small.UsedBytes == 63 * 9, "A type shares its partial byte but different types do not");
Check(new DiskItemKey("Sword", "Steel", 1) != new DiskItemKey("Sword", "Plasteel", 1),
    "Materials distinguish item types");
Check(!new DiskItemKey("Sword", "Steel", 1).Equals(new DiskItemKey("Sword", "Steel", 2)),
    "Quality distinguishes item types");
var restoredSpace = new DiskSpace(1024);
restoredSpace.SetCount(steel, 9000);
Check(restoredSpace.ItemCount == 9000 && restoredSpace.MaxAccept(steel) == 0,
    "Old over-capacity data is preserved and accepts no new items");
Check(restoredSpace.IsFull, "Over-capacity save is shown as full without removing items");
restoredSpace.SetCount(steel, 8000);
Check(restoredSpace.MaxAccept(steel) == 128, "Withdrawal restores available capacity without deleting the remainder");
var targets = new[] { new DiskPlanTarget { Id = 1, Capacity = 1024 }, new DiskPlanTarget { Id = 2, Capacity = 65536 },
    new DiskPlanTarget { Id = 3, Capacity = 4096, Frozen = true } };
var groups = new[] { new DiskPlanGroup { Key = steel, Count = 100000 },
    new DiskPlanGroup { Key = new DiskItemKey("Armor", null, 4), Count = 1 },
    new DiskPlanGroup { Key = new DiskItemKey("Meal", null, -1), Count = 500, Frozen = true } };
Check(DiskPlanner.TryPlan(targets, groups, out var plan) && plan.Single(p => p.Key.Equals(steel)).Target == 2
    && plan.Single(p => p.Key.Def == "Armor").Target == 1 && plan.Single(p => p.Key.Def == "Meal").Target == 3,
    "Optimization puts bulk items on a large disk, one-off gear on a small disk and preserves freezing");
Check(!DiskPlanner.TryPlan(targets.Where(t => !t.Frozen).ToArray(), groups, out var impossible) && impossible == null,
    "An impossible optimization yields no partial layout and never silently thaws food");

// Exercise different contours and ore veins, rather than blessing one sample seed.
for (int seed = 0; seed < 100; seed++)
{
    var meteor = CertusMeteorLayout.Create(seed);
    int width = meteor.Footprint.Max(c => c.x) - meteor.Footprint.Min(c => c.x) + 1;
    int height = meteor.Footprint.Max(c => c.z) - meteor.Footprint.Min(c => c.z) + 1;
    double density = meteor.Footprint.Count / (double)(width * height);
    if (width < 18 || width > 22 || height < 18 || height > 22 || density < 0.65 || density > 0.88)
        throw new Exception("Meteor must be a roughly 20x20 circle, seed " + seed);
    if (meteor.Rocks.Count < 170 || meteor.Rocks.Count > 280
        || Math.Abs(meteor.Ore.Count / (double)meteor.Rocks.Count - 0.3) > 0.003)
        throw new Exception("Meteor must have hundreds of tiles at 70/30 stone/ore, seed " + seed);
    if (meteor.Sources.Length != 3 || meteor.Sources.Distinct().Count() != 3
        || meteor.Sources.Any(c => !meteor.Footprint.Contains(c) || meteor.Rocks.Contains(c) || Math.Abs(c.x) > 1 || Math.Abs(c.z) > 1))
        throw new Exception("The central source cluster must never overlap a rock, seed " + seed);
    var remaining = meteor.Footprint.Except(meteor.Rocks).ToHashSet();
    double emptyFraction = (remaining.Count - meteor.Sources.Length) / (double)meteor.Footprint.Count;
    if (emptyFraction < 0.18 || emptyFraction > 0.4)
        throw new Exception("Meteor needs visible empty fissures and pockets, seed " + seed);
    var queue = new Queue<(int x, int z)>();
    var first = meteor.Sources[0]; remaining.Remove(first); queue.Enqueue(first);
    bool reachesOutside = false;
    while (queue.Count > 0)
    {
        var c = queue.Dequeue();
        foreach (var n in new[] { (c.x + 1, c.z), (c.x - 1, c.z), (c.x, c.z + 1), (c.x, c.z - 1) })
        {
            if (!meteor.Footprint.Contains(n)) reachesOutside = true;
            if (remaining.Remove(n)) queue.Enqueue(n);
        }
    }
    if (!reachesOutside || meteor.Sources.Any(c => remaining.Contains(c)))
        throw new Exception("Central cluster must connect to outside through a real crack, seed " + seed);
}
Check(true, "100 roughly 20x20 meteor layouts have 70/30 stone/ore, broad holes and walkable cracks to the central cluster");
var reproducible = CertusMeteorLayout.Create(473);
var replay = CertusMeteorLayout.Create(473);
Check(reproducible.Footprint.SequenceEqual(replay.Footprint) && reproducible.Ore.SetEquals(replay.Ore),
    "Meteor contours and veins reproduce from the same seed");
int classified = 0;
Check(CertusMeteorSiting.TryFind(70, 70, reproducible, (x, z) => { classified++; return 0; }, out var anchor, out var foundation)
    && anchor == (35, 35) && !foundation && classified == 4900,
    "Impact siting evaluates each map cell once and prefers a central land footprint");
Check(CertusMeteorSiting.TryFind(70, 70, reproducible, (x, z) => x >= 25 && x <= 45 && z >= 25 && z <= 45 ? -1 : 0,
    out anchor, out foundation) && reproducible.Footprint.All(c => !(c.x + anchor.x >= 25 && c.x + anchor.x <= 45
        && c.z + anchor.z >= 25 && c.z + anchor.z <= 45)), "Protected structures in the center move the entire meteor footprint elsewhere");
Check(CertusMeteorSiting.TryFind(70, 70, reproducible, (x, z) => 1, out anchor, out foundation) && foundation,
    "A wet encounter map gets a complete meteor on a stone foundation instead of an empty successful event");
Check(!CertusMeteorSiting.TryFind(70, 70, reproducible, (x, z) => -1, out anchor, out foundation),
    "Impact siting never overwrites protected cells even when the whole map is blocked");
Check(new[] { 0, 3749, 3750, 7499, 7500, 11249, 11250, 14999 }.Select(p => CertusGrowthStages.Select(p, 15000, 0))
    .SequenceEqual(new[] { 0, 0, 1, 1, 2, 2, 3, 3 }), "Four growth intervals switch at quarter-cycle boundaries");
Check(CertusGrowthStages.Select(0, 15000, 4) == 4 && CertusGrowthStages.Select(8000, 15000, 1) == 4,
    "A mature or partially harvested source retains its fifth stage until the last pending crystal is gathered");
Check(CertusGrowthStages.Select(0, 15000, 0) == 0, "Harvested source starts its next cycle on the empty frame");
