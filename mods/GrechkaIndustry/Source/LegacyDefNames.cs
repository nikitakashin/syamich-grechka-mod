using System;
using System.Collections.Generic;

namespace Grechka.Industry
{
    public static class LegacyDefNames
    {
        private static readonly string[] Prefixes = { "Blueprint_Install_", "Blueprint_", "Frame_" };
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Grechka_AssemblyBenchT1", "Grechka_AssemblyBenchT1_Building" },
            { "Grechka_AssemblyBenchT2", "Grechka_AssemblyBenchT2_Building" },
            { "Grechka_AssemblyBenchT3", "Grechka_AssemblyBenchT3_Building" },
            { "Grechka_ComponentT3", "Grechka_ComponentT3_Item" },
            { "Grechka_HousingT1", "Grechka_HousingT1_Item" },
            { "Grechka_HousingT2", "Grechka_HousingT2_Item" },
            { "Grechka_HousingT3", "Grechka_HousingT3_Item" },
            { "Grechka_MechaniteExtractorT3", "Grechka_MechaniteExtractorT3_Building" },
            { "Grechka_PackedSolarS0", "Grechka_PackedSolarS0_Item" },
            { "Grechka_PackedSolarS1", "Grechka_PackedSolarS1_Item" },
            { "Grechka_PackedSolarS2", "Grechka_PackedSolarS2_Item" },
            { "Grechka_SolarCellT2", "Grechka_SolarCellT2_Item" },
            { "Grechka_SolarS1", "Grechka_SolarS1_Building" },
            { "Grechka_SolarS2", "Grechka_SolarS2_Building" },
        };

        public static string Canonicalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (Names.TryGetValue(name, out string current)) return current;
            foreach (string prefix in Prefixes)
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    return prefix + Canonicalize(name.Substring(prefix.Length));
            return name;
        }
    }
}
