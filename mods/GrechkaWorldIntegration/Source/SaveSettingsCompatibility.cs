using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.WorldIntegration
{
    public static class SaveSettingsCompatibility
    {
        public static MethodBase OptionalMethod(string typeName, string name)
        {
            Type type = AccessTools.TypeByName(typeName);
            return type == null ? null : AccessTools.Method(type, name);
        }
        // Preserve KV's own canonical groups, including fuelled/electric versions.
        public static string Key(string name)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
            if (TierMachineFamilies.TryDescribe(def, out string family, out _)) name = family;
            switch (name)
            {
                case "ButcherSpot": case "TableButcher": return "Butcher";
                case "HandTailoringBench": case "ElectricTailoringBench": return "TailoringBench";
                case "FueledSmithy": case "ElectricSmithy": return "Smithy";
                case "FueledStove": case "ElectricStove": return "Stove";
                default: return name;
            }
        }

        public static void AddLegacyFiles(string key, List<FileInfo> files, string root)
        {
            var seen = new HashSet<string>(files.Select(f => f.FullName), StringComparer.Ordinal);
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!TierMachineFamilies.TryDescribe(def, out _, out _) || Key(def.defName) != key
                    || def.defName.IndexOfAny(new[] { '/', '\\' }) >= 0 || def.defName == key) continue;
                string directory = Path.Combine(root, def.defName);
                if (!Directory.Exists(directory)) continue;
                foreach (string path in Directory.GetFiles(directory))
                    if (seen.Add(Path.GetFullPath(path))) files.Add(new FileInfo(path));
            }
        }

        // Called only within KV's crafting-load operation. Its own parser retains
        // research checks, bill filters, counts, custom names and optional integrations.
        public static void FilterForTarget(BillStack stack, List<Bill> bills)
        {
            if (!(stack?.billGiver is Thing giver) || !TierMachineFamilies.TryDescribe(giver.def, out _, out _) || bills == null) return;
            var available = new HashSet<RecipeDef>(giver.def.AllRecipes);
            bills.RemoveAll(b => b?.recipe == null || !available.Contains(b.recipe));
        }
    }

    [HarmonyPatch]
    public static class SaveSettingsFamilyHook
    {
        static MethodBase TargetMethod() => SaveSettingsCompatibility.OptionalMethod("SaveStorageSettings.Patch_Building_GetGizmos", "GetTypeIBillGiver");
        static bool Prepare() => TargetMethod() != null;
        public static void Prefix(ref string defName) => defName = SaveSettingsCompatibility.Key(defName);
    }

    [HarmonyPatch]
    public static class SaveSettingsLegacyFilesHook
    {
        static MethodBase TargetMethod() => SaveSettingsCompatibility.OptionalMethod("SaveStorageSettings.Dialog.FileListDialog", "ReloadFiles");
        static bool Prepare() => TargetMethod() != null;
        public static void Postfix(object __instance, string ___StorageTypeName, List<FileInfo> ___files)
        {
            string type = __instance.GetType().FullName;
            if (type != "SaveStorageSettings.Dialog.LoadCraftingDialog" && type != "SaveStorageSettings.Dialog.SaveCraftingDialog") return;
            SaveSettingsCompatibility.AddLegacyFiles(___StorageTypeName, ___files,
                Path.Combine(GenFilePaths.SaveDataFolderPath, "SaveStorageSettings"));
        }
    }

    // The context ends even if KV's loader throws; unrelated policy and operation
    // dialogs never inherit a previous workbench's recipe restrictions.
    [HarmonyPatch]
    public static class SaveSettingsLoadScopeHook
    {
        [ThreadStatic] public static BillStack CurrentStack;
        static MethodBase TargetMethod() => SaveSettingsCompatibility.OptionalMethod("SaveStorageSettings.Dialog.LoadCraftingDialog", "DoFileInteraction");
        static bool Prepare() => TargetMethod() != null;
        public static void Prefix(BillStack ___BillStack, out BillStack __state)
        { __state = CurrentStack; CurrentStack = ___BillStack; }
        public static Exception Finalizer(Exception __exception, BillStack __state)
        { CurrentStack = __state; return __exception; }
    }

    [HarmonyPatch]
    public static class SaveSettingsLoadedBillsHook
    {
        static MethodBase TargetMethod() => SaveSettingsCompatibility.OptionalMethod("SaveStorageSettings.IOUtil", "LoadCraftingBills");
        static bool Prepare() => TargetMethod() != null;
        public static void Postfix(List<Bill> __result)
            => SaveSettingsCompatibility.FilterForTarget(SaveSettingsLoadScopeHook.CurrentStack, __result);
    }
}
