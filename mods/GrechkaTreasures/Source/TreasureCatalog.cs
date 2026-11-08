using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace Grechka.Treasures
{
    public sealed class TreasuresMod : Mod
    {
        public TreasuresMod(ModContentPack content) : base(content)
        {
            TreasureCatalog.Load(Path.Combine(content.RootDir, "Config", "Sites"));
        }
    }

    public sealed class TreasureLayout
    {
        public string Id;
        public string Label;
        public string Description;
        public string Clue;
        public string Gem;
        public string Enemy;
        public int Tier;
        public string[] Rows;
        public int Width => Rows[0].Length;
        public int Height => Rows.Length;
        public const string Symbols = " #+.,CDHTGSOMWB=~VLR";

        public static TreasureLayout Read(string file)
        {
            XElement root = XElement.Load(file, LoadOptions.PreserveWhitespace);
            string Required(string key) => (string)root.Element(key) ?? throw new InvalidDataException("Missing " + key + " in " + file);
            var layout = new TreasureLayout {
                Id = Required("id"), Label = Required("label"), Description = Required("description"),
                Clue = Required("clue"), Gem = Required("gem"), Enemy = Required("enemy"),
                Tier = int.Parse(Required("tier")), Rows = root.Element("rows")?.Elements("row").Select(r => r.Value).ToArray()
            };
            layout.Validate();
            return layout;
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id) || !Id.All(c => char.IsLetterOrDigit(c) || c == '_')) throw new InvalidDataException("Invalid layout ID");
            if (Rows == null || Rows.Length < 9 || Rows.Length > 65 || Rows[0].Length < 9 || Rows[0].Length > 65) throw new InvalidDataException(Id + ": bad size");
            if (Rows.Any(r => r.Length != Width || r.Any(c => !Symbols.Contains(c)))) throw new InvalidDataException(Id + ": nonrectangular or unsupported symbol");
            for (int z = 0; z < Height; z++) for (int x = 0; x < Width; x++) {
                char marker = Rows[z][x];
                if (marker != 'S' && marker != 'B') continue;
                int width = marker == 'S' ? 2 : 1;
                for (int dz = 0; dz < 2; dz++) for (int dx = 0; dx < width; dx++) {
                    if (dx == 0 && dz == 0) continue;
                    if (x + dx >= Width || z - dz < 0 || !".,".Contains(Rows[z - dz][x + dx]))
                        throw new InvalidDataException(Id + ": overlapping furniture footprint at " + x + "," + z);
                }
            }
            if (Tier < 1 || Tier > 5) throw new InvalidDataException(Id + ": bad tier");
            if (!TreasureLoot.Gems.Contains(Gem)) throw new InvalidDataException(Id + ": bad gem");
            if (Enemy != "pirates" && Enemy != "mechanoids") throw new InvalidDataException(Id + ": bad enemy");
            if (!Rows.Any(r => r.Contains('C')) || !Rows.Any(r => r.Contains('W'))) throw new InvalidDataException(Id + ": needs treasure and readable clue");
            // Each layout has at least one genuine chest reachable through ordinary doors from outdoors.
            // Secret H chambers may require demolition; traps are walkable and intentionally not excluded.
            var seen = new HashSet<(int x, int z)>();
            var queue = new Queue<(int x, int z)>();
            void Add(int x, int z) { if (x < 0 || z < 0 || x >= Width || z >= Height || Rows[z][x] == '#' || Rows[z][x] == '~' || !seen.Add((x,z))) return; queue.Enqueue((x,z)); }
            for (int x = 0; x < Width; x++) { Add(x,0); Add(x,Height-1); }
            for (int z = 0; z < Height; z++) { Add(0,z); Add(Width-1,z); }
            bool reachable = false;
            while (queue.Count > 0) { var p = queue.Dequeue(); if (Rows[p.z][p.x] == 'C') reachable = true; Add(p.x+1,p.z); Add(p.x-1,p.z); Add(p.x,p.z+1); Add(p.x,p.z-1); }
            if (!reachable) throw new InvalidDataException(Id + ": no reachable genuine treasure");
        }
    }

    public static class TreasureCatalog
    {
        static readonly Dictionary<string, TreasureLayout> layouts = new Dictionary<string, TreasureLayout>(StringComparer.Ordinal);
        public static IReadOnlyCollection<TreasureLayout> Layouts => layouts.Values;
        public static void Load(string folder)
        {
            layouts.Clear();
            if (!Directory.Exists(folder)) { Log.Error("[GrechkaTreasures] Missing prepared site catalog: " + folder); return; }
            foreach (string file in Directory.GetFiles(folder, "*.xml").OrderBy(f => f, StringComparer.Ordinal))
            {
                try { TreasureLayout layout = TreasureLayout.Read(file); if (!layouts.TryAdd(layout.Id, layout)) throw new InvalidDataException("Duplicate layout ID " + layout.Id); }
                catch (Exception ex) { Log.Error("[GrechkaTreasures] Invalid prepared site " + Path.GetFileName(file) + ": " + ex.Message); }
            }
        }
        public static TreasureLayout Get(string id) => id != null && layouts.TryGetValue(id, out TreasureLayout layout) ? layout : null;
        public static TreasureLayout Choose(int seed, int maximumTier = 5)
        {
            var choices = layouts.Values.Where(l => l.Tier <= maximumTier).OrderBy(l => l.Id, StringComparer.Ordinal).ToArray();
            return choices.Length == 0 ? null : choices[new Random(seed).Next(choices.Length)];
        }
    }

    public readonly struct TreasureLootEntry
    {
        public readonly string DefName;
        public readonly int Count;
        public TreasureLootEntry(string defName, int count) { DefName = defName; Count = count; }
    }

    public static class TreasureLoot
    {
        public static readonly string[] Gems = { "Diamond", "Ruby", "Sapphire", "Emerald", "Amethyst", "Topaz" };
        // Pure deterministic plan: opening/reloading a chest never rerolls or reconstructs its contents.
        public static List<TreasureLootEntry> Plan(int seed, int tier, string gem, bool decoy, bool looted = false)
        {
            tier = Math.Max(1, Math.Min(5, tier));
            var random = new Random(seed);
            var result = new List<TreasureLootEntry>();
            if (decoy) { result.Add(new TreasureLootEntry("Steel", 8 + random.Next(12))); result.Add(new TreasureLootEntry("Gold", 1)); return result; }
            if (!Gems.Contains(gem)) gem = Gems[random.Next(Gems.Length)];
            result.Add(new TreasureLootEntry("GrechkaTreasures_" + gem + "Cut", looted ? 1 : 3 + tier * 2 + random.Next(tier * 3 + 1)));
            result.Add(new TreasureLootEntry("Silver", looted ? 30 : 100 + tier * 180 + random.Next(200)));
            if (!looted) {
                result.Add(new TreasureLootEntry("GrechkaTreasures_" + Gems[random.Next(Gems.Length)] + "Raw", 4 + random.Next(3 + tier * 4)));
                if (tier >= 3 && random.Next(100) < 35) result.Add(new TreasureLootEntry("GrechkaTreasures_" + gem + "Monolith", 1));
                if (random.Next(100) < 25) result.Add(new TreasureLootEntry("GrechkaTreasures_TreasureMap", 1));
            }
            return result;
        }
    }
}
