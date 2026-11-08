using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace Grechka.Treasures
{
    public sealed class TreasureSite : Site
    {
        public string layoutId;
        public int treasureSeed;
        public int scenario;
        public bool layoutGenerated;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref layoutId, "treasureLayoutId");
            Scribe_Values.Look(ref treasureSeed, "treasureSeed");
            Scribe_Values.Look(ref scenario, "treasureScenario");
            Scribe_Values.Look(ref layoutGenerated, "treasureLayoutGenerated");
        }
        public bool CanDiscardLead => !HasMap;
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (!CanDiscardLead) yield break;
            yield return new Command_Action {
                defaultLabel = "GrechkaTreasures_DiscardLead".Translate(),
                defaultDesc = "GrechkaTreasures_DiscardLeadDescription".Translate(),
                icon = ContentFinder<UnityEngine.Texture2D>.Get("World/TreasureSite"),
                action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "GrechkaTreasures_DiscardLeadConfirmation".Translate(Label), () => { if (CanDiscardLead) Destroy(); }))
            };
        }
        public override string GetInspectString()
        {
            TreasureLayout layout = TreasureCatalog.Get(layoutId);
            return base.GetInspectString() + (layout == null ? "" : "\n" + "GrechkaTreasures_DangerTier".Translate(layout.Tier))
                + "\n" + "GrechkaTreasures_TemporarySite".Translate();
        }
    }

    public static class TreasureSites
    {
        public const int MaximumSites = 4;
        public static bool CanCreate => Current.Game != null && TreasureCatalog.Layouts.Count > 0
            && Find.Maps.Any(m => m.IsPlayerHome)
            && Find.WorldObjects.Sites.Count(s => s is TreasureSite) < MaximumSites;

        public static bool TryCreate(out TreasureSite site)
        {
            site = null;
            if (!CanCreate || !TileFinder.TryFindNewSiteTile(out PlanetTile tile, 2, 14)) return false;
            int seed = Rand.Int;
            // Early colonies receive small sites; greater wealth unlocks the full prepared catalog.
            float wealth = Find.Maps.Where(m => m.IsPlayerHome).Sum(m => m.wealthWatcher.WealthTotal);
            int maxTier = wealth < 40000 ? 2 : wealth < 100000 ? 3 : wealth < 220000 ? 4 : 5;
            TreasureLayout layout = TreasureCatalog.Choose(seed, maxTier);
            if (layout == null) return false;
            Faction enemy = layout.Enemy == "mechanoids" ? Faction.OfMechanoids :
                Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.def.defName == "Pirate" && f.HostileTo(Faction.OfPlayer))
                ?? Faction.OfAncientsHostile;
            site = SiteMaker.MakeSite(DefDatabase<SitePartDef>.GetNamed("GrechkaTreasures_TreasureSitePart"), tile, enemy, true,
                100f + layout.Tier * 180f, DefDatabase<WorldObjectDef>.GetNamed("GrechkaTreasures_TreasureSite")) as TreasureSite;
            if (site == null) return false;
            site.layoutId = layout.Id;
            site.treasureSeed = seed;
            site.scenario = new Random(seed ^ 73521).Next(4);
            site.customLabel = layout.Label;
            site.forceRemoveWorldObjectWhenMapRemoved = true;
            Find.WorldObjects.Add(site);
            return true;
        }
        public static void SendLetter(TreasureSite site)
        {
            TreasureLayout layout = TreasureCatalog.Get(site.layoutId);
            string text = (layout?.Description ?? "") + "\n\n" + "GrechkaTreasures_SiteLetter".Translate(layout?.Tier ?? 1)
                + "\n\n" + "GrechkaTreasures_TemporarySite".Translate();
            Find.LetterStack.ReceiveLetter("GrechkaTreasures_NewSite".Translate(), text, LetterDefOf.PositiveEvent, site);
        }
    }

    public sealed class IncidentWorker_TreasureLead : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => base.CanFireNowSub(parms) && TreasureSites.CanCreate;
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!TreasureSites.TryCreate(out TreasureSite site)) return false;
            TreasureSites.SendLetter(site);
            return true;
        }
    }

    public sealed class SitePartWorker_Treasure : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            if (!(map.Parent is TreasureSite site)) return;
            if (site.layoutGenerated) {
                // The site is explicitly disposable when its last map is removed. If a third-party mod
                // nevertheless regenerates that same world object, do not recreate valuable contents.
                Log.Warning("[GrechkaTreasures] Refused to regenerate an already visited treasure layout " + site.ID);
                return;
            }
            TreasureLayout layout = TreasureCatalog.Get(site.layoutId);
            if (layout == null) { Log.Error("[GrechkaTreasures] Missing saved layout " + site.layoutId + "; no unrelated layout was substituted."); return; }
            TreasureMapBuilder.Generate(map, site, layout);
            site.layoutGenerated = true;
        }
    }

    public static class TreasureMapBuilder
    {
        public static void Generate(Map map, TreasureSite site, TreasureLayout layout)
        {
            var origin = new IntVec3(map.Center.x - layout.Width / 2, 0, map.Center.z - layout.Height / 2);
            var guards = new List<IntVec3>();
            var spawnedGuards = new List<Pawn>();
            var markers = new List<(char symbol, IntVec3 cell, int index)>();
            // This is a newly generated, owned encounter footprint. No home map is ever modified.
            // All cells are processed before entities so generated walls are not erased on the next row.
            for (int z = 0; z < layout.Height; z++) for (int x = 0; x < layout.Width; x++) {
                char symbol = layout.Rows[z][x];
                if (symbol == ' ') continue;
                IntVec3 cell = origin + new IntVec3(x, 0, layout.Height - z - 1);
                if (!cell.InBounds(map)) continue;
                if (cell.GetThingList(map).Any(t => t is Pawn)) continue;
                foreach (Thing thing in cell.GetThingList(map).ToList()) thing.Destroy(DestroyMode.Vanish);
                map.roofGrid.SetRoof(cell, null);
                map.terrainGrid.SetTerrain(cell, symbol == '~' ? TerrainDefOf.WaterShallow : symbol == '=' ? TerrainDefOf.Bridge :
                    symbol == ',' ? TerrainDefOf.FlagstoneSandstone : TerrainDefOf.Concrete);
                markers.Add((symbol, cell, x + z * layout.Width));
            }
            foreach (var marker in markers) {
                char symbol = marker.symbol;
                IntVec3 cell = marker.cell;
                Thing Spawn(string defName, string stuff = null, Faction faction = null) {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (def == null) { Log.Warning("[GrechkaTreasures] Missing site object " + defName); return null; }
                    if (GenAdj.OccupiedRect(cell, Rot4.North, def.size).Any(c => !c.InBounds(map)
                        || c.GetThingList(map).Any(t => t is Pawn || t is Building))) {
                        Log.Warning("[GrechkaTreasures] Skipped obstructed site object " + defName + " at " + cell);
                        return null;
                    }
                    ThingDef material = stuff == null ? null : DefDatabase<ThingDef>.GetNamedSilentFail(stuff);
                    Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? material ?? ThingDefOf.Steel : null);
                    if (faction != null && def.CanHaveFaction) thing.SetFaction(faction);
                    return GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
                }
                if (symbol == '#') Spawn("Wall", "BlocksGranite");
                else if (symbol == '+') Spawn("Door", "Steel");
                else if (symbol == 'T') Spawn("TrapSpike", "Steel", site.Faction);
                else if (symbol == 'G') guards.Add(cell);
                else if (symbol == 'C' || symbol == 'H' || symbol == 'D') {
                    bool decoy = symbol == 'D';
                    Thing chest = Spawn(decoy ? "GrechkaTreasures_DecoyChest" : "GrechkaTreasures_TreasureChest");
                    (chest as ThingWithComps)?.GetComp<CompTreasureChest>()?.InitializeLoot(
                        unchecked(site.treasureSeed + marker.index * 7919), layout.Tier, layout.Gem, decoy,
                        site.scenario == 2 && symbol == 'C');
                }
                else if (symbol == 'W') {
                    Thing sign = Spawn("GrechkaTreasures_WarningSign");
                    var comp = (sign as ThingWithComps)?.GetComp<CompTreasureClue>();
                    if (comp != null) comp.text = layout.Clue;
                }
                else if (site.scenario == 2 && "SMVLRB".Contains(symbol)) { /* Visible valuables were already removed by the previous looters. */ }
                else if (symbol == 'S') Spawn("GrechkaTreasures_" + layout.Gem + "Statue");
                else if (symbol == 'M') Spawn("GrechkaTreasures_" + layout.Gem + "Monolith");
                else if (symbol == 'O') Spawn("GrechkaTreasures_" + layout.Gem + "Ore");
                else if (symbol == 'B') {
                    if (layout.Tier >= 3) Spawn("GrechkaTreasures_" + layout.Gem + "Bed");
                    else Spawn("Bed", "WoodLog");
                }
                else if (symbol == 'V') Spawn("GrechkaTreasures_" + layout.Gem + "Vase");
                else if (symbol == 'L') Spawn("GrechkaTreasures_" + layout.Gem + "Lamp");
                else if (symbol == 'R') Spawn("GrechkaTreasures_" + layout.Gem + "Throne");
                if (symbol == '.' || symbol == 'C' || symbol == 'D' || symbol == 'H')
                    map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
            }
            // Supported ruin roofs only. RoofGrid.SetRoof itself does not validate support;
            // remove unsupported new roof cells before the encounter can begin ticking.
            bool removedRoof;
            do {
                removedRoof = false;
                foreach (var marker in markers.Where(m => m.symbol == '.' || m.symbol == 'C' || m.symbol == 'D' || m.symbol == 'H')) {
                    if (map.roofGrid.RoofAt(marker.cell) != RoofDefOf.RoofConstructed
                        || RoofCollapseUtility.WithinRangeOfRoofHolder(marker.cell, map)) continue;
                    map.roofGrid.SetRoof(marker.cell, null);
                    removedRoof = true;
                }
            } while (removedRoof);
            // Scenario 0: deserted, 1: raider occupation, 2: plundered with a surviving cache,
            // 3: rival looters waiting in ambush. Markers and fixed traps remain in every scenario.
            if (site.scenario != 0) {
                int limit = site.scenario == 2 ? Math.Max(1, layout.Tier / 2) : 1 + layout.Tier * 2;
                string kindName = layout.Enemy == "mechanoids" ? (layout.Tier >= 4 ? "Lancer" : "Scyther") :
                    layout.Tier <= 2 ? "Pirate" : "Mercenary_Gunner";
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(kindName);
                if (kind != null && site.Faction != null)
                    foreach (IntVec3 cell in guards.Take(limit)) {
                        Pawn pawn = PawnGenerator.GeneratePawn(kind, site.Faction, map.Tile);
                        GenSpawn.Spawn(pawn, cell, map);
                        spawnedGuards.Add(pawn);
                    }
                if (spawnedGuards.Count > 0) {
                    LordJob behavior = site.scenario == 3 ? (LordJob)new LordJob_AssaultColony(site.Faction)
                        : new LordJob_DefendPoint(map.Center, 8f, 35f, false, true);
                    LordMaker.MakeNewLord(site.Faction, behavior, map, spawnedGuards);
                }
            }
        }
    }
}
