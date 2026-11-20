using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Tweaks
{
    // The scenario prepares its passengers and cargo before several terrain and building
    // generators run. Choose the landing cells after those generators finish.
    [HarmonyPatch(typeof(ScenPart_PlayerPawnsArriveMethod), "DoDropPods")]
    static class StartingDropPods
    {
        static readonly ConditionalWeakTable<Map, List<Action>> pending = new();

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return instructions.MethodReplacer(
                AccessTools.Method(typeof(DropPodUtility), nameof(DropPodUtility.DropThingGroupsNear)),
                AccessTools.Method(typeof(StartingDropPods), nameof(Deliver)));
        }

        static void Deliver(IntVec3 center, Map map, List<List<Thing>> groups, int openDelay,
            bool instaDrop, bool leaveSlag, bool canRoofPunch, bool forbid, bool allowFogged,
            bool canTransfer, Faction faction)
        {
            void Drop() => DropPodUtility.DropThingGroupsNear(center, map, groups, openDelay, instaDrop,
                leaveSlag, canRoofPunch, forbid, allowFogged, canTransfer, faction);

            if (!instaDrop && MapGenerator.mapBeingGenerated == map && Find.GameInitData?.startingTile == map.Tile)
                pending.GetOrCreateValue(map).Add(Drop);
            else
                Drop();
        }

        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
        static class Finish
        {
            static void Postfix(Map __result)
            {
                if (__result == null || !pending.TryGetValue(__result, out var deliveries)) return;
                pending.Remove(__result);
                foreach (var drop in deliveries) drop();
            }
        }
    }
}
