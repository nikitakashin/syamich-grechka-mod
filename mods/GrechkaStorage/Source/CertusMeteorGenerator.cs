using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Grechka.Storage
{
    internal static class CertusMeteorGenerator
    {
        internal static bool OrdinaryRock(Thing thing) => thing.def.building?.isNaturalRock == true
            && !thing.def.building.isResourceRock && thing.Faction == null;

        internal static int Classify(Map map, IntVec3 cell)
        {
            // Natural trees, host rock and loose chunks are expected at an impact site.
            // Protect pawns, constructed roofs/buildings and existing valuable deposits.
            if (cell.GetRoof(map) is RoofDef roof && !roof.isNatural) return -1;
            foreach (Thing thing in cell.GetThingList(map))
                if (thing is Pawn || (thing.def.category == ThingCategory.Building && !OrdinaryRock(thing))) return -1;
            TerrainDef terrain = cell.GetTerrain(map);
            return terrain == null ? -1 : terrain.IsWater || terrain.passability == Traversability.Impassable ? 1 : 0;
        }

        internal static void Generate(Map map, ThingDef stone = null)
        {
            CertusMeteorLayout layout = CertusMeteorLayout.Create(Rand.Int);
            if (!CertusMeteorSiting.TryFind(map.Size.x, map.Size.z, layout,
                (x, z) => Classify(map, new IntVec3(x, 0, z)), out var anchor, out bool foundation))
                throw new InvalidOperationException("[GrechkaStorage] Certus meteorite cannot fit without overwriting a protected structure or pawn.");
            IntVec3 center = new IntVec3(anchor.x, 0, anchor.z);
            var footprint = layout.Footprint.Select(c => center + new IntVec3(c.x, 0, c.z)).ToHashSet();
            var existing = footprint.SelectMany(c => c.GetThingList(map)).Distinct().ToList();

            // Plan every loose item's destination before changing the map. Keep instances,
            // quantities and special properties; never let rock spawning wipe a chunk/loot.
            var reserved = new HashSet<IntVec3>();
            var moves = new List<(Thing item, IntVec3 target)>();
            foreach (Thing item in existing.Where(t => t.def.category == ThingCategory.Item))
            {
                IntVec3 target = IntVec3.Invalid;
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(center, 40, true).Concat(CellRect.WholeMap(map).Cells))
                {
                    var rect = GenAdj.OccupiedRect(candidate, item.Rotation, item.def.size);
                    if (!rect.Cells.All(c => c.InBounds(map) && !footprint.Contains(c) && !reserved.Contains(c)
                        && c.Standable(map) && !c.GetThingList(map).Any(t => t is Pawn || t is Plant
                            || t.def.category == ThingCategory.Item || t.def.category == ThingCategory.Building))) continue;
                    target = candidate;
                    foreach (IntVec3 cell in rect.Cells) reserved.Add(cell);
                    break;
                }
                if (!target.IsValid)
                    throw new InvalidOperationException("[GrechkaStorage] No safe place to preserve loose items around the certus impact site.");
                moves.Add((item, target));
            }
            // Resolve all defs before any destructive preparation of natural scenery.
            ThingDef ore = DefDatabase<ThingDef>.GetNamed("Grechka_MineableCertus");
            ThingDef sourceDef = DefDatabase<ThingDef>.GetNamed("Grechka_CertusSource");
            stone ??= Find.World.NaturalRockTypesIn(map.Tile)
                .FirstOrDefault(d => d.building?.isNaturalRock == true && !d.building.isResourceRock)
                ?? DefDatabase<ThingDef>.GetNamed("Granite");
            foreach (var move in moves)
            {
                IntVec3 previous = move.item.Position; Rot4 rotation = move.item.Rotation;
                move.item.DeSpawn(DestroyMode.Vanish);
                try { GenSpawn.Spawn(move.item, move.target, map, rotation); }
                catch
                {
                    if (!move.item.Spawned && !move.item.Destroyed) GenSpawn.Spawn(move.item, previous, map, rotation);
                    throw;
                }
            }
            foreach (IntVec3 cell in footprint)
            {
                if (cell.GetRoof(map)?.isNatural == true) map.roofGrid.SetRoof(cell, null);
                if (Classify(map, cell) == 1) map.terrainGrid.SetTerrain(cell, TerrainDefOf.Gravel);
            }
            foreach (Thing thing in existing.Where(t => t is Plant || OrdinaryRock(t)))
                thing.Destroy(DestroyMode.Vanish);
            foreach (var offset in layout.Rocks)
                GenSpawn.Spawn(layout.Ore.Contains(offset) ? ore : stone, center + new IntVec3(offset.x, 0, offset.z), map);
            foreach (var offset in layout.Sources)
            {
                Building source = (Building)ThingMaker.MakeThing(sourceDef);
                source.SetFaction(Faction.OfPlayer);
                source.GetComp<CompCertusSource>().Mature();
                GenSpawn.Spawn(source, center + new IntVec3(offset.x, 0, offset.z), map);
            }
            Log.Message("[GrechkaStorage] Certus meteorite generated: map=" + map.uniqueID + " center=" + center
                + " stone=" + (layout.Rocks.Count - layout.Ore.Count) + " ore=" + layout.Ore.Count
                + " sources=" + layout.Sources.Length + " empty=" + (layout.Footprint.Count - layout.Rocks.Count - layout.Sources.Length)
                + " preservedItems=" + moves.Count + " foundation=" + foundation);
        }
    }
}
