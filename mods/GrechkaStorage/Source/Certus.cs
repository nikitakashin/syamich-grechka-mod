using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Grechka.Storage
{
    public sealed class CompProperties_CertusSource : CompProperties
    {
        public int cycleTicks = 15000;
        public int yield = 4;
        public CompProperties_CertusSource() { compClass = typeof(CompCertusSource); }
    }

    // The progress belongs to the minifiable building, so carrying/reinstalling
    // the source neither duplicates it nor resets a nearly finished cycle.
    public sealed class CompCertusSource : ThingComp
    {
        private int progress;
        private int pending;
        private CompProperties_CertusSource Props => (CompProperties_CertusSource)props;
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref progress, "certusGrowth");
            Scribe_Values.Look(ref pending, "certusPending");
        }
        public override void CompTickRare()
        {
            if (!parent.Spawned) return;
            if (pending == 0)
            {
                int accelerators = GenAdj.CellsAdjacentCardinal(parent).Where(c => c.InBounds(parent.Map))
                    .SelectMany(c => c.GetThingList(parent.Map)).OfType<Building>()
                    .Count(b => b.def.defName == "Grechka_CertusAccelerator" && DeviceStatus.Ready(b));
                progress += 250 * (1 + Math.Min(4, accelerators));
                if (progress >= Props.cycleTicks) { progress -= Props.cycleTicks; pending = Props.yield; }
            }
            if (pending == 0) return;
            ThingDef product = DefDatabase<ThingDef>.GetNamed("Grechka_CertusQuartz");
            if (!CompSpawner.TryFindSpawnCell(parent, product, pending, out IntVec3 cell)) return;
            Thing crystal = ThingMaker.MakeThing(product); crystal.stackCount = pending;
            int delivered = 0;
            GenPlace.TryPlaceThing(crystal, cell, parent.Map, ThingPlaceMode.Direct, (thing, amount) =>
            {
                delivered += amount;
                StorageProduction.Report(parent, thing, amount);
            });
            pending -= Math.Min(pending, delivered);
            if (!crystal.Destroyed && !crystal.Spawned) crystal.Destroy();
        }
        public override string CompInspectStringExtra() => pending > 0 ? "Кристаллы готовы: " + pending
            : "Рост certus: " + ((float)progress / Props.cycleTicks).ToString("P0");
    }

    public sealed class IncidentWorker_CertusMeteor : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => base.CanFireNowSub(parms)
            && Find.Maps.Any(m => m.IsPlayerHome)
            && !Find.WorldObjects.Sites.Any(s => s.parts.Any(p => p.def.defName == "Grechka_CertusMeteor"));
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, 3, 12)) return false;
            Site site = SiteMaker.MakeSite(DefDatabase<SitePartDef>.GetNamed("Grechka_CertusMeteor"), tile, null);
            Find.WorldObjects.Add(site);
            Find.LetterStack.ReceiveLetter("Метеорит certus", "Караван заметил кристаллический метеорит. На месте можно добыть certus-кварц и забрать источники роста для собственной фермы.", LetterDefOf.PositiveEvent, site);
            return true;
        }
    }

    public sealed class SitePartWorker_CertusMeteor : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            IntVec3 center = CellFinder.RandomClosewalkCellNear(map.Center, map, 20);
            ThingDef ore = DefDatabase<ThingDef>.GetNamed("Grechka_MineableCertus");
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, 4, true).Where(c => c.InBounds(map)
                && c.Standable(map) && c.GetEdifice(map) == null).Take(24))
                GenSpawn.Spawn(ore, cell, map);
            for (int i = 0; i < 3; i++)
            {
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(center, map, 9);
                if (cell.GetEdifice(map) != null) continue;
                Building source = (Building)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Grechka_CertusSource"));
                source.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(source, cell, map);
            }
        }
    }
}
