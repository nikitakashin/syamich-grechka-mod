using System;
using System.Collections.Generic;
using System.Linq;

namespace Grechka.Storage
{
    public readonly struct DiskItemKey : IEquatable<DiskItemKey>
    {
        public readonly string Def;
        public readonly string Stuff;
        public readonly int Quality;
        public DiskItemKey(string def, string stuff, int quality)
        { Def = def ?? ""; Stuff = stuff ?? ""; Quality = quality; }
        public bool Equals(DiskItemKey other) { return Def == other.Def && Stuff == other.Stuff && Quality == other.Quality; }
        public override bool Equals(object obj) { return obj is DiskItemKey other && Equals(other); }
        public override int GetHashCode() { return HashCode.Combine(Def, Stuff, Quality); }
        public static bool operator ==(DiskItemKey left, DiskItemKey right) { return left.Equals(right); }
        public static bool operator !=(DiskItemKey left, DiskItemKey right) { return !left.Equals(right); }
    }

    public sealed class DiskSpace
    {
        public const int TypeLimit = 63;
        public readonly long Capacity;
        private readonly Dictionary<DiskItemKey, long> counts = new Dictionary<DiskItemKey, long>();
        public long UsedBytes { get; private set; }
        public int Types { get { return counts.Count; } }
        public long ItemCount { get { return counts.Values.Sum(); } }
        public DiskSpace(long capacity)
        {
            if (capacity < 128 || capacity % 128 != 0 || capacity > 256L * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }
        public long Count(DiskItemKey key) { return counts.TryGetValue(key, out long count) ? count : 0; }
        private long Bytes(long count) { return count <= 0 ? 0 : Capacity / 128 + (count + 7) / 8; }
        public long MaxAccept(DiskItemKey key)
        {
            long count = Count(key);
            if (Types > TypeLimit || count == 0 && Types >= TypeLimit) return 0;
            long available = Capacity - (UsedBytes - Bytes(count)) - Capacity / 128;
            return Math.Max(0, available * 8 - count);
        }
        // Rebuilds may contain an old over-capacity save: preserve every item,
        // stop accepting additions, and always permit withdrawal.
        public void SetCount(DiskItemKey key, long count)
        {
            if (count < 0 || count > long.MaxValue / 8) throw new ArgumentOutOfRangeException(nameof(count));
            long old = Count(key);
            UsedBytes -= Bytes(old);
            if (count == 0) counts.Remove(key); else counts[key] = count;
            UsedBytes += Bytes(count);
        }
        public void Add(DiskItemKey key, long count)
        {
            if (count <= 0 || count > MaxAccept(key)) throw new InvalidOperationException("Disk has no room for this transfer.");
            SetCount(key, checked(Count(key) + count));
        }
    }

    public sealed class DiskPlanTarget
    {
        public int Id;
        public long Capacity;
        public bool Frozen;
    }
    public sealed class DiskPlanGroup
    {
        public DiskItemKey Key;
        public long Count;
        public bool Frozen;
    }
    public sealed class DiskPlanMove
    {
        public int Target;
        public DiskItemKey Key;
        public long Count;
        public bool Frozen;
    }
    public static class DiskPlanner
    {
        public static bool TryPlan(IReadOnlyList<DiskPlanTarget> targets, IEnumerable<DiskPlanGroup> groups,
            out List<DiskPlanMove> plan)
        {
            var result = new List<DiskPlanMove>();
            var spaces = targets.ToDictionary(t => t.Id, t => new DiskSpace(t.Capacity));
            foreach (DiskPlanGroup group in groups.OrderByDescending(g => g.Count).ThenBy(g => g.Key.Def, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Stuff, StringComparer.Ordinal).ThenBy(g => g.Key.Quality))
            {
                long remaining = group.Count;
                if (remaining < 0) throw new ArgumentOutOfRangeException(nameof(groups));
                var candidates = targets.Where(t => t.Frozen == group.Frozen).OrderBy(t => t.Capacity).ThenBy(t => t.Id).ToList();
                DiskPlanTarget whole = candidates.FirstOrDefault(t => spaces[t.Id].MaxAccept(group.Key) >= remaining);
                if (whole != null && remaining > 0)
                {
                    spaces[whole.Id].Add(group.Key, remaining);
                    result.Add(new DiskPlanMove { Target = whole.Id, Key = group.Key, Count = remaining, Frozen = group.Frozen });
                    continue;
                }
                foreach (DiskPlanTarget target in candidates.OrderByDescending(t => t.Capacity).ThenBy(t => t.Id))
                {
                    long count = Math.Min(remaining, spaces[target.Id].MaxAccept(group.Key));
                    if (count <= 0) continue;
                    spaces[target.Id].Add(group.Key, count);
                    result.Add(new DiskPlanMove { Target = target.Id, Key = group.Key, Count = count, Frozen = group.Frozen });
                    remaining -= count;
                    if (remaining == 0) break;
                }
                if (remaining != 0) { plan = null; return false; }
            }
            plan = result;
            return true;
        }
    }
}
