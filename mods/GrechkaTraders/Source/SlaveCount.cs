using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Traders
{
    [HarmonyPatch(typeof(StockGenerator_Slaves), nameof(StockGenerator_Slaves.GenerateThings))]
    public static class CaravanSlaveCount
    {
        private static readonly MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");
        public static bool ShouldOverride(StockGenerator_Slaves generator)
            => GrechkaTradersMod.Settings.customSlaveCount && generator.trader != null
                && !generator.trader.orbital && (generator.trader.defName.StartsWith("Caravan_", StringComparison.Ordinal)
                    || DefDatabase<FactionDef>.AllDefsListForReading.Exists(f => f.caravanTraderKinds != null
                        && f.caravanTraderKinds.Contains(generator.trader)));

        public static StockGenerator_Slaves ConfiguredCopy(StockGenerator_Slaves generator)
        {
            var copy = (StockGenerator_Slaves)Clone.Invoke(generator, null);
            int count = Math.Max(0, Math.Min(50, GrechkaTradersMod.Settings.slavesPerCaravan));
            copy.countRange = new IntRange(count, count);
            AccessTools.Field(typeof(StockGenerator_Slaves), "respectPopulationIntent").SetValue(copy, false);
            return copy;
        }

        public static void Postfix(StockGenerator_Slaves __instance, ref IEnumerable<Thing> __result)
        {
            if (!ShouldOverride(__instance)) return;
            // The iterator captures the generator. Change that reference to a private copy before
            // enumeration, keeping the native pawn generator and ideology checks intact.
            // Do not call GenerateThings recursively or mutate the shared stock generator.
            object iterator = __result;
            var field = AccessTools.Field(iterator.GetType(), "<>4__this");
            if (field == null) throw new InvalidOperationException("GrechkaTraders: native slave iterator owner was not found");
            field.SetValue(iterator, ConfiguredCopy(__instance));
        }
    }
}
