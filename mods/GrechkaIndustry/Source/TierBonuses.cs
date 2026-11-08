using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Industry
{
    public static class TierProductionPolicy
    {
        public static bool IsUpgrade(RecipeDef recipe) => recipe?.defName?.StartsWith("Grechka_Upgrade", StringComparison.Ordinal) == true;
        private static bool BuildingOrPackage(ThingDef def) => def != null && (def.category == ThingCategory.Building
            || def.thingClass != null && typeof(MinifiedThing).IsAssignableFrom(def.thingClass));
        public static bool SuppressOutput(RecipeDef recipe, ThingDef product) => IsUpgrade(recipe)
            || BuildingOrPackage(product) && recipe?.ingredients?.Any(i => i.filter.AllowedThingDefs.Any(BuildingOrPackage)) == true;
    }

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
        public Dictionary<string, float> CaptureCarry() => new Dictionary<string, float>(outputCarry);
        public void RestoreCarry(Dictionary<string, float> snapshot)
        { outputCarry = new Dictionary<string, float>(snapshot ?? throw new ArgumentNullException(nameof(snapshot))); }

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

        // Old untyped keys remain serialized, but cannot be attributed to an
        // unknown material/quality or a possibly changed recipe.
        private static string CarryKey(RecipeDef recipe, Thing product)
        {
            Thing inner = product.GetInnerIfMinified();
            var signature = new StringBuilder(recipe.workerClass?.FullName ?? "RecipeWorker");
            foreach (var input in recipe.ingredients ?? new List<IngredientCount>())
                signature.Append('|').Append(input.GetBaseCount().ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(string.Join(",", input.filter.AllowedThingDefs.Select(d => d.defName).OrderBy(n => n, StringComparer.Ordinal)));
            foreach (var output in recipe.products ?? new List<ThingDefCountClass>())
                signature.Append('|').Append(output.thingDef.defName).Append(':').Append(output.count);
            if (recipe.fixedIngredientFilter != null) signature.Append("|fixed:")
                .Append(string.Join(",", recipe.fixedIngredientFilter.AllowedThingDefs.Select(d => d.defName).OrderBy(n => n, StringComparer.Ordinal)));
            foreach (var special in recipe.specialProducts ?? new List<SpecialProductType>()) signature.Append('|').Append((int)special);
            ulong hash = 14695981039346656037UL;
            foreach (char c in signature.ToString()) { hash ^= c; hash *= 1099511628211UL; }
            int quality = inner.TryGetQuality(out QualityCategory grade) ? (int)grade : -1;
            return "v2/" + recipe.defName + "/" + inner.def.defName + "/" + hash.ToString("X16")
                + "/" + (inner.Stuff?.defName ?? "-") + "/" + quality + "/" + (inner.StyleDef?.defName ?? "-");
        }

        // A planner does not yet know the generated quality/material. Ignore
        // carry rather than promising an incompatible signature's extra item.
        public int PreviewExtra(RecipeDef recipe, ThingDef product)
        {
            if (TierProductionPolicy.SuppressOutput(recipe, product)) return 0;
            int count = recipe?.products?.FirstOrDefault(p => p.thingDef == product)?.count ?? 0;
            double value = count * (double)Props.outputBonus;
            return value > 0 && double.IsFinite(value) && value <= int.MaxValue ? (int)Math.Floor(value + .00001) : 0;
        }

        // Ten compatible one-item cycles at +10% award one extra item.
        public int TakeExtra(RecipeDef recipe, Thing product)
        {
            if (recipe == null || product == null || Props.outputBonus <= 0f || TierProductionPolicy.SuppressOutput(recipe, product.GetInnerIfMinified().def))
                return 0;
            string key = CarryKey(recipe, product);
            float carried = outputCarry.TryGetValue(key, out float previous) ? previous : 0f;
            double total = carried + (double)product.stackCount * Props.outputBonus;
            if (!double.IsFinite(total) || total > int.MaxValue) return 0;
            int extra = (int)Math.Floor(total + 0.00001f);
            outputCarry[key] = (float)(total - extra);
            return extra;
        }
    }

    [HarmonyPatch(typeof(RecipeWorker), nameof(RecipeWorker.GetIngredientCount))]
    internal static class TierIngredientHook
    {
        private static void Postfix(Bill bill, ref float __result)
        {
            var thing = bill?.billStack?.billGiver as ThingWithComps;
            if (TierProductionPolicy.IsUpgrade(bill?.recipe) || thing?.GetComp<CompAssemblyUpgrade>()?.IsUpgradeRecipe(bill.recipe) == true) return;
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
        private static void Postfix(RecipeDef recipeDef, IBillGiver billGiver, List<Thing> ingredients, ref IEnumerable<Thing> __result)
        {
            var thing = billGiver as ThingWithComps;
            CompTierBonuses bonus = thing?.GetComp<CompTierBonuses>();
            if (bonus == null || __result == null) return;
            __result = WithBonus(__result, recipeDef, bonus, ingredients);
        }

        private static IEnumerable<Thing> WithBonus(IEnumerable<Thing> original, RecipeDef recipe, CompTierBonuses bonus, List<Thing> ingredients)
        {
            // Capture before the lazy recipe iterator may transfer or destroy an input.
            var inputs = new HashSet<Thing>(ingredients ?? new List<Thing>());
            foreach (Thing input in inputs.ToList())
            {
                inputs.Add(input.GetInnerIfMinified());
                if (input is IThingHolder holder)
                    foreach (Thing held in ThingOwnerUtility.GetAllThingsRecursively(holder)) inputs.Add(held);
            }
            bool nativeSpecial = recipe.Worker.GetType() == typeof(RecipeWorker)
                && ingredients?.Count > 0
                && recipe.specialProducts?.Count > 0 && recipe.specialProducts.All(s => s == SpecialProductType.StoneBlocks
                    || s == SpecialProductType.Smelted || s == SpecialProductType.Butchery)
                && (ingredients ?? new List<Thing>()).All(input => recipe.specialProducts.All(s => NativeSpecialSource(input, s)));
            foreach (Thing product in original)
            {
                Thing inner = product is MinifiedThing packed ? packed.InnerThing : product;
                bool fresh = inner != null && !inputs.Contains(product) && !inputs.Contains(inner)
                    && !product.Spawned && product.holdingOwner == null && !product.Destroyed
                    && !(inner is Pawn) && !(inner is Corpse)
                    && (!(product is IThingHolder) || product is MinifiedThing)
                    && !(inner is IThingHolder);
                // Returning an existing input must preserve its identity and stack,
                // not merely disable extra copies while still splitting it.
                if (!fresh) { yield return product; continue; }
                bool explicitProduct = inner != null && recipe.products != null
                    && recipe.products.Exists(p => p.thingDef == inner.def);
                int extra = fresh && (explicitProduct || nativeSpecial && product.def.category == ThingCategory.Item)
                    ? bonus.TakeExtra(recipe, product) : 0;
                if (product.def.stackLimit > 1)
                {
                    long remaining = (long)product.stackCount + extra;
                    while (remaining > product.def.stackLimit)
                    {
                        int split = Math.Min(product.def.stackLimit, int.MaxValue - 1);
                        // SplitOff must create a separate object; keep the native
                        // comp/quality split behaviour without overflowing stackCount.
                        product.stackCount = (int)Math.Min(int.MaxValue, remaining);
                        Thing part = product.SplitOff(split);
                        remaining -= split;
                        yield return part;
                    }
                    product.stackCount = (int)remaining;
                    yield return product;
                }
                else
                {
                    yield return product;
                    for (int i = 0; i < extra; i++)
                    {
                        Thing created = ThingMaker.MakeThing(inner.def, inner.Stuff);
                        created.StyleDef = inner.StyleDef;
                        created.StyleSourcePrecept = inner.StyleSourcePrecept;
                        if (inner.TryGetQuality(out QualityCategory quality))
                            created.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Colony);
                        yield return product is MinifiedThing ? created.MakeMinified() : created;
                    }
                }
            }
        }
        private static bool NativeSpecialSource(Thing input, SpecialProductType kind)
        {
            Type implementation = AccessTools.Method(input.GetType(), kind == SpecialProductType.StoneBlocks ? "StoneBlockProducts"
                : kind == SpecialProductType.Smelted ? "SmeltProducts" : "ButcherProducts")?.DeclaringType;
            // Corpse's native iterator has already emitted its history/filth/tales
            // once. Only fresh returned products get replicas; never re-run it.
            return implementation == typeof(Thing) || kind == SpecialProductType.Butchery && implementation == typeof(Corpse);
        }
    }
}
