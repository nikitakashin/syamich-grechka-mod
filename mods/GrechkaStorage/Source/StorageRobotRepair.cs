using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grechka.Storage
{
    // Misc. Robots uses its own material search and separate repair drivers.
    internal static class StorageRobotRepair
    {
        internal static void Install(Harmony harmony)
        {
            Type helper = AccessTools.TypeByName("AIRobot.AIRobot_Helper");
            if (helper == null) return;
            harmony.Patch(AccessTools.Method(helper, "FindAvailableNearbyResources"),
                postfix: new HarmonyMethod(typeof(StorageRobotRepair), nameof(AddDiskMaterials)));
            findNearby = AccessTools.MethodDelegate<FindNearby>(AccessTools.Method(helper, "FindAvailableNearbyResources"));
            harmony.Patch(AccessTools.Method(helper, "GetAllNeededIngredients"),
                prefix: new HarmonyMethod(typeof(StorageRobotRepair), nameof(SelectMaterials)));
            foreach (string name in new[] { "RepairStationRobot", "RepairDamagedRobot" })
            {
                Type driver = AccessTools.TypeByName("AIRobot.X2_JobDriver_" + name);
                MethodInfo iterator = AccessTools.EnumeratorMoveNext(AccessTools.Method(driver, "MakeNewToils"));
                harmony.Patch(iterator, transpiler: new HarmonyMethod(typeof(StorageRobotRepair), nameof(EnableContainerPickup)));
            }
        }

        private delegate List<Thing> FindNearby(ThingDef resourceDef, Pawn pawn, out int total);
        private static FindNearby findNearby;

        private static List<Thing> OnDisks(Pawn pawn, ThingDef def) =>
            StorageMapAccess.Available(pawn.Map).Where(t => t.def == def && StorageMapAccess.CanCollect(pawn, t, Danger.Deadly)).ToList();

        private static void AddDiskMaterials(ThingDef resourceDef, Pawn pawn, ref int resourcesTotalAvailable, ref List<Thing> __result)
        {
            if (pawn?.Spawned != true) return;
            var stored = OnDisks(pawn, resourceDef);
            if (stored.Count == 0) return;
            __result = (__result ?? new List<Thing>()).Concat(stored).Distinct().ToList();
            resourcesTotalAvailable = (int)Math.Min(int.MaxValue, __result.Sum(t => (long)t.stackCount));
        }

        private static bool SelectMaterials(Pawn pawn, ThingDef ingredientDef, int countNeeded, ref List<Thing> ingredients,
            ref List<int> ingredientsCount, ref bool __result)
        {
            if (pawn?.Spawned != true) return true;
            var materials = findNearby(ingredientDef, pawn, out int total);
            ingredients = new List<Thing>();
            ingredientsCount = new List<int>();
            if (total < countNeeded || materials == null) { __result = false; return false; }
            foreach (Thing item in materials)
            {
                int count = Math.Min(countNeeded, item.stackCount);
                if (count <= 0) break;
                ingredients.Add(item);
                ingredientsCount.Add(count);
                countNeeded -= count;
            }
            __result = countNeeded == 0;
            return false;
        }

        private static IEnumerable<CodeInstruction> EnableContainerPickup(IEnumerable<CodeInstruction> instructions)
        {
            var go = AccessTools.Method(typeof(Toils_Goto), nameof(Toils_Goto.GotoThing), new[]{typeof(TargetIndex), typeof(PathEndMode), typeof(bool)});
            var fail = AccessTools.Method(typeof(ToilFailConditions), nameof(ToilFailConditions.FailOnDespawnedNullOrForbidden)).MakeGenericMethod(typeof(Toil));
            var carry = AccessTools.Method(typeof(Toils_Haul), nameof(Toils_Haul.StartCarryThing));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(go)) instruction.operand = AccessTools.Method(typeof(StorageRobotRepair), nameof(GoToTarget));
                else if (instruction.Calls(fail)) instruction.operand = AccessTools.Method(typeof(StorageRobotRepair), nameof(CheckTarget));
                else if (instruction.Calls(carry)) instruction.operand = AccessTools.Method(typeof(StorageRobotRepair), nameof(Carry));
                yield return instruction;
            }
        }

        private static Toil GoToTarget(TargetIndex index, PathEndMode mode, bool parent) =>
            Toils_Goto.GotoThing(index, mode, parent || index == TargetIndex.B);
        private static Toil CheckTarget(Toil toil, TargetIndex index) => index == TargetIndex.B
            ? toil.FailOnSelfAndParentsDespawnedOrNull(index).FailOnForbidden(index) : toil.FailOnDespawnedNullOrForbidden(index);
        private static Toil Carry(TargetIndex index, bool queue, bool subtract, bool fail, bool reserve, bool inventory) =>
            Toils_Haul.StartCarryThing(index, queue, subtract, fail, reserve, inventory || index == TargetIndex.B);
    }
}
