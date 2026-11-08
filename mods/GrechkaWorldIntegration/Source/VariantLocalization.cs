using HarmonyLib;
using Verse;

namespace Grechka.WorldIntegration
{
    // XML variants are created before translation injection. Derive their text
    // from the already translated source Def after language data is applied.
    [StaticConstructorOnStartup]
    internal static class VariantLocalization
    {
        static VariantLocalization() { Refresh(); }

        internal static void Refresh()
        {
            if (LanguageDatabase.activeLanguage == null) return;
            foreach (VariantEntry entry in ProfileVariants.Entries.Values)
            {
                ThingDef original = DefDatabase<ThingDef>.GetNamedSilentFail(entry.sourceName);
                ThingDef variant = DefDatabase<ThingDef>.GetNamedSilentFail(entry.name);
                if (original == null || variant == null) continue;
                if (entry.name != entry.sourceName)
                {
                    variant.label = (ProfileVariants.IsRetired(entry)
                        ? "GrechkaWorldIntegration_LegacyVariant"
                        : "GrechkaWorldIntegration_TierVariant").Translate(original.label, entry.tier).ToString();
                    variant.description = original.description;
                }
                RecipeDef upgrade = DefDatabase<RecipeDef>.GetNamedSilentFail(entry.UpgradeRecipe);
                if (upgrade == null) continue;
                upgrade.label = "GrechkaWorldIntegration_UpgradeRecipe".Translate(entry.tier + 1).ToString();
                upgrade.description = WorldText.Get("GrechkaWorldIntegration_UpgradeRecipeDescription", "Upgrade in place. The previous-tier housing and original machine count towards the cost. Material, hit points, fuel and bills are preserved.");
                upgrade.jobString = WorldText.Get("GrechkaWorldIntegration_UpgradeJob", "Upgrading machine.");
            }
        }
    }

    [HarmonyPatch(typeof(LoadedLanguage), nameof(LoadedLanguage.InjectIntoData_AfterImpliedDefs))]
    internal static class VariantLanguageInjectionHook
    {
        private static void Postfix() { VariantLocalization.Refresh(); }
    }
}
