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
