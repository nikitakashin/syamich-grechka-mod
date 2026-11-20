using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;

namespace Grechka.WorldIntegration
{
    // Read the active profile at the native battery boundary. Existing batteries and
    // in-place upgrades share this path without changing their saved storedPower.
    [HarmonyPatch(typeof(CompPowerBattery), "get_Props")]
    public static class BatteryTierPropertiesHook
    {
        private static readonly ConditionalWeakTable<CompProperties_Battery, Dictionary<int, CompProperties_Battery>> copies
            = new ConditionalWeakTable<CompProperties_Battery, Dictionary<int, CompProperties_Battery>>();
        private static readonly MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");
        private static readonly float[] Efficiency = { 0, 0, .5f, .7f, .85f, .95f };

        public static void Postfix(CompPowerBattery __instance, ref CompProperties_Battery __result)
        {
            int tier = WorldProfile.GetTier(__instance.parent);
            VariantEntry entry = ProfileVariants.Entry(__instance.parent?.def);
            if (__result == null || entry == null || tier < entry.minimum || tier > 5
                || ProfileVariants.IsRetired(entry)) return;
            int steps = tier - entry.minimum;
            float capacity = __result.storedEnergyMax * (float)Math.Pow(8, steps);
            float efficiency = Math.Max(__result.efficiency, Efficiency[tier]);
            if (capacity == __result.storedEnergyMax && efficiency == __result.efficiency) return;

            Dictionary<int, CompProperties_Battery> variants = copies.GetOrCreateValue(__result);
            int key = tier * 10 + entry.minimum;
            if (!variants.TryGetValue(key, out CompProperties_Battery properties)
                || properties.storedEnergyMax != capacity || properties.efficiency != efficiency)
            {
                properties = (CompProperties_Battery)Clone.Invoke(__result, null);
                properties.storedEnergyMax = capacity;
                properties.efficiency = efficiency;
                variants[key] = properties;
            }
            __result = properties;
        }
    }
}
