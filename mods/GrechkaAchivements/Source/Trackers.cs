using System;
using System.Linq;
using System.Reflection;
using AchievementsExpanded;
using Grechka.Quests;
using Verse;

namespace Grechka.Achivements
{
    // VAE creates one runtime tracker per card using its public copy constructor.
    public abstract class GrechkaTracker : TrackerBase
    {
        protected GrechkaTracker() { }
        protected GrechkaTracker(GrechkaTracker reference) : base(reference) { }
        public override string Key { get => GetType().FullName; set { } }
        protected override string[] DebugText => new[] { Key };
    }

    public sealed class FarmTracker : GrechkaTracker
    {
        public FarmTracker() { }
        public FarmTracker(FarmTracker reference) : base(reference) { }
        public override Func<bool> AttachToLongTick => Trigger;
        public override bool Trigger()
        {
            var farm = DefDatabase<TerrainDef>.GetNamedSilentFail("Grechka_Farm");
            var rice = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Rice");
            return farm != null && rice != null && Find.Maps.Any(map => map.IsPlayerHome &&
                map.listerThings.ThingsOfDef(rice)
                    .Any(plant => plant.Position.GetTerrain(map) == farm));
        }
    }

    public sealed class QuestRewardTracker : GrechkaTracker
    {
        public QuestRewardTracker() { }
        public QuestRewardTracker(QuestRewardTracker reference) : base(reference) { }
        public override Func<bool> AttachToLongTick => Trigger;
        public override bool Trigger()
        {
            var book = QuestGame.Current;
            return book?.Pack?.quests?.Any(book.Claimed) == true;
        }
    }

    public sealed class OpenBlockTracker : GrechkaTracker
    {
        // Omit the filter for the first opening of any Grechka lucky block.
        public ThingDef block;
        public OpenBlockTracker() { }
        public OpenBlockTracker(OpenBlockTracker reference) : base(reference) { block = reference.block; }
        public override MethodInfo MethodHook => Type.GetType("Grechka.LuckyBlocks.LuckyBlock, GrechkaLuckyBlocks")?.GetMethod("Open", Type.EmptyTypes);
        public override MethodInfo PatchMethod => typeof(OpenBlockTracker).GetMethod(nameof(AfterOpen));
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref block, "block");
        }
        public bool Accepts(ThingDef opened) => opened != null && (block == null || block == opened);

        public static void AfterOpen(Thing __instance, bool __result)
        {
            if (!__result) return;
            Current.Game?.GetComponent<ColonyMilestones>()?.BlockOpened();
            foreach (var card in AchievementPointManager.GetCards<OpenBlockTracker>())
                if (card.tracker is OpenBlockTracker tracker && tracker.Accepts(__instance.def))
                    card.UnlockCard();
        }
    }
}
