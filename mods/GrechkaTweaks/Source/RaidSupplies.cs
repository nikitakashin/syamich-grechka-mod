using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Tweaks
{
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "GenerateRaidLoot")]
    internal static class RaidSupplies
    {
        private static readonly string[][] supplyGroups =
        {
            new[] { "Pemmican", "MealSurvivalPack", "Chocolate" },
            new[] { "MedicineHerbal", "MedicineIndustrial" },
            new[] { "Steel", "Chemfuel", "Cloth", "ComponentIndustrial" },
            new[] { "Silver", "Gold", "Jade" }
        };

        internal static int Budget(float points) => float.IsFinite(points)
            ? (int)Math.Clamp(points * .75f, 0f, 20000f)
            : 0;

        internal static List<ThingDefCountClass> Plan(float points, Func<string, ThingDef> resolve)
        {
            float remaining = Budget(points);
            var result = new List<ThingDefCountClass>();
            if (remaining <= 0) return result;
            int groups = remaining >= 1000 ? 4 : remaining >= 250 ? 3 : 2;
            var available = supplyGroups.Select(group => group.Select(resolve)
                .Where(d => d != null && d.BaseMarketValue > 0 && float.IsFinite(d.BaseMarketValue)).ToList()).ToList();
            for (int slots = groups; slots > 0 && available.Count > 0; slots--)
            {
                float share = remaining / slots;
                var affordable = available.Where(g => g.Any(d => d.BaseMarketValue <= share)).ToList();
                if (affordable.Count == 0) continue;
                var group = affordable.RandomElement();
                available.Remove(group);
                var def = group.Where(d => d.BaseMarketValue <= share).RandomElement();
                int count = (int)Math.Floor(share / def.BaseMarketValue);
                result.Add(new ThingDefCountClass(def, count));
                remaining -= count * def.BaseMarketValue;
            }
            return result;
        }

        static void Postfix(float raidLootPoints, List<Pawn> pawns)
        {
            if (pawns == null || pawns.Count == 0) return;
            var carriers = pawns.Where(p => p?.inventory?.innerContainer != null && !p.Dead).ToList();
            if (carriers.Count == 0) return;
            int next = Rand.Range(0, carriers.Count);
            foreach (var entry in Plan(raidLootPoints, DefDatabase<ThingDef>.GetNamedSilentFail))
            {
                int remaining = entry.count;
                while (remaining > 0)
                {
                    Thing supplies = ThingMaker.MakeThing(entry.thingDef);
                    int count = Math.Min(remaining, supplies.def.stackLimit);
                    supplies.stackCount = count;
                    bool added = false;
                    for (int i = 0; i < carriers.Count && !added; i++)
                    {
                        added = carriers[next].inventory.innerContainer.TryAdd(supplies, false);
                        next = (next + 1) % carriers.Count;
                    }
                    if (!added)
                    {
                        if (!supplies.Destroyed) supplies.Destroy();
                        break;
                    }
                    remaining -= count;
                }
            }
        }
    }
}
