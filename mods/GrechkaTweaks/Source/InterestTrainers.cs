using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Grechka.Tweaks
{
    public sealed class CompProperties_InterestTrainer : CompProperties_UseEffect
    {
        public int grade = 2;
        public CompProperties_InterestTrainer() { compClass = typeof(CompUseEffect_InterestTrainer); }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (grade < 2 || grade > 6) yield return "Interest trainer grade must be between 2 and 6.";
        }
    }

    public static class InterestTrainers
    {
        public static bool EligiblePawn(Pawn pawn) => pawn?.skills != null
            && pawn.RaceProps?.Humanlike == true && pawn.RaceProps.IsFlesh && !pawn.Dead;

        // Grade 0 is an aversion, not an empty passion. Never replace it or an existing flame.
        public static bool EligibleSkill(SkillRecord skill) => skill != null
            && !skill.TotallyDisabled && Mastery.Grade(skill) == 1;

        public static List<SkillRecord> Candidates(Pawn pawn)
        {
            var result = new List<SkillRecord>();
            if (EligiblePawn(pawn))
                foreach (SkillRecord skill in pawn.skills.skills)
                    if (EligibleSkill(skill)) result.Add(skill);
            return result;
        }
    }

    public sealed class CompUseEffect_InterestTrainer : CompUseEffect
    {
        private int Grade => ((CompProperties_InterestTrainer)props).grade;

        public override AcceptanceReport CanBeUsedBy(Pawn pawn)
        {
            if (Grade < 2 || Grade > 6 || !InterestTrainers.EligiblePawn(pawn))
                return "GrechkaTweaks_TrainerInvalidPawn".Translate();
            if (InterestTrainers.Candidates(pawn).Count == 0)
                return "GrechkaTweaks_TrainerNoSkill".Translate();
            return base.CanBeUsedBy(pawn);
        }

        public override void DoEffect(Pawn usedBy)
        {
            // Recheck at completion. There is no separate unconditional DestroySelf comp:
            // a changed skill, cancelled job or repeated call cannot waste the last dose.
            if (parent == null || parent.Destroyed || parent.stackCount <= 0 || !CanBeUsedBy(usedBy)) return;
            List<SkillRecord> candidates = InterestTrainers.Candidates(usedBy);
            SkillRecord selected = candidates[Rand.Range(0, candidates.Count)];
            Mastery.SetGrade(selected, Grade);
            parent.SplitOff(1).Destroy(DestroyMode.Vanish);
            Messages.Message("GrechkaTweaks_TrainerApplied".Translate(usedBy.LabelShortCap,
                selected.def.LabelCap, ("GrechkaTweaks_Grade" + Grade).Translate()),
                usedBy, MessageTypeDefOf.PositiveEvent);
        }
    }
}
