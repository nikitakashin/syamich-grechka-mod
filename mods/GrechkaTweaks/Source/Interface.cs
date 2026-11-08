using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Tweaks
{
    static class SkillDisplay
    {
        internal static readonly string[] Icons = { "Hollow", null, null, null, "Triple", "Pink", "Violet" };
        internal static Texture2D Icon(SkillRecord s)
        {
            string path = Icons[Mastery.Grade(s)];
            return path == null ? null : ContentFinder<Texture2D>.Get("UI/GrechkaTweaks/" + path);
        }
        static Passion DrawInterest(SkillRecord skill, Rect rect)
        {
            var icon = Icon(skill);
            if (icon == null) return skill.passion;
            GUI.DrawTexture(rect, icon);
            return Passion.None;
        }
        static int DisplayLevel(SkillRecord s, bool includeAptitudes) => includeAptitudes ? Mastery.Effective(s) : Mastery.Level(s);
        [HarmonyPatch(typeof(SkillUI), "DrawSkill", typeof(SkillRecord), typeof(Rect), typeof(SkillUI.SkillDrawMode), typeof(string))]
        static class Row
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                bool firstPassion = true;
                foreach (var i in instructions)
                {
                    if (i.Calls(AccessTools.Method(typeof(SkillRecord), "GetLevel")))
                        yield return CodeInstruction.Call(typeof(SkillDisplay), nameof(DisplayLevel)).MoveLabelsFrom(i);
                    else if (i.LoadsField(AccessTools.Field(typeof(SkillRecord), "passion")))
                    {
                        yield return CodeInstruction.Call(typeof(TeamSkills), nameof(TeamSkills.IconSource)).MoveLabelsFrom(i);
                        if (firstPassion)
                        {
                            firstPassion = false;
                            yield return new CodeInstruction(OpCodes.Ldloc_2);
                            yield return CodeInstruction.Call(typeof(SkillDisplay), nameof(DrawInterest));
                        }
                        else yield return i;
                    }
                    else
                    {
                        if (i.LoadsConstant(20f)) i.operand = (float)Mastery.MaxLevel;
                        yield return i;
                    }
                }
            }
        }
        [HarmonyPatch(typeof(SkillUI), "GetSkillDescription")]
        static class Description
        {
            static bool Prefix(SkillRecord sk, ref string __result)
            {
                int grade = Mastery.Grade(sk);
                if (sk.TotallyDisabled || (Mastery.Level(sk) < 20 && grade == 1 && Mastery.Effective(sk) <= 20)) return true;
                var name = ("GrechkaTweaks_Grade" + grade).Translate();
                int level = Mastery.Level(sk);
                var xp = level < Mastery.MaxLevel ? "GrechkaTweaks_Experience".Translate(Mastery.Progress(sk).ToString("N0"), Mastery.Threshold(level).ToString("N0")).ToString() : "GrechkaTweaks_Max".Translate(Mastery.MaxLevel).ToString();
                __result = $"{sk.def.LabelCap}  {Mastery.Effective(sk)} / {Mastery.MaxLevel}\n{sk.def.description}\n\n" +
                    "GrechkaTweaks_Training".Translate(level, sk.Aptitude.ToString("+0;-0;0")) + "\n" + xp + "\n" +
                    "GrechkaTweaks_Rate".Translate(name, Mastery.Rates[grade].ToString("0.##"), sk.LearnRateFactor().ToString("P0"));
                return false;
            }
        }
    }
}
