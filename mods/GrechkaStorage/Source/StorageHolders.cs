using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace Grechka.Storage
{
    public sealed class MinifiedStorageDevice : MinifiedThing
    {
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            if (InnerThing is Building_DiskRack || InnerThing is Building_StorageController controller && controller.HasRecovery)
            { absorbed = true; return; }
            base.PreApplyDamage(ref dinfo, out absorbed);
        }
    }
    public sealed class StorageBuffer : IThingHolder, IExposable
    {
        private readonly IThingHolder parent;
        private ThingOwner<Thing> contents;
        private bool cold;
        private int lastRotTick = -1;
        public long Version { get; private set; }
        internal bool TickingRot { get; private set; }
        internal bool Destroyed { get { return parent is Thing thing && thing.Destroyed; } }
        public bool Cold { get { return cold; } }
        public ThingOwner<Thing> Contents { get { return contents; } }
        public IThingHolder ParentHolder { get { return parent; } }
        public StorageBuffer(IThingHolder parent)
        {
            this.parent = parent; contents = new ThingOwner<Thing>(this, false, LookMode.Deep); contents.dontTickContents = true;
            contents.OnContentsChanged += () => Version++;
        }
        public StorageBuffer(IThingHolder parent, bool cold) : this(parent) { this.cold = cold; }
        public void ExposeData()
        {
            Scribe_Values.Look(ref cold, "cold", false);
            Scribe_Deep.Look(ref contents, "contents", this);
            Scribe_Values.Look(ref lastRotTick, "rotTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && contents == null) contents = new ThingOwner<Thing>(this, false, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            { contents.dontTickContents = true; contents.OnContentsChanged += () => Version++; Version++; Register(); }
        }
        internal void Register()
        {
            if (Current.Game == null) return;
            Current.Game.GetComponent<StorageDiskClock>().Register(this);
            if (lastRotTick < 0) lastRotTick = Find.TickManager.TicksGame;
        }
        public void SettleRot()
        {
            if (Current.Game == null || Destroyed) return;
            int now = Find.TickManager.TicksGame, elapsed = lastRotTick < 0 ? 0 : Math.Max(0, now - lastRotTick);
            lastRotTick = now;
            if (cold || elapsed == 0) return;
            TickingRot = true;
            try { foreach (Thing thing in contents.ToList()) (thing as ThingWithComps)?.GetComp<CompRottable>()?.CompTickInterval(elapsed); }
            finally { TickingRot = false; }
        }
        public ThingOwner GetDirectlyHeldThings() { return contents; }
        public void GetChildHolders(List<IThingHolder> children) { ThingOwnerUtility.AppendThingHoldersFromThings(children, contents); }
    }

    public sealed class Building_DiskRack : Building, IThingHolder
    {
        public const int Slots = 10;
        private readonly Graphic[] stateGraphics = new Graphic[3];
        public override Graphic Graphic
        {
            get
            {
                int state = disks.Count == 0 ? 0 : disks.Count >= Slots ? 2 : 1;
                return stateGraphics[state] ??= GraphicDatabase.Get<Graphic_Single>(
                    "Buildings/Map/StorageDiskBlock/StorageDiskBlock_" + new[] { "Empty", "Partial", "Full" }[state],
                    ShaderDatabase.Cutout, Vector2.one, Color.white);
            }
        }
        private void RefreshDiskGraphic() { if (Spawned) Map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Buildings); }
        private ThingOwner<Thing> disks;
        private List<StorageDisk> installRequests = new List<StorageDisk>();
        private List<StorageDisk> removeRequests = new List<StorageDisk>();
        public IEnumerable<StorageDisk> InstallRequests { get { return installRequests; } }
        public IEnumerable<StorageDisk> RemoveRequests { get { return removeRequests; } }
        public IEnumerable<StorageDisk> Disks { get { return disks.OfType<StorageDisk>(); } }
        public bool HasSlot { get { return disks.Count < Slots; } }
        public Building_DiskRack() { disks = new ThingOwner<Thing>(this, false, LookMode.Deep); }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref disks, "grechkaInstalledDisks", this);
            Scribe_Collections.Look(ref installRequests, "grechkaInstallRequests", LookMode.Reference);
            Scribe_Collections.Look(ref removeRequests, "grechkaRemoveRequests", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && disks == null) disks = new ThingOwner<Thing>(this, false, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (installRequests == null) installRequests = new List<StorageDisk>();
                if (removeRequests == null) removeRequests = new List<StorageDisk>();
            }
        }
        public bool Install(StorageDisk disk)
        {
            if (!HasSlot || disk?.holdingOwner == null || disks.Contains(disk)) return false;
            disk.SettleRot();
            bool moved = disk.holdingOwner.TryTransferToContainer(disk, disks, false);
            if (moved) { installRequests.Remove(disk); disk.SealedContents = false; RefreshDiskGraphic(); }
            return moved;
        }
        public bool Remove(StorageDisk disk, ThingOwner destination)
        {
            if (!disks.Contains(disk)) return false;
            disk.SettleRot();
            bool moved = disks.TryTransferToContainer(disk, destination, false);
            if (moved) { removeRequests.Remove(disk); RefreshDiskGraphic(); }
            return moved;
        }
        public void RequestInstall(StorageDisk disk) { if (disk != null && !installRequests.Contains(disk)) installRequests.Add(disk); }
        public void RequestRemove(StorageDisk disk) { if (disks.Contains(disk) && !removeRequests.Contains(disk)) removeRequests.Add(disk); }
        public void CancelRequests(StorageDisk disk) { installRequests.Remove(disk); removeRequests.Remove(disk); }
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed) { absorbed = true; }
        public override string GetInspectString() { return base.GetInspectString() + "\nДиски: " + disks.Count + "/" + Slots; }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (!Spawned || Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action { defaultLabel = "Диски", defaultDesc = "Установка и извлечение дисков пешками.",
                icon = def.uiIcon, action = () => Find.WindowStack.Add(new Window_DiskRack(this)) };
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Destroyed) return;
            if (mode == DestroyMode.Deconstruct && disks.Any)
            {
                if (!Spawned || !disks.TryDropAll(Position, Map, ThingPlaceMode.Near))
                {
                    Messages.Message("Освободите место для извлечения дисков перед разборкой.", MessageTypeDefOf.RejectInput);
                    return;
                }
            }
            else if (disks.Any) disks.ClearAndDestroyContents(mode);
            base.Destroy(mode);
        }
        public ThingOwner GetDirectlyHeldThings() { return disks; }
        public void GetChildHolders(List<IThingHolder> children) { ThingOwnerUtility.AppendThingHoldersFromThings(children, disks); }
    }

    public enum StorageOrderKind { Import, InstallDisk, RemoveDisk }
    public sealed class StorageOrder : IExposable
    {
        public int Id;
        public StorageOrderKind Kind;
        public Thing Source;
        public Building_DiskRack Rack;
        public int Count;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref Kind, "kind"); Scribe_Values.Look(ref Count, "count");
            Scribe_References.Look(ref Source, "source"); Scribe_References.Look(ref Rack, "rack");
        }
    }

    public sealed class Building_StorageController : Building, IThingHolder
    {
        public const int OutputStacks = 32;
        private ThingOwner<Thing> root;
        private StorageBuffer output;
        private StorageBuffer recovery;
        private StorageBuffer coldRecovery;
        private List<StorageOrder> orders = new List<StorageOrder>();
        private int nextOrder = 1;
        public StorageBuffer Output { get { return output; } }
        public StorageBuffer Recovery { get { return recovery; } }
        public StorageBuffer ColdRecovery { get { return coldRecovery; } }
        public IEnumerable<StorageOrder> Orders { get { return orders; } }
        public CompSignalNode Node { get { return GetComp<CompSignalNode>(); } }
        public bool HasRecovery { get { return recovery.Contents.Any || coldRecovery.Contents.Any; } }
        public Building_StorageController()
        {
            root = new ThingOwner<Thing>(this, false, LookMode.Deep);
            output = new StorageBuffer(this); recovery = new StorageBuffer(this); coldRecovery = new StorageBuffer(this, true);
        }
        public override void PostMake() { base.PostMake(); output.Register(); recovery.Register(); coldRecovery.Register(); }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref output, "grechkaOutput", this);
            Scribe_Deep.Look(ref recovery, "grechkaRecovery", this);
            Scribe_Deep.Look(ref coldRecovery, "grechkaColdRecovery", this);
            Scribe_Collections.Look(ref orders, "grechkaHaulingOrders", LookMode.Deep);
            Scribe_Values.Look(ref nextOrder, "grechkaNextOrder", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (output == null) output = new StorageBuffer(this);
                if (recovery == null) recovery = new StorageBuffer(this);
                if (coldRecovery == null) coldRecovery = new StorageBuffer(this, true);
                if (orders == null) orders = new List<StorageOrder>();
                Prune();
            }
        }
        public override void TickRare()
        {
            base.TickRare(); output.Contents.DoTick(); recovery.Contents.DoTick(); coldRecovery.Contents.DoTick(); Prune();
        }
        public void Prune() { orders.RemoveAll(o => o == null || o.Count <= 0 || o.Source == null || o.Source.Destroyed
            || o.Kind != StorageOrderKind.Import && (o.Rack == null || o.Rack.Destroyed)); }
        public void Queue(Thing source, int count, StorageOrderKind kind, Building_DiskRack rack = null)
        {
            if (source == null || source.Destroyed || count <= 0) return;
            count = Math.Min(count, source.stackCount);
            StorageOrder existing = orders.FirstOrDefault(o => o.Source == source && o.Kind == kind && o.Rack == rack);
            if (existing != null) existing.Count = Math.Min(source.stackCount, Math.Max(existing.Count, count));
            else orders.Add(new StorageOrder { Id = nextOrder++, Source = source, Count = count, Kind = kind, Rack = rack });
        }
        public void Cancel(int id) { orders.RemoveAll(o => o.Id == id); }
        public StorageOrder Order(int id) { return orders.FirstOrDefault(o => o.Id == id); }
        public StorageOrder FindOrder(Thing source, Thing destination)
        {
            return orders.FirstOrDefault(o => o.Source == source && (o.Kind == StorageOrderKind.Import ? destination == this : destination == o.Rack));
        }
        public bool Online()
        {
            CompSignalNode node = Node;
            return Spawned && Faction == Faction.OfPlayer && DeviceStatus.Ready(this) && node?.Manager != null
                && node.Manager.ControllersIn(node.Manager.NetworkOf(this)).Any(c => DeviceStatus.Ready(c.parent));
        }
        public List<Building_DiskRack> Racks()
        {
            return Node?.Manager == null ? new List<Building_DiskRack>()
                : Node.Manager.ViewFor(Node).Devices.OfType<Building_DiskRack>().Where(DeviceStatus.Ready).OrderBy(r => r.thingIDNumber).ToList();
        }
        public List<StorageDisk> ActiveDisks() { return Online() ? Racks().SelectMany(r => r.Disks).Distinct().ToList() : new List<StorageDisk>(); }
        public int Importable(Thing thing)
        {
            if (!Online() || DiskItemPolicy.Problem(thing) != null) return 0;
            return (int)Math.Min(thing.stackCount, ActiveDisks().Sum(d => (long)d.Acceptable(thing)));
        }
        public int CompleteInput(int id, Thing carried)
        {
            StorageOrder order = Order(id);
            if (order == null || carried == null || !Online()) return 0;
            if (order.Source == null || DiskItemPolicy.Key(order.Source) != DiskItemPolicy.Key(carried)) return 0;
            int count = Math.Min(order.Count, carried.stackCount), moved = 0;
            if (order.Kind == StorageOrderKind.InstallDisk)
            {
                if (carried is StorageDisk disk && Racks().Contains(order.Rack) && order.Rack.Install(disk)) moved = 1;
            }
            else if (order.Kind == StorageOrderKind.Import)
            {
                bool food = (carried as ThingWithComps)?.GetComp<CompRottable>() != null;
                foreach (StorageDisk disk in ActiveDisks().OrderBy(d => d.Frozen == food ? 0 : 1).ThenBy(d => d.Capacity))
                {
                    if (carried.Destroyed || carried.holdingOwner == null || moved >= count) break;
                    moved += disk.Accept(carried, count - moved);
                }
            }
            order.Count -= moved; Prune(); return moved;
        }
        public int Withdraw(StorageDisk disk, Thing thing, int requested)
        {
            if (!Online() || !ActiveDisks().Contains(disk)) return 0;
            return WithdrawIntoOutput(thing, requested, (item, amount) => disk.Withdraw(item, amount, output.Contents));
        }
        public int WithdrawRecovery(StorageBuffer buffer, Thing thing, int requested)
        {
            if (buffer != recovery && buffer != coldRecovery) return 0;
            buffer.SettleRot();
            return WithdrawIntoOutput(thing, requested, (item, amount) => buffer.Contents.TryTransferToContainer(item, output.Contents, amount, false));
        }
        private int WithdrawIntoOutput(Thing thing, int requested, Func<Thing, int, int> move)
        {
            output.SettleRot();
            int total = 0;
            while (requested > 0 && thing != null && !thing.Destroyed && output.Contents.Count < OutputStacks)
            {
                int count = Math.Min(requested, Math.Min(thing.stackCount, Math.Max(1, thing.def.stackLimit)));
                int moved = move(thing, count);
                if (moved <= 0) break;
                total += moved; requested -= moved;
            }
            return total;
        }
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            if (HasRecovery) { absorbed = true; return; }
            base.PreApplyDamage(ref dinfo, out absorbed);
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Destroyed) return;
            if (mode == DestroyMode.Deconstruct && HasRecovery)
            {
                Messages.Message("Сначала извлеките вещи из буфера восстановления.", MessageTypeDefOf.RejectInput); return;
            }
            if (mode != DestroyMode.Vanish && output.Contents.Any && (!Spawned || !output.Contents.TryDropAll(Position, Map, ThingPlaceMode.Near)))
            {
                Messages.Message("Сначала освободите буфер выдачи контроллера.", MessageTypeDefOf.RejectInput); return;
            }
            output.Contents.ClearAndDestroyContents(mode); recovery.Contents.ClearAndDestroyContents(mode); coldRecovery.Contents.ClearAndDestroyContents(mode);
            base.Destroy(mode);
        }
        public ThingOwner GetDirectlyHeldThings() { return root; }
        public void GetChildHolders(List<IThingHolder> children) { children.Add(output); children.Add(recovery); children.Add(coldRecovery); }
    }
}
