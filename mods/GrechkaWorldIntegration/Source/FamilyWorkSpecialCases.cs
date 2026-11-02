using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.WorldIntegration
{
    // One native direct equality bypasses ThingRequest: keep the native power/uninstall predicate.
    [HarmonyPatch(typeof(WorkGiver_DeepDrill), nameof(WorkGiver_DeepDrill.ShouldSkip))]
    public static class FamilyDeepDrillSkipHook
    {
        public static ThingDef OriginalDefinition(Thing thing)
        {
            ThingDef actual = thing?.def;
            VariantEntry entry = ProfileVariants.Entry(actual);
            return entry != null && WorldProfile.IsApplied(actual)
                ? DefDatabase<ThingDef>.GetNamedSilentFail(entry.sourceName) ?? actual : actual;
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
        {
            var code = source.ToList(); FieldInfo field = AccessTools.Field(typeof(Thing), nameof(Thing.def));
            var matching = code.Where(i => i.opcode == OpCodes.Ldfld && Equals(i.operand, field)).ToList();
            if (matching.Count != 1) { Log.Error("[Grechka World] DeepDrill work predicate changed; family patch was not applied."); return code; }
            var replacement = new CodeInstruction(matching[0]) { opcode = OpCodes.Call, operand = AccessTools.Method(typeof(FamilyDeepDrillSkipHook), nameof(OriginalDefinition)) };
            code[code.IndexOf(matching[0])] = replacement;
            return code;
        }
    }
}
