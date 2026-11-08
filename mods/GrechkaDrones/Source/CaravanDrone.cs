using System;
using System.Text;
using AIRobot;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Grechka.Drones
{
    public class CaravanDrone : Drone
    {
        private bool energyInitialized;
        public float CargoCapacity => 50f * Math.Clamp(Properties?.tier ?? 1, 1, 5);
        public bool HasEnergy => needs?.food?.CurLevel > 0f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref energyInitialized, "grechkaCaravanEnergyInitialized");
            Scribe_References.Look(ref rechargeStation, "grechkaCaravanDock");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!energyInitialized && !respawningAfterLoad && needs?.food != null)
            {
                needs.food.CurLevel = CaravanEnergy.MaxNutrition;
                energyInitialized = true;
            }
            CaravanEnergy.SyncFromFood(this);
        }

        protected override void Tick()
        {
            // Misc's Tick refills food, destroys flat batteries and recalls robots from other maps.
            // This independent travelling model runs the ordinary Pawn tick instead.
            CaravanPawnTick.Tick(this);
            if (Spawned && rechargeStation == null && this.IsHashIntervalTick(250))
                rechargeStation = TryFindRechargeStation(this, Map);
        }

        public override string GetInspectString() => base.GetInspectString() + "\n" +
            "GrechkaDrones_Cargo".Translate(CargoCapacity.ToString("0"));
    }

    public static class CaravanPawnTick
    {
        // Nonvirtual dispatch skips the dock-bound Misc implementation while retaining Pawn hooks.
        public static readonly Action<Pawn> Tick = AccessTools.MethodDelegate<Action<Pawn>>(
            AccessTools.Method(typeof(Pawn), "Tick"), virtualCall: false);
    }

    public class JobGiver_CaravanRecharge : JobGiver_DroneRecharge
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!(pawn is CaravanDrone drone) || drone.rechargeStation?.Spawned != true
                || drone.rechargeStation.Map != drone.Map || drone.rechargeStation.powerComp?.PowerOn != true
                || CaravanFormingUtility.IsFormingCaravan(drone)) return null;
            return base.TryGiveJob(pawn);
        }
    }

    public class JobGiver_CaravanHaul : X2_JobGiver_Work
    {
        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
            => pawn is CaravanDrone drone && drone.HasEnergy
                ? base.TryIssueJobPackage(pawn, jobParams) : ThinkResult.NoJob;
    }

    public static class CaravanEnergy
    {
        // One authoritative pool: 2 nutrition = a full battery = one active day.
        // The native Food need makes meals, food policies, caravan supplies and their forecasts work normally.
        public const float MaxNutrition = 2f;
        public const float NutritionPerTick = MaxNutrition / 60000f;
        [ThreadStatic] private static bool synchronizing;

        public static void SyncFromFood(CaravanDrone drone)
        {
            if (synchronizing || drone.needs?.food == null || drone.needs.rest == null) return;
            synchronizing = true;
            try { drone.needs.rest.CurLevel = drone.needs.food.CurLevel / MaxNutrition; }
            finally { synchronizing = false; }
        }

        [HarmonyPatch(typeof(Pawn_NeedsTracker), "ShouldHaveNeed")]
        static class Needs
        {
            static bool Prefix(Pawn ___pawn, NeedDef nd, ref bool __result)
            {
                if (!(___pawn is CaravanDrone)) return true;
                __result = nd == NeedDefOf.Food || nd == NeedDefOf.Rest;
                return false;
            }
        }

        [HarmonyPatch(typeof(Need_Food), "get_MaxLevel")]
        static class FuelTank
        {
            static bool Prefix(Pawn ___pawn, ref float __result)
            {
                if (!(___pawn is CaravanDrone)) return true;
                __result = MaxNutrition;
                return false;
            }
        }

        [HarmonyPatch(typeof(Need_Food), nameof(Need_Food.FoodFallPerTickAssumingCategory))]
        static class FuelRate
        {
            static bool Prefix(Pawn ___pawn, ref float __result)
            {
                if (!(___pawn is CaravanDrone)) return true;
                __result = NutritionPerTick;
                return false;
            }
        }

        [HarmonyPatch(typeof(Need_Food), nameof(Need_Food.NeedInterval))]
        static class FuelUse
        {
            static bool Prefix(Need_Food __instance, Pawn ___pawn)
            {
                if (!(___pawn is CaravanDrone drone)) return true;
                if (!drone.Suspended && !drone.Dead)
                    __instance.CurLevel -= NutritionPerTick * 150f;
                // A depleted robot stops hauling; it never starves biologically or deletes its cargo.
                return false;
            }
        }

        [HarmonyPatch(typeof(Need_Rest), nameof(Need_Rest.NeedInterval))]
        static class NoSecondDrain
        {
            static bool Prefix(Pawn ___pawn) => !(___pawn is CaravanDrone);
        }

        [HarmonyPatch(typeof(Need_Rest), nameof(Need_Rest.TickResting))]
        static class NoFreeCampingCharge
        {
            static bool Prefix(Pawn ___pawn) => !(___pawn is CaravanDrone);
        }

        [HarmonyPatch(typeof(Need), "set_CurLevel")]
        static class SharedBattery
        {
            static void Postfix(Need __instance, Pawn ___pawn)
            {
                if (synchronizing || !(___pawn is CaravanDrone drone)) return;
                if (__instance is Need_Food) SyncFromFood(drone);
                else if (__instance is Need_Rest && drone.needs?.food != null)
                {
                    synchronizing = true;
                    try { drone.needs.food.CurLevel = __instance.CurLevel * MaxNutrition; }
                    finally { synchronizing = false; }
                }
            }
        }

        [HarmonyPatch(typeof(Need), "get_ShowOnNeedList")]
        static class OneBar
        {
            static void Postfix(Need __instance, Pawn ___pawn, ref bool __result)
            {
                if (___pawn is CaravanDrone && __instance is Need_Rest) __result = false;
            }
        }

        [HarmonyPatch(typeof(Need), "get_LabelCap")]
        static class EnergyLabel
        {
            static void Postfix(Need __instance, Pawn ___pawn, ref string __result)
            {
                if (___pawn is CaravanDrone && __instance is Need_Food)
                    __result = "GrechkaDrones_BioEnergy".Translate();
            }
        }

        [HarmonyPatch(typeof(Need_Food), nameof(Need_Food.GetTipString))]
        static class EnergyTip
        {
            static bool Prefix(Need_Food __instance, Pawn ___pawn, ref string __result)
            {
                if (!(___pawn is CaravanDrone)) return true;
                __result = "GrechkaDrones_BioEnergyTip".Translate(__instance.CurLevelPercentage.ToString("P0"));
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.Capacity))]
    public static class CaravanDroneCapacity
    {
        static bool Prefix(Pawn p, StringBuilder explanation, ref float __result)
        {
            if (!(p is CaravanDrone drone)) return true;
            __result = !drone.Dead && !drone.Downed && drone.HasEnergy ? drone.CargoCapacity : 0f;
            if (explanation != null)
            {
                if (explanation.Length > 0) explanation.AppendLine();
                explanation.Append("  - ").Append(p.LabelShortCap).Append(": ").Append(__result.ToStringMassOffset());
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.MaxStackSpaceEver))]
    public static class CaravanDroneCarryWeight
    {
        static bool Prefix(Pawn ___pawn, ThingDef td, ref int __result)
        {
            if (!(___pawn is CaravanDrone drone)) return true;
            float mass = td.GetStatValueAbstract(StatDefOf.Mass);
            // Keep the reserve needed to pick up food even when the main battery is flat.
            __result = Math.Min(td.stackLimit,
                mass > 0f ? (int)Math.Min(int.MaxValue, Math.Floor(drone.CargoCapacity / mass)) : td.stackLimit);
            return false;
        }
    }
}
