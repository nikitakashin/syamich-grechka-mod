using System;
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
        // -1: a complete single-view machine; 0..3: only this prepared N/E/S/W view.
        public int singleView = -1;
        // Opt-in: address prepared states by direction without making a west image serve every view.
        public bool directionalStates;
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
                    graphics[state] = BuildStateGraphic(parent.def.graphicData.Graphic, path, Props,
                        candidate => ContentFinder<Texture2D>.Get(candidate, false) == null ? null
                            : GraphicDatabase.Get<Graphic_Single>(candidate, ShaderDatabase.Cutout,
                                parent.def.graphicData.drawSize, Color.white, Color.white, parent.def.graphicData));
                }
                return graphics[state];
            }
        }
        // The loader returns null for absent art. No missing state may replace the base by a bad texture.
        public static Graphic BuildStateGraphic(Graphic original, string path,
            CompProperties_OperationalGraphics settings, Func<string, Graphic> loadPrepared)
        {
            if (string.IsNullOrEmpty(path)) return original;
            if (!settings.directionalStates)
            {
                Graphic single = loadPrepared(path);
                if (single == null) return original;
                return settings.singleView >= 0 && settings.singleView < 4
                    ? new Graphic_OperationalView(original, single, new Rot4(settings.singleView)) : single;
            }
            var views = new Graphic[4];
            string[] suffixes = { "north", "east", "south", "west" };
            for (int i = 0; i < views.Length; i++)
            {
                views[i] = loadPrepared(path + "_" + suffixes[i]);
                // The existing unsuffixed file has one known view; never use it at another angle.
                if (views[i] == null && settings.singleView == i) views[i] = loadPrepared(path);
            }
            return views.All(g => g == null) ? original : new Graphic_OperationalView(original, views);
        }
        public override void PostSpawnSetup(bool respawningAfterLoad) { base.PostSpawnSetup(respawningAfterLoad); Refresh(); }
        public override void ReceiveCompSignal(string signal) { base.ReceiveCompSignal(signal); if (parent.Spawned) Refresh(); }
        public override void CompTick() { if (parent.IsHashIntervalTick(30)) Refresh(); }
        public override void CompTickRare() { Refresh(); }
        private void Refresh()
        {
            if (!parent.Spawned) return;
            bool powered = parent.GetComp<CompPowerTrader>()?.PowerOn == true
                && parent.GetComp<CompFlickable>()?.SwitchIsOn != false
                && parent.GetComp<CompBreakdownable>()?.BrokenDown != true;
            bool active = Find.TickManager.TicksGame <= workingUntilTick;
            if (powered)
            {
                var extractor = parent.GetComp<CompMechaniteExtractor>();
                var ionizer = parent.GetComp<CompRoomIonizer>();
                if (extractor != null) active = extractor.Working;
                else if (ionizer != null) active = IonizerMap.EligibleRoom(parent.GetRoom());
                else if (parent is Building_Mechaponics)
                    active = parent.Position.GetThingList(parent.Map).OfType<Plant>().Any(p => p.Growth < 1f && p.GrowthRate > 0f);
                else if (parent.GetComp<CompPowerPlant>() is CompPowerPlant generator) active = generator.PowerOutput > 0;
            }
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
    /// <summary>Use each prepared state at its actual camera angle; retain every missing original view.</summary>
    public sealed class Graphic_OperationalView : Graphic
    {
        private readonly Graphic original;
        private readonly Graphic[] states;
        public Graphic_OperationalView(Graphic original, Graphic stateGraphic, Rot4 view)
            : this(original, Enumerable.Range(0, 4).Select(i => i == view.AsInt ? stateGraphic : null).ToArray()) { }
        public Graphic_OperationalView(Graphic original, Graphic[] preparedViews)
        {
            if (preparedViews == null || preparedViews.Length != 4)
                throw new ArgumentException("Exactly four N/E/S/W slots are required.", nameof(preparedViews));
            this.original = original;
            states = (Graphic[])preparedViews.Clone();
            data = original.data;
            path = original.path;
            drawSize = original.drawSize;
            color = original.color;
            colorTwo = original.colorTwo;
        }
        private Graphic Prepared(Rot4 rotation) => rotation.IsValid ? states[rotation.AsInt] : null;
        public Graphic ViewAt(Rot4 rotation) => Prepared(rotation) ?? original;
        public override Material MatAt(Rot4 rot, Thing thing = null)
            => Prepared(rot) is Graphic prepared ? prepared.MatSingle : original.MatAt(rot, thing);
        public override Material MatSingle => MatAt(Rot4.South);
        public override Material MatNorth => MatAt(Rot4.North);
        public override Material MatEast => MatAt(Rot4.East);
        public override Material MatSouth => MatAt(Rot4.South);
        public override Material MatWest => MatAt(Rot4.West);
        public override bool ShouldDrawRotated => original.ShouldDrawRotated;
        public override float DrawRotatedExtraAngleOffset => original.DrawRotatedExtraAngleOffset;
        public override bool WestFlipped => states[Rot4.West.AsInt] == null && original.WestFlipped;
        public override bool EastFlipped => states[Rot4.East.AsInt] == null && original.EastFlipped;
        // Original MeshAt consults its own flip flags, so explicit art must use this wrapper's flags.
        public override Mesh MeshAt(Rot4 rot) => Prepared(rot) == null ? original.MeshAt(rot) : base.MeshAt(rot);
        public override Graphic GetColoredVersion(Shader shader, Color one, Color two)
            => new Graphic_OperationalView(original.GetColoredVersion(shader, one, two),
                states.Select(g => g?.GetColoredVersion(shader, one, two)).ToArray());
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
