using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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
                "Grechka World: корпуса машин, явный словарь материалов и завершённые исследования. Новая колония получает текущий профиль; старые и новые добавленные правила применяются вручную с резервной копией.");
            if (Widgets.ButtonText(new Rect(rect.x, rect.y + 86, 320, 34), "Отчёт правил и материалов"))
                Find.WindowStack.Add(new WorldProfileReportWindow());
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.ResearchManager == null)
            {
                Widgets.Label(new Rect(rect.x, rect.y + 132, rect.width, 50), "Загрузите колонию для явного применения профиля.");
                return;
            }
            WorldProfileState state = Current.Game.GetComponent<WorldProfileState>();
            Widgets.Label(new Rect(rect.x, rect.y + 132, rect.width, 55),
                "Исследований осталось: " + ResearchRules.UnfinishedCount() + ". Машин профиля: " + WorldProfile.AppliedTiers.Count
                + ". Последняя копия: " + (state.lastBackup ?? "ещё не создавалась"));
            if (Widgets.ButtonText(new Rect(rect.x, rect.y + 198, Math.Min(rect.width, 440), 38), "Применить текущие правила с резервной копией"))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Создать отдельное сохранение, затем применить текущие правила корпусов/материалов и завершить исследования? Существующие предметы не преобразуются и не удаляются.",
                    () => LongEventHandler.QueueLongEvent(() =>
                    {
                        string backup = "Grechka-before-profile-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                        GameDataSaveLoader.SaveGame(backup);
                        string backupPath = GenFilePaths.FilePathForSavedGame(backup);
                        // SaveGame logs and swallows exceptions. Never apply when its backup failed.
                        if (!File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
                            throw new IOException("Резервная копия не создана; правила не изменены.");
                        state.lastBackup = backup;
                        WorldProfile.Apply(state, true);
                        ResearchRules.Result result = ResearchRules.ApplyAll();
                        Messages.Message("Grechka World: профиль применён. Копия: " + backup
                            + ". Незавершённых исследований: " + result.remaining + ".", MessageTypeDefOf.TaskCompletion);
                    }, "SavingLongEvent", false, error => Log.Error("[Grechka World] Профиль не завершён: " + error))));
        }
    }

    public sealed class WorldProfileReportWindow : Window
    {
        private Vector2 scroll;
        public WorldProfileReportWindow() { doCloseButton = true; doCloseX = true; absorbInputAroundWindow = true; }
        public override Vector2 InitialSize => new Vector2(900, 700);
        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(0, 0, inRect.width, 30), "Grechka World — применённые правила");
            var lines = WorldProfile.Report.ToList();
            foreach (MaterialGroupDef group in DefDatabase<MaterialGroupDef>.AllDefsListForReading)
                lines.Add(group.defName + " [" + group.form + "]: " + string.Join(", ", MaterialDictionary.Resolve(group).Select(d => d.defName)));
            string text = string.Join("\n", lines);
            Rect viewport = new Rect(0, 36, inRect.width, inRect.height - 88);
            Rect content = new Rect(0, 0, viewport.width - 22, Math.Max(viewport.height, Text.CalcHeight(text, viewport.width - 22)));
            Widgets.BeginScrollView(viewport, ref scroll, content);
            Widgets.Label(content, text);
            Widgets.EndScrollView();
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
