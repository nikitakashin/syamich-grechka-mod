using System;
using System.Collections.Generic;
using System.Linq;

namespace Grechka.Storage
{
    // Physical signal conductors only. Ordinary machines are endpoints and must
    // never join separate branches just because their footprints touch both.
    public sealed class SignalTopology
    {
        private sealed class Component
        {
            internal int id;
            internal int minimum;
            internal readonly HashSet<int> cells = new HashSet<int>();
        }

        private readonly int width;
        private readonly int cellCount;
        private int nextId = 1;
        private readonly Dictionary<int, HashSet<int>> sources = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<int, int> coverage = new Dictionary<int, int>();
        private readonly Dictionary<int, Component> byCell = new Dictionary<int, Component>();
        private readonly Dictionary<int, Component> components = new Dictionary<int, Component>();
        public event Action<int> NetworkChanged;
        public long Revision { get; private set; }
        public int NetworkCount { get { return components.Count; } }

        public SignalTopology(int width, int height)
        {
            if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width));
            this.width = width;
            cellCount = width * height;
        }

        public void SetSource(int sourceId, IEnumerable<int> cells)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            var next = new HashSet<int>(cells);
            if (next.Any(cell => cell < 0 || cell >= cellCount))
                throw new ArgumentOutOfRangeException(nameof(cells));
            sources.TryGetValue(sourceId, out HashSet<int> previous);
            if (previous != null && previous.SetEquals(next)) return;
            var removed = previous == null ? new int[0] : previous.Except(next).ToArray();
            var added = previous == null ? next.ToArray() : next.Except(previous).ToArray();
            if (next.Count == 0) sources.Remove(sourceId); else sources[sourceId] = next;
            foreach (int cell in removed)
            {
                int remaining = coverage[cell] - 1;
                if (remaining > 0) { coverage[cell] = remaining; NetworkChanged?.Invoke(byCell[cell].id); }
                else { coverage.Remove(cell); RemoveCell(cell); }
            }
            foreach (int cell in added)
            {
                coverage.TryGetValue(cell, out int count);
                coverage[cell] = count + 1;
                if (count == 0) AddCell(cell); else NetworkChanged?.Invoke(byCell[cell].id);
            }
            if (removed.Length > 0 || added.Length > 0) Revision++;
        }

        public void RemoveSource(int sourceId) { SetSource(sourceId, Array.Empty<int>()); }
        public int NetworkAt(int cell) { return byCell.TryGetValue(cell, out Component c) ? c.id : 0; }
        public int MinimumCell(int network) { return components.TryGetValue(network, out Component c) ? c.minimum : int.MaxValue; }
        public IEnumerable<int> Cells(int network)
        {
            if (components.TryGetValue(network, out Component c))
                foreach (int cell in c.cells) yield return cell;
        }

        public List<int> NetworksAt(IEnumerable<int> ports)
        {
            var ids = new HashSet<int>();
            foreach (int port in ports)
            {
                int id = NetworkAt(port);
                if (id != 0) ids.Add(id);
            }
            return ids.OrderBy(MinimumCell).ToList();
        }

        public int EndpointNetwork(IEnumerable<int> ports, ISet<int> controlledNetworks)
        {
            List<int> adjacent = NetworksAt(ports);
            foreach (int network in adjacent) if (controlledNetworks.Contains(network)) return network;
            return adjacent.Count == 0 ? 0 : adjacent[0];
        }

        public IEnumerable<int> CardinalNeighbours(int cell)
        {
            if (cell < 0 || cell >= cellCount) yield break;
            if (cell % width > 0) yield return cell - 1;
            if (cell % width + 1 < width) yield return cell + 1;
            if (cell >= width) yield return cell - width;
            if (cell + width < cellCount) yield return cell + width;
        }

        private void AddCell(int cell)
        {
            var adjacent = CardinalNeighbours(cell).Where(byCell.ContainsKey).Select(n => byCell[n]).Distinct().ToList();
            Component target;
            if (adjacent.Count == 0)
            {
                target = new Component { id = nextId++, minimum = cell };
                components.Add(target.id, target);
            }
            else target = adjacent.OrderByDescending(c => c.cells.Count).ThenBy(c => c.minimum).First();
            target.cells.Add(cell);
            target.minimum = Math.Min(target.minimum, cell);
            byCell.Add(cell, target);
            foreach (Component other in adjacent)
            {
                if (other == target) continue;
                foreach (int member in other.cells) { target.cells.Add(member); byCell[member] = target; }
                target.minimum = Math.Min(target.minimum, other.minimum);
                components.Remove(other.id);
                NetworkChanged?.Invoke(other.id);
            }
            NetworkChanged?.Invoke(target.id);
        }

        private void RemoveCell(int cell)
        {
            Component old = byCell[cell];
            old.cells.Remove(cell);
            byCell.Remove(cell);
            components.Remove(old.id);
            NetworkChanged?.Invoke(old.id);
            if (old.cells.Count == 0) return;

            // Only the affected old branch is traversed. Neighbours of the
            // removed cell seed every possible remaining connected piece.
            var unseen = new HashSet<int>(old.cells);
            bool reuse = true;
            foreach (int seed in CardinalNeighbours(cell))
            {
                if (!unseen.Remove(seed)) continue;
                var part = new Component { id = reuse ? old.id : nextId++, minimum = seed };
                reuse = false;
                var pending = new Queue<int>();
                pending.Enqueue(seed);
                while (pending.Count > 0)
                {
                    int current = pending.Dequeue();
                    part.cells.Add(current); byCell[current] = part;
                    part.minimum = Math.Min(part.minimum, current);
                    foreach (int neighbour in CardinalNeighbours(current))
                        if (unseen.Remove(neighbour)) pending.Enqueue(neighbour);
                }
                components.Add(part.id, part);
                NetworkChanged?.Invoke(part.id);
            }
        }
    }
}
