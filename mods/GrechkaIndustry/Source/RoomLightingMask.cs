using System.Collections.Generic;

namespace Grechka.Industry
{
    // Readers never see a set being edited while a render job samples lighting.
    public sealed class RoomLightingMask
    {
        private volatile HashSet<int> cells = new HashSet<int>();
        public bool Contains(int cell) { return cells.Contains(cell); }
        public bool IsEmpty { get { return cells.Count == 0; } }

        public HashSet<int> Replace(IEnumerable<int> coveredCells)
        {
            var next = new HashSet<int>(coveredCells);
            var changed = new HashSet<int>(cells);
            changed.SymmetricExceptWith(next);
            cells = next;
            return changed;
        }
    }
}
