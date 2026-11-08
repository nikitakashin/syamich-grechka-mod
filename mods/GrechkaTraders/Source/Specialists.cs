using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Grechka.Traders
{
    public sealed class SpecialistDef : Def
    {
        public List<string> packageIds = new List<string>();
        public List<string> curatedPackageIds = new List<string>();
        public List<string> extraThingDefs = new List<string>();
        public int offers = 12;
        public float discount = .15f;
        public bool caravan = true;
        public bool orbital = true;

        public bool Active => packageIds.Concat(curatedPackageIds).Any(p => ModsConfig.IsActive(p));
        public bool Owns(ThingDef def) => def?.modContentPack != null && packageIds.Any(p =>
            string.Equals(p, def.modContentPack.PackageIdPlayerFacing, StringComparison.OrdinalIgnoreCase))
            || def?.modContentPack != null && extraThingDefs.Contains(def.defName) && curatedPackageIds.Any(p =>
                string.Equals(p, def.modContentPack.PackageIdPlayerFacing, StringComparison.OrdinalIgnoreCase));
        public bool Matches(ThingDef def) => Owns(def) || def != null && extraThingDefs.Contains(def.defName);
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (packageIds.Count == 0 && curatedPackageIds.Count == 0) yield return "Specialist requires at least one package ID";
            if (offers < 1 || offers > 50) yield return "offers must be 1..50";
            if (discount < 0 || discount > .3f) yield return "discount must be 0..0.3";
        }
    }

    public sealed class SpecialistExtension : DefModExtension { public SpecialistDef specialist; }

    public static class SpecialistStock
    {
        public static bool Safe(ThingDef def)
        {
            if (def == null || def.thingClass == null || !def.tradeability.TraderCanSell()
                || def.destroyOnDrop || typeof(MinifiedThing).IsAssignableFrom(def.thingClass)
                || typeof(UnfinishedThing).IsAssignableFrom(def.thingClass)) return false;
            if (def.category == ThingCategory.Building) return def.minifiedDef != null;
            // Turret-only weapons are internal parts, even when their XML inherits tradeability.
            return def.category == ThingCategory.Item && !def.isUnfinishedThing
                && (!def.IsWeapon || def.weaponTags == null || !def.weaponTags.Contains("TurretGun"))
                && (!def.IsWeapon || def.GetCompProperties<CompProperties_Forbiddable>() != null);
        }

        private static readonly Dictionary<SpecialistDef, List<ThingDef>> cache = new Dictionary<SpecialistDef, List<ThingDef>>();
        public static List<ThingDef> Candidates(SpecialistDef specialist)
        {
            if (!cache.TryGetValue(specialist, out var defs))
                cache[specialist] = defs = DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(d => Safe(d) && specialist.Matches(d)).ToList();
            return defs;
        }

        public static List<ThingDef> SelectOffers(SpecialistDef specialist, IEnumerable<ThingDef> available)
        {
            var candidates = available.Distinct().ToList();
            var owned = candidates.Where(specialist.Owns).ToList();
            var support = candidates.Where(d => !specialist.Owns(d) && specialist.extraThingDefs.Contains(d.defName)).ToList();
            var chosen = new List<ThingDef>();
            int limit = Math.Min(specialist.offers, candidates.Count);
            void Take(List<ThingDef> pool)
            {
                ThingDef def = pool.RandomElement();
                chosen.Add(def); candidates.Remove(def); owned.Remove(def); support.Remove(def);
            }
            // A large themed catalogue used to fill every slot before basic supplies
            // could be considered. Preserve both the theme and one supporting offer.
            if (limit > 0 && owned.Count > 0) Take(owned);
            if (chosen.Count < limit && support.Count > 0) Take(support);
            while (chosen.Count < limit && candidates.Count > 0) Take(owned.Count > 0 ? owned : candidates);
            return chosen;
        }

        public static float Discount(SpecialistDef specialist, ThingDef def)
            => specialist != null && specialist.Owns(def)
                ? Math.Min(.3f, Math.Max(0f, specialist.discount * GrechkaTradersMod.Settings.discountMultiplier)) : 0f;

        public static bool Handles(SpecialistDef specialist, ThingDef def)
        {
            if (specialist == null || def == null) return false;
            if (Safe(def) && specialist.Matches(def)) return true;
            // The native trade window asks about the packed shell's def, not InnerThing.
            // Shared MinifiedThing shells can also hold other buildings; vanilla has no
            // per-instance stock-generator predicate. Prices still use the actual inner item.
            return def.thingClass != null && typeof(MinifiedThing).IsAssignableFrom(def.thingClass)
                && Candidates(specialist).Any(d => d.category == ThingCategory.Building && d.minifiedDef == def);
        }
    }

    public sealed class StockGenerator_Specialist : StockGenerator
    {
        public SpecialistDef specialist;
        public override bool HandlesThingDef(ThingDef def) => SpecialistStock.Handles(specialist, def);
        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            if (specialist == null || !specialist.Active) yield break;
            var chosen = SpecialistStock.SelectOffers(specialist, SpecialistStock.Candidates(specialist));
            foreach (var def in chosen)
            {
                int max = Math.Max(1, Math.Min(def.stackLimit, (int)(2000f / Math.Max(1f, def.BaseMarketValue))));
                int count = def.category == ThingCategory.Building ? 1 : Rand.RangeInclusive(1, max);
                foreach (var thing in StockGeneratorUtility.TryMakeForStock(def, count, faction))
                {
                    if (def.category == ThingCategory.Building)
                    {
                        // The stock maker finalizes the outer shell later. It does not forward
                        // that callback to the inner building (quality and mod-specific comps).
                        thing.PostGeneratedForTrader(trader, forTile, faction);
                        yield return thing.MakeMinified();
                    }
                    else yield return thing;
                }
            }
        }
    }

    [StaticConstructorOnStartup]
    public static class TraderRegistration
    {
        static TraderRegistration() => Register();
        public static void Register()
        {
            var traders = DefDatabase<TraderKindDef>.AllDefsListForReading.Where(t => !t.orbital
                && t.GetModExtension<SpecialistExtension>()?.specialist is SpecialistDef spec && spec.Active && spec.caravan).ToList();
            foreach (var faction in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                if (faction.isPlayer || faction.hidden || faction.permanentEnemy || !faction.humanlikeFaction
                    || faction.techLevel < TechLevel.Industrial || faction.caravanTraderKinds == null
                    || faction.caravanTraderKinds.Count == 0) continue;
                // Copy the list: XML inheritance may share the original collection between faction defs.
                faction.caravanTraderKinds = new List<TraderKindDef>(faction.caravanTraderKinds);
                foreach (var trader in traders)
                    if (!faction.caravanTraderKinds.Contains(trader)) faction.caravanTraderKinds.Add(trader);
            }
        }
    }

    [HarmonyPatch(typeof(TraderKindDef), nameof(TraderKindDef.CalculatedCommonality), MethodType.Getter)]
    public static class SpecialistAvailability
    {
        public static void Postfix(TraderKindDef __instance, ref float __result)
        {
            var spec = __instance.GetModExtension<SpecialistExtension>()?.specialist;
            if (spec != null && (!GrechkaTradersMod.Settings.specialistsEnabled || !spec.Active
                || (__instance.orbital ? !spec.orbital : !spec.caravan))) __result = 0f;
        }
    }

    [HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.GetPricePlayerBuy))]
    public static class SpecialistPrice
    {
        public static void Postfix(Thing thing, ref float __result)
        {
            if (thing == null || TradeSession.trader == null) return;
            var spec = TradeSession.trader.TraderKind?.GetModExtension<SpecialistExtension>()?.specialist;
            var def = thing is MinifiedThing mini ? mini.InnerThing?.def : thing.def;
            __result = Math.Max(TradeUtility.MinimumBuyPrice, __result * (1f - SpecialistStock.Discount(spec, def)));
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceTooltip))]
    public static class SpecialistPriceTooltip
    {
        public static void Postfix(Tradeable __instance, TradeAction action, ref string __result)
        {
            if (action != TradeAction.PlayerBuys || TradeSession.trader == null) return;
            var spec = TradeSession.trader.TraderKind?.GetModExtension<SpecialistExtension>()?.specialist;
            var thing = __instance.AnyThing;
            float discount = SpecialistStock.Discount(spec, thing is MinifiedThing m ? m.InnerThing?.def : thing?.def);
            if (discount > 0f) __result += "\n" + "GrechkaTraders_AppliedDiscount".Translate((discount * 100f).ToString("0"));
        }
    }
}
