using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Grechka.Storage
{
    internal static class FactoryInputPlanner
    {
        // Plan against real remaining units first. Never withdraw a partial failed plan.
        internal static List<(StorageDisk disk, Thing item, int count)> Select(
            List<object> definitions, List<object> owners,
            List<(StorageDisk disk, Thing item)> available, bool disallowMixing,
            out int missingIndex, out int shortage)
        {
            missingIndex = -1; shortage = 0;
            if (owners.Count != definitions.Count) return null;
            var categories = Enumerable.Range(0, definitions.Count).Where(i =>
                FactoryCraftAdapter.Get(owners[i], "PipeNetDef") == null
                && FactoryCraftAdapter.Get<int>(owners[i], "Required") > 0
                && FactoryCraftAdapter.Get(definitions[i], "thingCategory") != null).ToList();
            List<ThingDef> kinds = disallowMixing && categories.Count > 0
                ? available.Where(a => categories.All(i => FactoryCraftAdapter.Allowed(definitions[i], a.item)))
                    .Select(a => a.item.def).Distinct().ToList()
                : new List<ThingDef> { null };
            if (kinds.Count == 0) { missingIndex = categories[0]; shortage = FactoryCraftAdapter.Get<int>(owners[missingIndex], "Required"); return null; }
            int bestShortage = int.MaxValue, bestIndex = -1;
            foreach (ThingDef kind in kinds)
            {
                var left = available.ToDictionary(a => a.item, a => a.item.stackCount);
                var result = new List<(StorageDisk, Thing, int)>();
                int failed = -1, need = 0;
                // Exact ingredients must not be spent by a broader category first.
                foreach (int i in Enumerable.Range(0, definitions.Count).OrderBy(i => FactoryCraftAdapter.Get(definitions[i], "thing") == null ? 1 : 0))
                {
                    if (FactoryCraftAdapter.Get(owners[i], "PipeNetDef") != null) continue;
                    need = FactoryCraftAdapter.Get<int>(owners[i], "Required");
                    foreach (var candidate in available)
                    {
                        if (need <= 0) break;
                        if (left[candidate.item] <= 0 || !FactoryCraftAdapter.Allowed(definitions[i], candidate.item, kind)) continue;
                        int amount = Math.Min(need, left[candidate.item]);
                        result.Add((candidate.disk, candidate.item, amount)); left[candidate.item] -= amount; need -= amount;
                    }
                    if (need > 0) { failed = i; break; }
                }
                if (failed < 0) return result;
                if (need < bestShortage) { bestShortage = need; bestIndex = failed; }
            }
            missingIndex = bestIndex; shortage = bestShortage;
            return null;
        }
    }
}
