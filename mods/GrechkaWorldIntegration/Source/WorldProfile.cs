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
        public string note;
        public List<NamedCost> replacementCost;
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
            { error = "нет рецепта, позиции или доступного материала"; return false; }
            // Count recipes can mix 40+60 physical units. Nutrition/value recipes need their own adapter.
            if (!(recipe.IngredientValueGetter is IngredientValueGetter_Volume))
            { error = "неподдерживаемая единица ингредиента: " + recipe.IngredientValueGetter.GetType().Name; return false; }
            IngredientCount ingredient = recipe.ingredients[index];
            ThingDef old = ingredient.filter.AllowedThingDefs.FirstOrDefault();
            if (old == null) { error = "исходный фильтр пуст"; return false; }
            float value = recipe.IngredientValueGetter.ValuePerUnitOf(old);
            if (members.Any(d => Math.Abs(recipe.IngredientValueGetter.ValuePerUnitOf(d) - value) > 0.00001f))
            { error = "разная физическая единица: требуются отдельные формы/рецепты"; return false; }
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

        public static void Capture()
        {
            foreach (MachineProfileRuleDef rule in DefDatabase<MachineProfileRuleDef>.AllDefsListForReading)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(rule.targetDefName);
                if (MaterialDictionary.BelongsTo(def, rule.packageId) && !costs.ContainsKey(def)) costs.Add(def, Copy(def.costList));
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

        public static void RestoreBaseline()
        {
            foreach (var pair in costs) pair.Key.costList = Copy(pair.Value);
            foreach (RecipeBaseline baseline in recipes)
            {
                baseline.recipe.fixedIngredientFilter = Copy(baseline.fixedFilter);
                baseline.recipe.defaultIngredientFilter = baseline.defaultFilter == null ? null : Copy(baseline.defaultFilter);
                baseline.recipe.allowMixingIngredients = baseline.mixing;
                for (int i = 0; i < baseline.filters.Count && i < baseline.recipe.ingredients.Count; i++) baseline.recipe.ingredients[i].filter = Copy(baseline.filters[i]);
            }
            ProfileVariants.Restore(); CostListCalculator.Reset(); AppliedTiers.Clear(); Report.Clear();
            FamilyWorkDiscovery.Refresh();
        }

        public static void Apply(WorldProfileState state, bool acceptCurrent)
        {
            RestoreBaseline();
            if (acceptCurrent)
            {
                state.approvedMachines.Clear(); state.approvedMaterials.Clear(); state.approvedRecipes.Clear();
                foreach (MachineProfileRuleDef rule in DefDatabase<MachineProfileRuleDef>.AllDefsListForReading)
                    if (MaterialDictionary.BelongsTo(DefDatabase<ThingDef>.GetNamedSilentFail(rule.targetDefName), rule.packageId)
                        && (!rule.addHousing || MaterialDictionary.BelongsTo(DefDatabase<ThingDef>.GetNamedSilentFail("Grechka_HousingT" + rule.minimumTier + "_Item"), "grechka.industry"))
                        && (rule.replacementCost == null || rule.replacementCost.All(c => DefDatabase<ThingDef>.GetNamedSilentFail(c.thingDefName) != null))) state.approvedMachines.Add(rule.Signature);
                foreach (MaterialGroupDef group in DefDatabase<MaterialGroupDef>.AllDefsListForReading)
                    foreach (MaterialMember member in group.members)
                        if (MaterialDictionary.BelongsTo(DefDatabase<ThingDef>.GetNamedSilentFail(member.thingDefName), member.packageId)) state.approvedMaterials.Add(group.defName + ":" + member.Key);
                foreach (RecipeMaterialRuleDef rule in DefDatabase<RecipeMaterialRuleDef>.AllDefsListForReading)
                    if (MaterialDictionary.BelongsTo(DefDatabase<RecipeDef>.GetNamedSilentFail(rule.recipeDefName), rule.packageId)) state.approvedRecipes.Add(RecipeKey(rule));
                state.applied = true;
            }
            if (!state.applied) { Report.Add("Профиль этой колонии ещё не применён. Исходные стоимости и материалы сохранены."); return; }
            var accepted = new HashSet<string>(state.approvedMachines);
            foreach (MachineProfileRuleDef rule in DefDatabase<MachineProfileRuleDef>.AllDefsListForReading)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(rule.targetDefName);
                if (!MaterialDictionary.BelongsTo(def, rule.packageId)) { Report.Add(rule.defName + ": источник отсутствует"); continue; }
                if (!accepted.Contains(rule.Signature)) { Report.Add(rule.defName + ": требуется явное применение нового правила"); continue; }
                if (rule.addHousing)
                {
                    ThingDef housing = DefDatabase<ThingDef>.GetNamedSilentFail("Grechka_HousingT" + rule.minimumTier + "_Item");
                    if (housing == null) { Report.Add(rule.defName + ": Industry/корпус отсутствует, исходная цена сохранена"); continue; }
                    if (rule.replacementCost != null && rule.replacementCost.Any(c => DefDatabase<ThingDef>.GetNamedSilentFail(c.thingDefName) == null))
                    { Report.Add(rule.defName + ": материал цены отсутствует, исходная цена сохранена"); continue; }
                    def.costList = rule.replacementCost == null ? Copy(costs[def]) : rule.replacementCost
                        .Select(c => new ThingDefCountClass(DefDatabase<ThingDef>.GetNamed(c.thingDefName), c.count)).ToList();
                    if (!def.costList.Any(c => c.thingDef == housing)) def.costList.Add(new ThingDefCountClass(housing, 1));
                    Report.Add(rule.defName + ": " + def.defName + " + 1 корпус T" + rule.minimumTier);
                }
                else Report.Add(rule.defName + ": исходная стоимость; " + rule.note);
                if (rule.minimumTier > 0) AppliedTiers[def.defName] = rule.minimumTier;
            }
            var materialApproval = new HashSet<string>(state.approvedMaterials);
            foreach (RecipeMaterialRuleDef rule in DefDatabase<RecipeMaterialRuleDef>.AllDefsListForReading)
            {
                RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(rule.recipeDefName);
                MaterialGroupDef group = DefDatabase<MaterialGroupDef>.GetNamedSilentFail(rule.materialGroup);
                if (!MaterialDictionary.BelongsTo(recipe, rule.packageId) || group == null) { Report.Add(rule.defName + ": рецепт/группа отсутствует"); continue; }
                if (!state.approvedRecipes.Contains(RecipeKey(rule))) { Report.Add(rule.defName + ": требуется явное применение"); continue; }
                string error;
                var members = MaterialDictionary.Resolve(group, materialApproval);
                Report.Add(rule.defName + ": " + (MaterialDictionary.Apply(recipe, rule.ingredientIndex, members, out error)
                    ? "разрешены " + string.Join(", ", members.Select(m => m.defName)) : "не применено: " + error));
            }
            ProfileVariants.Apply(state, acceptCurrent);
            FamilyWorkDiscovery.Refresh();
            CostListCalculator.Reset();
            Log.Message("[Grechka World] Профиль: " + AppliedTiers.Count + " машин с минимальным тиром; подробный отчёт в настройках.");
        }

        private static string RecipeKey(RecipeMaterialRuleDef r) => r.packageId + "/" + r.recipeDefName + "/" + r.ingredientIndex + "/" + r.materialGroup;
    }

    public sealed class WorldProfileState : GameComponent
    {
        public bool applied;
        public string lastBackup;
        public List<string> approvedMachines = new List<string>();
        public List<string> approvedMaterials = new List<string>();
        public List<string> approvedRecipes = new List<string>();
        public WorldProfileState(Game game) { WorldProfile.RestoreBaseline(); }
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
