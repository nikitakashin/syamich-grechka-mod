using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Storage
{
    public sealed class GrechkaStorageMod : Mod
    {
        public GrechkaStorageMod(ModContentPack content) : base(content)
        { new Harmony("grechka.storage.signal-network").PatchAll(typeof(GrechkaStorageMod).Assembly); }
    }

    public sealed class NetworkView
    {
        public int NetworkId;
        public int CableCount;
        public List<Building> Devices = new List<Building>();
        public List<Building> OtherBranchDevices = new List<Building>();
    }

    public sealed class SignalNetworkMap : MapComponent
    {
        private SignalTopology topology;
        private readonly HashSet<CompSignalNode> controllers = new HashSet<CompSignalNode>();
        private readonly Dictionary<int, NetworkView> views = new Dictionary<int, NetworkView>();
        public SignalNetworkMap(Map map) : base(map) { }

        public void Register(CompSignalNode node)
        {
            if (topology == null)
            {
                topology = new SignalTopology(map.Size.x, map.Size.z);
                topology.NetworkChanged += id => views.Remove(id);
            }
            if (node.IsController) controllers.Add(node);
            topology.SetSource(node.parent.thingIDNumber, node.parent.OccupiedRect().Cells
                .Where(c => c.InBounds(map)).Select(c => map.cellIndices.CellToIndex(c)));
        }

        public void Unregister(CompSignalNode node)
        {
            controllers.Remove(node);
            topology?.RemoveSource(node.parent.thingIDNumber);
        }

        public int NetworkOf(Thing thing)
        {
            return topology != null && thing.Spawned && thing.Map == map
                ? topology.NetworkAt(map.cellIndices.CellToIndex(thing.Position)) : 0;
        }

        public List<CompSignalNode> ControllersIn(int network)
        {
            return controllers.Where(c => c.parent.Spawned && c.parent.Faction == Faction.OfPlayer
                && NetworkOf(c.parent) == network).OrderBy(c => c.parent.thingIDNumber).ToList();
        }

        private IEnumerable<int> Ports(Thing thing)
        {
            CellRect rect = thing.OccupiedRect();
            foreach (IntVec3 cell in rect.ExpandedBy(1).Cells)
            {
                if (!cell.InBounds(map)) continue;
                // Include the footprint and four sides, excluding diagonals.
                if ((cell.x < rect.minX || cell.x > rect.maxX) && (cell.z < rect.minZ || cell.z > rect.maxZ)) continue;
                yield return map.cellIndices.CellToIndex(cell);
            }
        }

        private int PrimaryNetwork(Building building, HashSet<int> controlled)
        {
            // Prefer a branch with a physical controller, even if it is off.
            // One endpoint can never expose its inventory to two separate nets.
            return topology.EndpointNetwork(Ports(building), controlled);
        }

        public NetworkView ViewFor(CompSignalNode controller)
        {
            int network = NetworkOf(controller.parent);
            if (network == 0) return new NetworkView();
            var controlled = new HashSet<int>(controllers.Where(c => c.parent.Spawned
                && c.parent.Faction == Faction.OfPlayer).Select(c => NetworkOf(c.parent)));
            NetworkView cached = CandidatesFor(network);
            var owned = cached.Devices.Where(b => b.Spawned && b.Faction == Faction.OfPlayer).ToList();
            var assignment = owned.ToDictionary(b => b, b => PrimaryNetwork(b, controlled));
            // Ownership and multi-port assignment are read live. A change in a
            // neighbouring branch must never leave a device exposed by two nets.
            return new NetworkView
            {
                NetworkId = network, CableCount = cached.CableCount,
                Devices = owned.Where(b => assignment[b] == network).ToList(),
                OtherBranchDevices = owned.Where(b => assignment[b] != network).ToList()
            };
        }

        private NetworkView CandidatesFor(int network)
        {
            if (views.TryGetValue(network, out NetworkView cached)) return cached;
            var candidates = new HashSet<Building>();
            var cables = new HashSet<int>();
            foreach (int index in topology.Cells(network))
            {
                foreach (int nearby in topology.CardinalNeighbours(index).Prepend(index))
                {
                    IntVec3 cell = map.cellIndices.IndexToCell(nearby);
                    foreach (Thing thing in map.thingGrid.ThingsListAtFast(cell))
                    {
                        if (!(thing is Building building) || building.def.IsBlueprint || building.def.IsFrame) continue;
                        CompSignalNode node = building.GetComp<CompSignalNode>();
                        if (node != null && node.IsCable)
                        {
                            if (NetworkOf(building) == network) cables.Add(building.thingIDNumber);
                            continue;
                        }
                        if (building is Building_WorkTable || building.GetComp<CompPowerTrader>() != null
                            || node != null && node.IsController) candidates.Add(building);
                    }
                }
            }
            var view = new NetworkView
            {
                NetworkId = network,
                CableCount = cables.Count,
                Devices = candidates.OrderBy(b => b.LabelCap.ToString()).ThenBy(b => b.thingIDNumber).ToList()
            };
            views[network] = view;
            return view;
        }

        public void InvalidateNear(Building building)
        {
            if (topology == null) return;
            foreach (int network in topology.NetworksAt(Ports(building))) views.Remove(network);
        }

        public override void MapRemoved()
        {
            views.Clear(); controllers.Clear(); topology = null;
            base.MapRemoved();
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.SpawnSetup))]
    internal static class StorageBuildingSpawnHook
    {
        private static void Postfix(Building __instance)
        { if (__instance.Spawned) __instance.Map.GetComponent<SignalNetworkMap>().InvalidateNear(__instance); }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.DeSpawn))]
    internal static class StorageBuildingDespawnHook
    {
        private static void Prefix(Building __instance)
        { if (__instance.Spawned) __instance.Map.GetComponent<SignalNetworkMap>().InvalidateNear(__instance); }
    }
}
