using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Quests
{
    // Recompute only goals referring to this item after the game finishes
    // changing its location or stack size. The regular 250-tick scan remains
    // a reconciliation path for mods that bypass these standard methods.
    [HarmonyPatch(typeof(Thing), "SpawnSetup", new Type[] { typeof(Map), typeof(bool) })]
    internal static class SpawnedItemHook
    {
        static void Postfix(Thing __instance)
        {
            QuestGame.Current?.ObservePotentialItem(__instance.def);
            StorageInventoryAdapter.ObserveCarrier(__instance);
        }
    }

    // A completed building or one claimed from another faction must be
    // observed immediately; the periodic scan is only reconciliation.
    [HarmonyPatch(typeof(Building), "SpawnSetup", new Type[] { typeof(Map), typeof(bool) })]
    internal static class SpawnedBuildingHook
    {
        static void Postfix(Building __instance)
        {
            QuestGame.Current?.ObservePotentialBuilding(__instance.def);
            StorageInventoryAdapter.ObserveCarrier(__instance);
        }
    }

    [HarmonyPatch(typeof(Building), "SetFaction", new Type[] { typeof(Faction), typeof(Pawn) })]
    internal static class ClaimedBuildingHook
    {
        static void Postfix(Building __instance)
        {
            QuestGame.Current?.ObservePotentialBuilding(__instance.def);
            StorageInventoryAdapter.ObserveCarrier(__instance);
        }
    }

    [HarmonyPatch(typeof(ThingOwner), "NotifyAdded", new Type[] { typeof(Thing) })]
    internal static class AddedItemHook
    {
        static void Postfix(Thing __0)
        {
            QuestGame.Current?.ObservePotentialItem(__0?.def);
            if (__0 != null) StorageInventoryAdapter.ObserveCarrier(__0);
        }
    }

    [HarmonyPatch(typeof(Thing), "TryAbsorbStack", new Type[] { typeof(Thing), typeof(bool) })]
    internal static class MergedMapStackHook
    {
        static void Prefix(Thing __instance, out int __state)
        {
            __state = __instance.stackCount;
        }
        static void Postfix(Thing __instance, int __state)
        {
            if (__instance.stackCount > __state) QuestGame.Current?.ObservePotentialItem(__instance.def);
        }
    }

    // Vanilla's map stack-change notification also reaches ASF storage. Some
    // mods use it after changing stackCount directly without TryAbsorbStack.
    [HarmonyPatch(typeof(ListerMergeables), "Notify_ThingStackChanged", new Type[] { typeof(Thing) })]
    internal static class ChangedMapStackHook
    {
        static void Postfix(Thing __0)
        {
            QuestGame.Current?.ObservePotentialItem(__0?.def);
        }
    }
}
