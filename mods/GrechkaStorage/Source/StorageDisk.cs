using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Storage
{
    public sealed class StorageDiskProperties : DefModExtension
    {
        public long capacityBytes = 1024;
        public bool freezing;
        public override IEnumerable<string> ConfigErrors()
        {
            if (capacityBytes < 128 || capacityBytes % 128 != 0 || capacityBytes > 256L * 1024 * 1024)
                yield return "Storage disk capacity must be a multiple of 128, up to 256 MiB.";
        }
    }

    public static class DiskItemPolicy
    {
        public static DiskItemKey Key(Thing thing)
        {
            Thing inner = thing.GetInnerIfMinified();
            CompQuality quality = (inner as ThingWithComps)?.GetComp<CompQuality>();
            return new DiskItemKey(inner.def.defName, inner.Stuff?.defName, quality == null ? -1 : (int)quality.Quality);
        }
        public static string Problem(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.stackCount <= 0) return "Предмет недоступен";
            if (thing is StorageDisk) return "Диски устанавливаются отдельно в дисковый блок";
            if (thing.def.category != ThingCategory.Item || thing is Pawn || thing is Corpse || thing is UnfinishedThing)
                return "Этот объект нельзя хранить на диске";
            if (thing is MinifiedThing minified)
            {
                if (minified.InnerThing == null) return "Пустая упаковка";
                if (HasContents(minified.InnerThing))
                    return "Сначала извлеките содержимое упакованного контейнера";
                if ((minified.InnerThing as ThingWithComps)?.GetComp<CompExplosive>() != null)
                    return "Опасное устройство требует отдельного адаптера";
                return null;
            }
            Type type = thing.GetType();
            if (type != typeof(Thing) && type != typeof(ThingWithComps) && !(thing is Apparel))
                return "Особый тип предмета требует адаптера: " + type.Name;
            if (HasContents(thing)) return "Сначала извлеките содержимое вложенного контейнера";
            if (thing is IThingHolder) return "Вложенные контейнеры не поддерживаются";
            if (!(thing is ThingWithComps withComps)) return null;
            foreach (ThingComp comp in withComps.AllComps)
            {
                if (comp is CompExplosive) return "Взрывоопасный предмет нельзя поместить на диск";
                if (comp is CompRottable && comp.GetType() != typeof(CompRottable)) return "Особая порча требует отдельного адаптера";
                if (comp is CompRottable || comp is CompEquippable) continue;
                Type c = comp.GetType();
                if (new[] { nameof(ThingComp.CompTick), nameof(ThingComp.CompTickRare), nameof(ThingComp.CompTickLong), nameof(ThingComp.CompTickInterval) }
                    .Any(name => c.GetMethod(name, name == nameof(ThingComp.CompTickInterval) ? new[] { typeof(int) } : Type.EmptyTypes)?.DeclaringType != typeof(ThingComp)))
                    return "Активный компонент требует адаптера: " + c.Name;
            }
            return null;
        }
        private static bool HasContents(Thing thing)
        {
            var holders = new Queue<IThingHolder>();
            var seen = new HashSet<IThingHolder>();
            if (thing is IThingHolder holder) holders.Enqueue(holder);
            if (thing is ThingWithComps wc) foreach (IThingHolder comp in wc.AllComps.OfType<IThingHolder>()) holders.Enqueue(comp);
            while (holders.Count > 0)
            {
                IThingHolder current = holders.Dequeue();
                if (!seen.Add(current)) continue;
                if (current.GetDirectlyHeldThings()?.Any == true) return true;
                var children = new List<IThingHolder>(); current.GetChildHolders(children);
                foreach (IThingHolder child in children) holders.Enqueue(child);
            }
            return false;
        }
        internal static bool CanCompact(Thing thing)
        {
            return thing.def.stackLimit > 1 && !(thing is MinifiedThing)
                && (!(thing is ThingWithComps wc) || wc.AllComps.All(c => c is CompForbiddable));
        }
    }

    public sealed class StorageDisk : ThingWithComps, IThingHolder
    {
        private ThingOwner<Thing> contents;
        private DiskSpace space;
        private int lastRotTick = -1;
        private bool tickingRot;
        public long Version { get; private set; }
        public long Capacity { get { return def.GetModExtension<StorageDiskProperties>().capacityBytes; } }
        public bool Frozen { get { return def.GetModExtension<StorageDiskProperties>().freezing; } }
        internal bool TickingRot { get { return tickingRot; } }
        public IEnumerable<Thing> Items { get { return contents; } }
        public DiskSpace Space { get { EnsureSpace(); return space; } }
        public bool SealedContents;
        public StorageDisk() { MakeContainer(); }
        private void MakeContainer()
        {
            contents = new ThingOwner<Thing>(this, false, LookMode.Deep);
            contents.dontTickContents = true;
            contents.OnContentsChanged += Changed;
        }
        private void Changed() { space = null; Version++; }
        private void Register()
        {
            if (Current.Game == null) return;
            Current.Game.GetComponent<StorageDiskClock>().Register(this);
            if (lastRotTick < 0) lastRotTick = Find.TickManager.TicksGame;
        }
        public override void PostMake() { base.PostMake(); Register(); }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref contents, "grechkaDiskContents", this);
            Scribe_Values.Look(ref SealedContents, "grechkaSealedContents");
            Scribe_Values.Look(ref lastRotTick, "grechkaDiskRotTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (contents == null) MakeContainer();
                else { contents.dontTickContents = true; contents.OnContentsChanged += Changed; }
                Changed(); Register();
            }
        }
        private void EnsureSpace()
        {
            if (space != null) return;
            space = new DiskSpace(Capacity);
            foreach (var group in contents.GroupBy(DiskItemPolicy.Key))
                space.SetCount(group.Key, group.Sum(t => (long)t.stackCount));
        }
        public int Acceptable(Thing thing)
        {
            return DiskItemPolicy.Problem(thing) == null
                ? (int)Math.Min(int.MaxValue, Space.MaxAccept(DiskItemPolicy.Key(thing))) : 0;
        }
        public int Accept(Thing thing, int requested, bool compact = true)
        { return AcceptWithResult(thing, requested, out Thing _, compact); }
        internal int AcceptWithResult(Thing thing, int requested, out Thing transferred, bool compact = true)
        {
            transferred = null;
            SettleRot();
            int count = Math.Min(requested, Math.Min(thing?.stackCount ?? 0, Acceptable(thing)));
            if (count <= 0 || thing.holdingOwner == null || thing.holdingOwner == contents) return 0;
            int moved = thing.holdingOwner.TryTransferToContainer(thing, contents, count, out transferred, false);
            Changed();
            if (compact && moved > 0) Compact(transferred);
            return moved;
        }
        internal int RestoreFrom(Thing thing, int count)
        {
            if (thing?.holdingOwner == null) return 0;
            int moved = thing.holdingOwner.TryTransferToContainer(thing, contents, count, false);
            Changed(); return moved;
        }
        internal void Compact(Thing incoming)
        {
            if (incoming == null || incoming.Destroyed || !DiskItemPolicy.CanCompact(incoming)) return;
            foreach (Thing other in contents.ToList())
            {
                if (other == incoming || other.Destroyed || !DiskItemPolicy.CanCompact(other)
                    || other.HitPoints != incoming.HitPoints || !other.CanStackWith(incoming)) continue;
                if ((other as ThingWithComps)?.GetComp<CompForbiddable>()?.Forbidden
                    != (incoming as ThingWithComps)?.GetComp<CompForbiddable>()?.Forbidden) continue;
                other.TryAbsorbStack(incoming, false);
                if (incoming.Destroyed || incoming.stackCount == 0) break;
            }
            Changed();
        }
        public int Withdraw(Thing thing, int count, ThingOwner target)
        { return MoveOut(thing, count, target, out Thing _); }
        internal int MoveOut(Thing thing, int count, ThingOwner target, out Thing transferred)
        {
            transferred = null;
            SettleRot();
            if (!contents.Contains(thing) || count <= 0) return 0;
            int moved = contents.TryTransferToContainer(thing, target, Math.Min(count, thing.stackCount), out transferred, false);
            Changed(); return moved;
        }
        public void SettleRot()
        {
            if (Destroyed || Current.Game == null) return;
            AdvanceRot(Find.TickManager.TicksGame);
        }
        internal void AdvanceRot(int now)
        {
            if (Destroyed) return;
            int elapsed = lastRotTick < 0 ? 0 : Math.Max(0, now - lastRotTick);
            lastRotTick = now;
            if (elapsed == 0 || Frozen) return;
            tickingRot = true;
            try
            {
                foreach (Thing thing in contents.ToList())
                    (thing as ThingWithComps)?.GetComp<CompRottable>()?.CompTickInterval(elapsed);
            }
            finally { tickingRot = false; }
        }
        public override string GetInspectString()
        {
            return base.GetInspectString() + (SealedContents ? "\nАрхив расхитителей: состав откроется после установки в блок" : "") + "\nПредметов: " + Space.ItemCount.ToString("N0")
                + "; типов: " + Space.Types + "/63\nПамять: " + Space.UsedBytes.ToString("N0") + "/" + Capacity.ToString("N0")
                + " байт" + (Frozen ? "\nПостоянная заморозка" : "\nОбычная порча по окружающей температуре");
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Destroyed) return;
            contents.ClearAndDestroyContents(mode);
            base.Destroy(mode);
        }
        public ThingOwner GetDirectlyHeldThings() { return contents; }
        public void GetChildHolders(List<IThingHolder> children) { ThingOwnerUtility.AppendThingHoldersFromThings(children, contents); }
    }

    public sealed class StorageDiskClock : GameComponent
    {
        private readonly List<System.WeakReference<StorageDisk>> disks = new List<System.WeakReference<StorageDisk>>();
        private readonly List<System.WeakReference<StorageBuffer>> buffers = new List<System.WeakReference<StorageBuffer>>();
        private readonly ConditionalWeakTable<object, object> registered = new ConditionalWeakTable<object, object>();
        public StorageDiskClock(Game game) { }
        public void Register(StorageDisk disk)
        {
            if (registered.TryGetValue(disk, out object _)) return;
            registered.Add(disk, new object()); disks.Add(new System.WeakReference<StorageDisk>(disk));
        }
        public void Register(StorageBuffer buffer)
        {
            if (registered.TryGetValue(buffer, out object _)) return;
            registered.Add(buffer, new object()); buffers.Add(new System.WeakReference<StorageBuffer>(buffer));
        }
        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0) return;
            for (int i = disks.Count - 1; i >= 0; i--)
            {
                if (!disks[i].TryGetTarget(out StorageDisk disk) || disk.Destroyed) disks.RemoveAt(i); else disk.SettleRot();
            }
            for (int i = buffers.Count - 1; i >= 0; i--)
            {
                if (!buffers[i].TryGetTarget(out StorageBuffer buffer) || buffer.Destroyed) buffers.RemoveAt(i); else buffer.SettleRot();
            }
        }
    }

    [HarmonyPatch(typeof(CompRottable), "TickInterval")]
    internal static class DiskRotHook
    {
        private static bool Prefix(CompRottable __instance)
        {
            IThingHolder owner = __instance.parent.holdingOwner?.Owner;
            if (owner is StorageDisk disk) return !disk.Frozen && disk.TickingRot;
            if (owner is StorageBuffer buffer) return !buffer.Cold && buffer.TickingRot;
            return true;
        }
    }

    [HarmonyPatch(typeof(ThingOwnerUtility), nameof(ThingOwnerUtility.TryGetFixedTemperature))]
    internal static class DiskTemperatureHook
    {
        private static bool Prefix(IThingHolder __0, ref float __2, ref bool __result)
        {
            if (!(__0 is StorageDisk disk && disk.Frozen) && !(__0 is StorageBuffer buffer && buffer.Cold)) return true;
            __2 = -10; __result = true; return false;
        }
    }
}
