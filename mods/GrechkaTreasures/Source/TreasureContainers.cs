using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grechka.Treasures
{
    public sealed class CompProperties_TreasureChest : CompProperties
    {
        public CompProperties_TreasureChest() { compClass = typeof(CompTreasureChest); }
    }

    public sealed class CompTreasureChest : ThingComp, IThingHolder
    {
        ThingOwner<Thing> contents;
        bool initialized;
        bool opened;
        bool decoy;
        int tier = 1;
        public bool Opened => opened;
        public bool Initialized => initialized;
        public bool HasContents => contents != null && contents.Count > 0;
        public bool CanOpen => !opened || HasContents;
        public CompTreasureChest() { contents = new ThingOwner<Thing>(this); }
        public ThingOwner GetDirectlyHeldThings() => contents;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, contents);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref initialized, "treasureInitialized");
            Scribe_Values.Look(ref opened, "treasureOpened");
            Scribe_Values.Look(ref decoy, "treasureDecoy");
            Scribe_Values.Look(ref tier, "treasureTier", 1);
            Scribe_Deep.Look(ref contents, "treasureContents", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && contents == null) contents = new ThingOwner<Thing>(this);
        }

        public void InitializeLoot(int seed, int dangerTier, string gem, bool isDecoy, bool looted)
        {
            if (initialized) return;
            initialized = true;
            tier = Math.Max(1, Math.Min(5, dangerTier));
            decoy = isDecoy;
            foreach (TreasureLootEntry entry in TreasureLoot.Plan(seed, tier, gem, decoy, looted)) {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(entry.DefName);
                if (def == null) { Log.Warning("[GrechkaTreasures] Missing loot definition " + entry.DefName + "; no substitution was made."); continue; }
                int remaining = entry.Count;
                while (remaining > 0) {
                    Thing thing = ThingMaker.MakeThing(def);
                    thing.stackCount = Math.Min(remaining, Math.Max(1, def.stackLimit));
                    remaining -= thing.stackCount;
                    contents.TryAdd(thing);
                }
            }
        }

        // Mark opened BEFORE transferring. Failed placement remains in the owner and can be retried.
        // No random generation or loot grants happen from this path.
        public bool Open(Pawn opener = null)
        {
            if (!initialized || !parent.Spawned || !CanOpen) return false;
            bool first = !opened;
            opened = true;
            if (first && decoy) Messages.Message("GrechkaTreasures_DecoyOpened".Translate(), parent, MessageTypeDefOf.NeutralEvent);
            contents.TryDropAll(parent.Position, parent.Map, ThingPlaceMode.Near);
            return true;
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (previousMap != null && HasContents && mode != DestroyMode.Vanish)
                contents.TryDropAll(parent.Position, previousMap, ThingPlaceMode.Near);
        }
        public override string CompInspectStringExtra()
            => (opened ? (HasContents ? "GrechkaTreasures_ChestRemaining" : "GrechkaTreasures_ChestEmpty") : "GrechkaTreasures_ChestSealed").Translate();
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (!CanOpen || !initialized || !parent.Spawned || !selPawn.IsColonistPlayerControlled) yield break;
            string label = (opened ? "GrechkaTreasures_CollectChest" : "GrechkaTreasures_OpenChest").Translate();
            if (!selPawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Deadly)) {
                yield return new FloatMenuOption(label + " (" + "NoPath".Translate() + ")", null); yield break;
            }
            yield return new FloatMenuOption(label, () => selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("GrechkaTreasures_OpenChest"), parent), JobTag.Misc));
        }
    }

    public sealed class CompProperties_TreasureMap : CompProperties
    {
        public CompProperties_TreasureMap() { compClass = typeof(CompTreasureMap); }
    }
    public sealed class CompTreasureMap : ThingComp
    {
        public bool Read(Pawn reader)
        {
            if (!parent.Spawned || !TreasureSites.TryCreate(out TreasureSite site)) return false;
            // Consuming this explicit, physical one-use map is separate from Grechka Quests objectives.
            parent.SplitOff(1).Destroy();
            TreasureSites.SendLetter(site);
            return true;
        }
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (!parent.Spawned || !selPawn.IsColonistPlayerControlled) yield break;
            string label = "GrechkaTreasures_ReadMap".Translate();
            if (!TreasureSites.CanCreate) { yield return new FloatMenuOption(label + " (" + "GrechkaTreasures_SiteLimit".Translate() + ")", null); yield break; }
            if (!selPawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some)) { yield return new FloatMenuOption(label + " (" + "NoPath".Translate() + ")", null); yield break; }
            yield return new FloatMenuOption(label, () => selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("GrechkaTreasures_ReadMap"), parent), JobTag.Misc));
        }
    }

    public sealed class CompProperties_TreasureClue : CompProperties
    {
        public CompProperties_TreasureClue() { compClass = typeof(CompTreasureClue); }
    }
    public sealed class CompTreasureClue : ThingComp
    {
        public string text;
        public override void PostExposeData() { base.PostExposeData(); Scribe_Values.Look(ref text, "treasureClue"); }
        public override string CompInspectStringExtra() => text ?? "GrechkaTreasures_DefaultClue".Translate();
    }

    public sealed class JobDriver_OpenTreasure : JobDriver
    {
        CompTreasureChest Chest => (job.targetA.Thing as ThingWithComps)?.GetComp<CompTreasureChest>();
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Chest?.CanOpen != true);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(480, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            Toil open = ToilMaker.MakeToil("OpenTreasureChest");
            open.initAction = () => Chest?.Open(pawn);
            open.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return open;
        }
    }
    public sealed class JobDriver_ReadTreasureMap : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(240, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            Toil read = ToilMaker.MakeToil("ReadTreasureCoordinates");
            read.initAction = () => {
                if ((job.targetA.Thing as ThingWithComps)?.GetComp<CompTreasureMap>()?.Read(pawn) != true)
                    Messages.Message("GrechkaTreasures_NoSiteTile".Translate(), MessageTypeDefOf.RejectInput, false);
            };
            read.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return read;
        }
    }
}
