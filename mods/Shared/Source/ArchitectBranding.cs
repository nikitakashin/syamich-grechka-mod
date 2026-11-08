using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Shared
{
    // Compiled into standalone modules, with one installed patch regardless of their load order.
    [HarmonyPatch(typeof(MainTabWindow_Architect), "DoCategoryButton")]
    [StaticConstructorOnStartup]
    public static class ArchitectCategoryBranding
    {
        private static readonly HashSet<string> Categories = new HashSet<string>
        {
            "Grechka_IndustryCategory"
        };
        // Language changes reload mod content and destroy the old textures.
        // Use the current content holder instead of retaining a texture across reloads.
        public static Texture2D ResolveIcon(DesignationCategoryDef category)
            => category?.modContentPack?.GetContentHolder<Texture2D>().Get("UI/GrechkaBrand/Grain")
                ?? ContentFinder<Texture2D>.Get("UI/GrechkaBrand/Grain", true);
        public static bool IsBranded(DesignationCategoryDef category) => category != null && Categories.Contains(category.defName);
        public static bool Prepare()
        {
            var target = AccessTools.Method(typeof(MainTabWindow_Architect), "DoCategoryButton");
            return Harmony.GetPatchInfo(target)?.Transpilers.Any(p => p.PatchMethod.DeclaringType?.FullName == typeof(ArchitectCategoryBranding).FullName) != true;
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonTextSubtle));
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (!instruction.Calls(original)) { yield return instruction; continue; }
                // Preserve the game's label, search highlighting, disabled tint, click and tutorial handling.
                var loadPanel = new CodeInstruction(OpCodes.Ldarg_1);
                loadPanel.labels.AddRange(instruction.labels);
                loadPanel.blocks.AddRange(instruction.blocks);
                yield return loadPanel;
                yield return CodeInstruction.Call(typeof(ArchitectCategoryBranding), nameof(Button));
                replaced++;
            }
            if (replaced != 1) throw new InvalidOperationException("Grechka Architect button API changed: " + replaced);
        }
        public static bool Button(Rect rect, string label, float barPercent, float textLeftMargin, SoundDef mouseoverSound,
            Vector2 functionalSizeOffset, Color? labelColor, bool highlight, ArchitectCategoryTab panel)
        {
            bool branded = IsBranded(panel?.def);
            bool clicked = Widgets.ButtonTextSubtle(rect, label, barPercent, branded ? 30f : textLeftMargin,
                mouseoverSound, functionalSizeOffset, labelColor, highlight);
            if (branded)
            {
                Texture2D icon = ResolveIcon(panel.def);
                Color previous = GUI.color;
                try
                {
                    if (labelColor.HasValue) GUI.color = labelColor.Value;
                    Widgets.DrawTextureFitted(new Rect(rect.x + 5f, rect.y + (rect.height - 20f) / 2f, 20f, 20f), icon, 1f);
                }
                finally { GUI.color = previous; }
            }
            return clicked;
        }
    }
}

namespace Grechka.Shared
{
    [HarmonyPatch(typeof(DesignationCategoryDef), "get_Visible")]
    public static class ArchitectEmptyCategoryVisibility
    {
        public static bool Prepare()
        {
            var target = AccessTools.PropertyGetter(typeof(DesignationCategoryDef), nameof(DesignationCategoryDef.Visible));
            return Harmony.GetPatchInfo(target)?.Postfixes.Any(p => p.PatchMethod.DeclaringType?.FullName == typeof(ArchitectEmptyCategoryVisibility).FullName) != true;
        }
        public static void Postfix(DesignationCategoryDef __instance, ref bool __result)
        {
            // Hide own categories while none of their commands are visible.
            if (__result && ArchitectCategoryBranding.IsBranded(__instance))
                __result = __instance.AllResolvedDesignators.Any(d => d.Visible);
        }
    }
}
