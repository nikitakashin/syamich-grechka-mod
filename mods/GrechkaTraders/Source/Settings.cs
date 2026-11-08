using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Grechka.Traders
{
    public sealed class TraderSettings : ModSettings
    {
        public float orbitalFrequency = 1f;
        public float caravanFrequency = 1f;
        public bool customSlaveCount;
        public int slavesPerCaravan = 5;
        public bool specialistsEnabled = true;
        public float discountMultiplier = 1f;

        public void Normalize()
        {
            orbitalFrequency = Clamp(orbitalFrequency, 0f, 5f, 1f);
            caravanFrequency = Clamp(caravanFrequency, 0f, 5f, 1f);
            slavesPerCaravan = Math.Max(0, Math.Min(50, slavesPerCaravan));
            discountMultiplier = Clamp(discountMultiplier, 0f, 2f, 1f);
        }

        private static float Clamp(float value, float min, float max, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));

        public override void ExposeData()
        {
            Scribe_Values.Look(ref orbitalFrequency, "orbitalFrequency", 1f);
            Scribe_Values.Look(ref caravanFrequency, "caravanFrequency", 1f);
            Scribe_Values.Look(ref customSlaveCount, "customSlaveCount", false);
            Scribe_Values.Look(ref slavesPerCaravan, "slavesPerCaravan", 5);
            Scribe_Values.Look(ref specialistsEnabled, "specialistsEnabled", true);
            Scribe_Values.Look(ref discountMultiplier, "discountMultiplier", 1f);
            Normalize();
        }
    }

    public sealed class GrechkaTradersMod : Mod
    {
        public static TraderSettings Settings = new TraderSettings();
        private Vector2 scrollPosition;
        private float contentHeight = 800f;
        public GrechkaTradersMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<TraderSettings>();
            new Harmony("grechka.traders").PatchAll(typeof(GrechkaTradersMod).Assembly);
        }

        public override string SettingsCategory() => Content.Name;
        public override void WriteSettings()
        {
            Settings.Normalize();
            ArrivalControl.ApplySettings();
            base.WriteSettings();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var view = new Rect(0f, 0f, Math.Max(100f, inRect.width - 20f), Math.Max(contentHeight, inRect.height));
            Widgets.BeginScrollView(inRect, ref scrollPosition, view);
            var list = new Listing_Standard();
            list.Begin(new Rect(0f, 0f, view.width, 2000f));
            list.Label("GrechkaTraders_FrequencyHelp".Translate());
            list.Label("GrechkaTraders_Orbital".Translate(Settings.orbitalFrequency.ToString("0.0")));
            Settings.orbitalFrequency = Step(list.Slider(Settings.orbitalFrequency, 0f, 5f), .1f);
            list.Label("GrechkaTraders_Caravans".Translate(Settings.caravanFrequency.ToString("0.0")));
            Settings.caravanFrequency = Step(list.Slider(Settings.caravanFrequency, 0f, 5f), .1f);
            list.Gap();
            list.CheckboxLabeled("GrechkaTraders_CustomSlaves".Translate(), ref Settings.customSlaveCount);
            if (Settings.customSlaveCount)
            {
                list.Label("GrechkaTraders_Slaves".Translate(Settings.slavesPerCaravan));
                Settings.slavesPerCaravan = (int)Math.Round(list.Slider(Settings.slavesPerCaravan, 0f, 50f));
            }
            list.Label("GrechkaTraders_SlaveHelp".Translate());
            list.Gap();
            list.CheckboxLabeled("GrechkaTraders_Specialists".Translate(), ref Settings.specialistsEnabled);
            list.Label("GrechkaTraders_Discount".Translate((Settings.discountMultiplier * 15f).ToString("0")));
            Settings.discountMultiplier = Step(list.Slider(Settings.discountMultiplier, 0f, 2f), 1f / 15f);
            list.Label("GrechkaTraders_DiscountHelp".Translate());
            if (list.ButtonText("GrechkaTraders_Reset".Translate()))
            {
                Settings.orbitalFrequency = Settings.caravanFrequency = Settings.discountMultiplier = 1f;
                Settings.customSlaveCount = false;
                Settings.slavesPerCaravan = 5;
                Settings.specialistsEnabled = true;
            }
            contentHeight = list.CurHeight + 12f;
            list.End();
            Widgets.EndScrollView();
            Settings.Normalize();
            ArrivalControl.ApplySettings();
        }
        private static float Step(float value, float step) => (float)Math.Round(value / step) * step;
    }
}
