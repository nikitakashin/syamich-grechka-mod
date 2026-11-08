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
    public sealed class GrechkaWorldSettings : ModSettings
    {
        public bool enableResearch;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enableResearch, "enableResearch", false);
        }
    }

    public sealed class GrechkaWorldIntegrationMod : Mod
    {
        public static GrechkaWorldSettings Settings;
        public static bool ResearchEnabled => Settings?.enableResearch ?? false;
        public GrechkaWorldIntegrationMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<GrechkaWorldSettings>();
            new Harmony("grechka.world.integration.research").PatchAll(typeof(GrechkaWorldIntegrationMod).Assembly);
        }

        public override string SettingsCategory() { return "Grechka World Integration"; }

        public override void DoSettingsWindowContents(Rect rect)
        {
            bool wasEnabled = ResearchEnabled;
            Widgets.CheckboxLabeled(new Rect(rect.x, rect.y, rect.width, 30), WorldText.Get("GrechkaWorldIntegration_0fd8cb357129", "Enable research (vanilla progression)"), ref Settings.enableResearch);
            if (wasEnabled != ResearchEnabled)
            {
                ResearchTree.SyncRequirements();
                ResearchTree.SyncResearchButton();
            }
            string description = WorldText.Get("GrechkaWorldIntegration_5ddf440e1562", "Enabled. Base game, DLC and mod research works normally. Grechka content is unlocked in a separate tree by tier. ")
                + WorldText.Get("GrechkaWorldIntegration_b5c93418ad15", "Disabled. All research is completed in a new colony. Research already completed in a save is preserved. ")
                + WorldText.Get("GrechkaWorldIntegration_5a5243d14a2e", "Enable this mode before creating a colony for a full research playthrough. In an existing colony, disabling the mode immediately removes Grechka tree requirements from buildings and recipes.");
            float height = Text.CalcHeight(description, rect.width);
            Widgets.Label(new Rect(rect.x, rect.y + 38, rect.width, height), description);
            float y = rect.y + 50 + height;
            if (Widgets.ButtonText(new Rect(rect.x, y, Math.Min(rect.width, 320), 34), WorldText.Get("GrechkaWorldIntegration_ea8b4e4df140", "Rules and materials report")))
                Find.WindowStack.Add(new WorldProfileReportWindow());
            if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.ResearchManager == null)
            {
                Widgets.Label(new Rect(rect.x, y + 48, rect.width, 50), WorldText.Get("GrechkaWorldIntegration_62f1501f8552", "Load a colony to apply the profile explicitly."));
                return;
            }
            WorldProfileState state = Current.Game.GetComponent<WorldProfileState>();
            Widgets.Label(new Rect(rect.x, y + 48, rect.width, 55),
                WorldText.Get("GrechkaWorldIntegration_c03eda2493bd", "Research remaining: ") + ResearchRules.UnfinishedCount() + WorldText.Get("GrechkaWorldIntegration_bcc95c9caa30", ". Profile machines: ") + WorldProfile.AppliedTiers.Count
                + WorldText.Get("GrechkaWorldIntegration_78d6dc563572", ". Last backup: ") + (state.lastBackup ?? WorldText.Get("GrechkaWorldIntegration_5a2b62c9f339", "not yet created")));
            if (Widgets.ButtonText(new Rect(rect.x, y + 114, Math.Min(rect.width, 440), 38), WorldText.Get("GrechkaWorldIntegration_b44fd7897452", "Apply current rules with a backup")))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    WorldText.Get("GrechkaWorldIntegration_edea9a333fc8", "Create a separate save, then apply the current housing and material rules? ")
                    + (ResearchEnabled ? WorldText.Get("GrechkaWorldIntegration_b08d40f6b1ce", "Research progress will be preserved.") : WorldText.Get("GrechkaWorldIntegration_c5d17a274e4a", "All research will be completed."))
                    + WorldText.Get("GrechkaWorldIntegration_cdf852d6ec17", " Existing items will be preserved."),
                    () => LongEventHandler.QueueLongEvent(() =>
                    {
                        string backup = "Grechka-before-profile-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                        GameDataSaveLoader.SaveGame(backup);
                        string backupPath = GenFilePaths.FilePathForSavedGame(backup);
                        // SaveGame logs and swallows exceptions. Never apply when its backup failed.
                        if (!File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
                            throw new IOException(WorldText.Get("GrechkaWorldIntegration_d142c17d2622", "The backup was not created. Rules were preserved."));
                        state.lastBackup = backup;
                        WorldProfile.Apply(state, true);
                        int remaining = ResearchEnabled ? ResearchRules.UnfinishedCount() : ResearchRules.ApplyAll().remaining;
                        Messages.Message(WorldText.Get("GrechkaWorldIntegration_7639ca6b48ce", "Grechka World profile applied. Backup: ") + backup
                            + WorldText.Get("GrechkaWorldIntegration_ef4720713b50", ". Unfinished research: ") + remaining + ".", MessageTypeDefOf.TaskCompletion);
                    }, "SavingLongEvent", false, error => Log.Error(WorldText.Get("GrechkaWorldIntegration_6450872dc979", "[Grechka World] Profile was not completed: ") + error))));
        }
    }

    public sealed class WorldProfileReportWindow : Window
    {
        private Vector2 scroll;
        public WorldProfileReportWindow() { doCloseButton = true; doCloseX = true; absorbInputAroundWindow = true; }
        public override Vector2 InitialSize => new Vector2(900, 700);
        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(0, 0, inRect.width, 30), WorldText.Get("GrechkaWorldIntegration_d571bbdb3c91", "Grechka World applied rules"));
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
            if (manager == null) throw new InvalidOperationException(WorldText.Get("GrechkaWorldIntegration_521426dd531a", "ResearchManager has not been created yet."));

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
                    if (!project.IsFinished) failed.Add(project.defName + WorldText.Get("GrechkaWorldIntegration_f4ac5186740c", " (still unfinished)"));
                }
                catch (Exception error) { failed.Add(project.defName + " (" + error.Message + ")"); }
            }

            int remaining = UnfinishedCount();
            if (failed.Count > 0 || remaining > 0)
                Log.Error(WorldText.Get("GrechkaWorldIntegration_ccc87bcd3ae6", "[Grechka World Integration] Unfinished after application: ") + remaining
                    + WorldText.Get("GrechkaWorldIntegration_cca16310493d", ". Errors: ") + string.Join("; ", failed.Take(20))
                    + (failed.Count > 20 ? WorldText.Get("GrechkaWorldIntegration_8c58d44c61a9", ". Additional errors: ") + (failed.Count - 20) : ""));
            else
                Log.Message(WorldText.Get("GrechkaWorldIntegration_29dad5caaeac", "[Grechka World Integration] All research completed. Newly completed: ") + (before - remaining) + ".");
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

        static void Postfix() { if (!GrechkaWorldIntegrationMod.ResearchEnabled) ResearchRules.ApplyAll(); }
    }

    // Hide the built-in tab even when an old saved keybinding still points to F6.
    [HarmonyPatch(typeof(MainTabsRoot), "SetCurrentTab", new Type[] { typeof(MainButtonDef), typeof(bool) })]
    internal static class ResearchTabGuard
    {
        static bool Prefix(MainButtonDef __0) { return GrechkaWorldIntegrationMod.ResearchEnabled || __0 == null || __0.defName != "Research"; }
    }
}
