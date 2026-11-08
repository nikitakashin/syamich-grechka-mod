using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Grechka.LuckyBlocks
{
    // Price the unopened lottery, without selecting or inspecting any block's saved outcome.
    public static class LuckyMarketValues
    {
        public static double ExpectedValue(IEnumerable<LuckyOutcome> outcomes,
            Func<LuckyItem, double> itemValue, Func<LuckyOutcome, double> animalValue)
        {
            double weighted = 0, weights = 0;
            foreach (LuckyOutcome outcome in outcomes)
            {
                double value = outcome.items.Sum(item => Math.Max(0, itemValue(item)) * item.count);
                if (outcome.effect == "animals") value += Math.Max(0, animalValue(outcome)) * outcome.pawnCount;
                // Hostile mechs are a risk, not a stack of owned, saleable pawns.
                weighted += value * outcome.weight;
                weights += outcome.weight;
            }
            return weights > 0 && double.IsFinite(weighted) ? Math.Max(1, weighted / weights) : 1;
        }

        public static void Refresh()
        {
            if (LuckyMod.Config == null || StatDefOf.MarketValue == null) return;
            var poolValues = new Dictionary<string, double>();
            var evaluating = new HashSet<string>();
            double PoolValue(string pool)
            {
                if (poolValues.TryGetValue(pool, out double known)) return known;
                if (!evaluating.Add(pool)) throw new InvalidOperationException("Cyclic Lucky block reward pool: " + pool);
                try
                {
                    return poolValues[pool] = ExpectedValue(LuckyResolver.Available(LuckyMod.Config, pool, Find.World?.factionManager != null), ItemValue, AnimalValue);
                }
                finally { evaluating.Remove(pool); }
            }
            double ItemValue(LuckyItem item)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(item.def);
                if (def == null) return 0;
                if (def.GetModExtension<LuckyProperties>() is LuckyProperties nested && nested.priceFromPool)
                    return PoolValue(nested.pool);
                ThingDef stuff = string.IsNullOrEmpty(item.stuff) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(item.stuff);
                QualityCategory quality = Enum.TryParse(item.quality, out QualityCategory parsed) ? parsed : QualityCategory.Normal;
                return StatDefOf.MarketValue.Worker.GetValue(StatRequest.For(def, stuff, quality));
            }
            double AnimalValue(LuckyOutcome outcome)
            {
                ThingDef race = DefDatabase<PawnKindDef>.GetNamedSilentFail(outcome.pawnKind)?.race;
                return race == null ? 0 : StatDefOf.MarketValue.Worker.GetValue(StatRequest.For(race, null));
            }
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                LuckyProperties properties = def.GetModExtension<LuckyProperties>();
                // General blocks retain their authored tier prices; themes follow the available lottery.
                if (properties == null || !properties.priceFromPool) continue;
                double value;
                try { value = PoolValue(properties.pool); }
                catch (InvalidOperationException error)
                {
                    Log.Warning("[Grechka Lucky Blocks] Original block price preserved: " + error.Message);
                    continue;
                }
                def.statBases ??= new List<StatModifier>();
                StatModifier price = def.statBases.FirstOrDefault(s => s.stat == StatDefOf.MarketValue);
                if (price == null) def.statBases.Add(price = new StatModifier { stat = StatDefOf.MarketValue });
                price.value = (float)Math.Min(1000000, value);
            }
            StatDefOf.MarketValue.Worker.DeleteStatCache();
        }
    }

    [StaticConstructorOnStartup]
    internal static class LuckyMarketValueStartup
    {
        static LuckyMarketValueStartup() { LongEventHandler.ExecuteWhenFinished(LuckyMarketValues.Refresh); }
    }
}
