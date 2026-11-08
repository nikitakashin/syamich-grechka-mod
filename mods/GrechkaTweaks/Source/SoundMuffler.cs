using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Grechka.Tweaks
{
    public sealed class CompProperties_SoundMuffler : CompProperties
    {
        public CompProperties_SoundMuffler() { compClass = typeof(CompSoundMuffler); }
    }

    public sealed class CompSoundMuffler : ThingComp
    {
        public const int DefaultRadius = 12;
        public const int MinRadius = 1;
        public const int MaxRadius = 30;
        private int radius = DefaultRadius;
        private CompPowerTrader power;
        private CompFlickable flick;
        private CompBreakdownable breakdown;
        private SoundMufflerMapComponent registeredMap;

        public int Radius
        {
            get => radius;
            set => radius = Math.Clamp(value, MinRadius, MaxRadius);
        }

        public bool Active => parent.Spawned && power?.PowerOn == true
            && flick?.SwitchIsOn != false && breakdown?.BrokenDown != true;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            registeredMap?.Unregister(this);
            power = parent.GetComp<CompPowerTrader>();
            flick = parent.GetComp<CompFlickable>();
            breakdown = parent.GetComp<CompBreakdownable>();
            registeredMap = parent.Map.GetComponent<SoundMufflerMapComponent>();
            registeredMap.Register(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            registeredMap?.Unregister(this);
            registeredMap = null;
            base.PostDeSpawn(map, mode);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref radius, "grechkaSoundMufflerRadius", DefaultRadius);
            Radius = radius;
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            GenDraw.DrawRadiusRing(parent.Position, Radius);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (parent.Faction?.IsPlayer != true) yield break;
            yield return new Command_Action
            {
                defaultLabel = "GrechkaTweaks_MufflerRadius".Translate(Radius),
                defaultDesc = "GrechkaTweaks_MufflerRadiusHelp".Translate(MinRadius, MaxRadius),
                icon = ContentFinder<Texture2D>.Get("Buildings/SoundMuffler"),
                action = () => Find.WindowStack.Add(new Dialog_Slider(
                    value => "GrechkaTweaks_MufflerRadius".Translate(value),
                    MinRadius, MaxRadius, value => Radius = value, Radius))
            };
        }

        public override string CompInspectStringExtra() =>
            "GrechkaTweaks_MufflerRadius".Translate(Radius) + "\n" +
            (Active ? "GrechkaTweaks_MufflerActive" : "GrechkaTweaks_MufflerInactive").Translate();
    }

    // Rebuilt by the comps on spawn/load; never retain maps in a static registry or scan all things per sound.
    public sealed class SoundMufflerMapComponent : MapComponent
    {
        private readonly List<CompSoundMuffler> mufflers = new List<CompSoundMuffler>();
        public SoundMufflerMapComponent(Map map) : base(map) { }

        public void Register(CompSoundMuffler muffler)
        {
            if (!mufflers.Contains(muffler)) mufflers.Add(muffler);
        }

        public void Unregister(CompSoundMuffler muffler) => mufflers.Remove(muffler);

        public bool Mutes(IntVec3 cell)
        {
            foreach (CompSoundMuffler muffler in mufflers)
            {
                if (!muffler.Active || muffler.parent.Map != map) continue;
                if (muffler.parent.Position.DistanceToSquared(cell) <= muffler.Radius * muffler.Radius)
                    return true;
            }
            return false;
        }
    }

    // Both one-shots and sustained samples use this getter at creation and on every audio update.
    // Keep sustainers alive: their volume recovers naturally after a power loss, move or radius edit.
    [HarmonyPatch(typeof(Sample), "get_SanitizedVolume")]
    public static class SoundMufflerVolume
    {
        public static bool Mutes(SoundInfo info)
        {
            if (info.IsOnCamera || info.forcedPlayOnCamera) return false;
            TargetInfo maker = info.Maker;
            if (!maker.IsValid || maker.ThingDestroyed) return false;
            Map map = maker.Map;
            return map != null && maker.Cell.IsValid
                && map.GetComponent<SoundMufflerMapComponent>()?.Mutes(maker.Cell) == true;
        }

        static bool Prefix(Sample __instance, ref float __result)
        {
            if (!Mutes(__instance.Info)) return true;
            __result = 0f;
            return false;
        }
    }
}
