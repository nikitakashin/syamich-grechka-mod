using HarmonyLib;
using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Verse;

namespace Grechka.WorldIntegration
{
    // XML variants are created before translation injection. Derive their text
    // from the already translated source Def after language data is applied.
    [StaticConstructorOnStartup]
    internal static class VariantLocalization
    {
        private sealed class SourceLabel { public string original, applied; }
        private static readonly ConditionalWeakTable<ThingDef, SourceLabel> sourceLabels = new ConditionalWeakTable<ThingDef, SourceLabel>();
        static VariantLocalization() { Refresh(); }

        internal static void Refresh()
        {
            if (LanguageDatabase.activeLanguage == null) return;
            foreach (VariantEntry entry in ProfileVariants.Entries.Values)
            {
                ThingDef original = DefDatabase<ThingDef>.GetNamedSilentFail(entry.sourceName);
                ThingDef variant = DefDatabase<ThingDef>.GetNamedSilentFail(entry.name);
                if (original == null || variant == null) continue;
                SourceLabel source = sourceLabels.GetValue(original, _ => new SourceLabel());
                if (original.label != source.applied) source.original = original.label;
                string label = source.original ?? original.label;
                string nameKey = "GrechkaWorldIntegration_DeviceName_" + entry.sourceName;
                if (nameKey.CanTranslate()) label = nameKey.Translate().ToString();
                if (entry.name != entry.sourceName)
                {
                    SetLabel(variant, ProfileVariants.IsRetired(entry) ? label : TierLabel(label, entry.tier));
                    variant.description = original.description;
                }
                else if (!ProfileVariants.IsRetired(entry))
                {
                    SetLabel(original, TierLabel(label, entry.tier));
                    source.applied = original.label;
                }
                RecipeDef upgrade = DefDatabase<RecipeDef>.GetNamedSilentFail(entry.UpgradeRecipe);
                if (upgrade == null) continue;
                SetLabel(upgrade, "GrechkaWorldIntegration_UpgradeRecipe".Translate(TierLabel(label, entry.tier + 1)).ToString());
                upgrade.description = WorldText.Get("GrechkaWorldIntegration_UpgradeRecipeDescription", "Upgrade in place. The previous-tier housing and original machine count towards the cost. Material, hit points, fuel and bills are preserved.");
                upgrade.jobString = WorldText.Get("GrechkaWorldIntegration_UpgradeJob", "Upgrading machine.");
            }
        }

        private static void SetLabel(Def def, string label)
        {
            if (def.label == label) return;
            def.label = label;
            def.ClearCachedData();
        }

        internal static string TierLabel(string label, int tier)
        {
            if (tier < 3) return label;
            string untiered = Regex.Replace(label, @"\s*\(?[TТ][1-5]\)?$", "");
            if (untiered != label)
                label = Regex.Replace(untiered, @"^(?:меха-|архо-|гречнев(?:ый|ая|ое|ые) |mecha[ -]|archo[ -]|grechka )", "", RegexOptions.IgnoreCase);
            string theme = Theme(tier, label);
            string separator = theme.EndsWith("-", StringComparison.Ordinal) ? "" : " ";
            return theme + separator + label + " (T" + tier + ")";
        }

        private static string Theme(int tier, string label)
        {
            string key = "GrechkaWorldIntegration_TierName" + tier;
            if (tier != 5) return key.Translate().ToString();
            // Agree with the first noun/adjective of the already translated source.
            string first = (label ?? "").Split(' ')[0].ToLowerInvariant();
            string gender = first.EndsWith("ые") || first.EndsWith("ы") || first.EndsWith("и") ? "Plural"
                : first.EndsWith("ая") || first.EndsWith("яя") || first.EndsWith("а") || first.EndsWith("я") || first.EndsWith("ость") ? "Feminine"
                : first.EndsWith("ое") || first.EndsWith("ее") || first.EndsWith("о") || first.EndsWith("е") ? "Neuter" : "";
            return (key + gender).Translate().ToString();
        }
    }

    [HarmonyPatch(typeof(LoadedLanguage), nameof(LoadedLanguage.InjectIntoData_AfterImpliedDefs))]
    internal static class VariantLanguageInjectionHook
    {
        private static void Postfix() { VariantLocalization.Refresh(); }
    }
}
