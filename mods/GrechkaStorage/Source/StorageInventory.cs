using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Grechka.Storage
{
    // Optional read-only boundary. Quests discovers these methods through
    // delegates; neither module references the other's assembly.
    public static class StorageInventory
    {
        private static List<ThingDef> carrierDefs;
        private static int knownDefCount;
        public static IEnumerable<Thing> HeldContents(Thing carrier)
        {
            Thing inner = carrier?.GetInnerIfMinified();
            foreach (Thing item in FactoryCraftMap.HeldContents(carrier)) yield return item;
            if (inner is StorageDisk disk && !disk.SealedContents)
            {
                foreach (Thing item in disk.Items) yield return item;
            }
            else if (inner is Building_StorageCrafter crafter)
            { foreach (Thing item in crafter.Contents) yield return item; }
            else if (inner is Building_DiskRack rack)
            {
                foreach (StorageDisk installed in rack.Disks)
                {
                    yield return installed;
                    foreach (Thing item in installed.Items) yield return item;
                }
            }
            else if (inner is Building_StorageController controller)
            {
                foreach (StorageBuffer buffer in new[] { controller.Output, controller.Recovery, controller.ColdRecovery })
                    foreach (Thing item in buffer.Contents) yield return item;
            }
        }
        public static IEnumerable<Thing> MapContents(Map map)
        {
            if (map == null) yield break;
            List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            if (carrierDefs == null || knownDefCount != all.Count)
            {
                knownDefCount = all.Count;
                carrierDefs = all.Where(d => d.thingClass != null && (typeof(StorageDisk).IsAssignableFrom(d.thingClass)
                    || typeof(Building_DiskRack).IsAssignableFrom(d.thingClass)
                    || typeof(Building_StorageController).IsAssignableFrom(d.thingClass)
                    || typeof(Building_StorageCrafter).IsAssignableFrom(d.thingClass)
                    || d.defName == "Grechka_PackedStorageDevice")).ToList();
            }
            foreach (ThingDef def in carrierDefs)
                foreach (Thing carrier in map.listerThings.ThingsOfDef(def))
                {
                    if (carrier.Position.Fogged(map) || (carrier is Building ? carrier.Faction != Faction.OfPlayer : !map.IsPlayerHome)) continue;
                    foreach (Thing item in HeldContents(carrier)) yield return item;
                }
        }
    }
}
