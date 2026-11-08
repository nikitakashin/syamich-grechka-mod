using System;
using UnityEngine;
using Verse;

namespace Grechka.Farms
{
    public sealed class FarmSettings : ModSettings
    {
        public const int DefaultFertilityPercent = 228;
        public const int MinFertilityPercent = 100;
        public const int MaxFertilityPercent = 1000;
        public int fertilityPercent = DefaultFertilityPercent;

        public void Normalize()
        {
            fertilityPercent = Math.Max(MinFertilityPercent, Math.Min(MaxFertilityPercent, fertilityPercent));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref fertilityPercent, "fertilityPercent", DefaultFertilityPercent);
            Normalize();
        }
    }

    public sealed class GrechkaFarmsMod : Mod
    {
        public static FarmSettings Settings = new FarmSettings();

        public GrechkaFarmsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<FarmSettings>();
        }

        public override string SettingsCategory() => Content.Name;

        public override void WriteSettings()
        {
            FarmFertility.ApplySettings();
            base.WriteSettings();
        }

        public override void DoSettingsWindowContents(Rect rect)
        {
            Settings.Normalize();
            var listing = new Listing_Standard();
            listing.Begin(rect);
            listing.Label("GrechkaFarms_Fertility".Translate(Settings.fertilityPercent));
            Settings.fertilityPercent = (int)Math.Round(listing.Slider(Settings.fertilityPercent,
                FarmSettings.MinFertilityPercent, FarmSettings.MaxFertilityPercent));
            listing.Gap();
            listing.Label("GrechkaFarms_Help".Translate());
            listing.Gap();
            if (listing.ButtonText("GrechkaFarms_Reset".Translate()))
                Settings.fertilityPercent = FarmSettings.DefaultFertilityPercent;
            listing.End();
            FarmFertility.ApplySettings();
        }
    }

    [StaticConstructorOnStartup]
    public static class FarmFertility
    {
        public const string TerrainDefName = "Grechka_Farm";

        static FarmFertility() { ApplySettings(); }

        public static void ApplySettings()
        {
            GrechkaFarmsMod.Settings.Normalize();
            var terrain = DefDatabase<TerrainDef>.GetNamedSilentFail(TerrainDefName);
            if (terrain == null) return;
            float fertility = GrechkaFarmsMod.Settings.fertilityPercent / 100f;
            if (terrain.fertility == fertility) return;
            terrain.fertility = fertility;
            // FertilityAt reads the shared def directly. Only the visual overlay is cached.
            if (Current.Game != null)
                foreach (var map in Find.Maps)
                    if (map.fertilityGrid != null) map.fertilityGrid.Drawer.SetDirty();
        }
    }
}
