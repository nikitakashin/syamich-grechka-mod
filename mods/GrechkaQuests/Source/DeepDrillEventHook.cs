using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Quests
{
    // Deep drilling bypasses both Mineable.TrySpawnYield and QuestManager output
    // notifications. Observe only the placement originating in its native cycle.
    [HarmonyPatch(typeof(CompDeepDrill), "TryProducePortion", new[] { typeof(float), typeof(Pawn) })]
    internal static class DeepDrillEventHook
    {
        internal static bool Installed { get; private set; }
        static readonly MethodInfo place = AccessTools.Method(typeof(GenPlace), nameof(GenPlace.TryPlaceThing), new[] {
            typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Action<Thing, int>),
            typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int) });
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            Installed = false;
            var body = instructions.ToList();
            if (body.Count(i => i.Calls(place)) != 1)
            {
                Log.Warning(QuestText.Get("GrechkaQuests_04b2d3dc1854", "[Grechka Quests] Не удалось подтвердить источник выхода глубинной буровой; адаптер пропущен."));
                return body;
            }
            var result = new List<CodeInstruction>();
            foreach (CodeInstruction instruction in body)
            {
                if (!instruction.Calls(place)) { result.Add(instruction); continue; }
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(instruction.labels); load.blocks.AddRange(instruction.blocks);
                result.Add(load); result.Add(new CodeInstruction(OpCodes.Ldarg_2));
                result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DeepDrillEventHook), nameof(PlaceProduced))));
            }
            Installed = true;
            return result;
        }
        internal static bool PlaceProduced(Thing thing, IntVec3 center, Map map, ThingPlaceMode mode,
            Action<Thing, int> placedAction, Predicate<IntVec3> validator, Rot4? rotation, int radius,
            CompDeepDrill drill, Pawn driller)
        {
            return GenPlace.TryPlaceThing(thing, center, map, mode, (placed, count) =>
            {
                placedAction?.Invoke(placed, count);
                QuestGame game = QuestGame.Current;
                if (game == null || placed == null || count <= 0) return;
                if (driller == null) game.RecordVerifiedMachineOutput("mine", drill?.parent, placed, count);
                else
                {
                    CompQuality quality = placed.TryGetComp<CompQuality>();
                    game.RecordVerifiedOutput("mine", driller, placed.def, placed.Stuff,
                        quality == null ? null : quality.Quality.ToString(), count);
                }
            }, validator, rotation, radius);
        }
    }
}
