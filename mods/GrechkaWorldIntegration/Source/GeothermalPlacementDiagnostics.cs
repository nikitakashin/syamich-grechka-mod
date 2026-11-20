using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.WorldIntegration
{
    // Capture intermittent failures without changing the native placement decision.
    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.CanDesignateCell))]
    public static class GeothermalPlacementDiagnostics
    {
        private static readonly FieldInfo rotation = AccessTools.Field(typeof(Designator_Place), "placingRot");
        private static readonly HashSet<string> recorded = new HashSet<string>();
        private static readonly System.WeakReference<Game> owner = new System.WeakReference<Game>(null);

        private static bool IsGeothermal(ThingDef def)
        {
            string source = ProfileVariants.Entry(def)?.sourceName ?? def?.defName;
            return source == "GeothermalGenerator" || source == "VPE_AdvancedGeothermalGenerator"
                || source == "VQE_Genetron_Geothermal";
        }

        public static bool TryBuildReport(ThingDef def, Map map, IntVec3 cell, Rot4 rot, AcceptanceReport result, out string report)
        {
            report = null;
            if (result.Accepted || !IsGeothermal(def) || map?.thingGrid == null || !cell.InBounds(map)
                || ThingDefOf.SteamGeyser == null) return false;
            Thing grid = map.thingGrid.ThingAt(cell, ThingDefOf.SteamGeyser);
            Thing listed = map.listerThings?.ThingsOfDef(ThingDefOf.SteamGeyser)?.FirstOrDefault(t => t.Position == cell);
            // Ordinary red previews away from a geyser should not produce logs.
            if (grid?.Position != cell && listed == null) return false;
            string Describe(Thing thing) => thing == null ? "none" : thing.ThingID + "@" + thing.Position;
            var cells = GenAdj.OccupiedRect(cell, rot, def.Size).Cells.Where(c => c.InBounds(map)).ToArray();
            var objects = cells.SelectMany(c => c.GetThingList(map)).Distinct().Take(16).Select(Describe);
            var terrain = cells.Select(c => c + ":" + (map.terrainGrid?.TerrainAt(c)?.defName ?? "none")
                + "/" + (map.terrainGrid?.FoundationAt(c)?.defName ?? "none"));
            report = "[Grechka World][GeothermalPlacement] def=" + def.defName + " cell=" + cell + " rot=" + rot
                + " map=" + map.uniqueID + " tick=" + (Current.Game?.tickManager?.TicksGame ?? -1)
                + " reason=" + (string.IsNullOrEmpty(result.Reason) ? "<no reason>" : result.Reason)
                + " gridGeyser=" + Describe(grid) + " listedGeyser=" + Describe(listed)
                + " appliedTier=" + WorldProfile.GetTier(new Thing { def = def })
                + " workers=" + string.Join(",", def.PlaceWorkers?.Select(w => w.GetType().FullName) ?? Enumerable.Empty<string>())
                + " occupants=[" + string.Join(";", objects) + "] terrain/foundation=[" + string.Join(";", terrain) + "]";
            return true;
        }

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Designator_Build __instance, IntVec3 __0, AcceptanceReport __result)
        {
            try
            {
                var def = __instance.PlacingDef as ThingDef;
                if (!IsGeothermal(def)) return;
                IntVec3 c = __0;
                if (!owner.TryGetTarget(out Game previous) || !ReferenceEquals(previous, Current.Game))
                { owner.SetTarget(Current.Game); recorded.Clear(); }
                Map map = Find.CurrentMap;
                if (map == null) return;
                string key = map.uniqueID + "/" + def.defName + "/" + c;
                if (__result.Accepted) { recorded.Remove(key); return; }
                if (recorded.Count >= 32 || recorded.Contains(key)) return;
                if (!TryBuildReport(def, map, c, (Rot4)rotation.GetValue(__instance), __result, out string report)) return;
                recorded.Add(key);
                Log.Message(report);
            }
            catch (Exception error)
            {
                // Diagnostics must never turn a harmless preview into an exception in the build UI.
                Log.WarningOnce("[Grechka World] Geothermal placement diagnostics unavailable: " + error.GetType().Name, 188011009);
            }
        }
    }
}
