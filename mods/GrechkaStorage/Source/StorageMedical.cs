using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grechka.Storage
{
    [HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.PotentiallyMissingIngredients))]
    internal static class StorageRecipeMenuHook
    {
        private static void Postfix(RecipeDef __instance, Pawn billDoer, Map map, ref IEnumerable<ThingDef> __result)
        {
            if (map == null) return;
            var missing = __result.ToList();
            if (missing.Count == 0) { __result = missing; return; }
            var stored = StorageMapAccess.Available(map).Where(t =>
                !t.PositionHeld.Fogged(map) && (billDoer == null || !t.IsForbidden(billDoer))).ToList();
            if (stored.Count > 0)
            {
                var reported = new HashSet<ThingDef>(missing);
                foreach (IngredientCount ingredient in __instance.ingredients)
                {
                    if (!stored.Any(t => ingredient.filter.Allows(t)
                        && (ingredient.IsFixedIngredient || __instance.fixedIngredientFilter.Allows(t)))) continue;
                    // Recipe menus report the cheapest allowed definition for alternatives.
                    // If two slots report the same def, remove only the entry belonging to this slot.
                    ThingDef representative = ingredient.IsFixedIngredient ? ingredient.FixedIngredient
                        : ingredient.filter.AllowedThingDefs.Where(d => __instance.fixedIngredientFilter.Allows(d))
                            .OrderBy(d => d.BaseMarketValue).FirstOrDefault();
                    if (representative == null || !reported.Contains(representative)) continue;
                    if (map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Any(t => !t.Position.Fogged(map)
                        && (billDoer == null || !t.IsForbidden(billDoer)) && ingredient.filter.Allows(t)
                        && (ingredient.IsFixedIngredient || __instance.fixedIngredientFilter.Allows(t)))) continue;
                    missing.Remove(representative);
                }
            }
            __result = missing;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestIngredientsHelper")]
    internal static class StorageWorkIngredientsHook
    {
        private static void Prefix(Predicate<Thing> thingValidator, ref Predicate<List<Thing>> foundAllIngredientsAndChoose,
            Pawn pawn, Thing billGiver, float searchRadius)
        {
            if (pawn.Map == null || billGiver == null) return;
            var choose = foundAllIngredientsAndChoose;
            List<Thing> stored = null;
            foundAllIngredientsAndChoose = candidates =>
            {
                if (choose(candidates)) return true;
                if (stored == null)
                {
                    var care = WorkGiver_DoBill.GetMedicalCareCategory(billGiver);
                    stored = StorageMapAccess.Available(pawn.Map).Where(t => thingValidator(t)
                        && (!(billGiver is Pawn) || !t.def.IsMedicine || care.AllowsMedicine(t.def))
                        && pawn.CanReserve(t) && StorageMapAccess.PickupPoint(pawn, t, Danger.Deadly) is Thing point
                        && point.Position.InHorDistOf(billGiver.Position, searchRadius)).ToList();
                }
                if (stored.Count == 0) return false;
                // Keep the region search's scratch list untouched even when a native chooser sorts its input.
                return choose(candidates.Concat(stored).Distinct().ToList());
            };
        }
    }
}
