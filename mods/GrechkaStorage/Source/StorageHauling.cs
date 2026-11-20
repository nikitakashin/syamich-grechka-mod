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
            return pawn.Map.listerBuildings.allBuildingsColonist.Where(t => t is Building_StorageController || t is Building_DiskRack);
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
                foreach (StorageDisk disk in rack.Disks.Where(d => d.IntakePaused && d.Items.Any() && d.HaulSourceEnabled))
                    foreach (Thing item in disk.Items)
                    {
                        int count = System.Math.Min(item.stackCount, System.Math.Min(item.def.stackLimit, pawn.carryTracker.AvailableStackSpace(item.def)));
                        if (count <= 0 || !pawn.CanReserve(item, 1, count)) continue;
                        Job unload = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Grechka_UnloadDisk"), rack, item, disk);
                        unload.count = count;
                        return unload;
                    }
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
                    if (!capacity.TryGetValue(key, out long free)) capacity[key] = free = disks.Where(d => !d.IntakePaused).Sum(d => d.Space.MaxAccept(key));
                    int count = (int)System.Math.Min(order.Count, System.Math.Min(source.stackCount, free));
                    count = System.Math.Min(count, pawn.carryTracker.AvailableStackSpace(source.def));
                    if (count <= 0 || DiskItemPolicy.Problem(source) != null
                        || !pawn.CanReserveAndReach(source, PathEndMode.ClosestTouch, Danger.Some)) continue;
                    Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Grechka_ImportStorage"), source, controller, controller);
                    job.count = count; return job;
                }
                Job automatic = controller.Output.Contents.Any ? null : controller.AutomaticImportJob(pawn);
                if (automatic != null) return automatic;
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
        private Building_StorageController Controller { get { return job.targetB.Thing as Building_StorageController; } }
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref orderId, "grechkaOrder", 0); }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (Controller != null)
            {
                Thing source = job.targetA.Thing;
                StorageOrder order = job.targetC.Thing is StorageDisk ? null : Controller.FindOrder(source, Controller);
                if (order != null) orderId = order.Id;
                else
                {
                    // Jobs queued by older saves still point at the controller in target C.
                    StorageDisk disk = job.targetC.Thing as StorageDisk ?? Controller.PreferredDisk(source, Controller.ActiveDisks());
                    if (disk == null || !CanDeliver(disk, source)
                        || StoreUtility.CurrentStoragePriorityOf(source) >= Controller.SettingsFor(disk.Frozen).Priority) return false;
                    job.targetC = disk;
                }
            }
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, job.count, null, errorOnFailed)
                && pawn.Reserve(job.GetTarget(TargetIndex.B), job, 1, -1, null, errorOnFailed);
        }
        private bool CanDeliver(StorageDisk disk, Thing item) =>
            disk != null && Controller.ActiveDisks().Contains(disk) && disk.Acceptable(item) > 0
            && Controller.SettingsFor(disk.Frozen).AllowedToAccept(item);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => Controller != null && (!Controller.Online() || (orderId != 0 && Controller.Order(orderId) == null)));
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
                else if (orderId != 0) Controller?.CompleteInput(orderId, carried);
                else if (Controller != null && carried != null)
                {
                    StorageDisk destination = job.targetC.Thing as StorageDisk ?? Controller.PreferredDisk(carried, Controller.ActiveDisks());
                    if (CanDeliver(destination, carried)) destination.Accept(carried, job.count);
                }
            });
        }
    }

    public sealed class JobDriver_UnloadDisk : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(job.targetB, job, 1, job.count, null, errorOnFailed);
        }

        internal static int TakeLoad(Pawn hauler, StorageDisk disk, Building_DiskRack rack, Thing item, int count)
        {
            if (disk == null || rack == null || disk.ParentHolder != rack || !disk.IntakePaused
                || !disk.HaulSourceEnabled || item?.ParentHolder != disk || hauler.carryTracker.CarriedThing != null) return 0;
            return disk.Withdraw(item, System.Math.Min(count, hauler.carryTracker.AvailableStackSpace(item.def)), hauler.carryTracker.innerContainer);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            AddFinishAction(_ => StorageHauling.Drop(pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                int moved = TakeLoad(pawn, job.targetC.Thing as StorageDisk, job.targetA.Thing as Building_DiskRack, job.targetB.Thing, job.count);
                if (moved == 0) { EndJobWith(JobCondition.Incompletable); return; }
                Thing carried = pawn.carryTracker.CarriedThing;
                if (StoreUtility.TryFindBestBetterStoreCellFor(carried, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out IntVec3 cell)
                    && pawn.Reserve(cell, job)) job.targetB = cell;
                else job.targetB = pawn.Position;
            });
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            yield return Toils_General.Do(() =>
            {
                if (!pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Direct, out _)) StorageHauling.Drop(pawn);
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
