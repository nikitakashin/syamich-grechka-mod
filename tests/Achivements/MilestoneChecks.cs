using System.Runtime.CompilerServices;
using AchievementsExpanded;
using Grechka.Achivements;
using Grechka.Quests;
using HarmonyLib;
using RimWorld;
using Verse;
using Quest = Grechka.Quests.Quest;

static class MilestoneChecks
{
    static readonly Faction Player = new Faction();
    static bool owned = true;
    static bool HasColony(ref bool __result) { __result = true; return false; }
    static bool OfPlayer(ref Faction __result) { __result = Player; return false; }
    static bool Owner(ref Faction __result) { __result = owned ? Player : null; return false; }

    public static void Run(Game game, QuestGame book, QuestProgress progress, Quest quest)
    {
        void Check(bool ok, string what) { if (!ok) throw new Exception(what); }
        var totals = new ColonyMilestones(game);
        game.components.Add(totals);
        totals.FinalizeInit();
        totals.FinalizeInit(); // Reloading or reinitializing cannot double-subscribe.
        var harmony = new Harmony("grechka.achivements.isolated");
        foreach (var (type, prop, method) in new[] {
            (typeof(QuestGame), nameof(book.HasColony), nameof(HasColony)),
            (typeof(Faction), nameof(Faction.OfPlayer), nameof(OfPlayer)),
            (typeof(Thing), nameof(Thing.Faction), nameof(Owner)) })
            harmony.Patch(AccessTools.PropertyGetter(type, prop), prefix: new HarmonyMethod(typeof(MilestoneChecks), method));
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
        var machine = new Building();
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(machine, (sbyte)0);
        void Report(string item, int count, string kind = "produce") =>
            book.RecordVerifiedMachineOutput(kind, machine, new ThingDef { defName = "Grechka_" + item }, null, null, count);
        var threshold = new MilestoneTracker { series = MilestoneSeries.Mechanites, target = 25 };
        Report("Mechanites", 24);
        Check(!threshold.Trigger() && threshold.PercentComplete.Item2 == "24 / 25", "premature milestone");
        Report("Mechanites", 5, "mine"); Report("Mechanites", 0);
        owned = false; Report("Mechanites", 3); owned = true;
        Check(totals.Total(threshold.series) == 24, "unverified output was credited");
        Report("Mechanites", 8);
        Check(threshold.Trigger() && totals.Total(threshold.series) == 32, "batch crossing a threshold");
        Report("Silicon", 1000); Report("SiliconSubstrate", 5);
        Check(totals.Total(MilestoneSeries.Silicon) == 1000, "silicon counted a derivative");
        foreach (var metal in new[] { "Quartz", "Silver", "Gold" }) Report(metal + "Processor", 2);
        for (int tier = 1; tier <= 5; tier++) Report($"HousingT{tier}_Item", 1);
        Check(totals.Total(MilestoneSeries.Processors) == 6 && totals.Total(MilestoneSeries.Housings) == 5, "family totals");
        progress.RecordClaim(book.Pack, quest); // Already claimed before the counters existed.
        book.Pack = new QuestPack { id = "another" };
        Check(totals.Total(MilestoneSeries.Quests) == 1, "reward history across books and duplicate claims");
        ((Dictionary<Type, string>)AccessTools.Field(typeof(AchievementPointManager), "typeToKey").GetValue(null))
            [typeof(OpenBlockTracker)] = nameof(OpenBlockTracker);
        ((Dictionary<string, HashSet<AchievementCard>>)AccessTools.Field(typeof(AchievementPointManager), "achievementLookup").GetValue(null))
            [nameof(OpenBlockTracker)] = new();
        OpenBlockTracker.AfterOpen(null, false);
        Check(totals.Total(MilestoneSeries.Blocks) == 0, "partial opening counted");
        OpenBlockTracker.AfterOpen(new Thing(), true);
        Check(totals.Total(MilestoneSeries.Blocks) == 1, "opening counted after both original cards unlocked");

        var prefs = AccessTools.Field(typeof(Prefs), "data");
        prefs.SetValue(null, Activator.CreateInstance(prefs.FieldType));
        var path = Path.GetTempFileName();
        try
        {
            Scribe.saver.InitSaving(path, "fixture"); totals.ExposeData(); Scribe.saver.FinalizeSaving();
            var restored = new ColonyMilestones(game);
            Scribe.loader.InitLoading(path); restored.ExposeData(); Scribe.loader.FinalizeLoading();
            foreach (var series in Enum.GetValues<MilestoneSeries>())
                Check(restored.Total(series) == totals.Total(series), $"lost {series} on load");
            File.WriteAllText(path, "<fixture/>");
            Scribe.loader.InitLoading(path); restored.ExposeData(); Scribe.loader.FinalizeLoading();
            Check(restored.Total(MilestoneSeries.Blocks) == 0 && restored.Total(MilestoneSeries.Quests) == 1,
                "old saves start new counters and retain claim history");
        }
        finally { File.Delete(path); }
    }
}
