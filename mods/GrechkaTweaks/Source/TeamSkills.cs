using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Tweaks
{
    static class TeamSkills
    {
        // The vanilla row has only one record. Substitute the icon source only during this summary row.
        [ThreadStatic] private static SkillRecord levelOwner;
        [ThreadStatic] private static SkillRecord interestOwner;
        internal static SkillRecord IconSource(SkillRecord skill) => levelOwner == skill ? interestOwner : skill;

        static SkillRecord StrongestInterest(SkillRecord leader, GameInitData init)
        {
            var best = leader;
            for (int i = 0; i < init.startingPawnCount; i++)
            {
                var candidate = init.startingAndOptionalPawns[i].skills.GetSkill(leader.def);
                if (!candidate.TotallyDisabled && Mastery.Grade(candidate) > Mastery.Grade(best))
                    best = candidate;
            }
            return best;
        }

        static void DrawTeamSkill(SkillRecord skill, Rect rect, SkillUI.SkillDrawMode mode, string tooltipPrefix)
        {
            var prevLevel = levelOwner;
            var prevInterest = interestOwner;
            try
            {
                levelOwner = skill;
                interestOwner = StrongestInterest(skill, Find.GameInitData);
                if (interestOwner != skill && Mouse.IsOver(rect))
                {
                    var grade = ("GrechkaTweaks_Grade" + Mastery.Grade(interestOwner)).Translate();
                    tooltipPrefix = "GrechkaTweaks_Team".Translate(tooltipPrefix, interestOwner.Pawn.LabelShort, grade);
                }
                SkillUI.DrawSkill(skill, rect, mode, tooltipPrefix);
            }
            finally
            {
                levelOwner = prevLevel;
                interestOwner = prevInterest;
            }
        }

        [HarmonyPatch(typeof(StartingPawnUtility), "DrawSkillSummaries")]
        static class SummaryRow
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var draw = AccessTools.Method(typeof(SkillUI), "DrawSkill",
                    new[] { typeof(SkillRecord), typeof(Rect), typeof(SkillUI.SkillDrawMode), typeof(string) });
                return instructions.MethodReplacer(draw, AccessTools.Method(typeof(TeamSkills), nameof(DrawTeamSkill)));
            }
        }

        [HarmonyPatch(typeof(StartingPawnUtility), "FindBestSkillOwner")]
        static class HighestLevel
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                foreach (var i in instructions)
                {
                    if (i.Calls(AccessTools.PropertyGetter(typeof(SkillRecord), nameof(SkillRecord.Level))))
                        yield return CodeInstruction.Call(typeof(Mastery), nameof(Mastery.Effective)).MoveLabelsFrom(i);
                    else if (i.LoadsField(AccessTools.Field(typeof(SkillRecord), nameof(SkillRecord.passion))))
                        yield return CodeInstruction.Call(typeof(Mastery), nameof(Mastery.Grade)).MoveLabelsFrom(i);
                    else yield return i;
                }
            }
        }
    }
}
