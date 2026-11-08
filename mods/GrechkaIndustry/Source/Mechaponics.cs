using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Industry
{
    public sealed class MechaponicsProperties : DefModExtension
    {
        public float growthMultiplier = 5f;
    }

    // Keep vanilla plant selection, sowing, harvesting and serialization.
    public sealed class Building_Mechaponics : Building_PlantGrower
    {
        private CompPowerTrader power;
        private CompFlickable flick;
        private CompBreakdownable breakdown;

        public bool Powered { get { return power != null && power.PowerOn
            && (flick == null || flick.SwitchIsOn) && (breakdown == null || !breakdown.BrokenDown); } }
        public float GrowthMultiplier { get { return def.GetModExtension<MechaponicsProperties>()?.growthMultiplier ?? 1f; } }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            power = GetComp<CompPowerTrader>();
            flick = GetComp<CompFlickable>();
            breakdown = GetComp<CompBreakdownable>();
        }

        public override void TickRare()
        {
            // In 1.7, Building inherits ThingWithComps.TickRare, which ticks only
            // comps. PlantGrower's override additionally rots crops without power.
            // Retain component ticks while omitting that hydroponic-only damage.
            foreach (ThingComp comp in AllComps) comp.CompTickRare();
        }

        public override string GetInspectString()
        {
            var result = new StringBuilder(base.GetInspectString());
            if (result.Length > 0) result.AppendLine();
            result.Append(Powered ? IndustryText.Get("GrechkaIndustry_e039ffb8b453", "Mechaponics growth ×") + GrowthMultiplier.ToString("0.##")
                : IndustryText.Get("GrechkaIndustry_a3ff1280eb4b", "Mechaponics growth paused"));
            return result.ToString();
        }

        internal static Building_Mechaponics For(Plant plant)
        {
            if (plant == null || !plant.Spawned) return null;
            var things = plant.Position.GetThingList(plant.Map);
            for (int i = 0; i < things.Count; i++)
                if (things[i] is Building_Mechaponics basin) return basin;
            return null;
        }
    }

    [HarmonyPatch(typeof(Building_PlantGrower), nameof(Building_PlantGrower.CanAcceptSowNow))]
    internal static class MechaponicsSowingHook
    {
        private static void Postfix(Building_PlantGrower __instance, ref bool __result)
        {
            if (__instance is Building_Mechaponics basin) __result = __result && basin.Powered;
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.GrowthRateFactor_Light), MethodType.Getter)]
    internal static class MechaponicsLightHook
    {
        private static void Postfix(Plant __instance, ref float __result)
        {
            Building_Mechaponics basin = Building_Mechaponics.For(__instance);
            if (basin != null) __result = basin.Powered ? 1f : 0f;
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.GrowthRate), MethodType.Getter)]
    internal static class MechaponicsGrowthHook
    {
        private static void Postfix(Plant __instance, ref float __result)
        {
            Building_Mechaponics basin = Building_Mechaponics.For(__instance);
            if (basin != null) __result = basin.Powered ? __result * basin.GrowthMultiplier : 0f;
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.GrowthRateCalcDesc), MethodType.Getter)]
    internal static class MechaponicsGrowthDescriptionHook
    {
        private static void Postfix(Plant __instance, ref string __result)
        {
            Building_Mechaponics basin = Building_Mechaponics.For(__instance);
            if (basin != null)
                __result += basin.Powered ? IndustryText.Get("GrechkaIndustry_27973df0c9f9", "\nMechaponics ×") + basin.GrowthMultiplier.ToString("0.##")
                    : IndustryText.Get("GrechkaIndustry_14d9dd5218a6", "\nMechaponics has no power or is switched off");
        }
    }
}
