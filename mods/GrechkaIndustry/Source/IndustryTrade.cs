using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Grechka.Industry
{
    public sealed class StockGenerator_Industry : StockGenerator
    {
        public int offers = 6;
        private static bool Own(ThingDef def) => def?.modContentPack?.PackageId == "grechka.industry";
        public override bool HandlesThingDef(ThingDef thingDef) => Own(thingDef);
        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            var candidates = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => Own(d)
                && (d.category == ThingCategory.Item && !typeof(MinifiedThing).IsAssignableFrom(d.thingClass)
                    || d.category == ThingCategory.Building && d.minifiedDef != null)
                && d.tradeability != Tradeability.None).ToList();
            for (int i = 0; i < offers && candidates.Count > 0; i++)
            {
                int index = Rand.Range(0, candidates.Count);
                ThingDef def = candidates[index]; candidates.RemoveAt(index);
                Thing item = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                if (def.category == ThingCategory.Building) item = item.MakeMinified();
                else item.stackCount = Math.Min(def.stackLimit, Rand.RangeInclusive(1, Math.Max(1,
                    Math.Min(100, (int)(2000 / Math.Max(1, item.MarketValue))))));
                yield return item;
            }
        }
    }
}
