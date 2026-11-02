using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Grechka.Storage
{
    public sealed class StockGenerator_DataSalvagers : StockGenerator
    {
        public override bool HandlesThingDef(ThingDef thingDef) => typeof(StorageDisk).IsAssignableFrom(thingDef.thingClass);
        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            string[] sizes = { "1K", "4K", "16K", "64K", "256K", "512K", "1M" };
            for (int i = 0; i < 4; i++)
            {
                ThingDef diskDef = DefDatabase<ThingDef>.GetNamedSilentFail("Grechka_Disk_" + sizes.RandomElement() + "_Item");
                if (diskDef == null) continue;
                StorageDisk disk = (StorageDisk)ThingMaker.MakeThing(diskDef);
                float roll = Rand.Value;
                string[] pool = roll < .60f ? new[] { "Steel", "WoodLog", "Cloth", "Potatoes", "BlocksGranite" }
                    : roll < .90f ? new[] { "ComponentIndustrial", "ComponentSpacer", "Plasteel", "Uranium", "Grechka_QuartzProcessor" }
                    : roll < .99f ? new[] { "BionicArm", "BionicLeg", "MedicineIndustrial", "Gun_ChargeRifle", "Grechka_ComponentT4_Item" }
                    : new[] { "Gold", "Silver", "Grechka_ComponentT5_Item", "ArchotechEye", "ArchotechArm" };
                var staging = new ThingOwner<Thing>(disk, false, LookMode.Deep);
                foreach (string name in pool.InRandomOrder().Take(Rand.RangeInclusive(2, 5)))
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    if (def == null || def.MadeFromStuff) continue;
                    Thing item = ThingMaker.MakeThing(def);
                    item.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
                    if (DiskItemPolicy.Problem(item) != null) { item.Destroy(); continue; }
                    int max = disk.Acceptable(item);
                    int count = def.stackLimit == 1 ? 1 : Rand.RangeInclusive(20, roll < .6f ? 2000 : roll < .9f ? 100 : 30);
                    item.stackCount = Math.Min(max, count);
                    if (item.stackCount <= 0) { item.Destroy(); continue; }
                    staging.TryAdd(item, false); disk.Accept(item, item.stackCount);
                }
                staging.ClearAndDestroyContents();
                disk.SealedContents = true;
                yield return disk;
            }
        }
    }
}
