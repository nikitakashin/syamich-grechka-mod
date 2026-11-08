using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Tweaks
{
    static class Editors
    {
        internal readonly record struct Hook(Type Target, string Method, Type Adapter, string Callback, HarmonyPatchType Kind);
        internal static void Apply(string name, params Hook[] hooks)
        {
            var h = new Harmony("grechka.tweaks." + name);
            try
            {
                var resolved = hooks.Select(a => (
                    original: AccessTools.DeclaredMethod(a.Target, a.Method) ?? throw new MissingMethodException(a.Target?.FullName, a.Method),
                    patch: new HarmonyMethod(AccessTools.DeclaredMethod(a.Adapter, a.Callback) ?? throw new MissingMethodException(a.Callback)),
                    kind: a.Kind)).ToArray();
                foreach (var r in resolved)
                    h.Patch(r.original, prefix: r.kind == HarmonyPatchType.Prefix ? r.patch : null,
                        postfix: r.kind == HarmonyPatchType.Postfix ? r.patch : null,
                        transpiler: r.kind == HarmonyPatchType.Transpiler ? r.patch : null);
            }
            catch (Exception e)
            {
                h.UnpatchAll(h.Id);
                Log.Warning("[Grechka Tweaks] Unsupported " + name + " adapter: " + e);
            }
        }
        internal static void Install()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetType("CharacterEditor.SkillTool") != null);
            if (assembly != null) CharacterEditorAdapter.Install(assembly);
            var edb = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetType("EdB.PrepareCarefully.PanelSkills") != null);
            if (edb != null) PrepareCarefullyAdapter.Install(edb);
        }
    }

    static class CharacterEditorAdapter
    {
        internal static void Install(Assembly a)
        {
            var tools = a.GetType("CharacterEditor.SkillTool");
            var bio = a.GetType("CharacterEditor.CEditor+EditorUI+BlockBio");
            Type self = typeof(CharacterEditorAdapter);
            var hooks = new List<Editors.Hook>
            {
                new(tools,"GetSkillAsSeparatedString",self,nameof(Write),HarmonyPatchType.Postfix),
                new(tools,"SetSkillsFromSeparatedString",self,nameof(Read),HarmonyPatchType.Postfix),
                new(tools,"CopySkillFromSkillRecord",self,nameof(Copy),HarmonyPatchType.Postfix),
                new(bio,"ATogglePassion",self,nameof(Toggle),HarmonyPatchType.Prefix),
                new(bio,"ACopySkills",self,nameof(FreezeClipboard),HarmonyPatchType.Postfix)
            };
            foreach (var method in new[] {"DrawSkills","AAddSkillLevel","ASubSkillLevel","RecalcSkills","ARandomSkills"})
                hooks.Add(new(bio,method,self,nameof(Fields),HarmonyPatchType.Transpiler));
            Editors.Apply("charactereditor", hooks.ToArray());
        }
        static void Write(SkillRecord __0, ref string __result)
        {
            if (__0?.def == null || string.IsNullOrEmpty(__result)) return;
            // CE reads only five-column rows. Its old reader skips the six-column extension.
            __result += FormattableString.Invariant($":~GT1|{__0.def.defName}|{Mastery.Level(__0)}|{Mastery.Grade(__0)}|{Mastery.Progress(__0):R}|{__0.xpSinceMidnight:R}");
        }
        static void Read(Pawn __0, string __1)
        {
            if (__0?.skills == null) return;
            foreach (var s in __0.skills.skills)
            {
                Mastery.SetLevel(s, s.levelInt, s.xpSinceLastLevel);
                if ((int)s.passion <= 2) Mastery.SetGrade(s, (int)s.passion + 1);
            }
            foreach (var row in (__1 ?? "").Split(':'))
            {
                var f = row.Split('|');
                if (f.Length != 6 || f[0] != "~GT1") continue;
                var s = __0.skills.skills.Find(x => x.def.defName == f[1]);
                if (s == null || !int.TryParse(f[2], out int level) || !int.TryParse(f[3], out int grade) ||
                    !float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float xp)) continue;
                Mastery.SetLevel(s, level, xp);
                Mastery.SetGrade(s, grade);
                if (float.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float daily) && float.IsFinite(daily)) s.xpSinceMidnight = daily;
            }
        }
        static void Copy(Pawn __0, SkillRecord __1)
        {
            if (__0?.skills == null || __1?.def == null) return;
            Transfer(__1, __0.skills.GetSkill(__1.def));
        }
        static void Transfer(SkillRecord from, SkillRecord to)
        {
            Mastery.SetLevel(to, Mastery.Level(from), Mastery.Progress(from));
            Mastery.SetGrade(to, Mastery.Grade(from));
            to.xpSinceMidnight = from.xpSinceMidnight;
        }
        static void FreezeClipboard(List<SkillRecord> ___lOfCopySkills)
        {
            for (int i = 0; i < ___lOfCopySkills.Count; i++)
            {
                var from = ___lOfCopySkills[i];
                var clone = new SkillRecord(from.Pawn, from.def);
                Transfer(from, clone);
                ___lOfCopySkills[i] = clone;
            }
        }
        static bool Toggle(SkillRecord __0)
        {
            Mastery.SetGrade(__0, (Mastery.Grade(__0) + (Event.current?.button == 1 ? 6 : 1)) % 7);
            return false;
        }
        static void EditLevel(SkillRecord s, int level)
        {
            if (Mastery.Level(s) != level) Mastery.SetLevel(s, level, 0);
        }
        static void VanillaPassion(SkillRecord s, Passion p) => Mastery.SetGrade(s, (int)p + 1);
        static IEnumerable<CodeInstruction> Fields(IEnumerable<CodeInstruction> instructions)
        {
            var field = AccessTools.Field(typeof(SkillRecord), "levelInt");
            foreach (var i in instructions)
            {
                if (i.LoadsField(field)) yield return CodeInstruction.Call(typeof(Mastery), nameof(Mastery.Level)).MoveLabelsFrom(i);
                else if (i.StoresField(field)) yield return CodeInstruction.Call(typeof(CharacterEditorAdapter), nameof(EditLevel)).MoveLabelsFrom(i);
                else if (i.StoresField(AccessTools.Field(typeof(SkillRecord), "passion")))
                    yield return CodeInstruction.Call(typeof(CharacterEditorAdapter), nameof(VanillaPassion)).MoveLabelsFrom(i);
                else yield return i;
            }
        }
    }
}
