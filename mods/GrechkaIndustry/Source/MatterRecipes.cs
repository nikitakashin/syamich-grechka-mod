using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Industry
{
    public sealed class MatterPolicyDef : Def
    {
        public float matterPerValue = 10;
        public float recycleEfficiency = .05f;
        public float recycleEnergyPerValue = 1000000;
        public float recycleWorkPerValue = 600;
        public float synthesisEnergyPerMatter = 200000;
        public float synthesisWorkPerMatter = 120;
        public bool Valid => float.IsFinite(matterPerValue) && matterPerValue > 0
            && float.IsFinite(recycleEfficiency) && recycleEfficiency > 0 && recycleEfficiency < .0625f
            && float.IsFinite(recycleEnergyPerValue) && recycleEnergyPerValue > 0
            && float.IsFinite(recycleWorkPerValue) && recycleWorkPerValue > 0
            && float.IsFinite(synthesisEnergyPerMatter) && synthesisEnergyPerMatter > 0
            && float.IsFinite(synthesisWorkPerMatter) && synthesisWorkPerMatter > 0;
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!Valid) yield return IndustryText.Get("GrechkaIndustry_MatterConstants", "Matter constants must be finite and positive; recycleEfficiency must remain below 0.0625 with T5 bonuses.");
        }
    }
    public sealed class MatterRecipeProperties : DefModExtension
    {
        public bool recycle;
        public ThingDef output;
        public ThingDef stuff;
        public float factor;
        public float efficiency;
    }
    public static class MatterWorkContract
    {
        // External workers must respect the electrical process clock.
        public static float EffectiveWorkerMultiplier(RecipeDef recipe, float requestedMultiplier)
            => recipe?.GetModExtension<MatterRecipeProperties>() == null ? requestedMultiplier : 1f;
    }
    // Each recipe has a stable ID and vanilla bills own its reservations, hauling,
    // work, ingredient consumption and save state. No world items are teleported.
    [StaticConstructorOnStartup]
    public static class MatterRecipes
    {
        private static bool hashLimitReported;
        private static readonly System.Reflection.MethodInfo giveHash = AccessTools.Method(typeof(ShortHashGiver), "GiveShortHash");
        static MatterRecipes()
        {
            MatterPolicyDef policy = DefDatabase<MatterPolicyDef>.GetNamedSilentFail("Grechka_MatterPolicy");
            ThingDef matter = DefDatabase<ThingDef>.GetNamedSilentFail("Grechka_Matter");
            if (policy == null || matter == null) return;
            if (!policy.Valid) { Log.Error(IndustryText.Get("GrechkaIndustry_1d7f4fb22aa0", "[Grechka Industry] Invalid matter constants. Recipes were not created.")); return; }
            MatterCraftValue.Rebuild();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading.OrderBy(d => d.defName).ToArray())
            {
                if (hashLimitReported) break;
                if (!Supported(def) || def == matter) continue;
                try
                {
                    ThingDef defaultStuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
                    float value = def.GetStatValueAbstract(StatDefOf.MarketValue, defaultStuff);
                    if (!float.IsFinite(value) || value <= 0) continue;
                    AddRecycle(def, matter, value, policy);
                    // Include material variants without cloning the state of an existing item.
                    var materials = def.MadeFromStuff
                        ? GenStuff.AllowedStuffsFor(def).OrderBy(d => d.defName).ToArray()
                        : new ThingDef[] { null };
                    foreach (ThingDef stuff in materials)
                        AddSynthesis(def, stuff, matter, def.GetStatValueAbstract(StatDefOf.MarketValue, stuff), policy);
                }
                catch (Exception e) { Log.Warning(IndustryText.Get("GrechkaIndustry_2493ed5a52c6", "[Grechka Industry] Matter skipped ") + def.defName + ": " + e.Message); }
            }
        }
        private static bool Supported(ThingDef def)
        {
            return def.category == ThingCategory.Item && def.tradeability != Tradeability.None
                && def.thingClass != null && !typeof(IThingHolder).IsAssignableFrom(def.thingClass)
                && !typeof(Corpse).IsAssignableFrom(def.thingClass)
                && !typeof(UnfinishedThing).IsAssignableFrom(def.thingClass)
                && !typeof(MinifiedThing).IsAssignableFrom(def.thingClass)
                && (def.comps == null || def.comps.All(c => c.compClass != null
                    && !typeof(IThingHolder).IsAssignableFrom(c.compClass)))
                && !def.destroyOnDrop;
        }
        private static RecipeDef Base(string name, string label, ThingDef input, float count, ThingDef output, int amount, float work, int tier, bool recycle)
        {
            var ingredient = new IngredientCount();
            ingredient.filter.SetAllow(input, true);
            // Vanilla ingredient values use 0.1 per physical silver/gold unit.
            ingredient.SetBaseCount(count * (input.smallVolume ? .1f : 1f));
            var recipe = new RecipeDef
            {
                defName = name, label = label, description = label + IndustryText.Get("GrechkaIndustry_da283b60afc2", ". Produces a new item of normal quality. Container contents and unique state are not copied."),
                jobString = label, workAmount = work, workSkill = SkillDefOf.Crafting,
                workSpeedStat = DefDatabase<StatDef>.GetNamed("Grechka_MatterFixedWorkSpeed"),
                unfinishedThingDef = DefDatabase<ThingDef>.GetNamedSilentFail("UnfinishedComponent"), workTableSpeedStat = DefDatabase<StatDef>.GetNamed("Grechka_MatterFixedWorkSpeed"),
                ingredients = new List<IngredientCount> { ingredient }, fixedIngredientFilter = new ThingFilter(),
                products = new List<ThingDefCountClass> { new ThingDefCountClass(output, amount) },
                recipeUsers = new List<ThingDef> { DefDatabase<ThingDef>.GetNamed("Grechka_Matter" + (recycle ? "Recycler" : "Synthesizer") + "T" + tier + "_Building") },
                modExtensions = new List<DefModExtension>()
            };
            // Optional Grechka World tree. Generated matter recipes follow the same
            // research as their machines and products; synthesis cannot skip a locked tier.
            ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Grechka_Research_MatterT" + tier);
            if (research != null)
            {
                recipe.researchPrerequisites = new List<ResearchProjectDef> { research };
                if (!recycle)
                {
                    if (output.researchPrerequisites != null) recipe.researchPrerequisites.AddRange(output.researchPrerequisites);
                    if (output.recipeMaker?.researchPrerequisite != null) recipe.researchPrerequisites.Add(output.recipeMaker.researchPrerequisite);
                    if (output.recipeMaker?.researchPrerequisites != null) recipe.researchPrerequisites.AddRange(output.recipeMaker.researchPrerequisites);
                    recipe.researchPrerequisites = recipe.researchPrerequisites.Distinct().ToList();
                }
            }
            recipe.fixedIngredientFilter.SetAllow(input, true);
            return recipe;
        }
        private static void Register(RecipeDef recipe)
        {
            // StaticConstructorOnStartup runs after vanilla's global short-hash pass.
            // Reserve a hash before adding a late generated recipe to the database.
            var hashes = (Dictionary<Type, HashSet<ushort>>)AccessTools.Field(typeof(ShortHashGiver), "takenHashesPerDeftype").GetValue(null);
            if (!hashes.TryGetValue(typeof(RecipeDef), out var taken))
                hashes[typeof(RecipeDef)] = taken = new HashSet<ushort>(DefDatabase<RecipeDef>.AllDefsListForReading.Select(d => d.shortHash));
            // Leave space for other mods adding late recipes, and never exhaust the ushort probe loop.
            if (taken.Count >= 60000)
            {
                if (!hashLimitReported) Log.Error(IndustryText.Get("GrechkaIndustry_8fbd4d2be5a9", "[Grechka Industry] Safe limit of 60000 RecipeDefs reached. Remaining matter recipes were not created because the pack contains too many material variants."));
                hashLimitReported = true;
                return;
            }
            giveHash.Invoke(null, new object[] { recipe, typeof(RecipeDef), taken });
            DefGenerator.AddImpliedDef(recipe);
            recipe.ResolveReferences();
            if (recipe.GetModExtension<MatterRecipeProperties>() != null) MatterCraftValue.GeneratedRecipeAdded();
            else MatterCraftValue.Invalidate();
            foreach (ThingDef user in recipe.recipeUsers)
            {
                AccessTools.Field(typeof(ThingDef), "allRecipesCached").SetValue(user, null);
            }
        }
        private static void AddRecycle(ThingDef input, ThingDef matter, float value, MatterPolicyDef policy)
        {
            // Keep historical recipe identities even when their current production
            // value cannot be proven. Availability and active-job guards pause
            // these recipes before consuming any reserved/unfinished ingredients.
            double preciseCap = MatterCraftValue.UnitValue(input, value);
            float cap = (float)preciseCap;
            if (cap > preciseCap) cap = MathF.BitDecrement(cap);
            bool blocked = cap <= 0;
            if (!blocked) value = cap;
            foreach (int tier in new[] { 4, 5 })
            {
                int batch = MatterEconomy.RecycleBatch(value, policy.matterPerValue, policy.recycleEfficiency, tier == 4 ? .35 : .5);
                if (batch == 0) { Log.Warning(IndustryText.Get("GrechkaIndustry_d19e03356c37", "[Grechka Industry] Matter batch size is unrepresentable for ") + input.defName); continue; }
                float work = MatterEconomy.Work(policy.recycleWorkPerValue / (tier == 4 ? 15 : 60), policy.recycleEnergyPerValue,
                    value * batch, tier == 4 ? 8000000 : 30000000, 1);
                if (work == float.MaxValue) { Log.Warning(IndustryText.Get("GrechkaIndustry_1e1bdf9ead8f", "[Grechka Industry] Matter recycling budget is unrepresentable for ") + input.defName); continue; }
                var recipe = Base("Grechka_Recycle_T" + tier + "_" + input.defName, IndustryText.Get("GrechkaIndustry_100794a50088", "matter from: ") + input.label,
                    input, batch, matter, Math.Max(1, MatterEconomy.RecycleYield(value * batch, policy.matterPerValue, policy.recycleEfficiency)), work, tier, true);
                recipe.modExtensions.Add(new MatterRecipeProperties { recycle = true, output = matter,
                    factor = policy.matterPerValue, efficiency = policy.recycleEfficiency });
                if (blocked) recipe.description += "\n" + MatterSafety.PauseReason;
                Register(recipe);
            }
        }
        private static void AddSynthesis(ThingDef output, ThingDef stuff, ThingDef matter, float value, MatterPolicyDef policy)
        {
            int cost = MatterEconomy.SynthesisCost(value, policy.matterPerValue);
            if (cost <= 0) return;
            foreach (int tier in new[] { 4, 5 })
            {
                float work = MatterEconomy.Work(policy.synthesisWorkPerMatter / (tier == 4 ? 15 : 60), policy.synthesisEnergyPerMatter,
                    cost, tier == 4 ? 12000000 : 60000000, 1);
                if (work == float.MaxValue) { Log.Warning(IndustryText.Get("GrechkaIndustry_672acf4bea2a", "[Grechka Industry] Matter synthesis budget is unrepresentable for ") + output.defName); continue; }
                var recipe = Base("Grechka_Synthesize_T" + tier + "_" + output.defName + (stuff == null ? "" : "_" + stuff.defName),
                    IndustryText.Get("GrechkaIndustry_f78d5b0df7d8", "synthesis: ") + output.label + (stuff == null ? "" : " (" + stuff.label + ")"), matter, cost, output, 1, work, tier, false);
                recipe.modExtensions.Add(new MatterRecipeProperties { output = output, stuff = stuff });
                Register(recipe);
            }
        }
    }
    [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    internal static class MatterProductHook
    {
        private static bool Prefix(RecipeDef recipeDef, Pawn worker, List<Thing> ingredients, ref IEnumerable<Thing> __result)
        {
            var props = recipeDef.GetModExtension<MatterRecipeProperties>();
            if (props == null) return true;
            if (MatterSafety.Blocked(recipeDef)) throw new InvalidOperationException(MatterSafety.PauseReason);
            var snapshot = MatterSnapshots.For(recipeDef, worker);
            if (snapshot != null)
                props = new MatterRecipeProperties { recycle = props.recycle, output = props.output, stuff = props.stuff,
                    factor = snapshot.factor, efficiency = snapshot.efficiency };
            if (props.recycle && (!float.IsFinite(props.efficiency) || props.efficiency <= 0 || props.efficiency >= .0625f))
                throw new InvalidOperationException(MatterSafety.PauseReason);
            __result = Products(props, ingredients);
            return false;
        }
        private static IEnumerable<Thing> Products(MatterRecipeProperties props, List<Thing> ingredients)
        {
            int count = 1;
            if (props.recycle)
            {
                double value = ingredients.Sum(MatterCraftValue.Value);
                count = MatterEconomy.RecycleYield(value, props.factor, props.efficiency);
                if (count <= 0) throw new InvalidOperationException(IndustryText.Get("GrechkaIndustry_RecycleInsufficient",
                    "This batch is worth less than one unit of matter. Its ingredients have not been consumed."));
            }
            while (count > 0)
            {
                Thing thing = ThingMaker.MakeThing(props.output, props.stuff);
                thing.stackCount = Math.Min(count, props.output.stackLimit);
                thing.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
                count -= thing.stackCount;
                yield return thing;
            }
        }
    }
}
