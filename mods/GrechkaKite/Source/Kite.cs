using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Grechka.Kite
{
    public sealed class KiteSettings : ModSettings
    {
        public bool enemiesFireWhileMoving = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enemiesFireWhileMoving, "enemiesFireWhileMoving", true);
        }
    }

    public sealed class GrechkaKiteMod : Mod
    {
        public GrechkaKiteMod(ModContentPack content) : base(content)
        {
            MovingFire.Settings = GetSettings<KiteSettings>();
            new Harmony("grechka.kite").PatchAll(typeof(GrechkaKiteMod).Assembly);
        }

        public override string SettingsCategory() { return Content.Name; }

        public override void DoSettingsWindowContents(Rect rect)
        {
            var listing = new Listing_Standard();
            listing.Begin(rect);
            listing.Label("GrechkaKite_Intro".Translate());
            listing.Gap();
            listing.CheckboxLabeled("GrechkaKite_Enemies".Translate(),
                ref MovingFire.Settings.enemiesFireWhileMoving, "GrechkaKite_EnemiesTip".Translate());
            listing.Gap();
            listing.Label("GrechkaKite_Help".Translate());
            listing.End();
        }
    }

    public static class MovingFire
    {
        public static KiteSettings Settings = new KiteSettings();
        public const int ScanInterval = 10;

        public static bool CombatMove(Job job)
        {
            if (job == null) return false;
            JobDef def = job.def;
            return def == JobDefOf.Goto || def == JobDefOf.AttackStatic || def == JobDefOf.Wait_Combat;
        }

        public static bool Eligible(Pawn pawn, Job job)
        {
            if (pawn == null || !CombatMove(job) || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.InMentalState
                || pawn.WorkTagIsDisabled(WorkTags.Violent)) return false;
            if (pawn.Faction == Faction.OfPlayer)
                return pawn.Drafted && pawn.drafter.FireAtWill;
            return Settings.enemiesFireWhileMoving && pawn.HostileTo(Faction.OfPlayer);
        }

        public static bool SupportedVerb(Pawn pawn, Verb verb)
        {
            // Only the equipped primary's ordinary ranged attack. No abilities,
            // melee, apparel verbs or auto-casting of manual-only weapons.
            return pawn?.equipment?.Primary != null && verb?.verbTracker != null
                && verb.EquipmentSource == pawn.equipment.Primary
                && verb.verbProps != null && verb.verbProps.Ranged
                && verb.verbProps.violent && !verb.verbProps.onlyManualCast;
        }

        public static bool MobileStance(Pawn pawn)
        {
            Stance stance = pawn?.stances?.curStance;
            return (stance is Stance_Warmup || stance is Stance_Cooldown)
                && SupportedVerb(pawn, ((Stance_Busy)stance).verb);
        }

        public static bool BlocksMovement(Pawn_StanceTracker tracker)
        {
            // This substitutes only the path follower's busy check. Other users
            // of FullBodyBusy (jobs, combat and UI) retain native behaviour.
            return tracker.FullBodyBusy && (tracker.stunner.Stunned
                || !Eligible(tracker.pawn, tracker.pawn.CurJob) || !MobileStance(tracker.pawn));
        }

        public static bool ValidTarget(Pawn pawn, Verb verb, Thing target)
        {
            return target != null && target.Spawned && target.Map == pawn.Map
                && target.HostileTo(pawn) && !(target is Pawn victim && (victim.Dead || victim.Downed))
                && verb.CanHitTarget(target);
        }

        public static void Tick(Pawn pawn)
        {
            if (pawn?.pather == null || !pawn.pather.Moving || !Eligible(pawn, pawn.CurJob)
                || pawn.stances == null || pawn.stances.FullBodyBusy
                || !pawn.IsHashIntervalTick(ScanInterval)) return;
            Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (!SupportedVerb(pawn, verb) || verb.Bursting || !verb.Available()) return;

            Thing target = pawn.mindState?.enemyTarget;
            if (!ValidTarget(pawn, verb, target))
                target = AttackTargetFinder.BestShootTargetFromCurrentPosition(pawn,
                    TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable
                    | TargetScanFlags.NeedLOSToAll | TargetScanFlags.LOSBlockableByGas,
                    candidate => ValidTarget(pawn, verb, candidate), maxDistance: verb.EffectiveRange)?.Thing;
            if (target != null)
                verb.TryStartCastOn(target, surpriseAttack: false, canHitNonTargetPawns: true, preventFriendlyFire: true);
        }
    }

    [HarmonyPatch(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.StanceTrackerTick))]
    public static class MovingFireTickPatch
    {
        public static void Postfix(Pawn_StanceTracker __instance) { MovingFire.Tick(__instance.pawn); }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    public static class MovingFirePathPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Pawn_StanceTracker), nameof(Pawn_StanceTracker.FullBodyBusy));
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    var replacement = new CodeInstruction(instruction);
                    replacement.opcode = OpCodes.Call;
                    replacement.operand = AccessTools.Method(typeof(MovingFire), nameof(MovingFire.BlocksMovement));
                    yield return replacement;
                    replaced++;
                }
                else yield return instruction;
            }
            if (replaced != 1)
                throw new InvalidOperationException("[GrechkaKite] Expected one path-follower busy check; found " + replaced + ".");
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class MovingFireOrderPatch
    {
        public static void Prefix(Pawn ___pawn, Job newJob, ref bool cancelBusyStances)
        {
            // Repeated move orders must not reset a running aim/burst/cooldown.
            // Other orders still cancel stances using the game's own rules.
            if (newJob?.def == JobDefOf.Goto && MovingFire.Eligible(___pawn, newJob) && MovingFire.MobileStance(___pawn))
                cancelBusyStances = false;
        }
    }
}
