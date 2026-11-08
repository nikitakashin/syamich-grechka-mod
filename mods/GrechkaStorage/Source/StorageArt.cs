using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Storage
{
    internal static class StorageArt
    {
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        internal static void Icon(Rect rect, string name)
        {
            if (!icons.TryGetValue(name, out Texture2D image)) icons[name] = image = ContentFinder<Texture2D>.Get("UI/" + name, false);
            if (image != null) GUI.DrawTexture(rect, image, ScaleMode.ScaleToFit);
        }
        internal static bool Button(Rect rect, string label, string icon)
        {
            bool clicked = Widgets.ButtonText(rect, "");
            float size = Math.Min(22, rect.height - 6);
            Icon(new Rect(rect.x + 4, rect.y + (rect.height - size) / 2, size, size), icon);
            TextAnchor anchor = Text.Anchor; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x + size + 7, rect.y, Math.Max(0, rect.width - size - 10), rect.height), label);
            Text.Anchor = anchor;
            return clicked;
        }
        internal static void Status(Rect rect, string label, string icon, string tooltip = null)
        {
            float size = Math.Min(22, rect.height);
            if (icon != null) Icon(new Rect(rect.x, rect.y, size, size), icon);
            float inset = icon == null ? 0 : size + 5;
            Widgets.Label(new Rect(rect.x + inset, rect.y, Math.Max(0, rect.width - inset), rect.height), label);
            if (tooltip != null) TooltipHandler.TipRegion(rect, tooltip);
        }
    }

    public sealed class CompProperties_StorageMachineArt : CompProperties
    {
        public string texture;
        public bool certusAccelerator;
        public CompProperties_StorageMachineArt() { compClass = typeof(CompStorageMachineArt); }
    }
    public sealed class CompStorageMachineArt : ThingComp
    {
        readonly Graphic[] graphics = new Graphic[3];
        int activeUntil = -1, previous = -1;
        CompProperties_StorageMachineArt Props => (CompProperties_StorageMachineArt)props;
        public static int SelectState(bool ready, bool working) => !ready ? 0 : working ? 2 : 1;
        internal int State
        {
            get
            {
                bool ready = DeviceStatus.Ready(parent);
                bool working = activeUntil >= 0 && Find.TickManager.TicksGame < activeUntil;
                if (ready && Props.certusAccelerator)
                    working = GenAdj.CellsAdjacentCardinal(parent).Where(c => c.InBounds(parent.Map))
                        .SelectMany(c => c.GetThingList(parent.Map)).OfType<ThingWithComps>()
                        .Any(t => t.GetComp<CompCertusSource>()?.Growing == true);
                return SelectState(ready, working);
            }
        }
        internal Graphic CurrentGraphic
        {
            get
            {
                int state = State;
                // Prepared states contain one direction. Rotate that authored frame intact;
                // never pretend the directional-only PNGs contain matching state variants.
                return graphics[state] ??= GraphicDatabase.Get<Graphic_Single>(Props.texture + "_" + new[] { "Off", "Idle", "Active" }[state],
                    ShaderDatabase.Cutout, parent.def.graphicData.drawSize, Color.white);
            }
        }
        internal static void Worked(ThingWithComps machine, int interval)
        {
            CompStorageMachineArt art = machine?.GetComp<CompStorageMachineArt>();
            if (art == null || !machine.Spawned) return;
            art.activeUntil = Find.TickManager.TicksGame + Math.Max(1, interval); art.Refresh();
        }
        void Refresh()
        {
            if (!parent.Spawned) return;
            int state = State;
            if (state == previous) return;
            previous = state; parent.Map.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlagDefOf.Buildings);
        }
        public override void PostSpawnSetup(bool respawningAfterLoad) { base.PostSpawnSetup(respawningAfterLoad); Refresh(); }
        public override void CompTick() { if (!Props.certusAccelerator || parent.IsHashIntervalTick(60)) Refresh(); }
        public override void CompTickRare() => Refresh();
        public override void ReceiveCompSignal(string signal) { base.ReceiveCompSignal(signal); Refresh(); }
    }
    [HarmonyPatch(typeof(Thing), "get_Graphic")]
    internal static class StorageStateGraphicHook
    {
        static void Postfix(Thing __instance, ref Graphic __result)
        {
            if (__instance is ThingWithComps thing && thing.Spawned && thing.GetComp<CompStorageMachineArt>() is CompStorageMachineArt art)
                __result = art.CurrentGraphic;
            else if (__instance is ThingWithComps source && source.Spawned && source.GetComp<CompCertusSource>() is CompCertusSource growth)
                __result = growth.CurrentGraphic;
        }
    }
    [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.UsedThisTick))]
    internal static class StorageWorkGraphicHook
    {
        static void Postfix(Building_WorkTable __instance) => CompStorageMachineArt.Worked(__instance, 2);
    }
}
