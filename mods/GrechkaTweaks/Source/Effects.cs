using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Tweaks
{
    // Only extend named, audited consumers. Mod stats with level-indexed tables stay within 0–20.
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    static class MasterStats
    {
        private static readonly Dictionary<string, string> skills = new()
        {
            ["ConstructionSpeed"]="Construction", ["SmoothingSpeed"]="Construction",
            ["MiningSpeed"]="Mining", ["DeepDrillingSpeed"]="Mining",
            ["CookSpeed"]="Cooking", ["ButcheryFleshSpeed"]="Cooking", ["DrugCookingSpeed"]="Cooking",
            ["PlantWorkSpeed"]="Plants", ["AnimalGatherSpeed"]="Animals", ["TrainAnimalChance"]="Animals",
            ["MedicalTendQuality"]="Medicine", ["MedicalOperationSpeed"]="Medicine", ["MedicalTendSpeed"]="Medicine",
            ["ResearchSpeed"]="Intellectual", ["DrugSynthesisSpeed"]="Intellectual", ["HackingSpeed"]="Intellectual",
            ["TradePriceImprovement"]="Social", ["NegotiationAbility"]="Social",
            ["MeleeHitChance"]="Melee", ["ShootingAccuracyPawn"]="Shooting",
            ["GeneralLaborSpeed"]="Crafting", ["ButcheryMechanoidSpeed"]="Crafting",
            ["MedicalSurgerySuccessChance"]="Medicine"
        };
        static void Postfix(StatRequest req, StatDef ___stat, ref float __result)
        {
            if (req.Thing is not Pawn p || p.skills == null || !skills.TryGetValue(___stat.defName, out var skill)) return;
            var def = DefDatabase<SkillDef>.GetNamedSilentFail(skill);
            if (def == null) return;
            float mastery = Math.Max(0, Mastery.Effective(p.skills.GetSkill(def)) - 20) / 80f;
            if (mastery <= 0) return;
            // Combat uses skill points at this stage; vanilla later maps points to a bounded chance.
            if (___stat.defName is "MeleeHitChance" or "ShootingAccuracyPawn")
                __result += 20f * mastery;
            else if (___stat.defName == "MedicalSurgerySuccessChance") __result *= 1f + .15f * mastery;
            else __result *= 1f + .5f * mastery;
        }
    }

    [HarmonyPatch(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn), typeof(Pawn), typeof(SkillDef), typeof(bool))]
    static class QualityFromSkill
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var i in instructions)
                yield return i.Calls(AccessTools.PropertyGetter(typeof(SkillRecord), "Level"))
                    ? CodeInstruction.Call(typeof(Mastery), nameof(Mastery.Effective)).MoveLabelsFrom(i) : i;
        }
    }
    [HarmonyPatch(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn), typeof(int), typeof(bool))]
    static class QualityCurve
    {
        internal static float Center(int level) => 4.2f + .6f * Math.Clamp(level - 20, 0, Mastery.MaxLevel - 20) / 80f;
        static int TableIndex(int level) => Math.Min(20, level);
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var i in instructions)
            {
                if (i.opcode == OpCodes.Switch)
                    yield return CodeInstruction.Call(typeof(QualityCurve), nameof(TableIndex)).MoveLabelsFrom(i);
                if (i.LoadsConstant(4.2f))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(i);
                    yield return CodeInstruction.Call(typeof(QualityCurve), nameof(Center));
                }
                else yield return i;
            }
        }
    }
}
