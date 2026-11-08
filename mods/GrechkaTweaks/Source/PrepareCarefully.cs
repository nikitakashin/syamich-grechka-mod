using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Tweaks
{
    // Optional bridge for EdB's V5 pawn model (including the RimWorldist 1.7 port).
    // Customizations and save records are distinct objects and persist independently of pawn IDs.
    static class PrepareCarefullyAdapter
    {
        private sealed class Extra
        {
            public int grade = -1, originalGrade = -1, savedLevel = -1;
            public float xp, daily;
        }
        private static readonly ConditionalWeakTable<object, Extra> extra = new();
        private static object Get(object o, string name) => Traverse.Create(o).Property(name).GetValue();
        private static void Set(object o, string name, object value) => Traverse.Create(o).Property(name).SetValue(value);
        private static IEnumerable<object> Rows(object model) => ((IEnumerable)Get(model,"Skills")).Cast<object>();
        private static SkillDef Def(object row) => (SkillDef)Get(row,"SkillDef");
        private static IEnumerable<object> SavedRows(object record) => ((IEnumerable)Traverse.Create(record).Field("skills").GetValue()).Cast<object>();
        private static string Name(object row) => Traverse.Create(row).Field("name").GetValue<string>();
        internal static void Install(Assembly assembly)
        {
            var hooks = new List<Editors.Hook>();
            void Hook(string target, string method, string callback, HarmonyPatchType kind = HarmonyPatchType.Postfix)
                => hooks.Add(new(assembly.GetType("EdB.PrepareCarefully." + target),method,typeof(PrepareCarefullyAdapter),callback,kind));
            Hook("MapperPawnToCustomizations","MapSkills",nameof(Map));
            Hook("PawnCustomizer","ApplySkillCustomizationsToPawn",nameof(Apply));
            Hook("PawnSaver","ConvertSkills",nameof(Write));
            Hook("PawnLoaderV5","LoadOtherValues",nameof(Read));
            Hook("SaveRecordSkillV4","ExposeData",nameof(Expose));
            Hook("PanelSkills","GetPassionTextureForSkill",nameof(Icon));
            Hook("PanelSkills","DrawSkill",nameof(LevelInstructions),HarmonyPatchType.Transpiler);
            Hook("ControllerTabViewPawns","AdjustSkillLevelByOne",nameof(LevelInstructions),HarmonyPatchType.Transpiler);
            Hook("ControllerTabViewPawns","AdjustSkillPassion",nameof(AdjustGrade),HarmonyPatchType.Prefix);
            Hook("ManagerPawns","RecalculateSkillLevelsFromSkillGains",nameof(LevelInstructions),HarmonyPatchType.Transpiler);
            Hook("ManagerPawns","SetSkillLevel",nameof(Edited));
            Hook("ControllerTabViewPawns","ClearSkillsAndPassions",nameof(Cleared));
            Hook("ControllerTabViewPawns","ResetAddedSkillLevelsAndPassions",nameof(Reset));
            Hook("CostCalculator","CalculatePawnCost",nameof(Cost));
            Editors.Apply("preparecarefully",hooks.ToArray());
        }
        static void Map(Pawn __0, object __1)
        {
            foreach (var row in Rows(__1))
            {
                var s = __0.skills.GetSkill(Def(row));
                int level = Mastery.Level(s) + s.Aptitude;
                Set(row,"Level",level);
                Set(row,"OriginalLevel",level);
                var e = extra.GetOrCreateValue(row);
                e.grade = e.originalGrade = Mastery.Grade(s);
                e.xp = Mastery.Progress(s);
                e.daily = s.xpSinceMidnight;
            }
        }
        static void Apply(Pawn __0, object __1)
        {
            foreach (var row in Rows(__1))
            {
                if (Def(row) == null) continue;
                var s = __0.skills.GetSkill(Def(row));
                var e = extra.GetOrCreateValue(row);
                Mastery.SetGrade(s,e.grade >= 0 && Mastery.BasePassion(e.grade) == s.passion ? e.grade : (int)s.passion + 1);
                Mastery.SetLevel(s,Mastery.Level(s),e.xp);
                s.xpSinceMidnight = e.daily;
            }
        }
        static void Write(object __0, object __1)
        {
            var model = Rows(__1).Where(r => Def(r) != null).ToDictionary(r => Def(r).defName);
            foreach (var record in SavedRows(__0))
            {
                if (Name(record) == null || !model.TryGetValue(Name(record),out var row)) continue;
                var source = extra.GetOrCreateValue(row);
                var dest = extra.GetOrCreateValue(record);
                dest.savedLevel = (int)Get(row,"Level");
                dest.grade = source.grade;
                dest.xp = source.xp;
                dest.daily = source.daily;
                Traverse.Create(record).Field("value").SetValue(Math.Clamp(dest.savedLevel,0,20));
            }
        }
        static void Expose(object __instance)
        {
            var e = extra.GetOrCreateValue(__instance);
            Scribe_Values.Look(ref e.savedLevel,"grechkaLevelV1",-1);
            Scribe_Values.Look(ref e.grade,"grechkaGradeV1",-1);
            Mastery.ExposeXp(ref e.xp,"grechkaXpV1");
            Mastery.ExposeXp(ref e.daily,"grechkaDailyV1");
        }
        static void Read(object __0, object __1)
        {
            var model = Rows(__1).Where(r => Def(r) != null).ToDictionary(r => Def(r).defName);
            foreach (var record in SavedRows(__0))
            {
                if (Name(record) == null || !model.TryGetValue(Name(record),out var row) || !extra.TryGetValue(record,out var source)) continue;
                var dest = extra.GetOrCreateValue(row);
                dest.grade = dest.originalGrade = source.grade is >= 0 and <= 6 ? source.grade : -1;
                dest.xp = source.xp;
                dest.daily = source.daily;
                if (source.savedLevel == -1) continue;
                Set(row,"Level",source.savedLevel);
                Set(row,"OriginalLevel",source.savedLevel);
            }
        }
        static void Icon(SkillRecord __0, ref Texture2D __result) => __result = SkillDisplay.Icon(__0) ?? __result;
        static IEnumerable<CodeInstruction> LevelInstructions(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var i in instructions)
            {
                if (i.Calls(AccessTools.PropertyGetter(typeof(SkillRecord),"Level")))
                    yield return CodeInstruction.Call(typeof(Mastery),nameof(Mastery.Effective)).MoveLabelsFrom(i);
                else
                {
                    if (i.LoadsConstant(20f)) i.operand = (float)Mastery.MaxLevel;
                    yield return i;
                }
            }
        }
        static object Current(object view) => Get(Get(view,"ViewState"),"CurrentPawn");
        static bool AdjustGrade(object __instance, SkillDef __0, int __1)
        {
            var selected = Current(__instance);
            if (selected == null || Get(selected,"Pawn") is not Pawn p || __1 == 0) return true;
            var row = Rows(Get(selected,"Customizations")).FirstOrDefault(r => Def(r) == __0);
            if (row == null) return true;
            var s = p.skills.GetSkill(__0);
            int grade = (Mastery.Grade(s) + Math.Sign(__1) + 7) % 7;
            extra.GetOrCreateValue(row).grade = grade;
            Mastery.SetGrade(s,grade);
            Traverse.Create(Get(__instance,"PawnManager")).Method("UpdatePawnSkillPassion",selected,__0,s.passion).GetValue();
            return false;
        }
        static void Edited(object __0, SkillDef __1)
        {
            if (__0 == null) return;
            var row = Rows(Get(__0,"Customizations")).FirstOrDefault(r => Def(r) == __1);
            if (row == null) return;
            var s = ((Pawn)Get(__0,"Pawn")).skills.GetSkill(__1);
            extra.GetOrCreateValue(row).xp = 0;
            Mastery.SetLevel(s,Mastery.Level(s),0);
        }
        static void Cleared(object __instance) => ResetGrades(Current(__instance), true);
        static void Reset(object __instance) => ResetGrades(Current(__instance), false);
        static void ResetGrades(object selected, bool clear)
        {
            if (selected == null) return;
            foreach (var row in Rows(Get(selected,"Customizations")))
            {
                var s = ((Pawn)Get(selected,"Pawn")).skills.GetSkill(Def(row));
                var e = extra.GetOrCreateValue(row);
                e.grade = clear ? 1 : e.originalGrade >= 0 ? e.originalGrade : (int)s.passion + 1;
                Mastery.SetGrade(s,e.grade);
                e.xp = 0;
                Mastery.SetLevel(s,Mastery.Level(s),0);
            }
        }
        static void Cost(object __0, object __result)
        {
            if (__result == null || Get(__0,"Pawn") is not Pawn p || p.skills == null) return;
            double surcharge = 0;
            foreach (var s in p.skills.skills)
                surcharge += 10 * Math.Max(0,Mastery.Level(s)-20) + 20 * Math.Max(0,Mastery.Grade(s)-3);
            if (surcharge == 0) return;
            var cost = Traverse.Create(__result);
            cost.Field("marketValue").SetValue(cost.Field("marketValue").GetValue<double>() + surcharge);
            cost.Method("ComputeTotal").GetValue();
        }
    }
}
