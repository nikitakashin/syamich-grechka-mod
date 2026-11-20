using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grechka.WorldIntegration
{
    // Recipes without workTableSpeedStat normally ignore the machine's own speed.
    // Use the tiered worktable's configured stat for both workers and Storage.
    [HarmonyPatch]
    public static class TierWorktableSpeed
    {
        private static readonly FieldInfo tableSpeed = AccessTools.Field(typeof(RecipeDef), nameof(RecipeDef.workTableSpeedStat));

        static MethodBase TargetMethod() => PatchProcessor.GetOriginalInstructions(
                AccessTools.Method(typeof(Toils_Recipe), nameof(Toils_Recipe.DoRecipeWork)))
            .Where(i => i.opcode == OpCodes.Ldftn).Select(i => i.operand).OfType<MethodInfo>()
            .FirstOrDefault(m => m.GetParameters() is { Length: 1 } p && p[0].ParameterType == typeof(int)
                && PatchProcessor.GetOriginalInstructions(m).Any(i => i.LoadsField(tableSpeed)));

        static bool Prepare() => TargetMethod() != null;

        public static StatDef ForRecipe(RecipeDef recipe, Building_WorkTable table)
        {
            StatDef original = recipe?.workTableSpeedStat;
            if (original != null || recipe == null || table?.def == null || TierMachineFamilies.IsUpgrade(recipe)) return original;
            bool profile = WorldProfile.IsApplied(table.def) && ProfileVariants.Entry(table.def)?.worktable == true;
            bool owned = table.def.modContentPack != null && TierMachineFamilies.TryDescribe(table.def, out string family, out _)
                && DefDatabase<ThingDef>.GetNamedSilentFail(family)?.modContentPack == table.def.modContentPack
                && ProfileVariants.Entry(table.def) == null;
            return profile || owned ? StatDefOf.WorkTableWorkSpeedFactor : null;
        }

        internal static StatDef SpeedStat(StatDef original, JobDriver_DoBill driver) => original
            ?? ForRecipe(driver.job.RecipeDef, driver.BillGiver as Building_WorkTable);

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var locals = __originalMethod.GetMethodBody().LocalVariables;
            var driver = locals.Single(l => l.LocalType == typeof(JobDriver_DoBill));
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (!instruction.LoadsField(tableSpeed)) continue;
                yield return CodeInstruction.LoadLocal(driver.LocalIndex);
                yield return CodeInstruction.Call(typeof(TierWorktableSpeed), nameof(SpeedStat));
            }
        }
    }
}
