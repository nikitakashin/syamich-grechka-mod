using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Industry
{
    public sealed class CompProperties_TierBonuses : CompProperties
    {
        public float inputDiscount;
        public float outputBonus;
        public CompProperties_TierBonuses() { compClass = typeof(CompTierBonuses); }
    }

    public sealed class CompTierBonuses : ThingComp
    {
        private Dictionary<string, float> outputCarry = new Dictionary<string, float>();
        private CompProperties_TierBonuses Props { get { return (CompProperties_TierBonuses)props; } }
        public float InputDiscount { get { return Props.inputDiscount; } }

        internal void CopyProgressFrom(CompTierBonuses previous)
        {
            if (previous != null) outputCarry = new Dictionary<string, float>(previous.outputCarry);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref outputCarry, "grechkaOutputCarry", LookMode.Value, LookMode.Value);
            if (outputCarry == null) outputCarry = new Dictionary<string, float>();
        }

        // Carry is keyed by recipe and output item. Ten one-item cycles at +10%
        // therefore award one extra item, even across save/load boundaries.
        public int TakeExtra(RecipeDef recipe, Thing product)
        {
            if (recipe == null || product == null || Props.outputBonus <= 0f)
                return 0;
            string key = recipe.defName + "/" + product.def.defName;
            float carried = outputCarry.TryGetValue(key, out float previous) ? previous : 0f;
            float total = carried + product.stackCount * Props.outputBonus;
            int extra = (int)Math.Floor(total + 0.00001f);
            outputCarry[key] = total - extra;
            return extra;
        }
    }

    [HarmonyPatch(typeof(RecipeWorker), nameof(RecipeWorker.GetIngredientCount))]
    internal static class TierIngredientHook
    {
        private static void Postfix(Bill bill, ref float __result)
        {
            var thing = bill?.billStack?.billGiver as ThingWithComps;
            if (thing?.GetComp<CompAssemblyUpgrade>()?.IsUpgradeRecipe(bill.recipe) == true) return;
            CompTierBonuses bonus = thing?.GetComp<CompTierBonuses>();
            if (bonus == null || bonus.InputDiscount <= 0f || __result <= 0f) return;
            // IngredientCount is a value, not always a physical item count:
            // gold and silver have smallVolume=0.1. Vanilla CountRequiredOfFor
            // divides by ValuePerUnitOf and rounds up the physical units.
            __result *= 1f - bonus.InputDiscount;
        }
    }

    [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    internal static class TierProductHook
    {
        private static void Postfix(RecipeDef recipeDef, IBillGiver billGiver, ref IEnumerable<Thing> __result)
        {
            var thing = billGiver as ThingWithComps;
            CompTierBonuses bonus = thing?.GetComp<CompTierBonuses>();
            if (bonus == null || __result == null) return;
            __result = WithBonus(__result, recipeDef, bonus);
        }

        private static IEnumerable<Thing> WithBonus(IEnumerable<Thing> original, RecipeDef recipe, CompTierBonuses bonus)
        {
            foreach (Thing product in original)
            {
                Thing inner = product is MinifiedThing packed ? packed.InnerThing : product;
                // Only explicit fresh products receive replicas. A special recipe
                // returning a pawn, corpse or an existing container does not.
                bool explicitProduct = inner != null && recipe.products != null
                    && recipe.products.Exists(p => p.thingDef == inner.def);
                int extra = explicitProduct ? bonus.TakeExtra(recipe, product) : 0;
                if (product.def.stackLimit > 1)
                {
                    product.stackCount += extra;
                    while (product.stackCount > product.def.stackLimit)
                        yield return product.SplitOff(product.def.stackLimit);
                    yield return product;
                }
                else
                {
                    yield return product;
                    for (int i = 0; i < extra; i++)
                    {
                        Thing created = ThingMaker.MakeThing(inner.def, inner.Stuff);
                        if (inner.TryGetQuality(out QualityCategory quality))
                            created.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Colony);
                        yield return product is MinifiedThing ? created.MakeMinified() : created;
                    }
                }
            }
        }
    }
}
