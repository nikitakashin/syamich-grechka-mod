using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Storage
{
    public sealed class Building_QuartzCable : Building
    {
        private static int visibilityFrame = -1;
        private static bool visible;
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (visibilityFrame != Time.frameCount)
            {
                visibilityFrame = Time.frameCount;
                visible = Find.Selector.SelectedObjects.OfType<ThingWithComps>().Any(t => t.GetComp<CompSignalNode>() != null)
                    || Find.DesignatorManager.SelectedDesignator is Designator_Build build
                        && build.PlacingDef is ThingDef def && def.comps.OfType<CompProperties_SignalNode>().Any();
            }
            if (!visible) return;
            drawLoc.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            base.DrawAt(drawLoc, flip);
        }
    }

    // Reuses the game's real 4x4 linked atlas layout, without using its power
    // link flags or a CompPowerTransmitter. The prepared StyleAtlas is a visual
    // reference, not an engine-compatible 16-variant atlas.
    public sealed class Graphic_QuartzCable : Graphic_Linked
    {
        public override void Init(GraphicRequest request)
        {
            subGraphic = GraphicDatabase.Get<Graphic_Single>(request.path, request.shader,
                request.drawSize, request.color, request.colorTwo);
            data = request.graphicData; path = request.path; drawSize = request.drawSize;
            color = request.color; colorTwo = request.colorTwo;
        }
        public override Graphic GetColoredVersion(Shader shader, Color color, Color colorTwo)
        {
            var graphic = new Graphic_QuartzCable();
            graphic.subGraphic = subGraphic.GetColoredVersion(shader, color, colorTwo);
            graphic.data = data; graphic.path = path; graphic.drawSize = drawSize;
            graphic.color = color; graphic.colorTwo = colorTwo;
            return graphic;
        }
        public override bool ShouldLinkWith(IntVec3 cell, Thing parent)
        {
            return parent.Spawned && cell.InBounds(parent.Map) && cell.GetThingList(parent.Map).Any(t =>
                StoragePlacement.NodeProperties(t.def) != null);
        }
        public override Material MatAt(Rot4 rot, Thing thing = null)
        {
            return thing != null && thing.Spawned ? LinkedDrawMatFrom(thing, thing.Position) : base.MatAt(rot, thing);
        }
    }

    internal static class StoragePlacement
    {
        internal static CompProperties_SignalNode NodeProperties(BuildableDef def)
        {
            ThingDef thing = def as ThingDef;
            if (thing != null && (thing.IsBlueprint || thing.IsFrame)) thing = thing.entityDefToBuild as ThingDef;
            return thing?.comps?.OfType<CompProperties_SignalNode>().FirstOrDefault();
        }
    }

    public sealed class PlaceWorker_QuartzCable : PlaceWorker
    {
        public override bool ForceAllowPlaceOver(BuildableDef other)
        {
            ThingDef thing = other as ThingDef;
            if (thing != null && (thing.IsBlueprint || thing.IsFrame)) thing = thing.entityDefToBuild as ThingDef;
            return thing != null && thing.category == ThingCategory.Building && !thing.mineable
                && StoragePlacement.NodeProperties(thing)?.cable != true;
        }

        public override AcceptanceReport AllowsPlacing(BuildableDef def, IntVec3 loc, Rot4 rot, Map map,
            Thing thingToIgnore = null, Thing thing = null)
        {
            if (!loc.InBounds(map)) return new AcceptanceReport("За пределами карты.");
            return loc.GetThingList(map).Any(t => t != thingToIgnore && StoragePlacement.NodeProperties(t.def)?.cable == true)
                ? new AcceptanceReport("Здесь уже есть кварцевый кабель или его чертёж.") : AcceptanceReport.WasAccepted;
        }
    }
}
