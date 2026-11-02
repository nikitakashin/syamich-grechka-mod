using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Industry
{
    public sealed class CompProperties_OperationalGraphics : CompProperties
    {
        public string offPath;
        public string idlePath;
        public string activePath;
        public CompProperties_OperationalGraphics() { compClass = typeof(CompOperationalGraphics); }
    }
    public sealed class CompOperationalGraphics : ThingComp
    {
        private readonly Graphic[] graphics = new Graphic[3];
        private int state;
        private int workingUntilTick = -10000;
        private CompProperties_OperationalGraphics Props => (CompProperties_OperationalGraphics)props;
        public void Used(int durationTicks = 90) { workingUntilTick = Find.TickManager.TicksGame + System.Math.Max(1, durationTicks); }
        public Graphic Graphic
        {
            get
            {
                if (graphics[state] == null)
                {
                    string path = state == 0 ? Props.offPath : state == 1 ? Props.idlePath : Props.activePath;
                    graphics[state] = GraphicDatabase.Get<Graphic_Single>(path, ShaderDatabase.Cutout,
                        parent.def.graphicData.drawSize, Color.white);
                }
                return graphics[state];
            }
        }
        public override void PostSpawnSetup(bool respawningAfterLoad) { base.PostSpawnSetup(respawningAfterLoad); Refresh(); }
        public override void ReceiveCompSignal(string signal) { base.ReceiveCompSignal(signal); if (parent.Spawned) Refresh(); }
        public override void CompTick() { if (parent.IsHashIntervalTick(30)) Refresh(); }
        private void Refresh()
        {
            bool powered = parent.GetComp<CompPowerTrader>()?.PowerOn == true
                && parent.GetComp<CompFlickable>()?.SwitchIsOn != false
                && parent.GetComp<CompBreakdownable>()?.BrokenDown != true;
            bool active = Find.TickManager.TicksGame <= workingUntilTick;
            var emitter = parent.GetComp<CompArchoEmitter>();
            if (powered && emitter != null)
            {
                var settings = emitter.Settings;
                active = parent.Map.mapPawns.AllPawnsSpawned.Any(p => p.Position.InHorDistOf(parent.Position, settings.radius)
                    && (settings.role == "Suppression" ? p.HostileTo(parent)
                        : p.Faction != null && p.Faction == parent.Faction));
            }
            int next = !powered ? 0 : active ? 2 : 1;
            if (state == next) return;
            state = next;
            parent.Map.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlagDefOf.Things);
        }
    }
    public static class OperationalWork
    {
        public static void ReportWork(Thing machine, int durationTicks)
            => (machine as ThingWithComps)?.GetComp<CompOperationalGraphics>()?.Used(durationTicks);
    }
    [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.UsedThisTick))]
    internal static class OperationalWorkGraphicHook
    {
        private static void Postfix(Building_WorkTable __instance) => __instance.GetComp<CompOperationalGraphics>()?.Used();
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.Graphic), MethodType.Getter)]
    internal static class OperationalGraphicHook
    {
        private static void Postfix(Thing __instance, ref Graphic __result)
        {
            if (!__instance.Spawned || !(__instance is ThingWithComps thing)) return;
            var state = thing.GetComp<CompOperationalGraphics>();
            if (state != null) __result = state.Graphic;
        }
    }
}
