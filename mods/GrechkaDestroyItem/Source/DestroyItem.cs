using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Grechka.DestroyItem
{
    public sealed class GrechkaDestroyItemMod : Mod
    {
        public GrechkaDestroyItemMod(ModContentPack content) : base(content)
        {
            new Harmony("grechka.destroyitem").PatchAll();
        }
    }

    [DefOf]
    public static class DestroyDefs
    {
        public static DesignationDef Grechka_DestroyItem;
        public static ThoughtDef Grechka_DestroyedItem;
        static DestroyDefs() { DefOfHelper.EnsureInitializedInCtor(typeof(DestroyDefs)); }
        public static JobDef Job => DefDatabase<JobDef>.GetNamed("Grechka_DestroyItem");
    }

    public static class DestroyOrders
    {
        public const int WorkTicks = 120;

        public static bool IsTarget(Thing thing) => thing != null && thing.def != null
            && !(thing is Pawn) && !thing.Destroyed && thing.Spawned
            && thing.def.destroyable
            && (thing.def.category == ThingCategory.Item || thing is Corpse);

        public static bool IsMarked(Thing thing) => IsTarget(thing)
            && thing.Map.designationManager.DesignationOn(thing, DestroyDefs.Grechka_DestroyItem) != null;

        public static void Mark(Thing thing)
        {
            if (IsTarget(thing) && !IsMarked(thing))
                thing.Map.designationManager.AddDesignation(new Designation(thing, DestroyDefs.Grechka_DestroyItem));
        }

        public static void Cancel(Thing thing)
        {
            if (thing?.Spawned == true)
                thing.Map.designationManager.TryRemoveDesignationOn(thing, DestroyDefs.Grechka_DestroyItem);
        }

        // No skill, work-tag or manipulation requirement, including for manual orders.
        public static bool CanExecute(Pawn pawn, Thing thing, bool forced) => IsTarget(thing)
            && pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed
            && pawn.Faction?.IsPlayer == true && pawn.Map == thing.Map && pawn.jobs != null
            && (forced || !thing.IsForbidden(pawn))
            && pawn.CanReserveAndReach(thing, PathEndMode.Touch, forced ? Danger.Deadly : Danger.Some,
                1, -1, null, false);

        public static bool Complete(Pawn pawn, Thing thing)
        {
            if (!IsMarked(thing) || pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed
                || pawn.Faction?.IsPlayer != true || pawn.Map != thing.Map
                || !pawn.CanReachImmediate(thing, PathEndMode.Touch)) return false;
            thing.Destroy(DestroyMode.Vanish);
            if (!thing.Destroyed) return false;
            // Vanilla memory merging renews the single existing memory (stackLimit = 1).
            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(DestroyDefs.Grechka_DestroyedItem);
            return true;
        }

        public static Command MakeCommand(Thing thing)
        {
            bool marked = IsMarked(thing);
            return new DestroyCommand
            {
                defaultLabel = (marked ? "GrechkaDestroy_Cancel" : "GrechkaDestroy_Order").Translate(),
                defaultDesc = (marked ? "GrechkaDestroy_CancelDesc" : "GrechkaDestroy_OrderDesc").Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Designators/Deconstruct"),
                hotKey = KeyBindingDefOf.Designator_Deconstruct,
                action = () => { if (marked) Cancel(thing); else Mark(thing); }
            };
        }
    }

    public sealed class DestroyCommand : Command_Action { }

    [HarmonyPatch]
    public static class ItemGizmos
    {
        // Include item overrides that omit base.GetGizmos, including other loaded mods.
        public static IEnumerable<MethodBase> TargetMethods() => GenTypes.AllTypes
            .Where(t => typeof(Thing).IsAssignableFrom(t) && !typeof(Pawn).IsAssignableFrom(t)
                && !typeof(Building).IsAssignableFrom(t) && !t.ContainsGenericParameters)
            .Select(t => AccessTools.DeclaredMethod(t, nameof(Thing.GetGizmos), Type.EmptyTypes))
            .Where(m => m != null && !m.IsAbstract && m.ReturnType == typeof(IEnumerable<Gizmo>));

        public static void Postfix(Thing __instance, ref IEnumerable<Gizmo> __result)
        {
            if (DestroyOrders.IsTarget(__instance)) __result = Append(__result, __instance);
        }

        private static IEnumerable<Gizmo> Append(IEnumerable<Gizmo> original, Thing thing)
        {
            bool found = false;
            if (original != null)
                foreach (Gizmo gizmo in original) { if (gizmo is DestroyCommand) found = true; yield return gizmo; }
            if (!found) yield return DestroyOrders.MakeCommand(thing);
        }
    }

    public sealed class WorkGiver_DestroyItem : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.HaulableEver);
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => pawn.Map.designationManager
            .SpawnedDesignationsOfDef(DestroyDefs.Grechka_DestroyItem).Select(d => d.target.Thing);
        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DestroyDefs.Grechka_DestroyItem);
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            DestroyOrders.IsMarked(t) && DestroyOrders.CanExecute(pawn, t, forced);
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            HasJobOnThing(pawn, t, forced) ? JobMaker.MakeJob(DestroyDefs.Job, t) : null;
    }

    public sealed class FloatMenuOptionProvider_DestroyItem : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => false;
        protected override bool MechanoidCanDo => true;

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            if (!DestroyOrders.IsTarget(clickedThing)) return null;
            Pawn pawn = context.FirstSelectedPawn;
            string label = "GrechkaDestroy_Prioritize".Translate(clickedThing.LabelCap);
            if (!DestroyOrders.CanExecute(pawn, clickedThing, true))
                return new FloatMenuOption(label + " (" + "GrechkaDestroy_Unavailable".Translate() + ")", null);
            return new FloatMenuOption(label, () =>
            {
                if (!DestroyOrders.CanExecute(pawn, clickedThing, true)) return;
                DestroyOrders.Mark(clickedThing);
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DestroyDefs.Job, clickedThing), JobTag.Misc);
            });
        }
    }

    public sealed class JobDriver_DestroyItem : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !DestroyOrders.IsMarked(TargetThingA));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(ticks: DestroyOrders.WorkTicks, face: TargetIndex.A)
                .WithProgressBarToilDelay(TargetIndex.A).FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() => { if (!DestroyOrders.Complete(pawn, TargetThingA)) EndJobWith(JobCondition.Incompletable); });
        }
    }
}
