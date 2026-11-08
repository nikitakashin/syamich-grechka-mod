using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Grechka.Tweaks
{
    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    static class TradeExperience
    {
        internal readonly record struct Deal(Pawn Negotiator, float Experience);
        internal static float Value(int count, float price) => Math.Abs((float)count) * Math.Max(0, price);

        static void Prefix(TradeDeal __instance, out Deal __state)
        {
            __state = default;
            if (TradeSession.trader == null || TradeSession.giftMode || TradeSession.TradeCurrency != TradeCurrency.Silver) return;
            double value = 0;
            foreach (var line in __instance.AllTradeables)
                if (line.CountToTransfer != 0 && !line.IsCurrency && !line.IsFavor)
                    value += Value(line.CountToTransfer, line.GetPriceFor(line.ActionToDo));
            __state = new Deal(TradeSession.playerNegotiator, (float)(value * .1));
        }
        static void Postfix(bool __result, bool actuallyTraded, Deal __state)
        {
            if (__result && actuallyTraded && __state.Experience > 0)
                __state.Negotiator?.skills?.Learn(SkillDefOf.Social, __state.Experience);
        }
    }

    static class SleepExperience
    {
        private sealed class Pending { public int ticks; }
        private static readonly ConditionalWeakTable<Pawn_SkillTracker, Pending> pending = new();

        internal static bool Eligible(Pawn p) => p?.skills != null && p.needs?.rest != null &&
            (p.IsColonist || p.IsSlaveOfColony) && !p.Dead && !p.Suspended && !p.Deathresting &&
            p.health.capacities.CanBeAwake;
        internal static bool OnMap(Pawn p) => p.Spawned && p.jobs?.curDriver?.asleep == true && Eligible(p);
        internal static bool InCaravan(Pawn p, Caravan c) => c?.NightResting == true && !c.pather.MovingNow && Eligible(p);

        internal static void Add(Pawn p, int delta)
        {
            if (delta <= 0) return;
            var entry = pending.GetOrCreateValue(p.skills);
            entry.ticks += delta;
            if (entry.ticks < 200) return;
            float xp = entry.ticks * (10f / GenDate.TicksPerHour);
            entry.ticks = 0;
            foreach (var skill in p.skills.skills)
                if (!skill.TotallyDisabled) p.skills.Learn(skill.def, xp);
        }
        [HarmonyPatch(typeof(Pawn_SkillTracker), nameof(Pawn_SkillTracker.ExposeData))]
        static class SaveRemainder
        {
            static void Postfix(Pawn_SkillTracker __instance)
            {
                if (Scribe.mode != LoadSaveMode.LoadingVars && !pending.TryGetValue(__instance, out _)) return;
                Scribe_Values.Look(ref pending.GetOrCreateValue(__instance).ticks, "grechkaSleepTicks");
            }
        }
        [HarmonyPatch(typeof(Toils_LayDown), "ApplyBedRelatedEffects")]
        static class MapRest
        {
            static void Postfix(Pawn p, bool asleep, bool gainRest, int delta)
            {
                if (asleep && gainRest && Eligible(p)) Add(p, delta);
            }
        }
        [HarmonyPatch(typeof(Caravan_NeedsTracker), "TrySatisfyRestNeed")]
        static class CaravanRest
        {
            static void Postfix(Caravan ___caravan, Pawn pawn, int delta)
            {
                if (InCaravan(pawn, ___caravan)) Add(pawn, delta);
            }
        }
        [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Interval))]
        static class Forgetting
        {
            static bool Prefix(SkillRecord __instance)
            {
                if (Mastery.Level(__instance) > 20) return false;
                if (__instance.levelInt < 10) return true;
                var p = __instance.Pawn;
                return !OnMap(p) && (p.Spawned || !InCaravan(p, p.GetCaravan()));
            }
        }
    }
}
