using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Grechka.Drones
{
    [HarmonyPatch(typeof(CaravanUIUtility), nameof(CaravanUIUtility.AddPawnsSections))]
    internal static class CaravanDroneSectionHook
    {
        // The native sections cover colonists, animals and overseen Biotech mechs.
        // Misc robots satisfy none of them. Keep their existing eligible transferables
        // in one dedicated section, without changing race or caravan eligibility.
        private static void Prefix(ref List<TransferableOneWay> transferables, out List<TransferableOneWay> __state)
        {
            __state = transferables.Where(t => t.AnyThing is CaravanDrone).ToList();
            if (__state.Count > 0) transferables = transferables.Except(__state).ToList();
        }

        private static void Postfix(TransferableOneWayWidget widget, List<TransferableOneWay> __state)
        {
            if (__state != null && __state.Count > 0) widget.AddSection("GrechkaDrones_CaravanSection".Translate(), __state);
        }
    }
}
