using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grechka.Storage
{
    public sealed class WorkGiver_HarvestCertus : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(DefDatabase<ThingDef>.GetNamed("Grechka_CertusSource"));
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
            => t is ThingWithComps source && source.Spawned && source.Faction == pawn.Faction
                && source.GetComp<CompCertusSource>()?.ReadyToHarvest == true
                && !t.IsForbidden(pawn) && !t.IsBurning()
                && pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some, 1, -1, null, forced);
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
            => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Grechka_HarvestCertus"), t);
    }

    public sealed class JobDriver_HarvestCertus : JobDriver
    {
        CompCertusSource Source => (job.targetA.Thing as ThingWithComps)?.GetComp<CompCertusSource>();
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Source?.ReadyToHarvest != true);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(180, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            Toil harvest = ToilMaker.MakeToil("HarvestCertus");
            harvest.initAction = () => Source?.Harvest();
            harvest.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return harvest;
        }
    }
}
