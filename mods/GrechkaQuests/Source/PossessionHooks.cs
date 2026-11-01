using System;
using HarmonyLib;
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
        }
    }

    [HarmonyPatch(typeof(ThingOwner), "NotifyAdded", new Type[] { typeof(Thing) })]
    internal static class AddedItemHook
    {
        static void Postfix(Thing __0)
        {
            QuestGame.Current?.ObservePotentialItem(__0?.def);
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
}
