using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Treasures
{
    public sealed class RecipeWorker_CutAnyGem : RecipeWorker { }

    [StaticConstructorOnStartup]
    public static class GemCutting
    {
        static GemCutting()
        {
            new Harmony("grechka.treasures.gem-cutting").CreateClassProcessor(typeof(ProductsHook)).Patch();
        }

        public static ThingDef ProductFor(ThingDef raw)
        {
            if (raw == null) return null;
            foreach (string gem in TreasureLoot.Gems)
                if (raw.defName == "GrechkaTreasures_" + gem + "Raw")
                    return DefDatabase<ThingDef>.GetNamed("GrechkaTreasures_" + gem + "Cut");
            return null;
        }

        public static IEnumerable<Thing> Products(List<Thing> ingredients)
        {
            foreach (Thing ingredient in ingredients)
            {
                ThingDef productDef = ProductFor(ingredient.def);
                if (productDef == null) continue;
                int remaining = ingredient.stackCount;
                while (remaining > 0)
                {
                    Thing product = ThingMaker.MakeThing(productDef);
                    product.stackCount = System.Math.Min(remaining, productDef.stackLimit);
                    remaining -= product.stackCount;
                    yield return product;
                }
            }
        }

        [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
        public static class ProductsHook
        {
            static bool Prefix(RecipeDef recipeDef, List<Thing> ingredients, ref IEnumerable<Thing> __result)
            {
                if (recipeDef.workerClass != typeof(RecipeWorker_CutAnyGem)) return true;
                __result = Products(ingredients);
                return false;
            }
        }
    }
}
