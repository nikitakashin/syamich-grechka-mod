using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Tweaks
{
    // Vanilla consumers continue to see their supported level and Passion ranges.
    public static class Mastery
    {
        private sealed class Extra { public int levels; public int special = -1; public float progress; }
        private static readonly ConditionalWeakTable<SkillRecord, Extra> data = new();
        public const int MaxLevel = 200;
        public static readonly float[] Rates = { .15f, .35f, 1.25f, 2f, 4f, 7f, 12f };

        public static int Level(SkillRecord s) => s.levelInt + (s.levelInt == 20 && data.TryGetValue(s, out var e) ? e.levels : 0);
        public static int Effective(SkillRecord s) => s.TotallyDisabled ? 0 : Math.Clamp(Level(s) + s.Aptitude, 0, MaxLevel);
        public static float Progress(SkillRecord s) => Level(s) > 20 ? data.GetOrCreateValue(s).progress : s.xpSinceLastLevel;
        public static float Threshold(int level) => level >= 20 ? 30000f + 250f * (level - 20) : SkillRecord.XpRequiredToLevelUpFrom(level);
        public static Passion BasePassion(int grade) => grade <= 1 ? Passion.None : grade == 2 ? Passion.Minor : Passion.Major;
        public static int Grade(SkillRecord s)
        {
            if (data.TryGetValue(s, out var e) && e.special >= 0 && BasePassion(e.special) == s.passion)
                return e.special;
            return Math.Clamp((int)s.passion + 1, 1, 3);
        }
        public static void SetGrade(SkillRecord s, int grade)
        {
            grade = Math.Clamp(grade, 0, 6);
            s.passion = BasePassion(grade);
            data.GetOrCreateValue(s).special = grade is 0 or >= 4 ? grade : -1;
        }
        public static void SetLevel(SkillRecord s, int level, float progress)
        {
            level = Math.Clamp(level, 0, MaxLevel);
            var e = data.GetOrCreateValue(s);
            e.levels = Math.Max(0, level - 20);
            s.levelInt = Math.Min(20, level);
            if (!float.IsFinite(progress)) progress = 0;
            if (level == MaxLevel) progress = Math.Min(progress, 0);
            e.progress = level > 20 ? progress : 0;
            s.xpSinceLastLevel = level <= 20 ? progress : 0;
        }
        // Serialize IEEE bits so a save written with a comma decimal separator loads identically.
        internal static void ExposeXp(ref float value, string key)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            Scribe_Values.Look(ref bits, key);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                float parsed = BitConverter.Int32BitsToSingle(bits);
                value = float.IsFinite(parsed) ? parsed : 0;
            }
        }
        internal static void Expose(SkillRecord s)
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars && !data.TryGetValue(s, out _)) return;
            var e = data.GetOrCreateValue(s);
            Scribe_Values.Look(ref e.levels, "grechkaMasteryV1");
            ExposeXp(ref e.progress, "grechkaMasteryXpV1");
            Scribe_Values.Look(ref e.special, "grechkaInterestV1", -1);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                e.levels = s.levelInt == 20 ? Math.Clamp(e.levels, 0, MaxLevel - 20) : 0;
                if (e.special is not (0 or 4 or 5 or 6)) e.special = -1;
                if (!float.IsFinite(e.progress)) e.progress = 0;
            }
        }
        internal static void Advance(SkillRecord s, float total)
        {
            int level = Level(s);
            while (level < MaxLevel && total >= Threshold(level)) total -= Threshold(level++);
            while (level > 0 && total <= -1000f) total += Threshold(--level);
            SetLevel(s, level, level == 0 ? Math.Max(0, total) : total);
        }
    }

    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.ExposeData))]
    static class SaveSkill { static void Postfix(SkillRecord __instance) => Mastery.Expose(__instance); }

    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Level), MethodType.Setter)]
    static class ExplicitLevel
    {
        static void Prefix(SkillRecord __instance, int value, out float __state)
        {
            __state = Mastery.Level(__instance) == value ? Mastery.Progress(__instance) : __instance.xpSinceLastLevel;
        }
        static void Postfix(SkillRecord __instance, int value, float __state)
        {
            Mastery.SetLevel(__instance, value, __state);
        }
    }

    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.LearnRateFactor))]
    static class InterestRate
    {
        static void Postfix(SkillRecord __instance, ref float __result)
        {
            if (DebugSettings.fastLearning) return;
            int grade = Mastery.Grade(__instance);
            float vanilla = __instance.passion switch { Passion.None => .35f, Passion.Minor => 1f, Passion.Major => 1.5f, _ => 0f };
            if (vanilla > 0f) __result *= Mastery.Rates[grade] / vanilla;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), "GenerateSkills")]
    static class GenerateInterest
    {
        static void Postfix(Pawn pawn, PawnGenerationRequest request)
        {
            if (pawn.skills == null) return;
            bool starter = request.Context == PawnGenerationContext.PlayerStarter;
            foreach (var skill in pawn.skills.skills)
            {
                if (skill.TotallyDisabled) continue;
                if (skill.passion == Passion.None && Rand.Chance(.1f)) Mastery.SetGrade(skill, 0);
                else if (starter && skill.passion == Passion.Minor && Rand.Chance(.10f)) Mastery.SetGrade(skill, 4);
                else if (skill.passion == Passion.Major)
                {
                    float roll = Rand.Value;
                    int grade = starter
                        ? roll < .05f ? 6 : roll < .20f ? 5 : roll < .50f ? 4 : 3
                        : roll < .005f ? 6 : roll < .03f ? 5 : roll < .13f ? 4 : 3;
                    Mastery.SetGrade(skill, grade);
                }
            }
        }
    }

    [HarmonyPatch(typeof(StartingPawnUtility), nameof(StartingPawnUtility.NewGeneratedStartingPawn))]
    static class StartingSpecialist
    {
        static void Postfix(Pawn __result)
        {
            if (__result?.skills == null || !__result.DevelopmentalStage.Adult() || !Rand.Chance(.2f)) return;
            SkillRecord best = null;
            foreach (var skill in __result.skills.skills)
            {
                if (skill.TotallyDisabled) continue;
                if (best == null || Mastery.Effective(skill) > Mastery.Effective(best) ||
                    (Mastery.Effective(skill) == Mastery.Effective(best) && Mastery.Grade(skill) > Mastery.Grade(best)))
                    best = skill;
            }
            if (best == null || Mastery.Effective(best) >= 21) return;
            Mastery.SetLevel(best, Rand.RangeInclusive(21, 35) - best.Aptitude, 0);
        }
    }

    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
    static class LearnMastery
    {
        static bool Prefix(SkillRecord __instance, float xp, bool direct, bool ignoreLearnRate)
        {
            var s = __instance;
            if (Mastery.Level(s) < 20 || (Mastery.Level(s) == 20 && xp <= 0)) return true;
            if (s.TotallyDisabled || !float.IsFinite(xp)) return false;
            if (xp > 0 && !ignoreLearnRate) xp *= s.LearnRateFactor(direct);
            if (!direct) s.xpSinceMidnight += xp;
            Mastery.Advance(s, Mastery.Progress(s) + xp);
            return false;
        }

        // Learn already accounted for the day's XP and subtracted the cost of level 19.
        // Capture the remainder exactly where vanilla would discard it on reaching level 20.
        static float CarryOver(float value, float min, float max, SkillRecord skill)
        {
            Mastery.Advance(skill, Math.Max(0, value));
            return skill.xpSinceLastLevel;
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var clamp = AccessTools.Method(typeof(UnityEngine.Mathf), "Clamp", new[] { typeof(float), typeof(float), typeof(float) });
            int matches = 0;
            foreach (var i in instructions)
            {
                if (i.Calls(clamp))
                {
                    matches++;
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(i);
                    yield return CodeInstruction.Call(typeof(LearnMastery), nameof(CarryOver));
                }
                else yield return i;
            }
            if (matches != 1) throw new InvalidOperationException("Unexpected SkillRecord.Learn cap in this RimWorld build");
        }
    }
}
