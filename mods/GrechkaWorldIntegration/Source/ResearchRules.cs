using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.WorldIntegration
{
    public sealed class GrechkaWorldIntegrationMod : Mod
    {
        public GrechkaWorldIntegrationMod(ModContentPack content) : base(content)
        {
            new Harmony("grechka.world.integration.research").PatchAll(typeof(GrechkaWorldIntegrationMod).Assembly);
        }

        public override string SettingsCategory() { return "Grechka World Integration"; }

        public override void DoSettingsWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 80),
                "В новой игре все исследования завершаются автоматически. Для существующего сохранения правила нужно применить явно.");

            if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.ResearchManager == null)
            {
                Widgets.Label(new Rect(rect.x, rect.y + 90, rect.width, 35), "Загрузите колонию, чтобы применить правила к старому сохранению.");
                return;
            }

            int unfinished = ResearchRules.UnfinishedCount();
            Widgets.Label(new Rect(rect.x, rect.y + 90, rect.width, 35), "Незавершённых исследований: " + unfinished);
            if (unfinished <= 0) return;
            if (Widgets.ButtonText(new Rect(rect.x, rect.y + 134, Math.Min(rect.width, 360), 38),
                "Применить правила к этой колонии"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Завершить все " + unfinished + " исследований в текущем сохранении? Это действие изменит прогресс колонии после следующего сохранения.",
                    () =>
                    {
                        ResearchRules.Result result = ResearchRules.ApplyAll();
                        Messages.Message(result.remaining == 0 && result.errors == 0
                            ? "Grechka World: все исследования завершены."
                            : "Grechka World: осталось незавершённых исследований: " + result.remaining
                                + "; ошибок при применении: " + result.errors + ". Подробности в журнале.",
                            result.remaining == 0 && result.errors == 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent);
                    }));
            }
        }
    }

    internal static class ResearchRules
    {
        internal readonly struct Result
        {
            internal readonly int changed;
            internal readonly int remaining;
            internal readonly int errors;
            internal Result(int changed, int remaining, int errors)
            { this.changed = changed; this.remaining = remaining; this.errors = errors; }
        }

        internal static int UnfinishedCount()
        {
            return DefDatabase<ResearchProjectDef>.AllDefsListForReading.Count(project => !project.IsFinished);
        }

        internal static Result ApplyAll()
        {
            ResearchManager manager = Find.ResearchManager;
            if (manager == null) throw new InvalidOperationException("ResearchManager ещё не создан.");

            int before = UnfinishedCount();
            var failed = new List<string>();
            // FinishProject handles prerequisites, techprints, anomaly analysis,
            // research mods, unlock notifications and completion signals.
            foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading.ToList())
            {
                if (project.IsFinished) continue;
                try
                {
                    manager.FinishProject(project, false, null, false);
                    if (!project.IsFinished) failed.Add(project.defName + " (осталось незавершённым)");
                }
                catch (Exception error) { failed.Add(project.defName + " (" + error.Message + ")"); }
            }

            int remaining = UnfinishedCount();
            if (failed.Count > 0 || remaining > 0)
                Log.Error("[Grechka World Integration] После применения незавершённых: " + remaining
                    + ". Ошибки: " + string.Join("; ", failed.Take(20))
                    + (failed.Count > 20 ? "; и ещё " + (failed.Count - 20) : ""));
            else
                Log.Message("[Grechka World Integration] Завершены все исследования; новых: " + (before - remaining) + ".");
            return new Result(before - remaining, remaining, failed.Count);
        }
    }

    // Game.InitNewGame calls this after map creation and immediately before
    // Game.StartedNewGame, so other GameComponents see completed research.
    [HarmonyPatch]
    internal static class NewGameResearchHook
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("ResearchUtility"), "ApplyPlayerStartingResearch");
        }

        static void Postfix() { ResearchRules.ApplyAll(); }
    }

    // Hide the built-in tab even when an old saved keybinding still points to F6.
    [HarmonyPatch(typeof(MainTabsRoot), "SetCurrentTab", new Type[] { typeof(MainButtonDef), typeof(bool) })]
    internal static class ResearchTabGuard
    {
        static bool Prefix(MainButtonDef __0) { return __0 == null || __0.defName != "Research"; }
    }
}
