using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Grechka.Quests
{
    // The vanilla arrival action passes a null faction to MakeDropPodAt. Keep
    // the owning flight's actual cargo identity across that call instead of
    // guessing ownership from the incoming skyfaller or its contents.
    internal static class OwnedTransportArrival
    {
        [ThreadStatic] static HashSet<ActiveTransporterInfo> activeCargo;
        static readonly FieldInfo cargoField = AccessTools.Field(typeof(TravellingTransporters), "transporters");

        internal static void Begin(TravellingTransporters flight, out HashSet<ActiveTransporterInfo> previous)
        {
            previous = activeCargo;
            activeCargo = flight.IsPlayerControlled && cargoField?.GetValue(flight) is List<ActiveTransporterInfo> cargo
                ? new HashSet<ActiveTransporterInfo>(cargo) : null;
        }

        internal static void End(HashSet<ActiveTransporterInfo> previous) { activeCargo = previous; }
        internal static bool Contains(ActiveTransporterInfo cargo) { return cargo != null && activeCargo?.Contains(cargo) == true; }
    }

    [HarmonyPatch(typeof(TravellingTransporters), "DoArrivalAction")]
    internal static class OwnTransportArrivalHook
    {
        static void Prefix(TravellingTransporters __instance, out HashSet<ActiveTransporterInfo> __state)
        {
            OwnedTransportArrival.Begin(__instance, out __state);
        }
        static void Postfix(HashSet<ActiveTransporterInfo> __state) { OwnedTransportArrival.End(__state); }
        static Exception Finalizer(Exception __exception, HashSet<ActiveTransporterInfo> __state)
        {
            OwnedTransportArrival.End(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SkyfallerMaker), "SpawnSkyfaller",
        new Type[] { typeof(ThingDef), typeof(Thing), typeof(IntVec3), typeof(Map) })]
    internal static class OwnTransportSkyfallerHook
    {
        static void Postfix(Thing __1, Skyfaller __result)
        {
            if (__1 is ActiveTransporter transporter && OwnedTransportArrival.Contains(transporter.Contents))
                QuestGame.Current?.MarkOwnedLandingPod(__result);
        }
    }
}
