using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Industry
{
    public sealed class PackedBuildingProperties : DefModExtension { public ThingDef building; }
    internal static class PackingScope { [ThreadStatic] internal static int depth; }

    // Construction refunds create resources directly from costList. A packed
    // machine resource must contain a real machine, rather than an empty crate.
    [HarmonyPatch(typeof(ThingMaker), nameof(ThingMaker.MakeThing))]
    internal static class PackedBuildingMakerHook
    {
        private static void Postfix(ThingDef def, Thing __result)
        {
            if (PackingScope.depth != 0 || !(__result is MinifiedThing packed) || packed.InnerThing != null) return;
            ThingDef building = def.GetModExtension<PackedBuildingProperties>()?.building;
            if (building != null)
                packed.InnerThing = ThingMaker.MakeThing(building, building.MadeFromStuff ? GenStuff.DefaultStuffFor(building) : null);
        }
    }
    // MinifyUtility and MinifiedThing.SplitOff supply their own real inner thing.
    // Never manufacture a replacement while these vanilla ownership paths run.
    [HarmonyPatch(typeof(MinifyUtility), nameof(MinifyUtility.MakeMinified))]
    internal static class PackedBuildingWrapHook
    {
        private static void Prefix(out int __state) { __state = PackingScope.depth; PackingScope.depth++; }
        private static Exception Finalizer(Exception __exception, int __state) { PackingScope.depth = __state; return __exception; }
    }
    [HarmonyPatch(typeof(MinifiedThing), nameof(MinifiedThing.SplitOff))]
    internal static class PackedBuildingSplitHook
    {
        private static void Prefix(out int __state) { __state = PackingScope.depth; PackingScope.depth++; }
        private static Exception Finalizer(Exception __exception, int __state) { PackingScope.depth = __state; return __exception; }
    }
}
