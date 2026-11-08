using System.Reflection;
using Grechka.Tweaks;
using HarmonyLib;
using RimWorld;
using Verse;

static class TrainerChecks
{
    static readonly HashSet<SkillRecord> Disabled = new();
    static int notifications;
    public static bool SkillDisabled(SkillRecord __instance, ref bool __result)
    { __result = Disabled.Contains(__instance); return false; }
    public static bool Notify() { notifications++; return false; }
    public static bool CanUse(CompUsable __instance, Pawn __0, ref AcceptanceReport __result)
    { __result = __instance.parent.GetComp<CompUseEffect_InterestTrainer>().CanBeUsedBy(__0); return false; }

    public static int Run(Harmony fixture)
    {
        int checks = 0;
        void Check(bool ok, string message)
        {
            if (!ok) throw new Exception(message);
            checks++; Console.WriteLine("PASS trainer: " + message);
        }
        fixture.Unpatch(AccessTools.Method(typeof(SkillRecord), "get_TotallyDisabled"), HarmonyPatchType.Prefix, fixture.Id);
        fixture.Patch(AccessTools.Method(typeof(SkillRecord), "get_TotallyDisabled"),
            prefix: new HarmonyMethod(typeof(TrainerChecks), nameof(SkillDisabled)));
        fixture.Patch(AccessTools.Method(typeof(Messages), nameof(Messages.Message),
            new[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(TrainerChecks), nameof(Notify)));
        Current.Game.uniqueIDsManager = new UniqueIDsManager();
        AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, new List<Map>());
        var language = new LoadedLanguage("English");
        AccessTools.Field(typeof(LoadedLanguage), "workerInt").SetValue(language, new LanguageWorker_English());
        AccessTools.Field(typeof(LoadedLanguage), "dataIsLoaded").SetValue(language, true);
        LanguageDatabase.activeLanguage = language; LanguageDatabase.defaultLanguage = language;
        var flesh = new FleshTypeDef { defName = "Normal" };
        FleshTypeDefOf.Mechanoid = new FleshTypeDef { defName = "Mechanoid" };
        Pawn PawnWithSkills(bool human = true, bool mech = false)
        {
            var pawn = new Pawn { def = new ThingDef { defName = "TrainerFixture", label = "human",
                race = new RaceProperties { intelligence = human ? Intelligence.Humanlike : Intelligence.Animal } } };
            AccessTools.Field(typeof(RaceProperties), "fleshType").SetValue(pawn.RaceProps,
                mech ? FleshTypeDefOf.Mechanoid : flesh);
            pawn.health = new Pawn_HealthTracker(pawn);
            pawn.Name = new NameSingle("TrainerFixture");
            pawn.skills = new Pawn_SkillTracker(pawn);
            pawn.skills.skills = new List<SkillRecord>();
            return pawn;
        }
        SkillRecord Skill(Pawn pawn, string name, int grade)
        {
            var def = new SkillDef { defName = name, label = name, skillLabel = name };
            var skill = new SkillRecord(pawn, def);
            Mastery.SetGrade(skill, grade);
            pawn.skills.skills.Add(skill);
            return skill;
        }
        ThingWithComps Item(int grade, int count)
        {
            var def = new ThingDef { defName = "FixtureInterestTrainer_Item", label = "trainer",
                category = ThingCategory.Item, thingClass = typeof(ThingWithComps), stackLimit = 10,
                comps = new List<CompProperties> { new CompProperties_Usable { useJob = new JobDef { defName = "UseItem", driverClass = typeof(JobDriver_UseItem) }, useDuration = 600 },
                    new CompProperties_InterestTrainer { grade = grade } } };
            var item = new ThingWithComps { def = def, stackCount = count };
            AccessTools.Method(typeof(ThingWithComps), "InitializeComps").Invoke(item, null);
            return item;
        }
        foreach (int grade in Enumerable.Range(2, 5))
        {
            Disabled.Clear();
            var pawn = PawnWithSkills();
            var target = Skill(pawn, "ModdedCustomSkill", 1);
            Mastery.SetLevel(target, 167, 1234.5f); target.xpSinceMidnight = 781;
            var excluded = Enumerable.Range(0, 7).Where(g => g != 1)
                .Select(g => Skill(pawn, "Existing" + g, g)).ToList();
            var disabled = Skill(pawn, "DisabledBlank", 1); Disabled.Add(disabled);
            var item = Item(grade, 3);
            var effect = item.GetComp<CompUseEffect_InterestTrainer>();
            Check(effect.CanBeUsedBy(pawn) && InterestTrainers.Candidates(pawn).SequenceEqual(new[] { target }),
                "grade " + grade + " only an enabled empty skill qualifies, including mod skills");
            effect.DoEffect(pawn);
            Check(Mastery.Grade(target) == grade && Mastery.Level(target) == 167
                && Mastery.Progress(target) == 1234.5f && target.xpSinceMidnight == 781,
                "grade " + grade + " changes interest permanently without altering mastery or either XP counter");
            Check(item.stackCount == 2 && !item.Destroyed && excluded.Select(Mastery.Grade)
                .SequenceEqual(new[] { 0, 2, 3, 4, 5, 6 }) && Mastery.Grade(disabled) == 1,
                "exactly one dose is consumed; aversion, all flames and disabled skill are unchanged");
            int sent = notifications;
            Check(!effect.CanBeUsedBy(pawn), "use is rejected without an eligible skill");
            effect.DoEffect(pawn);
            Check(item.stackCount == 2 && notifications == sent, "completion recheck preserves dose and emits no success on failure");

            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
            try
            {
                Scribe.saver.InitSaving(path, "trainedSkill"); target.ExposeData(); Scribe.saver.FinalizeSaving();
                var restored = new SkillRecord(pawn, target.def);
                Scribe.loader.InitLoading(path); restored.ExposeData(); Scribe.loader.FinalizeLoading();
                Check(Mastery.Grade(restored) == grade && Mastery.Level(restored) == 167
                    && Mastery.Progress(restored) == 1234.5f && restored.xpSinceMidnight == 781,
                    "grade " + grade + " survives actual native Scribe save/load with exact XP");
            }
            finally { Scribe.mode = LoadSaveMode.Inactive; File.Delete(path); }
        }
        Disabled.Clear();
        var racePawn = PawnWithSkills();
        var first = Skill(racePawn, "First", 1);
        var second = Skill(racePawn, "Second", 1);
        var lastDose = Item(3, 1);
        var use = lastDose.GetComp<CompUseEffect_InterestTrainer>();
        // A queued use is valid, but both skills change before completion.
        Check(use.CanBeUsedBy(racePawn), "queued use starts with two candidates");
        Mastery.SetGrade(first, 2); Mastery.SetGrade(second, 0);
        use.DoEffect(racePawn);
        Check(!lastDose.Destroyed && lastDose.stackCount == 1 && Mastery.Grade(second) == 0,
            "last dose survives when candidates disappear during the job");
        Mastery.SetGrade(first, 1);
        use.DoEffect(racePawn);
        Check(lastDose.Destroyed && Mastery.Grade(first) == 3, "last dose is destroyed after a successful application");
        Mastery.SetGrade(second, 1); use.DoEffect(racePawn);
        Check(Mastery.Grade(second) == 1, "a destroyed trainer cannot apply twice");

        foreach (var pawn in new[] { PawnWithSkills(false), PawnWithSkills(true, true), new Pawn(), null })
        {
            var item = Item(2, 1); var effect = item.GetComp<CompUseEffect_InterestTrainer>();
            Check(!effect.CanBeUsedBy(pawn), "animals, mechs and pawns without skills are ineligible");
            effect.DoEffect(pawn); Check(!item.Destroyed, "invalid pawn preserves dose");
        }
        var dead = PawnWithSkills(); Skill(dead, "DeadSkill", 1);
        AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(dead.health, PawnHealthState.Dead);
        Check(!Item(2, 1).GetComp<CompUseEffect_InterestTrainer>().CanBeUsedBy(dead), "dead pawns cannot use trainers");
        foreach (int grade in new[] { -1, 0, 1, 7 })
        {
            var item = Item(grade, 1); var effect = item.GetComp<CompUseEffect_InterestTrainer>();
            Check(!effect.CanBeUsedBy(racePawn), "invalid or lowering grade " + grade + " is rejected");
            effect.DoEffect(racePawn); Check(!item.Destroyed, "invalid grade never consumes the item");
        }
        // Reproducible game RNG: both eligible slots participate equally, independent of their level.
        int selectedFirst = 0;
        bool exactlyOne = true;
        Rand.PushState(13579);
        try
        {
            for (int n = 0; n < 500; n++)
            {
                Mastery.SetGrade(first, 1); Mastery.SetGrade(second, 1);
                Mastery.SetLevel(first, 0, 0); Mastery.SetLevel(second, 100, 0);
                Item(2, 1).GetComp<CompUseEffect_InterestTrainer>().DoEffect(racePawn);
                exactlyOne &= (Mastery.Grade(first) == 2) != (Mastery.Grade(second) == 2);
                if (Mastery.Grade(first) == 2) selectedFirst++;
            }
        }
        finally { Rand.PopState(); }
        Check(exactlyOne, "exactly one random skill changed on each of 500 uses");
        Check(selectedFirst > 200 && selectedFirst < 300, "seeded random selection reaches both skills without level weighting");
        fixture.Patch(AccessTools.Method(typeof(CompUsable), nameof(CompUsable.CanBeUsedBy)),
            prefix: new HarmonyMethod(typeof(TrainerChecks), nameof(CanUse)));
        Mastery.SetGrade(first, 1); Mastery.SetGrade(second, 2);
        var nativeItem = Item(6, 2); var nativeUse = nativeItem.GetComp<CompUsable>();
        nativeUse.UsedBy(racePawn);
        Check(Mastery.Grade(first) == 6 && nativeItem.stackCount == 1,
            "native CompUsable completion dispatches the effect and consumes one dose");
        nativeUse.UsedBy(racePawn);
        Check(nativeItem.stackCount == 1, "native use revalidates candidates before dispatching effects");
        Disabled.Clear();
        return checks;
    }
}
