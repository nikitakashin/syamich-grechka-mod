using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Grechka.Industry
{
    // Recycling uses a conservative production value, independently of shop prices.
    // Each explicit crafting edge conserves this value even with T5's minimum
    // half-input / double-output contract. Repeated relaxations cover complete
    // recipe chains; unresolved custom producers fail closed for their products.
    public static class MatterCraftValue
    {
        private sealed class Input
        {
            public double count;
            public List<(ThingDef def, double units)> options;
        }
        private sealed class Edge
        {
            public List<Input> inputs;
            public List<ThingDef> outputs;
            public double outputCount;
        }
        private static readonly Dictionary<ThingDef, double> values = new Dictionary<ThingDef, double>();
        private static bool ready;
        private static int recipeCount;
        public static void Invalidate() { ready = false; }
        internal static void GeneratedRecipeAdded() { recipeCount = DefDatabase<RecipeDef>.AllDefsListForReading.Count; }
        public static double UnitValue(ThingDef def, double marketValue)
        {
            if (!ready || recipeCount != DefDatabase<RecipeDef>.AllDefsListForReading.Count) Rebuild();
            return def != null && values.TryGetValue(def, out double cap) && double.IsFinite(marketValue)
                ? Math.Max(0, Math.Min(marketValue, cap)) : 0;
        }
        public static double Value(Thing thing) => thing == null ? 0 : UnitValue(thing.def, thing.MarketValue) * thing.stackCount;
        public static void Rebuild(Func<ThingDef, double> marketValue = null)
        {
            values.Clear();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                double price = 0;
                try { price = marketValue != null ? marketValue(def) : def.GetStatValueAbstract(StatDefOf.MarketValue, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null); }
                catch { }
                values[def] = double.IsFinite(price) && price > 0 ? price : 0;
            }
            var edges = new List<Edge>();
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe.GetModExtension<MatterRecipeProperties>() != null || recipe.products?.Count == 0 || recipe.products == null) continue;
                var outputs = recipe.products.Where(p => p.thingDef != null && p.count > 0).ToList();
                if (outputs.Count == 0) continue;
                foreach (var output in outputs) if (!values.ContainsKey(output.thingDef)) values[output.thingDef] = 0;
                var edge = new Edge { outputs = outputs.Select(p => p.thingDef).Distinct().ToList(), outputCount = outputs.Sum(p => (double)p.count), inputs = new List<Input>() };
                bool supported = (recipe.workerClass == null || recipe.workerClass == typeof(RecipeWorker))
                    && (recipe.specialProducts == null || recipe.specialProducts.Count == 0)
                    && recipe.IngredientValueGetter is IngredientValueGetter_Volume
                    && recipe.ingredients != null;
                if (supported)
                    foreach (IngredientCount ingredient in recipe.ingredients)
                    {
                        double count = ingredient.GetBaseCount();
                        if (!double.IsFinite(count) || count < 0) { supported = false; break; }
                        var options = new List<(ThingDef, double)>();
                        foreach (ThingDef def in ingredient.filter.AllowedThingDefs)
                        {
                            double units = recipe.IngredientValueGetter.ValuePerUnitOf(def);
                            if (!double.IsFinite(units) || units <= 0 || !values.ContainsKey(def)) continue;
                            options.Add((def, units));
                        }
                        if (count > 0 && options.Count == 0) { supported = false; break; }
                        if (count > 0) edge.inputs.Add(new Input { count = count, options = options });
                    }
                if (supported) edges.Add(edge);
                else foreach (ThingDef def in edge.outputs) values[def] = 0;
            }
            HashSet<ThingDef> changing = null;
            for (int pass = 0; pass < 128; pass++)
            {
                changing = new HashSet<ThingDef>();
                foreach (Edge edge in edges)
                {
                    double inputValue = edge.inputs.Sum(i => i.count * i.options.Min(o => values[o.def] / o.units));
                    double cap = inputValue * .25 / edge.outputCount;
                    if (!double.IsFinite(cap) || cap < 1e-12) cap = 0;
                    foreach (ThingDef output in edge.outputs)
                        if (values[output] > cap) { values[output] = cap; changing.Add(output); }
                }
                if (changing.Count == 0) break;
            }
            // A profitable conversion cycle has no positive fixed lower bound.
            // Never keep the finite, still decreasing approximation from the cap.
            if (changing?.Count > 0)
            {
                foreach (ThingDef def in changing) values[def] = 0;
                for (int pass = 0; pass <= values.Count; pass++)
                {
                    bool changed = false;
                    foreach (Edge edge in edges)
                    {
                        double cap = edge.inputs.Sum(i => i.count * i.options.Min(o => values[o.def] / o.units)) * .25 / edge.outputCount;
                        foreach (ThingDef output in edge.outputs)
                            if (values[output] > cap) { values[output] = cap; changed = true; }
                    }
                    if (!changed) break;
                }
            }
            ready = true;
            recipeCount = DefDatabase<RecipeDef>.AllDefsListForReading.Count;
        }
    }
}
