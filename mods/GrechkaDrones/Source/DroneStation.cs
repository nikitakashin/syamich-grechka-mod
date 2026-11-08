using System.Collections.Generic;
using System.Linq;
using AIRobot;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Grechka.Drones
{
    public class DroneStation : X2_Building_AIRobotRechargeStation
    {
        private static readonly System.Action<X2_Building_AIRobotRechargeStation> RefreshGraphic =
            AccessTools.MethodDelegate<System.Action<X2_Building_AIRobotRechargeStation>>(
                AccessTools.Method(typeof(X2_Building_AIRobotRechargeStation), "UpdateGraphic"));
        public bool captureEnemies;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref captureEnemies, "grechkaCaptureEnemies");
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            var props = DefDatabase<ThingDef>.GetNamedSilentFail(((X2_ThingDef_AIRobot_Building_RechargeStation)def).spawnThingDef)?.GetModExtension<DroneProperties>();
            if (Faction?.IsPlayer == true && props?.role == DroneRole.Rescue)
                yield return Drone.CaptureCommand(() => captureEnemies, value => captureEnemies = value);
        }

        public bool HoldingForRepair => GetRobot is Drone drone && !drone.Dead && drone.repairing;

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // A deployed carrier owns real inventory and can live away from its dock.
            // Misc normally destroys the robot together with the station, including world pawns.
            if (robot is CaravanDrone carrier && (container == null || !container.Contains(carrier)))
            {
                carrier.rechargeStation = null;
                robot = null;
            }
            base.Destroy(mode);
        }

        protected override void Tick()
        {
            // A travelling robot remains owned by its dock, but must never receive recall/charge orders.
            if (GetRobot is CaravanDrone travelling && (container == null || !container.Contains(travelling))
                && (!travelling.Spawned || travelling.Map != Map || CaravanFormingUtility.IsFormingCaravan(travelling)))
            {
                isRechargeActive = false;
                RefreshGraphic(this);
                return;
            }
            Drone drone = GetRobot as Drone;
            bool contained = drone != null && container != null && container.Contains(drone);
            bool powered = powerComp != null && powerComp.PowerOn;
            bool autoLaunch = SpawnRobotAfterRecharge;
            float rest = contained ? drone.needs.rest.CurLevel : 0f;
            if (contained && (drone.repairing || !powered)) SpawnRobotAfterRecharge = false;
            try { base.Tick(); }
            finally
            {
                SpawnRobotAfterRecharge = autoLaunch;
                // Misc's contained recharge branch does not check power. Scope the fix to our docks.
                if (contained && !powered && !drone.Spawned && !drone.Dead) drone.needs.rest.CurLevel = rest;
            }
            if (!contained || !powered || drone.Dead || drone.Spawned || !drone.repairing) return;
            if (this.IsHashIntervalTick(300))
            {
                Hediff_MissingPart missing = drone.health.hediffSet.GetMissingPartsCommonAncestors().FirstOrDefault();
                if (missing != null) drone.health.RestorePart(missing.Part);
                // Native Misc repairs only non-permanent wounds. Mechanical repair covers scars too.
                Hediff_Injury scar = drone.health.hediffSet.hediffs.OfType<Hediff_Injury>().FirstOrDefault(h => h.IsPermanent());
                if (scar != null) scar.Heal(2f * drone.HealthScale);
            }
            if (drone.HealthFraction >= DroneRules.RepairedHealth
                && !drone.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any()
                && !drone.health.hediffSet.GetMissingPartsCommonAncestors().Any())
                drone.repairing = false;
        }
    }

    [StaticConstructorOnStartup]
    public static class Startup
    {
        static Startup() { new Harmony("grechka.drones").PatchAll(typeof(Startup).Assembly); }
    }

    [HarmonyPatch(typeof(X2_Building_AIRobotRechargeStation), "Button_SpawnBot")]
    public static class HoldRepairHook
    {
        public static bool Prefix(X2_Building_AIRobotRechargeStation __instance)
            => !(__instance is DroneStation station) || !station.HoldingForRepair;
    }
}
