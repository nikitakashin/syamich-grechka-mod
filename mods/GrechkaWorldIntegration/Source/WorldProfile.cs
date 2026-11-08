using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.WorldIntegration
{
    public sealed class MachineProfileRuleDef : Def
    {
        public string packageId;
        public string targetDefName;
        public int minimumTier;
        public bool addHousing;
        public bool retired;
        [MustTranslate] public string note;
        public List<NamedCost> replacementCost;
        public List<string> previousSignatures;
        public string Signature => packageId.ToLowerInvariant() + "/" + targetDefName + "/" + minimumTier + "/" + addHousing
            + (replacementCost == null ? "" : "/" + string.Join(";", replacementCost.Select(c => c.thingDefName + "=" + c.count)));
    }

    public sealed class NamedCost { public string thingDefName; public int count; }

    public sealed class MaterialMember
    {
        public string packageId;
        public string thingDefName;
        public string Key => packageId.ToLowerInvariant() + "/" + thingDefName;
    }

    public sealed class MaterialGroupDef : Def
    {
        public string form;
        public List<MaterialMember> members = new List<MaterialMember>();
    }

    public sealed class RecipeMaterialRuleDef : Def
    {
        public string packageId;
        public string recipeDefName;
        public int ingredientIndex;
        public string materialGroup;
    }

    /// <summary>Explicit 1:1 groups. Names and translated labels are never used for equivalence.</summary>
    public static class MaterialDictionary
    {
        public static bool BelongsTo(Def def, string packageId)
            => def?.modContentPack != null && string.Equals(def.modContentPack.PackageIdPlayerFacing,
                packageId, StringComparison.OrdinalIgnoreCase);

        public static List<ThingDef> Resolve(MaterialGroupDef group, ISet<string> approved = null)
        {
            var result = new List<ThingDef>();
            foreach (MaterialMember member in group.members)
            {
                if (approved != null && !approved.Contains(group.defName + ":" + member.Key)) continue;
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(member.thingDefName);
                if (BelongsTo(def, member.packageId) && !result.Contains(def)) result.Add(def);
            }
            return result;
        }

        public static bool Apply(RecipeDef recipe, int index, List<ThingDef> members, out string error)
        {
            error = null;
            if (recipe == null || index < 0 || index >= recipe.ingredients.Count || members == null || members.Count == 0)
            { error = WorldText.Get("GrechkaWorldIntegration_5356d44c4c84", "recipe, ingredient position or available material is missing"); return false; }
            // Count recipes can mix 40+60 physical units. Nutrition/value recipes need their own adapter.
            if (!(recipe.IngredientValueGetter is IngredientValueGetter_Volume))
            { error = WorldText.Get("GrechkaWorldIntegration_6e0e1c1d9ff3", "unsupported ingredient unit: ") + recipe.IngredientValueGetter.GetType().Name; return false; }
            IngredientCount ingredient = recipe.ingredients[index];
            ThingDef old = ingredient.filter.AllowedThingDefs.FirstOrDefault();
            if (old == null) { error = WorldText.Get("GrechkaWorldIntegration_ae1fef0a2fb7", "the original filter is empty"); return false; }
            float value = recipe.IngredientValueGetter.ValuePerUnitOf(old);
            if (members.Any(d => Math.Abs(recipe.IngredientValueGetter.ValuePerUnitOf(d) - value) > 0.00001f))
            { error = WorldText.Get("GrechkaWorldIntegration_ed9cca862246", "different physical units require separate forms or recipes"); return false; }
            var filter = new ThingFilter();
            foreach (ThingDef member in members)
            {
                filter.SetAllow(member, true); recipe.fixedIngredientFilter.SetAllow(member, true);
                // Existing bills own their filter; only defaults for subsequently created bills expand.
                recipe.defaultIngredientFilter?.SetAllow(member, true);
            }
            ingredient.filter = filter;
            recipe.allowMixingIngredients = true;
            return true;
        }
    }

    [StaticConstructorOnStartup]
    public static class WorldProfile
    {
        private sealed class RecipeBaseline
        {
            public RecipeDef recipe;
            public bool mixing;
            public ThingFilter fixedFilter;
            public ThingFilter defaultFilter;
            public List<ThingFilter> filters;
        }
        private static readonly Dictionary<ThingDef, List<ThingDefCountClass>> costs = new Dictionary<ThingDef, List<ThingDefCountClass>>();
        private static readonly Dictionary<ThingDef, CostListForDifficulty> difficultyCosts = new Dictionary<ThingDef, CostListForDifficulty>();
        private static readonly List<RecipeBaseline> recipes = new List<RecipeBaseline>();
        public static readonly List<string> Report = new List<string>();
        public static readonly Dictionary<string, int> AppliedTiers = new Dictionary<string, int>();
        public static int GetTier(Thing thing) => thing != null && AppliedTiers.TryGetValue(thing.def.defName, out int tier) ? tier : 0;
        public static int GetMinimumTier(ThingDef def) => def != null && AppliedTiers.TryGetValue(def.defName, out int tier) ? (ProfileVariants.Entry(def)?.minimum ?? tier) : 0;
        private static readonly FieldInfo StatWorkerCache = AccessTools.Field(typeof(StatDef),"workerInt");
        public static void InvalidateThingStats(Thing thing)
        {
            if(thing==null)return;
            // Only existing workers: avoid instantiating unrelated mod stat workers during an upgrade.
            foreach(StatDef stat in DefDatabase<StatDef>.AllDefsListForReading)
                if(StatWorkerCache.GetValue(stat) is StatWorker worker)worker.ClearCacheForThing(thing);
        }
        public static string OriginalDefName(ThingDef def) => ProfileVariants.Entry(def)?.sourceName ?? def?.defName;
        public static bool IsApplied(ThingDef def) => def != null && AppliedTiers.ContainsKey(def.defName);
        static WorldProfile() { Capture(); }
        private static ThingFilter Copy(ThingFilter source) { var copy = new ThingFilter(); if (source != null) copy.CopyAllowancesFrom(source); return copy; }
        private static List<ThingDefCountClass> Copy(List<ThingDefCountClass> source)
            => source?.Select(x => new ThingDefCountClass(x.thingDef, x.count)).ToList() ?? new List<ThingDefCountClass>();
        private static CostListForDifficulty CopyDifficultyCost(CostListForDifficulty source)
            => source == null ? null : new CostListForDifficulty { difficultyVar = source.difficultyVar,
                invert = source.invert, costStuffCount = source.costStuffCount, costList = Copy(source.costList) };

        public static void Capture()
        {
            foreach (MachineProfileRuleDef rule in DefDatabase<MachineProfileRuleDef>.AllDefsListForReading)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(rule.targetDefName);
                if (MaterialDictionary.BelongsTo(def, rule.packageId) && !costs.ContainsKey(def))
                {
                    costs.Add(def, Copy(def.costList));
                    difficultyCosts.Add(def, CopyDifficultyCost(def.costListForDifficulty));
                }
            }
            foreach (RecipeMaterialRuleDef rule in DefDatabase<RecipeMaterialRuleDef>.AllDefsListForReading)
            {
                RecipeDef def = DefDatabase<RecipeDef>.GetNamedSilentFail(rule.recipeDefName);
                if (MaterialDictionary.BelongsTo(def, rule.packageId) && !recipes.Any(r => r.recipe == def))
                    recipes.Add(new RecipeBaseline { recipe = def, mixing = def.allowMixingIngredients,
                        fixedFilter = Copy(def.fixedIngredientFilter), defaultFilter = def.defaultIngredientFilter == null ? null : Copy(def.defaultIngredientFilter),
                        filters = def.ingredients.Select(i => Copy(i.filter)).ToList() });
            }
        }

        public static List<ThingDefCountClass> BaselineCosts(ThingDef def) => Copy(costs.TryGetValue(def, out var saved) ? saved : def.costList);
        public static CostListForDifficulty BaselineDifficultyCosts(ThingDef def)
            => CopyDifficultyCost(difficultyCosts.TryGetValue(def, out var saved) ? saved : def.costListForDifficulty);

        // BuildableDef.CostList chooses this branch instead of costList when its
        // difficulty condition applies. Keep both prices tiered without flattening
        // the native barrel/stuff rules or sharing mutable costs between variants.
        public static void ApplyDifficultyHousing(ThingDef target, ThingDef original, ThingDef housing)
        {
            target.costListForDifficulty = BaselineDifficultyCosts(original);
            if (target.costListForDifficulty != null
                && !target.costListForDifficulty.costList.Any(c => c.thingDef == housing))
                target.costListForDifficulty.costList.Add(new ThingDefCountClass(housing, 1));
        }

        public static void RestoreBaseline()
        {
            TierRecipeInheritance.Clear();
            TierArtwork.Restore();
            foreach (var pair in costs)
            {
                pair.Key.costList = Copy(pair.Value);
                pair.Key.costListForDifficulty = BaselineDifficultyCosts(pair.Key);
            }
            foreach (RecipeBaseline baseline in recipes)
            {
                baseline.recipe.fixedIngredientFilter = Copy(baseline.fixedFilter);
                baseline.recipe.defaultIngredientFilter = baseline.defaultFilter == null ? null : Copy(baseline.defaultFilter);
                baseline.recipe.allowMixingIngredients = baseline.mixing;
                for (int i = 0; i < baseline.filters.Count && i < baseline.recipe.ingredients.Count; i++) baseline.recipe.ingredients[i].filter = Copy(baseline.filters[i]);
            }
            ProfileVariants.Restore(); CostListCalculator.Reset(); AppliedTiers.Clear(); Report.Clear();
            FamilyWorkDiscovery.Refresh();
            RefreshRelatedMarketValues();
        }

        public static void Apply(WorldProfileState state, bool acceptCurrent)
        {
            if (state.ownerGame != null && !ReferenceEquals(state.ownerGame, Current.Game)) return;
            ResearchTree.SyncRequirements();
            RestoreBaseline();
            if (acceptCurrent)
            {
                state.approvedMachines.Clear(); state.approvedMaterials.Clear(); state.approvedRecipes.Clear();
                foreach (MachineProfileRuleDef rule in DefDatabase<MachineProfileRuleDef>.AllDefsListForReading)
                    if (!rule.retired && MaterialDictionary.BelongsTo(DefDatabase<ThingDef>.GetNamedSilentFail(rule.targetDefName), rule.packageId)
                        && (!rule.addHousing || MaterialDictionary.BelongsTo(DefDatabase<ThingDef>.GetNamedSilentFail("Grechka_HousingT" + rule.minimumTier + "_Item"), "grechka.industry"))
                        && (rule.replacementCost == null || rule.replacementCost.All(c => DefDatabase<ThingDef>.GetNamedSilentFail(c.thingDefName) != null))) state.approvedMachines.Add(rule.Signature);
                foreach (MaterialGroupDef group in DefDatabase<MaterialGroupDef>.AllDefsListForReading)
                    foreach (MaterialMember member in group.members)
                        if (MaterialDictionary.BelongsTo(DefDatabase<ThingDef>.GetNamedSilentFail(member.thingDefName), member.packageId)) state.approvedMaterials.Add(group.defName + ":" + member.Key);
                foreach (RecipeMaterialRuleDef rule in DefDatabase<RecipeMaterialRuleDef>.AllDefsListForReading)
                    if (MaterialDictionary.BelongsTo(DefDatabase<RecipeDef>.GetNamedSilentFail(rule.recipeDefName), rule.packageId)) state.approvedRecipes.Add(RecipeKey(rule));
                state.applied = true;
            }
            if (!state.applied) { Report.Add(WorldText.Get("GrechkaWorldIntegration_9b9fdcfe0d7d", "The profile has not been applied to this colony yet. Original costs and materials are preserved.")); return; }
            MigrateBalanceApprovals(state);
            var accepted = new HashSet<string>(state.approvedMachines);
            foreach (MachineProfileRuleDef rule in DefDatabase<MachineProfileRuleDef>.AllDefsListForReading)
            {
                if (rule.retired) { Report.Add(rule.defName + WorldText.Get("GrechkaWorldIntegration_f7189d87e985", ": using original behavior. Previous variants are retained for old saves")); continue; }
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(rule.targetDefName);
                if (!MaterialDictionary.BelongsTo(def, rule.packageId)) { Report.Add(rule.defName + WorldText.Get("GrechkaWorldIntegration_86fca31dd7c4", ": source is missing")); continue; }
                if (!accepted.Contains(rule.Signature)) { Report.Add(rule.defName + WorldText.Get("GrechkaWorldIntegration_716ee9cebafa", ": explicit application of the new rule is required")); continue; }
                if (rule.addHousing)
                {
                    ThingDef housing = DefDatabase<ThingDef>.GetNamedSilentFail("Grechka_HousingT" + rule.minimumTier + "_Item");
                    if (housing == null) { Report.Add(rule.defName + WorldText.Get("GrechkaWorldIntegration_476b62b92f2f", ": Industry or housing is missing. Original cost preserved")); continue; }
                    if (rule.replacementCost != null && rule.replacementCost.Any(c => DefDatabase<ThingDef>.GetNamedSilentFail(c.thingDefName) == null))
                    { Report.Add(rule.defName + WorldText.Get("GrechkaWorldIntegration_9c81309268b0", ": cost material is missing. Original cost preserved")); continue; }
                    def.costList = rule.replacementCost == null ? Copy(costs[def]) : rule.replacementCost
                        .Select(c => new ThingDefCountClass(DefDatabase<ThingDef>.GetNamed(c.thingDefName), c.count)).ToList();
                    if (!def.costList.Any(c => c.thingDef == housing)) def.costList.Add(new ThingDefCountClass(housing, 1));
                    ApplyDifficultyHousing(def, def, housing);
                    Report.Add(rule.defName + ": " + def.defName + WorldText.Get("GrechkaWorldIntegration_9efbd0388542", " + 1 housing T") + rule.minimumTier);
                }
                else Report.Add(rule.defName + WorldText.Get("GrechkaWorldIntegration_3654d1a8d30f", ": original cost. ") + rule.note);
                if (rule.minimumTier > 0) AppliedTiers[def.defName] = rule.minimumTier;
            }
            var materialApproval = new HashSet<string>(state.approvedMaterials);
            foreach (RecipeMaterialRuleDef rule in DefDatabase<RecipeMaterialRuleDef>.AllDefsListForReading)
            {
                RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(rule.recipeDefName);
                MaterialGroupDef group = DefDatabase<MaterialGroupDef>.GetNamedSilentFail(rule.materialGroup);
                if (!MaterialDictionary.BelongsTo(recipe, rule.packageId) || group == null) { Report.Add(rule.defName + WorldText.Get("GrechkaWorldIntegration_f23b4e8d9e0e", ": recipe or group is missing")); continue; }
                if (!state.approvedRecipes.Contains(RecipeKey(rule))) { Report.Add(rule.defName + WorldText.Get("GrechkaWorldIntegration_c43ef190d9f6", ": explicit application required")); continue; }
                string error;
                var members = MaterialDictionary.Resolve(group, materialApproval);
                Report.Add(rule.defName + ": " + (MaterialDictionary.Apply(recipe, rule.ingredientIndex, members, out error)
                    ? WorldText.Get("GrechkaWorldIntegration_3f66c9d84cf2", "allowed ") + string.Join(", ", members.Select(m => m.defName)) : WorldText.Get("GrechkaWorldIntegration_586870b0b280", "not applied: ") + error));
            }
            ProfileVariants.Apply(state, acceptCurrent);
            TierArtwork.Apply();
            FamilyWorkDiscovery.Refresh();
            CostListCalculator.Reset();
            RefreshRelatedMarketValues();
            Log.Message(WorldText.Get("GrechkaWorldIntegration_8c9f3d7d8335", "[Grechka World] Profile: ") + AppliedTiers.Count + WorldText.Get("GrechkaWorldIntegration_dec7488fd72a", " machines with minimum tier. Detailed report in settings."));
        }

        private static string RecipeKey(RecipeMaterialRuleDef r) => r.packageId + "/" + r.recipeDefName + "/" + r.ingredientIndex + "/" + r.materialGroup;

        private static void RefreshRelatedMarketValues()
        {
            Type matter = AccessTools.TypeByName("Grechka.Industry.MatterCraftValue");
            if (matter != null) AccessTools.Method(matter, "Invalidate")?.Invoke(null, null);
            Type market = AccessTools.TypeByName("Grechka.LuckyBlocks.LuckyMarketValues");
            if (market == null) return;
            try { AccessTools.Method(market, "Refresh")?.Invoke(null, null); }
            catch (Exception error) { Log.Warning("[Grechka World] Optional reward price refresh deferred: " + error.GetBaseException().Message); }
        }

        // Only exact, explicitly listed prior prices inherit the existing approval.
        // Unknown/new rules still require the normal explicit profile application.
        internal static void MigrateBalanceApprovals(WorldProfileState state)
        {
            if (!state.applied) return;
            foreach (MachineProfileRuleDef rule in DefDatabase<MachineProfileRuleDef>.AllDefsListForReading)
            {
                if (rule.retired || rule.previousSignatures == null
                    || !MaterialDictionary.BelongsTo(DefDatabase<ThingDef>.GetNamedSilentFail(rule.targetDefName), rule.packageId)
                    || rule.replacementCost?.Any(c => DefDatabase<ThingDef>.GetNamedSilentFail(c.thingDefName) == null) == true) continue;
                foreach (string previous in rule.previousSignatures)
                    for (int tier = 0; tier <= 5; tier++)
                    {
                        string suffix = tier == 0 ? "" : "/variantT" + tier;
                        string current = rule.Signature + suffix;
                        if (state.approvedMachines.Contains(previous + suffix) && !state.approvedMachines.Contains(current))
                            state.approvedMachines.Add(current);
                    }
            }
        }
    }

    public sealed class WorldProfileState : GameComponent
    {
        public bool applied;
        public string lastBackup;
        public List<string> approvedMachines = new List<string>();
        public List<string> approvedMaterials = new List<string>();
        public List<string> approvedRecipes = new List<string>();
        internal readonly Game ownerGame;
        public WorldProfileState(Game game) { ownerGame = game; WorldProfile.RestoreBaseline(); }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref applied, "grechkaWorldApplied");
            Scribe_Values.Look(ref lastBackup, "grechkaWorldLastBackup");
            Scribe_Collections.Look(ref approvedMachines, "grechkaWorldMachines", LookMode.Value);
            Scribe_Collections.Look(ref approvedMaterials, "grechkaWorldMaterials", LookMode.Value);
            Scribe_Collections.Look(ref approvedRecipes, "grechkaWorldRecipes", LookMode.Value);
            approvedMachines ??= new List<string>(); approvedMaterials ??= new List<string>(); approvedRecipes ??= new List<string>();
        }
        public override void StartedNewGame() { WorldProfile.Apply(this, true); }
        public override void LoadedGame() { WorldProfile.Apply(this, false); }
    }
}
