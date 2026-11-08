using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Grechka.RealRuinsRobotsFix
{
    public sealed class RealRuinsRobotsMod : Mod
    {
        public RealRuinsRobotsMod(ModContentPack content) : base(content)
        {
            new Harmony("grechka.patch.realruinsrobots").CreateClassProcessor(typeof(RealRuinsMechSelection)).Patch();
        }
    }

    [HarmonyPatch]
    public static class RealRuinsMechSelection
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var type = AccessTools.TypeByName("RealRuins.MechanoidsForcesGenerator");
            if (type != null)
                yield return AccessTools.DeclaredMethod(type, "GenerateForces");
        }

        static PawnKindDef SelectGuard(IEnumerable<PawnKindDef> candidates, Func<PawnKindDef, float> weight)
        {
            // Real Ruins weights each mech by 1 / combatPower and then spends
            // combatPower from its budget. Misc. Robots++ workers have zero power.
            var eligible = candidates.Where(k => k.combatPower > 0 && float.IsFinite(1f / k.combatPower)
                && float.IsFinite(k.combatPower));
            eligible.TryRandomElementByWeight(weight, out var guard);
            return guard;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            var pick = AccessTools.Method(typeof(GenCollection), nameof(GenCollection.RandomElementByWeight),
                generics: new[] { typeof(PawnKindDef) });
            int index = code.FindIndex(i => i.Calls(pick));
            if (index < 0 || code.FindLastIndex(i => i.Calls(pick)) != index)
            {
                Log.Warning("[Grechka] Real Ruins guard selector changed. Compatibility patch skipped.");
                return code;
            }

            // Keep any incoming branch labels on the call. If the pool is empty,
            // end this group before constructing a PawnGenerationRequest.
            code[index].operand = AccessTools.Method(typeof(RealRuinsMechSelection), nameof(SelectGuard));
            var selected = generator.DefineLabel();
            code.InsertRange(index + 1, new[] {
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Brtrue, selected),
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(OpCodes.Ret),
                new CodeInstruction(OpCodes.Nop).WithLabels(selected)
            });
            return code;
        }
    }
}
