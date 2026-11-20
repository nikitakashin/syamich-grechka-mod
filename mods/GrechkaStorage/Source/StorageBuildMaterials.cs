using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Storage
{
    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.ProcessInput))]
    internal static class StorageBuildMaterialHook
    {
        // The Architect menu first enumerates resource definitions, then checks
        // the map lister for a stack. Disk contents need to participate in both.
        private static IEnumerable<ThingDef> MaterialDefs(Dictionary<ThingDef, int> counts, out HashSet<ThingDef> onDisk)
        {
            onDisk = new HashSet<ThingDef>(StorageMapAccess.Available(Find.CurrentMap)
                .Select(item => item.def).Where(def => def.IsStuff));
            return counts.Keys.Union(onDisk);
        }

        private static int AvailableStacks(ListerThings lister, ThingDef def, HashSet<ThingDef> onDisk)
        {
            int count = lister.ThingsOfDef(def).Count;
            return count == 0 && onDisk.Contains(def) ? 1 : count;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var onDisk = generator.DeclareLocal(typeof(HashSet<ThingDef>));
            var matcher = new CodeMatcher(instructions)
                .MatchStartForward(new CodeMatch(i => i.Calls(AccessTools.PropertyGetter(typeof(Dictionary<ThingDef, int>), "Keys"))))
                .ThrowIfInvalid("Architect material list not found");
            matcher.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldloca, onDisk).MoveLabelsFrom(matcher.Instruction))
                .Insert(CodeInstruction.Call(typeof(StorageBuildMaterialHook), nameof(MaterialDefs)));
            matcher.MatchStartForward(
                    new CodeMatch(i => i.Calls(AccessTools.Method(typeof(ListerThings), nameof(ListerThings.ThingsOfDef)))),
                    new CodeMatch(i => i.Calls(AccessTools.PropertyGetter(typeof(List<Thing>), "Count"))))
                .ThrowIfInvalid("Architect material availability check not found");
            matcher.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldloc, onDisk).MoveLabelsFrom(matcher.Instruction))
                .SetInstruction(CodeInstruction.Call(typeof(StorageBuildMaterialHook), nameof(AvailableStacks)).MoveLabelsFrom(matcher.Instruction));
            return matcher.InstructionEnumeration();
        }
    }
}
