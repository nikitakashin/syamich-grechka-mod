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
            && float.IsFinite(recycleEfficiency) && recycleEfficiency > 0 && recycleEfficiency <= .125f
            && float.IsFinite(recycleEnergyPerValue) && recycleEnergyPerValue > 0
            && float.IsFinite(recycleWorkPerValue) && recycleWorkPerValue > 0
            && float.IsFinite(synthesisEnergyPerMatter) && synthesisEnergyPerMatter > 0
            && float.IsFinite(synthesisWorkPerMatter) && synthesisWorkPerMatter > 0;
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!Valid) yield return "Matter constants must be finite and positive; recycleEfficiency must not exceed 0.125 with T5 bonuses.";
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
            if (!policy.Valid) { Log.Error("[Grechka Industry] Некорректные константы материи; рецепты не созданы."); return; }
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
                catch (Exception e) { Log.Warning("[Grechka Industry] Материя: пропущен " + def.defName + ": " + e.Message); }
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
                defName = name, label = label, description = label + ". Результат новый, обычного качества; содержимое контейнеров и уникальное состояние не копируются.",
                jobString = label, workAmount = work, workSkill = SkillDefOf.Crafting,
                workSpeedStat = DefDatabase<StatDef>.GetNamed("Grechka_MatterFixedWorkSpeed"),
                unfinishedThingDef = DefDatabase<ThingDef>.GetNamedSilentFail("UnfinishedComponent"), workTableSpeedStat = DefDatabase<StatDef>.GetNamed("Grechka_MatterFixedWorkSpeed"),
                ingredients = new List<IngredientCount> { ingredient }, fixedIngredientFilter = new ThingFilter(),
                products = new List<ThingDefCountClass> { new ThingDefCountClass(output, amount) },
                recipeUsers = new List<ThingDef> { DefDatabase<ThingDef>.GetNamed("Grechka_Matter" + (recycle ? "Recycler" : "Synthesizer") + "T" + tier + "_Building") },
                modExtensions = new List<DefModExtension>()
            };
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
                if (!hashLimitReported) Log.Error("[Grechka Industry] Достигнут безопасный предел 60000 RecipeDef. Оставшиеся рецепты материи не созданы: слишком много вариантов материалов в сборке.");
                hashLimitReported = true;
                return;
            }
            giveHash.Invoke(null, new object[] { recipe, typeof(RecipeDef), taken });
            DefGenerator.AddImpliedDef(recipe);
            recipe.ResolveReferences();
            foreach (ThingDef user in recipe.recipeUsers)
            {
                AccessTools.Field(typeof(ThingDef), "allRecipesCached").SetValue(user, null);
            }
        }
        private static void AddRecycle(ThingDef input, ThingDef matter, float value, MatterPolicyDef policy)
        {
            int batch = Math.Min(100, Math.Max(1, (int)Math.Ceiling(20 / value)));
            foreach (int tier in new[] { 4, 5 })
            {
                float work = MatterEconomy.Work(policy.recycleWorkPerValue / (tier == 4 ? 15 : 60), policy.recycleEnergyPerValue,
                    value * batch, tier == 4 ? 8000000 : 30000000, 1);
                var recipe = Base("Grechka_Recycle_T" + tier + "_" + input.defName, "материя из: " + input.label,
                    input, batch, matter, Math.Max(1, MatterEconomy.RecycleYield(value * batch, policy.matterPerValue, policy.recycleEfficiency)), work, tier, true);
                recipe.modExtensions.Add(new MatterRecipeProperties { recycle = true, output = matter,
                    factor = policy.matterPerValue, efficiency = policy.recycleEfficiency });
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
                var recipe = Base("Grechka_Synthesize_T" + tier + "_" + output.defName + (stuff == null ? "" : "_" + stuff.defName),
                    "синтез: " + output.label + (stuff == null ? "" : " (" + stuff.label + ")"), matter, cost, output, 1, work, tier, false);
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
            var snapshot = MatterSnapshots.For(recipeDef, worker);
            if (snapshot != null)
                props = new MatterRecipeProperties { recycle = props.recycle, output = props.output, stuff = props.stuff,
                    factor = snapshot.factor, efficiency = snapshot.efficiency };
            __result = Products(props, ingredients);
            return false;
        }
        private static IEnumerable<Thing> Products(MatterRecipeProperties props, List<Thing> ingredients)
        {
            int count = 1;
            if (props.recycle)
            {
                double value = ingredients.Sum(t => (double)t.MarketValue * t.stackCount);
                count = MatterEconomy.RecycleYield(value, props.factor, props.efficiency);
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
