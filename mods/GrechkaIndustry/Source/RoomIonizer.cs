using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Industry
{
    public sealed class CompProperties_RoomIonizer : CompProperties
    {
        public int cleaningLayersPerCycle = 8;
        public CompProperties_RoomIonizer() { compClass = typeof(CompRoomIonizer); }
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (cleaningLayersPerCycle <= 0 || cleaningLayersPerCycle > 256)
                yield return IndustryText.Get("GrechkaIndustry_IonizerBudget", "Ionizer cleaning budget must be between 1 and 256 layers per cycle.");
        }
    }

    public sealed class CompRoomIonizer : ThingComp
    {
        private CompPowerTrader power;
        private CompFlickable flick;
        private CompBreakdownable breakdown;
        private IonizerMap manager;
        public int CleaningBudget { get { return ((CompProperties_RoomIonizer)props).cleaningLayersPerCycle; } }
        public bool Powered { get { return parent.Spawned && power != null && power.PowerOn
            && (flick == null || flick.SwitchIsOn) && (breakdown == null || !breakdown.BrokenDown); } }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            power = parent.GetComp<CompPowerTrader>();
            flick = parent.GetComp<CompFlickable>();
            breakdown = parent.GetComp<CompBreakdownable>();
            manager = parent.Map.GetComponent<IonizerMap>();
            manager.Register(this);
        }

        public override void ReceiveCompSignal(string signal)
        {
            base.ReceiveCompSignal(signal);
            manager?.RequestRefresh();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            manager?.Unregister(this);
            manager = null;
            base.PostDeSpawn(map, mode);
        }

        public override string CompInspectStringExtra()
        {
            if (!Powered) return IndustryText.Get("GrechkaIndustry_eddf53e5a2fb", "Ionizer is switched off or unpowered");
            Room room = parent.GetRoom();
            return IonizerMap.EligibleRoom(room) ? IndustryText.Get("GrechkaIndustry_1eea1717956d", "Lighting and cleaning the entire room. Cells covered: ") + room.CellCount + IndustryText.Get("GrechkaIndustry_eff4f301fa12", " cells")
                : IndustryText.Get("GrechkaIndustry_4c39231095b5", "The ionizer requires an enclosed room");
        }
    }

    public sealed class IonizerMap : MapComponent
    {
        private sealed class Service
        {
            internal Room room;
            internal List<IntVec3> cells;
            internal int cleaningBudget;
            internal bool shapeDirty = true;
            internal bool filthDirty = true;
            internal readonly Queue<Filth> filth = new Queue<Filth>();
        }

        private static readonly ConditionalWeakTable<GlowGrid, IonizerMap> Lighting = new ConditionalWeakTable<GlowGrid, IonizerMap>();
        private readonly HashSet<CompRoomIonizer> devices = new HashSet<CompRoomIonizer>();
        private readonly Dictionary<Room, Service> rooms = new Dictionary<Room, Service>();
        private readonly RoomLightingMask lightingMask = new RoomLightingMask();
        private bool refreshRequested;
        private int nextRefresh;
        private int nextCleaning;
        private const int CleaningInterval = 250;

        public IonizerMap(Map map) : base(map) { }
        public void Register(CompRoomIonizer device) { devices.Add(device); RequestRefresh(); }
        public void Unregister(CompRoomIonizer device) { devices.Remove(device); RequestRefresh(); }
        public void RequestRefresh() { refreshRequested = true; }

        public static bool EligibleRoom(Room room)
        {
            return room != null && !room.Dereferenced && !room.IsDoorway && !room.PsychologicallyOutdoors;
        }

        internal void RoomChanged(Room room)
        {
            if (rooms.TryGetValue(room, out Service service)) service.shapeDirty = true;
            if (devices.Count > 0) RequestRefresh();
        }

        internal void FilthChanged(Room room)
        {
            if (rooms.TryGetValue(room, out Service service)) service.filthDirty = true;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            RequestRefresh();
        }

        public override void MapComponentTick()
        {
            if (devices.Count == 0 && rooms.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            // Recheck power before every cleaning batch as well as on signals.
            if (refreshRequested || now >= nextRefresh || now >= nextCleaning)
            {
                refreshRequested = false;
                nextRefresh = now + 60;
                RefreshRooms();
            }
            if (now < nextCleaning) return;
            nextCleaning = now + CleaningInterval;
            foreach (Service service in rooms.Values) Clean(service);
        }

        private void RefreshRooms()
        {
            var active = new Dictionary<Room, int>();
            foreach (CompRoomIonizer device in devices)
            {
                if (!device.Powered || device.parent.Map != map) continue;
                Room room = device.parent.GetRoom();
                if (!EligibleRoom(room)) continue;
                active.TryGetValue(room, out int previous);
                // One appliance already covers a room. Duplicates do not multiply
                // light or cleaning; a stronger future tier can replace the budget.
                active[room] = Math.Max(previous, device.CleaningBudget);
            }

            bool changed = false;
            foreach (Room room in rooms.Keys.Where(room => !active.ContainsKey(room)).ToList())
            {
                rooms.Remove(room);
                changed = true;
            }
            foreach (var entry in active)
            {
                if (!rooms.TryGetValue(entry.Key, out Service service))
                {
                    rooms.Add(entry.Key, service = new Service { room = entry.Key });
                    changed = true;
                }
                service.cleaningBudget = entry.Value;
                if (!service.shapeDirty) continue;
                service.cells = entry.Key.Cells.ToList();
                service.shapeDirty = false;
                service.filthDirty = true;
                changed = true;
            }
            if (!changed) return;

            var cells = rooms.Values.SelectMany(service => service.cells)
                .Where(cell => !map.terrainGrid.TerrainAt(cell).exposesToVacuum)
                .Select(cell => map.cellIndices.CellToIndex(cell));
            HashSet<int> dirty = lightingMask.Replace(cells);
            Lighting.Remove(map.glowGrid);
            if (!lightingMask.IsEmpty) Lighting.Add(map.glowGrid, this);
            foreach (int index in dirty)
                map.mapDrawer.MapMeshDirty(map.cellIndices.IndexToCell(index), MapMeshFlagDefOf.GroundGlow);
        }

        private bool IsCleanable(Filth filth, Room room)
        {
            return filth != null && filth.Spawned && !filth.Destroyed && filth.Map == map
                && filth.GetType() == typeof(Filth) && filth.def.filth != null
                && filth.def.filth.cleaningWorkToReduceThickness > 0
                && (filth.def.comps == null || filth.def.comps.Count == 0)
                && filth.Position.GetRoom(map) == room;
        }

        private void Clean(Service service)
        {
            if (!EligibleRoom(service.room)) return;
            if (service.filthDirty)
            {
                service.filth.Clear();
                foreach (Filth filth in service.room.ContainedThings<Filth>())
                    if (IsCleanable(filth, service.room)) service.filth.Enqueue(filth);
                service.filthDirty = false;
            }
            int budget = service.cleaningBudget;
            int examined = 0;
            while (budget > 0 && service.filth.Count > 0 && examined++ < 256)
            {
                Filth filth = service.filth.Peek();
                if (!IsCleanable(filth, service.room)) { service.filth.Dequeue(); continue; }
                filth.ThinFilth();
                budget--;
                if (!filth.Spawned || filth.Destroyed) service.filth.Dequeue();
            }
        }

        internal static bool Illuminated(GlowGrid grid, int index)
        { return Lighting.TryGetValue(grid, out IonizerMap manager) && manager.lightingMask.Contains(index); }
        internal static bool Illuminated(GlowGrid grid, IntVec3 cell)
        { return Lighting.TryGetValue(grid, out IonizerMap manager) && manager.lightingMask.Contains(manager.map.cellIndices.CellToIndex(cell)); }

        internal static void ApplyVisual(ref Color32 color)
        {
            color.r = Math.Max(color.r, (byte)180);
            color.g = Math.Max(color.g, (byte)190);
            color.b = Math.Max(color.b, (byte)210);
            // Keep alpha: vanilla uses it to distinguish sunlight-level overlight.
        }

        public override void MapRemoved()
        {
            if (map.glowGrid != null) Lighting.Remove(map.glowGrid);
            devices.Clear(); rooms.Clear(); lightingMask.Replace(Array.Empty<int>());
            base.MapRemoved();
        }
    }

    [HarmonyPatch(typeof(GlowGrid), nameof(GlowGrid.GroundGlowAt))]
    internal static class IonizerGameLightHook
    {
        private static void Postfix(GlowGrid __instance, IntVec3 __0, ref float __result)
        {
            if (IonizerMap.Illuminated(__instance, __0)) __result = Math.Max(__result, 0.5f);
        }
    }

    [HarmonyPatch(typeof(GlowGrid), nameof(GlowGrid.VisualGlowAt), new Type[] { typeof(int) })]
    internal static class IonizerVisualIndexHook
    {
        private static void Postfix(GlowGrid __instance, int __0, ref Color32 __result)
        { if (IonizerMap.Illuminated(__instance, __0)) IonizerMap.ApplyVisual(ref __result); }
    }

    [HarmonyPatch(typeof(GlowGrid), nameof(GlowGrid.VisualGlowAt), new Type[] { typeof(IntVec3) })]
    internal static class IonizerVisualCellHook
    {
        private static void Postfix(GlowGrid __instance, IntVec3 __0, ref Color32 __result)
        { if (IonizerMap.Illuminated(__instance, __0)) IonizerMap.ApplyVisual(ref __result); }
    }

    [HarmonyPatch(typeof(Room), nameof(Room.Notify_RoomShapeChanged))]
    internal static class IonizerRoomShapeHook
    {
        private static void Postfix(Room __instance)
        {
            if (!__instance.Dereferenced) __instance.Map?.GetComponent<IonizerMap>()?.RoomChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(Room), nameof(Room.Notify_RoofChanged))]
    internal static class IonizerRoomRoofHook
    {
        private static void Postfix(Room __instance)
        {
            if (!__instance.Dereferenced) __instance.Map?.GetComponent<IonizerMap>()?.RoomChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(Room), nameof(Room.Notify_ContainedThingSpawnedOrDespawned))]
    internal static class IonizerRoomFilthHook
    {
        private static void Postfix(Room __instance, Thing __0)
        {
            if (__0 is Filth && !__instance.Dereferenced)
                __instance.Map?.GetComponent<IonizerMap>()?.FilthChanged(__instance);
        }
    }
}
