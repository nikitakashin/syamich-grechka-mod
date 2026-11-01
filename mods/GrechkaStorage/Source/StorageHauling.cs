using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grechka.Storage
{
    public sealed class WorkGiver_StorageHaul : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest { get { return ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial); } }
        public override int MaxRegionsToScanBeforeGlobalSearch { get { return 0; } }
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (string name in new[] { "Grechka_StorageController", "Grechka_DiskRack" })
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (def == null) continue;
                foreach (Thing thing in pawn.Map.listerThings.ThingsOfDef(def))
                    if (thing.Faction == Faction.OfPlayer) yield return thing;
            }
        }
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false) { return JobOnThing(pawn, thing, forced) != null; }
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!thing.Spawned || thing.Faction != Faction.OfPlayer || pawn.carryTracker.CarriedThing != null
                || !pawn.CanReserveAndReach(thing, PathEndMode.Touch, Danger.Some)) return null;
            if (thing is Building_DiskRack rack)
            {
                StorageDisk removing = rack.RemoveRequests.FirstOrDefault(d => d != null && !d.Destroyed && rack.Disks.Contains(d));
                if (removing != null) return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Grechka_RemoveDisk"), rack, removing);
                if (!rack.HasSlot) return null;
                StorageDisk installing = rack.InstallRequests.FirstOrDefault(d => d != null && !d.Destroyed && d.Spawned
                    && d.Map == pawn.Map && pawn.CanReserveAndReach(d, PathEndMode.ClosestTouch, Danger.Some));
                if (installing == null) return null;
                Job diskJob = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Grechka_ImportStorage"), installing, rack);
                diskJob.count = 1; return diskJob;
            }
            if (!(thing is Building_StorageController controller)) return null;
            controller.Prune();
            if (controller.Online())
            {
                List<StorageDisk> disks = controller.ActiveDisks();
                var capacity = new Dictionary<DiskItemKey, long>();
                foreach (StorageOrder order in controller.Orders)
                {
                    Thing source = order.Source;
                    if (order.Kind != StorageOrderKind.Import || !source.Spawned || source.Map != pawn.Map
                        || source.IsForbidden(pawn) || source.IsBurning()) continue;
                    DiskItemKey key = DiskItemPolicy.Key(source);
                    if (!capacity.TryGetValue(key, out long free)) capacity[key] = free = disks.Sum(d => d.Space.MaxAccept(key));
                    int count = (int)System.Math.Min(order.Count, System.Math.Min(source.stackCount, free));
                    count = System.Math.Min(count, pawn.carryTracker.AvailableStackSpace(source.def));
                    if (count <= 0 || DiskItemPolicy.Problem(source) != null
                        || !pawn.CanReserveAndReach(source, PathEndMode.ClosestTouch, Danger.Some)) continue;
                    Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Grechka_ImportStorage"), source, controller, controller);
                    job.count = count; return job;
                }
            }
            Thing output = controller.Output.Contents.FirstOrDefault();
            return output == null ? null : JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Grechka_ExportStorage"), controller, output);
        }
    }

    internal static class StorageHauling
    {
        internal static void Drop(Pawn pawn)
        {
            if (pawn.carryTracker.CarriedThing != null)
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
        }
    }

    public sealed class JobDriver_ImportStorage : JobDriver
    {
        private int orderId;
        private Building_StorageController Controller { get { return job.GetTarget(TargetIndex.C).Thing as Building_StorageController; } }
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref orderId, "grechkaOrder", 0); }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (Controller != null)
            {
                StorageOrder order = Controller.FindOrder(job.GetTarget(TargetIndex.A).Thing, job.GetTarget(TargetIndex.B).Thing);
                if (order == null) return false;
                orderId = order.Id;
            }
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, job.count, null, errorOnFailed)
                && pawn.Reserve(job.GetTarget(TargetIndex.B), job, 1, -1, null, errorOnFailed);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => Controller != null && (!Controller.Online() || Controller.Order(orderId) == null));
            AddFinishAction(_ => StorageHauling.Drop(pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, false, false, true, false);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                Thing carried = pawn.carryTracker.CarriedThing;
                if (job.GetTarget(TargetIndex.B).Thing is Building_DiskRack rack && carried is StorageDisk disk)
                {
                    if (rack.InstallRequests.Contains(disk)) rack.Install(disk);
                }
                else Controller?.CompleteInput(orderId, carried);
            });
        }
    }

    public sealed class JobDriver_RemoveDisk : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        { return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed); }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            AddFinishAction(_ => StorageHauling.Drop(pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                var rack = job.GetTarget(TargetIndex.A).Thing as Building_DiskRack;
                var disk = job.GetTarget(TargetIndex.B).Thing as StorageDisk;
                if (rack != null && disk != null && rack.RemoveRequests.Contains(disk)) rack.Remove(disk, pawn.carryTracker.innerContainer);
            });
            yield return Toils_General.Do(() => StorageHauling.Drop(pawn));
        }
    }

    public sealed class JobDriver_ExportStorage : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        { return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed); }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            AddFinishAction(_ => StorageHauling.Drop(pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                var controller = job.GetTarget(TargetIndex.A).Thing as Building_StorageController;
                Thing thing = job.GetTarget(TargetIndex.B).Thing;
                if (controller == null || !controller.Output.Contents.Contains(thing)) return;
                controller.Output.SettleRot();
                int count = System.Math.Min(thing.stackCount, System.Math.Min(thing.def.stackLimit, pawn.carryTracker.AvailableStackSpace(thing.def)));
                if (count > 0) controller.Output.Contents.TryTransferToContainer(thing, pawn.carryTracker.innerContainer, count, false);
            });
            yield return Toils_General.Do(() => StorageHauling.Drop(pawn));
        }
    }
}
