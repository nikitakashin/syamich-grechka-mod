using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Grechka.Storage
{
    public static class StorageOptimization
    {
        public static string Optimize(Building_StorageController controller)
        {
            if (!controller.Online()) return "Нет работающего подключения.";
            if (controller.HasRecovery) return "Сначала освободите буфер восстановления.";
            List<StorageDisk> disks = controller.ActiveDisks();
            if (disks.Count < 2) return "Для распределения нужны хотя бы два доступных диска.";
            foreach (StorageDisk disk in disks) disk.SettleRot();
            var targets = disks.Select(d => new DiskPlanTarget { Id = d.thingIDNumber, Capacity = d.Capacity, Frozen = d.Frozen }).ToList();
            var groups = disks.SelectMany(d => d.Items.Select(t => new DiskPlanGroup
                { Key = DiskItemPolicy.Key(t), Count = t.stackCount, Frozen = d.Frozen }))
                .GroupBy(g => (g.Key, g.Frozen)).Select(g => new DiskPlanGroup
                    { Key = g.Key.Key, Frozen = g.Key.Frozen, Count = g.Sum(v => v.Count) }).ToList();
            if (!DiskPlanner.TryPlan(targets, groups, out List<DiskPlanMove> plan))
                return "Не удалось подобрать полную раскладку. Текущее содержимое сохранено.";

            StorageBuffer Stage(bool frozen) { return frozen ? controller.ColdRecovery : controller.Recovery; }
            controller.Recovery.SettleRot(); controller.ColdRecovery.SettleRot();
            var origin = new Dictionary<Thing, StorageDisk>();
            var byId = disks.ToDictionary(d => d.thingIDNumber);
            try
            {
                foreach (StorageDisk disk in disks)
                    foreach (Thing thing in disk.Items.ToList())
                    {
                        origin[thing] = disk;
                        int count = thing.stackCount;
                        int moved = disk.MoveOut(thing, count, Stage(disk.Frozen).Contents, out Thing transferred);
                        if (transferred != null) origin[transferred] = disk;
                        if (moved != count) throw new InvalidOperationException("Incomplete staging transfer");
                    }
                foreach (DiskPlanMove move in plan)
                {
                    long remaining = move.Count;
                    StorageDisk destination = byId[move.Target];
                    foreach (Thing thing in Stage(move.Frozen).Contents.Where(t => DiskItemPolicy.Key(t) == move.Key).ToList())
                    {
                        int count = (int)Math.Min(remaining, thing.stackCount);
                        if (count == 0) break;
                        StorageDisk original = origin[thing];
                        int moved = destination.AcceptWithResult(thing, count, out Thing transferred, false);
                        if (transferred != null) origin[transferred] = original;
                        if (moved != count) throw new InvalidOperationException("Incomplete planned transfer");
                        remaining -= moved;
                    }
                    if (remaining != 0) throw new InvalidOperationException("Planned group missing");
                }
                if (controller.HasRecovery) throw new InvalidOperationException("Unplaced staged items");
            }
            catch (Exception error)
            {
                Log.Error("[Grechka Storage] Optimization rolled back: " + error);
                try
                {
                    foreach (StorageDisk disk in disks)
                        foreach (Thing thing in disk.Items.ToList())
                        {
                            int moved = disk.MoveOut(thing, thing.stackCount, Stage(disk.Frozen).Contents, out Thing transferred);
                            if (transferred != null && origin.TryGetValue(thing, out StorageDisk original)) origin[transferred] = original;
                        }
                    foreach (StorageBuffer stage in new[] { controller.Recovery, controller.ColdRecovery })
                        foreach (Thing thing in stage.Contents.ToList())
                            if (origin.TryGetValue(thing, out StorageDisk original) && !original.Destroyed)
                                original.RestoreFrom(thing, thing.stackCount);
                }
                catch (Exception restoreError) { Log.Error("[Grechka Storage] Recovery buffer retained: " + restoreError); }
                return controller.HasRecovery
                    ? "Операция остановлена. Остаток сохранён в буфере восстановления; заморозка сохранена."
                    : "Операция отменена, предметы возвращены на исходные диски.";
            }
            // Compaction is optional after the lossless layout has committed.
            foreach (StorageDisk disk in disks)
                foreach (Thing thing in disk.Items.ToList())
                    if (!thing.Destroyed) disk.Compact(thing);
            return "Предметы распределены по дискам. Свежесть, свойства и холод сохранены.";
        }
        public static long RestoreRecovery(Building_StorageController controller)
        {
            if (!controller.Online()) return 0;
            long total = 0;
            foreach (StorageBuffer stage in new[] { controller.Recovery, controller.ColdRecovery })
            {
                stage.SettleRot();
                foreach (Thing thing in stage.Contents.ToList())
                    foreach (StorageDisk disk in controller.ActiveDisks().Where(d => d.Frozen == stage.Cold).OrderBy(d => d.Capacity))
                    {
                        if (thing.Destroyed || thing.holdingOwner != stage.Contents) break;
                        total += disk.Accept(thing, thing.stackCount);
                    }
            }
            return total;
        }
    }
}
