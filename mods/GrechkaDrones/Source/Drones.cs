using System;
using System.Collections.Generic;
using System.Linq;
using AIRobot;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grechka.Drones
{
    public enum DroneRole { Rescue, Warrior, Caravan }

    public sealed class DroneProperties : DefModExtension
    {
        public DroneRole role;
        public int tier = 1;
        public float foamRadius = 3f;
        public int foamCooldownTicks = 300;
    }

    // A latched retreat cannot be cancelled by a new enemy or a manual job re-evaluation.
    public static class DroneRules
    {
        public const float RetreatHealth = .30f;
        public const float RepairedHealth = .999f;
        public static bool ShouldRetreat(float health, bool repairing) => repairing || health <= RetreatHealth;
        public static bool ShouldPreempt(bool capturing, bool ownRescueAvailable) => capturing && ownRescueAvailable;
        public static bool BlocksFire(DamageDef damage) => damage != null && (damage == DamageDefOf.Flame || damage == DamageDefOf.Burn);
        public static bool IsOwn(Pawn pawn) => pawn != null && pawn.Faction?.IsPlayer == true && !pawn.IsPrisoner;
    }

    public class Drone : X2_AIRobot
    {
        public bool repairing;
        public bool recharging;
        public bool captureEnemies;
        public int nextFoamTick;
        public DroneProperties Properties => def.GetModExtension<DroneProperties>();
        public DroneStation Station => rechargeStation as DroneStation;
        public bool CaptureEnemies
        {
            get => Station != null ? Station.captureEnemies : captureEnemies;
            set { captureEnemies = value; if (Station != null) Station.captureEnemies = value; }
        }
        public float HealthFraction => health?.summaryHealth?.SummaryHealthPercent ?? 1f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref repairing, "grechkaRepairing");
            Scribe_Values.Look(ref recharging, "grechkaRecharging");
            Scribe_Values.Look(ref captureEnemies, "grechkaCaptureEnemies");
            Scribe_Values.Look(ref nextFoamTick, "grechkaNextFoamTick");
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (Faction?.IsPlayer == true && Properties?.role == DroneRole.Rescue)
                yield return CaptureCommand(() => CaptureEnemies, value => CaptureEnemies = value);
        }
        public static Command_Toggle CaptureCommand(Func<bool> get, Action<bool> set) => new Command_Toggle
        {
            defaultLabel = "GrechkaDrones_Capture".Translate(),
            defaultDesc = "GrechkaDrones_CaptureDesc".Translate(),
            icon = TexCommand.SelectCarriedPawn,
            isActive = get,
            toggleAction = () => set(!get())
        };

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            if (DroneRules.BlocksFire(dinfo.Def)) { absorbed = true; return; }
            base.PreApplyDamage(ref dinfo, out absorbed);
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned || Dead || Downed || Faction?.IsPlayer != true || Properties == null) return;
            if (Properties.role == DroneRole.Warrior && DroneRules.ShouldRetreat(HealthFraction, repairing))
            {
                repairing = true;
                if (CurJobDef != DroneDefs.Grechka_DroneRetreat && this.IsHashIntervalTick(15))
                    ReplaceJob(DroneJobs.Retreat(this));
                return;
            }
            if (!this.IsHashIntervalTick(30) || Drafted) return;
            if (Properties.role == DroneRole.Rescue)
            {
                bool capture = CurJobDef == JobDefOf.Capture;
                bool foam = CurJobDef == DroneDefs.Grechka_DroneFoam;
                bool idle = DroneJobs.IsIdle(CurJobDef);
                if (capture || foam || idle)
                {
                    Job rescue = DroneJobs.RescueOwn(this);
                    if (rescue != null) { ReplaceJob(rescue); return; }
                }
                if (CurJobDef == JobDefOf.Capture && !CaptureEnemies)
                    jobs.EndCurrentJob(JobCondition.InterruptForced);
                else if (idle)
                    ReplaceJob(DroneJobs.Emergency(this));
            }
            else if (DroneJobs.IsIdle(CurJobDef))
                ReplaceJob(DroneJobs.Defend(this));
        }

        private void ReplaceJob(Job next)
        {
            if (next == null) return;
            // Drop first, before changing jobs. A failed drop must never delete a carried patient.
            if (carryTracker.CarriedThing != null && !carryTracker.TryDropCarriedThing(Position, ThingPlaceMode.Near, out _)) return;
            jobs.StartJob(next, JobCondition.InterruptForced, keepCarryingThingOverride: false);
        }
    }

    [DefOf]
    public static class DroneDefs
    {
        public static JobDef Grechka_DroneFoam;
        public static JobDef Grechka_DroneRetreat;
        public static JobDef Grechka_DroneRescueFloor;
        static DroneDefs() { DefOfHelper.EnsureInitializedInCtor(typeof(DroneDefs)); }
    }

    // Misc's recharge TryGiveJob always emits a job; its energy gate lives in GetPriority.
    // Honor that gate when starting, then keep the session until full or productive work.
    public class JobGiver_DroneRecharge : X2_JobGiver_RechargeEnergy
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!(pawn is Drone drone)) return GetPriority(pawn) > 0f ? base.TryGiveJob(pawn) : null;
            var station = drone.rechargeStation;
            var energy = drone.needs?.rest;
            if (station == null || station.Map != drone.Map || energy == null || drone.Drafted) return null;
            if (energy.CurLevel >= 1f)
            {
                FinishRecharge(drone);
                return null;
            }
            // Misc's short recharge job ends repeatedly. Its entry threshold must not
            // send a partially charged drone straight back out on an idle patrol.
            bool continuing = drone.recharging || station.isRechargeActive;
            if (!continuing && GetPriority(pawn) <= 0f) return null;
            if (drone.Properties?.role == DroneRole.Warrior && DroneRules.ShouldRetreat(drone.HealthFraction, drone.repairing))
                return null;
            if (continuing && energy.CurLevel >= .30f && drone.Properties != null)
            {
                Job work = DroneJobs.Work(drone);
                if (work != null)
                {
                    FinishRecharge(drone);
                    return work;
                }
            }
            Job recharge = base.TryGiveJob(pawn);
            if (recharge != null) drone.recharging = true;
            return recharge;
        }

        private static void FinishRecharge(Drone drone)
        {
            drone.recharging = false;
            drone.rechargeStation.isRechargeActive = false;
        }
    }

    public class JobGiver_Drone : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!(pawn is Drone drone) || drone.Faction?.IsPlayer != true || drone.Properties == null || drone.Drafted) return null;
            if (drone.Properties.role == DroneRole.Warrior)
            {
                if (DroneRules.ShouldRetreat(drone.HealthFraction, drone.repairing))
                { drone.repairing = true; return DroneJobs.Retreat(drone); }
                return DroneJobs.Defend(drone) ?? DroneJobs.Patrol(drone);
            }
            return DroneJobs.RescueOwn(drone) ?? DroneJobs.Emergency(drone) ?? JobMaker.MakeJob(JobDefOf.Wait, 60);
        }
    }

    public static class DroneJobs
    {
        // Productive work only: patrol and idle waits must never interrupt charging.
        public static Job Work(Drone drone) => drone.Properties.role == DroneRole.Warrior
            ? Defend(drone) : drone.Properties.role == DroneRole.Rescue ? RescueOwn(drone) ?? Emergency(drone) : null;

        public static bool IsIdle(JobDef job) => job == JobDefOf.Wait || job == JobDefOf.Goto || job == JobDefOf.Wait_Wander;

        public static bool Allowed(Drone drone, IntVec3 cell) => cell.InBounds(drone.Map) && !cell.Fogged(drone.Map)
            && (drone.playerSettings?.AreaRestrictionInPawnCurrentMap == null || drone.playerSettings.AreaRestrictionInPawnCurrentMap[cell]);

        public static bool NeedsRescue(Pawn patient) => patient != null && !patient.Dead && patient.Downed && patient.Spawned && !patient.InBed();

        public static Job RescueOwn(Drone drone)
        {
            foreach (Pawn patient in drone.Map.mapPawns.AllPawnsSpawned
                .Where(p => p != drone && DroneRules.IsOwn(p) && NeedsRescue(p))
                .OrderBy(p => p.Position.DistanceToSquared(drone.Position)))
            {
                Job rescue = TakeToBed(drone, patient, false);
                if (rescue != null) return rescue;
                Job floor = RescueToSafety(drone, patient);
                if (floor != null) return floor;
            }
            return null;
        }

        public static Job TakeToBed(Drone drone, Pawn patient, bool capture)
        {
            if (!NeedsRescue(patient) || !Allowed(drone, patient.Position)
                || !drone.CanReserveAndReach(patient, PathEndMode.Touch, Danger.Deadly)) return null;
            Building_Bed bed = RestUtility.FindBedFor(patient, drone, true, guestStatus: capture ? GuestStatus.Prisoner : (GuestStatus?)null);
            if (bed == null || bed.ForPrisoners != (capture || patient.IsPrisoner)
                || !Allowed(drone, bed.Position) || !drone.CanReserveAndReach(bed, PathEndMode.OnCell, Danger.Deadly, bed.SleepingSlotsCount, 0)) return null;
            return JobMaker.MakeJob(capture ? JobDefOf.Capture : JobDefOf.Rescue, patient, bed);
        }

        public static bool SafeHomeCell(Drone drone, IntVec3 cell)
        {
            if (!Allowed(drone, cell) || !cell.Standable(drone.Map) || !drone.Map.areaManager.Home[cell]) return false;
            foreach (IntVec3 near in GenRadial.RadialCellsAround(cell, 3f, true))
                if (near.InBounds(drone.Map) && FireUtility.ContainsStaticFire(near, drone.Map)) return false;
            return !drone.Map.mapPawns.AllPawnsSpawned.Any(p => IsThreatToColony(drone, p) && p.Position.DistanceToSquared(cell) < 144);
        }

        public static Job RescueToSafety(Drone drone, Pawn patient)
        {
            if (!Allowed(drone, patient.Position) || SafeHomeCell(drone, patient.Position)
                || !drone.CanReserveAndReach(patient, PathEndMode.Touch, Danger.Deadly)) return null;
            IntVec3 origin = drone.rechargeStation?.Position ?? drone.Position;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(origin, 12f, true))
                if (SafeHomeCell(drone, cell) && !cell.GetThingList(drone.Map).OfType<Pawn>().Any()
                    && drone.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Deadly))
                    return JobMaker.MakeJob(DroneDefs.Grechka_DroneRescueFloor, patient, cell);
            return null;
        }

        public static Job Emergency(Drone drone)
        {
            if (Find.TickManager.TicksGame >= drone.nextFoamTick)
            {
                Fire fire = drone.Map.listerThings.ThingsInGroup(ThingRequestGroup.Fire).OfType<Fire>()
                    .Where(f => f.Spawned && Allowed(drone, f.Position) && (drone.Map.areaManager.Home[f.Position]
                        || f.parent is Pawn p && DroneRules.IsOwn(p)))
                    .OrderBy(f => f.Position.DistanceToSquared(drone.Position))
                    .FirstOrDefault(f => drone.CanReserveAndReach(f, PathEndMode.Touch, Danger.Deadly));
                if (fire != null) return JobMaker.MakeJob(DroneDefs.Grechka_DroneFoam, fire);
            }
            if (drone.CaptureEnemies)
                foreach (Pawn patient in drone.Map.mapPawns.AllPawnsSpawned.Where(p => NeedsRescue(p)
                    && p.RaceProps.Humanlike && (p.HostileTo(drone) || p.IsPrisonerOfColony))
                    .OrderBy(p => p.Position.DistanceToSquared(drone.Position)))
                {
                    Job capture = TakeToBed(drone, patient, true);
                    if (capture != null) return capture;
                }
            return null;
        }

        public static bool IsThreat(Drone drone, Pawn target) => IsThreatToColony(drone, target) && Allowed(drone, target.Position);

        public static bool IsThreatToColony(Drone drone, Pawn target)
        {
            if (target == null || target == drone || target.Dead || target.Downed || !target.Spawned
                || target.Map != drone.Map || target.IsPrisonerOfColony) return false;
            if (target.HostileTo(drone)) return true;
            if (!target.RaceProps.Animal) return false;
            if (target.mindState?.enemyTarget is Pawn victim && DroneRules.IsOwn(victim)) return true;
            // Predators use a hunting job and can attack our people without a hostile faction.
            return target.CurJobDef == JobDefOf.PredatorHunt && DroneRules.IsOwn(target.CurJob?.targetA.Pawn);
        }

        public static Job Defend(Drone drone)
        {
            Pawn target = drone.Map.mapPawns.AllPawnsSpawned.Where(p => IsThreat(drone, p))
                .OrderBy(p => p.Position.DistanceToSquared(drone.Position))
                .FirstOrDefault(p => drone.CanReach(p, PathEndMode.Touch, Danger.Deadly));
            if (target == null) return null;
            Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            job.expiryInterval = 180;
            job.checkOverrideOnExpire = true;
            job.killIncappedTarget = false;
            return job;
        }

        public static Job Patrol(Drone drone)
        {
            List<IntVec3> cells = drone.Map.areaManager.Home.ActiveCells.ToList();
            for (int i = 0; i < Math.Min(24, cells.Count); i++)
            {
                IntVec3 cell = cells[Rand.Range(0, cells.Count)];
                if (cell == drone.Position || !Allowed(drone, cell) || !cell.Standable(drone.Map)
                    || !drone.CanReach(cell, PathEndMode.OnCell, Danger.Some)) continue;
                Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
                job.locomotionUrgency = LocomotionUrgency.Walk;
                job.expiryInterval = 600;
                job.checkOverrideOnExpire = true;
                return job;
            }
            return JobMaker.MakeJob(JobDefOf.Wait, 60);
        }

        public static Job Retreat(Drone drone)
        {
            var station = drone.rechargeStation;
            if (station != null && station.Spawned && station.Map == drone.Map
                && drone.CanReach(station, PathEndMode.OnCell, Danger.Deadly))
            {
                Job job = JobMaker.MakeJob(DroneDefs.Grechka_DroneRetreat, station);
                job.locomotionUrgency = LocomotionUrgency.Sprint;
                return job;
            }
            // Preserve the retreat latch if the dock is destroyed or unreachable; never return to combat.
            return JobMaker.MakeJob(JobDefOf.Wait, 60);
        }
    }

    public class JobDriver_Foam : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(20, TargetIndex.A);
            Toil burst = ToilMaker.MakeToil("GrechkaFoamBurst");
            burst.initAction = () =>
            {
                Drone drone = (Drone)pawn;
                if (Find.TickManager.TicksGame < drone.nextFoamTick) return;
                IntVec3 center = job.targetA.Cell;
                drone.nextFoamTick = Find.TickManager.TicksGame + drone.Properties.foamCooldownTicks;
                GenExplosion.DoExplosion(center, pawn.Map, drone.Properties.foamRadius, DamageDefOf.Extinguish, pawn,
                    explosionSound: SoundDefOf.Explosion_FirefoamPopper, postExplosionSpawnThingDef: ThingDefOf.Filth_FireFoam, postExplosionSpawnChance: 1f, applyDamageToExplosionCellsNeighbors: true,
                    screenShakeFactor: 0f);
            };
            burst.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return burst;
        }
    }

    // A medical bed is preferred. Without one, move our downed pawn out of fire/combat to the home area.
    public class JobDriver_RescueFloor : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed) && pawn.Reserve(job.targetB, job, errorOnFailed: errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !(job.targetA.Thing is Pawn patient) || patient.Dead || !patient.Downed);
            this.FailOn(() => !DroneJobs.SafeHomeCell((Drone)pawn, job.targetB.Cell));
            AddFinishAction(condition =>
            {
                if (pawn.carryTracker.CarriedThing != null && pawn.Spawned)
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            });
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, null, false);
        }
    }

    public class JobDriver_Retreat : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            Toil dock = ToilMaker.MakeToil("GrechkaDockForRepair");
            dock.initAction = () =>
            {
                if (job.targetA.Thing is DroneStation station && pawn is Drone drone
                    && drone.rechargeStation == station && station.GetRobot == drone)
                    station.AddRobotToContainer(drone);
            };
            dock.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return dock;
        }
    }
}
