using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grechka.Storage
{
    public class ITab_RegularDisks : ITab_Storage
    {
        protected virtual bool Frozen => false;
        public ITab_RegularDisks() { labelKey = "GrechkaStorage_RegularTab"; }
        protected override IStoreSettingsParent SelStoreSettingsParent =>
            (SelObject as Building_StorageController)?.TabParent(Frozen);
        public override bool IsVisible => base.IsVisible && ((Building_StorageController)SelObject).HasDiskType(Frozen);
    }

    public sealed class ITab_FrozenDisks : ITab_RegularDisks
    {
        protected override bool Frozen => true;
        public ITab_FrozenDisks() { labelKey = "GrechkaStorage_FrozenTab"; }
    }

    public sealed partial class Building_StorageController : IStoreSettingsParent
    {
        private sealed class FrozenTabParent : IStoreSettingsParent
        {
            private readonly Building_StorageController controller;
            public FrozenTabParent(Building_StorageController controller) { this.controller = controller; }
            public bool StorageTabVisible => true;
            public StorageSettings GetStoreSettings() => controller.SettingsFor(true);
            public StorageSettings GetParentStoreSettings() => controller.GetParentStoreSettings();
            public void Notify_SettingsChanged() { }
        }

        private FrozenTabParent frozenParent;
        private StorageSettings regularSettings, frozenSettings, legacySettings;
        internal IStoreSettingsParent TabParent(bool frozen) =>
            frozen ? (IStoreSettingsParent)(frozenParent ??= new FrozenTabParent(this)) : this;
        internal bool HasDiskType(bool frozen) => Node?.Manager?.ViewFor(Node).Devices
            .OfType<Building_DiskRack>().SelectMany(r => r.Disks).Any(d => d.Frozen == frozen) == true;

        public bool StorageTabVisible => true;
        public StorageSettings GetStoreSettings() => SettingsFor(false);
        public StorageSettings SettingsFor(bool frozen) => frozen
            ? frozenSettings ??= CreateSettings(true) : regularSettings ??= CreateSettings(false);
        public StorageSettings GetParentStoreSettings() => def.building?.fixedStorageSettings;
        public void Notify_SettingsChanged() { }

        private StorageSettings CreateSettings(bool frozen)
        {
            var settings = new StorageSettings(TabParent(frozen));
            if (legacySettings != null) settings.CopyFrom(legacySettings);
            foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                bool allowed = item.category == ThingCategory.Item && item.stackLimit > 1
                    && (item.GetCompProperties<CompProperties_Rottable>() != null) == frozen;
                if (legacySettings == null || !allowed) settings.filter.SetAllow(item, allowed);
            }
            return settings;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (!Spawned || Faction != Faction.OfPlayer) yield break;
            foreach (bool frozen in new[] { false, true })
            {
                if (!HasDiskType(frozen)) continue;
                string label = StorageText.Get(frozen ? "GrechkaStorage_FrozenTab" : "GrechkaStorage_RegularTab");
                foreach (Gizmo gizmo in StorageSettingsClipboard.CopyPasteGizmosFor(SettingsFor(frozen)))
                {
                    if (gizmo is Command command)
                    {
                        command.defaultLabel += " (" + label + ")";
                        if (frozen && HasDiskType(false)) command.hotKey = null;
                    }
                    yield return gizmo;
                }
            }
        }

        private void ExposeStorageSettings()
        {
            if (Scribe.mode != LoadSaveMode.Saving)
                Scribe_Deep.Look(ref legacySettings, "grechkaStorageSettings", this);
            Scribe_Deep.Look(ref regularSettings, "grechkaRegularStorageSettings", this);
            Scribe_Deep.Look(ref frozenSettings, "grechkaFrozenStorageSettings", TabParent(true));
            if (Scribe.mode == LoadSaveMode.PostLoadInit && legacySettings != null)
            {
                SettingsFor(false);
                SettingsFor(true);
                legacySettings = null;
            }
        }

        internal StorageDisk PreferredDisk(Thing item, IEnumerable<StorageDisk> disks)
        {
            if (DiskItemPolicy.Problem(item) != null) return null;
            return disks.Where(d => !d.IntakePaused && SettingsFor(d.Frozen).AllowedToAccept(item) && d.Acceptable(item) > 0)
                .OrderByDescending(d => SettingsFor(d.Frozen).Priority)
                .ThenByDescending(d => d.Frozen == (item.def.GetCompProperties<CompProperties_Rottable>() != null))
                .ThenBy(d => d.Capacity).FirstOrDefault();
        }

        internal Job AutomaticImportJob(Pawn pawn)
        {
            List<StorageDisk> disks = ActiveDisks();
            if (!disks.Any(d => !d.IntakePaused)) return null;
            foreach (Thing source in Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
                .OrderBy(t => t.Position.DistanceToSquared(pawn.Position)))
            {
                if (!source.Spawned || source.IsForbidden(pawn) || source.IsBurning()) continue;
                StorageDisk disk = PreferredDisk(source, disks);
                if (disk == null) continue;
                StoragePriority priority = SettingsFor(disk.Frozen).Priority;
                if (StoreUtility.CurrentStoragePriorityOf(source) >= priority
                    || !HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, source, false)
                    || !pawn.CanReserveAndReach(source, PathEndMode.ClosestTouch, Danger.Some)) continue;
                if (priority < StoragePriority.Critical && StoreUtility.TryFindBestBetterStorageFor(source, pawn, Map, priority,
                    pawn.Faction, out _, out _, false)) continue;
                if (priority < StoragePriority.Critical && Map.listerBuildings.allBuildingsColonist.OfType<Building_StorageController>()
                    .Any(c => c != this && c.PreferredDisk(source, c.ActiveDisks()) is StorageDisk other
                        && c.SettingsFor(other.Frozen).Priority > priority && pawn.CanReach(c, PathEndMode.Touch, Danger.Some))) continue;
                int count = System.Math.Min(source.stackCount, System.Math.Min(disk.Acceptable(source), pawn.carryTracker.AvailableStackSpace(source.def)));
                if (count <= 0) continue;
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Grechka_ImportStorage"), source, this, disk);
                job.count = count;
                return job;
            }
            return null;
        }
    }
}
