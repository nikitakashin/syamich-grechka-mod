using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Grechka.Storage
{
    internal static class StorageMapAccess
    {
        internal static Building_StorageController ControllerFor(StorageDisk disk)
        {
            if (disk.SealedContents || !(disk.ParentHolder is Building_DiskRack rack) || !rack.Spawned || !DeviceStatus.Ready(rack)) return null;
            return rack.Map.listerBuildings.allBuildingsColonist.OfType<Building_StorageController>()
                .Where(c => c.ActiveDisks().Contains(disk)).OrderByDescending(c => c.SettingsFor(disk.Frozen).Priority).ThenBy(c => c.thingIDNumber).FirstOrDefault();
        }

        internal static IEnumerable<Thing> Available(Map map)
        {
            if (map == null) yield break;
            var seen = new HashSet<StorageDisk>();
            foreach (var controller in map.listerBuildings.allBuildingsColonist.OfType<Building_StorageController>())
                foreach (StorageDisk disk in controller.ActiveDisks())
                {
                    if (!seen.Add(disk) || disk.SealedContents) continue;
                    disk.SettleRot();
                    foreach (Thing item in disk.Items)
                        if (!item.Destroyed && item.stackCount > 0) yield return item;
                }
        }
        internal static bool CanCollect(Pawn pawn, Thing item, Danger danger)
        {
            return pawn.CanReserve(item) && PickupPoint(pawn, item, danger) != null;
        }

        internal static Thing DeliveryFromDisk(Pawn pawn, IConstructible site, bool forced, out int amount)
        {
            amount = 0;
            var needed = new Dictionary<ThingDef, int>();
            foreach (var cost in site.TotalMaterialCost())
            {
                int count = !forced && site is IHaulEnroute enroute
                    ? enroute.GetSpaceRemainingWithEnroute(cost.thingDef, pawn) : site.ThingCountNeeded(cost.thingDef);
                if (count > 0) needed[cost.thingDef] = count;
            }
            if (needed.Count == 0) return null;
            foreach (Thing item in Available(pawn.Map).Where(t => needed.ContainsKey(t.def))
                .OrderBy(t => t.PositionHeld.DistanceToSquared(pawn.Position)))
            {
                if (!CanCollect(pawn, item, forced ? Danger.Deadly : pawn.NormalMaxDanger())) continue;
                amount = Math.Min(needed[item.def], Math.Min(item.stackCount, pawn.carryTracker.AvailableStackSpace(item.def)));
                if (amount <= 0) continue;
                return item;
            }
            return null;
        }

        internal static Thing PickupPoint(Pawn pawn, Thing item, Danger danger)
        {
            if (!(item?.ParentHolder is StorageDisk disk) || !(disk.ParentHolder is Building_DiskRack rack)
                || !rack.Spawned || rack.Map != pawn.Map || disk.SealedContents || !DeviceStatus.Ready(rack)
                || item.IsForbidden(pawn) || rack.IsForbidden(pawn)) return null;
            bool Accessible(Thing endpoint) => !endpoint.IsForbidden(pawn) && pawn.CanReach(endpoint, PathEndMode.Touch, danger);
            var connected = rack.Map.listerBuildings.allBuildingsColonist.OfType<Building_StorageController>()
                .Where(c => c.Online() && c.Racks().Contains(rack));
            if (Accessible(rack)) return connected.Any() ? rack : null;
            return connected.Where(Accessible).OrderBy(c => c.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
        }
    }

    // Native container pickup normally walks to the spawned parent (the rack).
    // A reachable controller is also a physical access point for the same disk.
    [HarmonyPatch(typeof(Toils_Goto), nameof(Toils_Goto.GotoThing), new[] { typeof(TargetIndex), typeof(PathEndMode), typeof(bool) })]
    internal static class StorageWorkPickupHook
    {
        private static void Postfix(TargetIndex ind, bool canGotoSpawnedParent, Toil __result)
        {
            if (!canGotoSpawnedParent) return;
            var toil = __result;
            Action original = toil.initAction;
            toil.initAction = () =>
            {
                Pawn pawn = toil.actor;
                Thing item = pawn.CurJob.GetTarget(ind).Thing;
                if (!(item?.ParentHolder is StorageDisk)) { original(); return; }
                Thing point = StorageMapAccess.PickupPoint(pawn, item, Danger.Deadly);
                if (point == null) { pawn.jobs.EndCurrentJob(JobCondition.Incompletable); return; }
                pawn.pather.StartPath(point, PathEndMode.Touch);
            };
        }
    }

    // Native HaulToContainer and caravan pickup already know how to collect an
    // item from its real IHaulSource owner. Never spawn copies or rewrite its map.
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ResourceDeliverJobFor")]
    internal static class StorageConstructionHook
    {
        private static void Postfix(Pawn pawn, IConstructible c, bool forced, ref Job __result)
        {
            if (__result != null || pawn?.Map == null || c is Blueprint_Install) return;
            Thing item = StorageMapAccess.DeliveryFromDisk(pawn, c, forced, out int amount);
            if (item == null) return;
            __result = JobMaker.MakeJob(JobDefOf.HaulToContainer, item, (Thing)c, (Thing)c);
            __result.count = amount;
            __result.haulMode = HaulMode.ToContainer;
            if (FloatMenuMakerMap.makingFor == pawn) JobFailReason.Clear();
        }
    }

    // The scanner rejects a partially supplied site before asking for a delivery job.
    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanGetResources_NewTemp))]
    internal static class StorageConstructionScanHook
    {
        private static void Postfix(Thing thing, Pawn pawn, bool forced, ref bool __result)
        {
            if (!__result && thing is IConstructible site && !(thing is Blueprint_Install)
                && pawn.Map != null && thing.Map == pawn.Map)
            {
                __result = StorageMapAccess.DeliveryFromDisk(pawn, site, forced, out _) != null;
            }
        }
    }

    [HarmonyPatch(typeof(ItemAvailability), nameof(ItemAvailability.ThingsAvailableAnywhere))]
    internal static class StorageConstructionAvailabilityHook
    {
        private static void Postfix(Map ___map, ThingDef need, int amount, Pawn pawn, ref bool __result)
        {
            if (__result) return;
            long stored = StorageMapAccess.Available(___map).Where(t => t.def == need && !t.IsForbidden(pawn.Faction)).Sum(t => (long)t.stackCount);
            if (stored == 0) return;
            long onMap = ___map.listerThings.ThingsOfDef(need).Where(t => !t.IsForbidden(pawn)).Sum(t => (long)t.stackCount);
            __result = stored + onMap >= amount;
        }
    }

    [HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.UpdateResourceCounts))]
    internal static class StorageResourceCountHook
    {
        private static void Postfix(Map ___map, Dictionary<ThingDef, int> ___countedAmounts)
        {
            foreach (var group in StorageMapAccess.Available(___map).Where(t => t.def.CountAsResource && !t.IsNotFresh()).GroupBy(t => t.def))
            {
                ___countedAmounts.TryGetValue(group.Key, out int existing);
                ___countedAmounts[group.Key] = (int)Math.Min(int.MaxValue, existing + group.Sum(t => (long)t.stackCount));
            }
        }
    }

    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.Reset))]
    internal static class StorageTradeGoodsHook
    {
        private delegate bool SellPosition(TradeDeal deal, Thing thing, out string reason);
        private static readonly SellPosition inSellablePosition = AccessTools.MethodDelegate<SellPosition>(
            AccessTools.Method(typeof(TradeDeal), "InSellablePosition"));
        private static readonly Action<TradeDeal, Thing, Transactor> add = AccessTools.MethodDelegate<Action<TradeDeal, Thing, Transactor>>(
            AccessTools.Method(typeof(TradeDeal), "AddToTradeables"));

        static void Postfix(TradeDeal __instance, List<Tradeable> ___tradeables, List<string> ___cannotSellReasons)
        {
            ITrader trader = TradeSession.trader;
            Pawn negotiator = TradeSession.playerNegotiator;
            Map map = trader is TradeShip ship ? ship.Map :
                negotiator?.Spawned == true ? negotiator.Map : null;
            if (map == null || trader == null) return;
            var seen = new HashSet<Thing>(___tradeables.SelectMany(row => row.thingsColony));
            foreach (Thing item in StorageMapAccess.Available(map))
            {
                if (!seen.Add(item) || !TradeUtility.PlayerSellableNow(item, trader)) continue;
                // Use the rack only for the vanilla location restrictions. Sale,
                // pricing and grouping use the original item and its real owner.
                if (item.ParentHolder?.ParentHolder is not Building_DiskRack rack) continue;
                if (inSellablePosition(__instance, rack, out string reason))
                    add(__instance, item, Transactor.Colony);
                else if (reason != null && !___cannotSellReasons.Contains(reason))
                    ___cannotSellReasons.Add(reason);
            }
        }
    }

    [HarmonyPatch]
    internal static class StorageUpgradeMaterialsPatch
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("Grechka.WorldIntegration.WorkGiver_FactoryUpgrade");
            return type == null ? null : AccessTools.Method(type, "FindMaterials");
        }
        static bool Prepare() => TargetMethod() != null;
        static void Postfix(Pawn pawn, ThingDef material, Danger danger, ref Thing __result)
        {
            if (__result != null) return;
            __result = StorageMapAccess.Available(pawn.Map).Where(t => t.def == material)
                .OrderBy(t => t.PositionHeld.DistanceToSquared(pawn.Position))
                .FirstOrDefault(t => StorageMapAccess.CanCollect(pawn, t, danger));
        }
    }

    [HarmonyPatch(typeof(TradeDeal), "InSellablePosition")]
    internal static class StorageTradePositionHook
    {
        private static bool Prefix(ref Thing t, ref string reason, ref bool __result)
        {
            if (!(t.ParentHolder is StorageDisk disk)) return true;
            if (disk.ParentHolder is Building_DiskRack rack && rack.Spawned
                && rack.Map == TradeSession.playerNegotiator?.Map && disk.HaulSourceEnabled)
            {
                // Only the position check uses the rack. TradeDeal still groups,
                // prices and transfers the original item from its disk.
                t = rack;
                return true;
            }
            reason = null;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(CaravanFormingUtility), nameof(CaravanFormingUtility.AllReachableColonyItems))]
    internal static class StorageCaravanHook
    {
        private static void Postfix(Map map, bool allowEvenIfReserved, ref List<Thing> __result)
        {
            var seen = new HashSet<Thing>(__result);
            foreach (Thing item in StorageMapAccess.Available(map))
                if (item.def.EverHaulable && item.GetInnerIfMinified().def.canLoadIntoCaravan
                    && (allowEvenIfReserved || !map.reservationManager.IsReservedByAnyoneOf(item, Faction.OfPlayer)) && seen.Add(item))
                    __result.Add(item);
        }
    }

    [HarmonyPatch(typeof(GatherItemsForCaravanUtility), nameof(GatherItemsForCaravanUtility.FindThingToHaul))]
    internal static class StorageCaravanPickupHook
    {
        private static void Postfix(Pawn p, Lord lord, ref Thing __result)
        {
            if (__result != null || !(lord?.LordJob is LordJob_FormAndSendCaravan caravan)) return;
            // A running packing job keeps the pickup mode of its first target. Let it
            // finish loading its carrier; a fresh gather job can then collect from a disk.
            if (p.CurJob?.def == JobDefOf.PrepareCaravan_GatherItems && p.jobs.curDriver is JobDriver_PrepareCaravan_GatherItems) return;
            var needed = new HashSet<Thing>(caravan.transferables.Where(t => GatherItemsForCaravanUtility.CountLeftToTransfer(p, t, lord) > 0).SelectMany(t => t.things));
            __result = StorageMapAccess.Available(p.Map).Where(t => needed.Contains(t)
                && StorageMapAccess.CanCollect(p, t, Danger.Some)).OrderBy(t => t.PositionHeld.DistanceToSquared(p.Position)).FirstOrDefault();
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
    internal static class StorageExternalSplitHook
    {
        private static void Prefix(Thing __instance, out StorageDisk __state)
        {
            __state = __instance.ParentHolder as StorageDisk;
            __state?.SettleRot();
        }
        private static void Postfix(StorageDisk __state) => __state?.ContentsChangedExternally();
    }

    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry), new[] { typeof(Thing), typeof(int), typeof(bool) })]
    internal static class StoragePickupPowerHook
    {
        private static bool Prefix(Thing item, ref int __result)
        {
            if (!(item?.ParentHolder is StorageDisk disk) || disk.HaulSourceEnabled) return true;
            __result = 0;
            return false;
        }
    }
}
