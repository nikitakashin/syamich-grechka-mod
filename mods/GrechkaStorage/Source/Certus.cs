using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
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
        readonly Graphic[] graphics = new Graphic[5];
        int previousStage = -1;
        internal bool Growing => parent.Spawned && pending == 0;
        public bool ReadyToHarvest => pending > 0;
        public int GrowthStage => CertusGrowthStages.Select(progress, Props.cycleTicks, pending);
        private CompProperties_CertusSource Props => (CompProperties_CertusSource)props;
        internal Graphic CurrentGraphic => graphics[GrowthStage] ??= GraphicDatabase.Get<Graphic_Single>(
            "Buildings/Map/CertusGrowthSource/CertusGrowthSource_" + GrowthStage,
            ShaderDatabase.Cutout, parent.def.graphicData.drawSize, Color.white);
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref progress, "certusGrowth");
            Scribe_Values.Look(ref pending, "certusPending");
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            Refresh();
        }
        internal void Mature()
        {
            progress = 0;
            pending = Props.yield;
            Refresh();
        }
        void Refresh()
        {
            if (!parent.Spawned || previousStage == GrowthStage) return;
            previousStage = GrowthStage;
            parent.Map.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlagDefOf.Buildings);
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
                if (progress >= Props.cycleTicks) Mature();
            }
            Refresh();
        }
        // Called only at the end of a pawn's reserved harvest job. Keep unplaced output ripe.
        internal int Harvest()
        {
            if (!parent.Spawned || pending == 0) return 0;
            ThingDef product = DefDatabase<ThingDef>.GetNamed("Grechka_CertusQuartz");
            if (!CompSpawner.TryFindSpawnCell(parent, product, pending, out IntVec3 cell)) return 0;
            Thing crystal = ThingMaker.MakeThing(product); crystal.stackCount = pending;
            int delivered = 0;
            GenPlace.TryPlaceThing(crystal, cell, parent.Map, ThingPlaceMode.Direct, (thing, amount) =>
            {
                delivered += amount;
                StorageProduction.Report(parent, thing, amount);
            });
            pending -= Math.Min(pending, delivered);
            if (!crystal.Destroyed && !crystal.Spawned) crystal.Destroy();
            if (pending == 0) progress = 0;
            Refresh();
            return delivered;
        }
        public override string CompInspectStringExtra() => pending > 0 ? StorageText.Get("GrechkaStorage_Text105") + pending
            : StorageText.Get("GrechkaStorage_Text106") + ((float)progress / Props.cycleTicks).ToString("P0");
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
            Find.LetterStack.ReceiveLetter(StorageText.Get("GrechkaStorage_Text107"), StorageText.Get("GrechkaStorage_Text108"), LetterDefOf.PositiveEvent, site);
            return true;
        }
    }

    public sealed class SitePartWorker_CertusMeteor : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            CertusMeteorGenerator.Generate(map);
        }
    }
}
