using System;
using System.Collections.Generic;
using System.Linq;

namespace Grechka.Storage
{
    // Pure layout: generate the entire footprint before looking for a safe place on the map.
    // A blocked cell must never silently turn a large meteor into a tiny fragment.
    internal sealed class CertusMeteorLayout
    {
        internal readonly List<(int x, int z)> Footprint = new List<(int, int)>();
        internal readonly List<(int x, int z)> Rocks = new List<(int, int)>();
        internal readonly HashSet<(int x, int z)> Ore = new HashSet<(int, int)>();
        internal readonly (int x, int z)[] Sources = { (-1, 0), (0, 0), (0, 1) };

        internal static CertusMeteorLayout Create(int seed)
        {
            var random = new Random(seed);
            var layout = new CertusMeteorLayout();
            double phaseA = random.NextDouble() * Math.PI * 2;
            double phaseB = random.NextDouble() * Math.PI * 2;
            for (int z = -11; z <= 11; z++)
                for (int x = -11; x <= 11; x++)
                {
                    double angle = Math.Atan2(z, x);
                    double radius = 10 + 0.55 * Math.Sin(3 * angle + phaseA) + 0.3 * Math.Sin(5 * angle + phaseB);
                    if (x * x + z * z > radius * radius) continue;
                    layout.Footprint.Add((x, z));
                    layout.Rocks.Add((x, z));
                }

            // A broad central hollow and three winding fractures open out to the rim.
            // These are genuine empty cells, not dark lines painted on solid rock.
            var gaps = new HashSet<(int x, int z)>();
            foreach (var cell in layout.Footprint)
                if (Math.Abs(cell.x) <= 2 && Math.Abs(cell.z) <= 2) gaps.Add(cell);
            double direction = random.NextDouble() * Math.PI * 2;
            for (int arm = 0; arm < 3; arm++)
            {
                double angle = direction + arm * Math.PI * 2 / 3;
                double bend = random.NextDouble() * Math.PI * 2;
                (int x, int z) previous = (0, 0);
                for (int step = 1; step <= 24; step++)
                {
                    double distance = step * 0.5;
                    double sideways = Math.Sin(distance * 0.65 + bend) * 0.9;
                    var next = (x: (int)Math.Round(Math.Cos(angle) * distance - Math.Sin(angle) * sideways),
                        z: (int)Math.Round(Math.Sin(angle) * distance + Math.Cos(angle) * sideways));
                    // Fill cardinal steps so diagonal turns never seal a crack.
                    while (previous.x != next.x) { previous.x += Math.Sign(next.x - previous.x); gaps.Add(previous); }
                    while (previous.z != next.z) { previous.z += Math.Sign(next.z - previous.z); gaps.Add(previous); }
                }
            }
            int targetGaps = (int)Math.Ceiling(layout.Footprint.Count * 0.27);
            for (int pocket = 0; pocket < 80 && layout.Footprint.Count(c => gaps.Contains(c)) < targetGaps; pocket++)
            {
                var cell = layout.Footprint[random.Next(layout.Footprint.Count)];
                gaps.Add(cell); gaps.Add((cell.x + 1, cell.z)); gaps.Add((cell.x, cell.z + 1));
            }
            layout.Rocks.RemoveAll(c => gaps.Contains(c));

            // Short rounded veins rather than a checkerboard of independent ore rolls.
            var nuclei = new List<(int x, int z)>();
            for (int i = 0; i < 7; i++) nuclei.Add(layout.Rocks[random.Next(layout.Rocks.Count)]);
            int oreCount = (int)Math.Round(layout.Rocks.Count * 0.3, MidpointRounding.AwayFromZero);
            var ranked = layout.Rocks.Select(c => (cell: c,
                score: nuclei.Min(n => (c.x - n.x) * (c.x - n.x) + (c.z - n.z) * (c.z - n.z)) + random.NextDouble() * 2));
            foreach (var entry in ranked.OrderBy(c => c.score).Take(oreCount)) layout.Ore.Add(entry.cell);
            return layout;
        }
    }

    internal static class CertusMeteorSiting
    {
        // -1 is protected, 0 is usable land, 1 is water requiring a stone foundation.
        // Classify once: trees/stone/chunks must not make a 20x20 footprint impossible.
        internal static bool TryFind(int width, int height, CertusMeteorLayout layout,
            Func<int, int, int> classify, out (int x, int z) center, out bool needsFoundation)
        {
            var cells = new int[width * height];
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++) cells[z * width + x] = classify(x, z);
            var candidates = new List<(int x, int z)>();
            for (int z = 11; z < height - 11; z++)
                for (int x = 11; x < width - 11; x++) candidates.Add((x, z));
            candidates.Sort((a, b) => Distance(a, width, height).CompareTo(Distance(b, width, height)));
            foreach (bool allowWater in new[] { false, true })
                foreach (var candidate in candidates)
                {
                    bool valid = true, water = false;
                    foreach (var offset in layout.Footprint)
                    {
                        int kind = cells[(candidate.z + offset.z) * width + candidate.x + offset.x];
                        if (kind < 0 || (kind > 0 && !allowWater)) { valid = false; break; }
                        water |= kind > 0;
                    }
                    if (!valid) continue;
                    center = candidate; needsFoundation = water; return true;
                }
            center = default; needsFoundation = false; return false;
        }
        static long Distance((int x, int z) c, int width, int height)
            => (long)(c.x - width / 2) * (c.x - width / 2) + (long)(c.z - height / 2) * (c.z - height / 2);
    }

    internal static class CertusGrowthStages
    {
        // Four intervals below maturity; the fifth frame persists until a pawn harvests it.
        internal static int Select(int progress, int cycleTicks, int pending)
            => pending > 0 ? 4 : Math.Min(3, (int)(Math.Max(0, (long)progress) * 4 / Math.Max(1, cycleTicks)));
    }
}
