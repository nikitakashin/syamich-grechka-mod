using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Quests
{
    // These are completion events, not stock-change observations. Mods using the
    // standard recipe/harvest notifications participate without a separate adapter.
    [HarmonyPatch(typeof(QuestManager), "Notify_ThingsProduced", new Type[] { typeof(Pawn), typeof(List<Thing>) })]
    internal static class RecipeOutputHook
    {
        static void Postfix(Pawn __0, List<Thing> __1)
        {
            QuestGame game = QuestGame.Current;
            if (game == null || __1 == null) return;
            foreach (Thing output in __1)
            {
                if (output == null || output.def == null || output.stackCount <= 0) continue;
                CompQuality quality = output.TryGetComp<CompQuality>();
                game.RecordVerifiedOutput("produce", __0, output.def, output.Stuff,
                    quality == null ? null : quality.Quality.ToString(), output.stackCount);
            }
        }
    }

    [HarmonyPatch(typeof(QuestManager), "Notify_PlantHarvested", new Type[] { typeof(Pawn), typeof(Thing) })]
    internal static class HarvestOutputHook
    {
        static void Postfix(Pawn __0, Thing __1)
        {
            if (__1 == null || __1.def == null || __1.stackCount <= 0) return;
            AdditionalHarvestAdapter.MarkMainProduct(__1);
            QuestGame game = QuestGame.Current;
            if (game == null) return;
            CompQuality quality = __1.TryGetComp<CompQuality>();
            game.RecordVerifiedOutput("harvest", __0, __1.def, __1.Stuff,
                quality == null ? null : quality.Quality.ToString(), __1.stackCount);
        }
    }

    // Nutrient paste dispensing creates a meal without going through the
    // standard recipe notification. VNPE also patches this same method and
    // can replace its result, so observe the final returned Thing once.
    [HarmonyPatch(typeof(Building_NutrientPasteDispenser), "TryDispenseFood")]
    internal static class NutrientPasteOutputHook
    {
        static void Postfix(Building_NutrientPasteDispenser __instance, Thing __result)
        {
            if (__result != null)
                QuestGame.Current?.RecordVerifiedMachineOutput("produce", __instance, __result, __result.stackCount);
        }
    }

    // The vanilla mine has no quest notification. Capture the resource total just
    // around its own yield-placement call, so a failed drop does not count.
    [HarmonyPatch(typeof(Mineable), "TrySpawnYield", new Type[] { typeof(Map), typeof(bool), typeof(Pawn) })]
    internal static class MiningOutputHook
    {
        internal struct State
        {
            internal ThingDef product;
            internal int before;
        }

        static int Total(Map map, ThingDef product)
        {
            if (map == null || product == null) return 0;
            long count = 0;
            foreach (Thing thing in map.listerThings.ThingsOfDef(product)) count += Math.Max(0, thing.stackCount);
            return (int)Math.Min(int.MaxValue, count);
        }

        static void Prefix(Mineable __instance, Map __0, Pawn __2, out State __state)
        {
            __state = default;
            QuestGame game = QuestGame.Current;
            if (game == null || game.Pack == null || game.LoadError != null || !game.HasColony || __2 == null
                || __2.Faction != Faction.OfPlayer || __2.IsPrisoner || __0 == null) return;
            ThingDef product = __instance.def?.building?.mineableThing;
            if (product == null) return;
            __state.product = product;
            __state.before = Total(__0, product);
        }

        static void Postfix(Map __0, Pawn __2, State __state)
        {
            if (__state.product == null) return;
            int delta = Total(__0, __state.product) - __state.before;
            if (delta > 0) QuestGame.Current?.RecordVerifiedOutput("mine", __2, __state.product, null, null, delta);
        }
    }
}
