using System.Reflection;
using Grechka.Industry;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

internal static class BalanceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var prices = new Dictionary<ThingDef, double>();
        ThingDef Item(string name, double price, bool small = false)
        {
            var def = new ThingDef { defName = "GrechkaNativeTest_Balance" + name, category = ThingCategory.Item,
                thingClass = typeof(Thing), stackLimit = 100000, smallVolume = small };
            DefDatabase<ThingDef>.Add(def); prices[def] = price; return def;
        }
        var steel = Item("Steel", 1.9); var component = Item("Component", 25);
        var advanced = Item("Advanced", 200); var mecha = Item("Mecha", 1300);
        var plasteel = Item("Plasteel", 9); var gold = Item("Gold", 10, true);
        var uranium = Item("Uranium", 6); var mechanites = Item("Mechanites", 12);
        RecipeDef Recipe(string name, ThingDef output, int count, params (ThingDef def, float count)[] inputs)
        {
            var recipe = new RecipeDef { defName = "GrechkaNativeTest_Balance" + name,
                ingredients = new(), products = new() { new(output, count) }, workerClass = typeof(RecipeWorker), fixedIngredientFilter = new ThingFilter(), defaultIngredientFilter = new ThingFilter() };
            foreach (var input in inputs)
            {
                var ingredient = new IngredientCount(); ingredient.SetBaseCount(input.count);
                ingredient.filter.SetAllow(input.def, true); recipe.fixedIngredientFilter.SetAllow(input.def, true); recipe.defaultIngredientFilter.SetAllow(input.def, true); recipe.ingredients.Add(ingredient);
            }
            DefDatabase<RecipeDef>.Add(recipe); return recipe;
        }
        var normalRecipe = Recipe("Normal", component, 5, (steel, 60));
        var advancedRecipe = Recipe("AdvancedRecipe", advanced, 1, (component, 1), (steel, 12), (plasteel, 5), (gold, .2f));
        var mechaRecipe = Recipe("MechaRecipe", mecha, 1, (advanced, 1), (plasteel, 8), (uranium, 4), (mechanites, 10));
        MatterCraftValue.Rebuild(def => prices.TryGetValue(def, out double price) ? price : 0);
        double Cap(ThingDef def) => MatterCraftValue.UnitValue(def, prices[def]);
        check(Math.Abs(Cap(component) - 5.7) < .00001 && Cap(advanced) < 30 && Cap(mecha) < 100,
            "Recycler production value follows all crafting stages instead of inflated shop prices");

        var bench = new Building_WorkTable { def = new ThingDef { defName = "GrechkaNativeTest_BalanceBench" } };
        var bonus = new CompTierBonuses { parent = bench, props = new CompProperties_TierBonuses { inputDiscount = .5f, outputBonus = 1 } };
        AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(bench, new List<ThingComp> { bonus });
        var worker = new RecipeWorker();
        double CostFromSynthesizedInputs(RecipeDef recipe, int cycles)
        {
            var bill = new Bill_Production(recipe, null); bench.BillStack.AddBill(bill);
            double cost = 0;
            foreach (var input in recipe.ingredients)
            {
                var def = input.filter.AllowedThingDefs.Single();
                int actual = (int)Math.Ceiling(worker.GetIngredientCount(input, bill) / recipe.IngredientValueGetter.ValuePerUnitOf(def) - .00001);
                double synthPerUnit = Math.Ceiling(MatterEconomy.SynthesisCost(prices[def], 10) * .5) / 2;
                cost += cycles * actual * synthPerUnit;
            }
            return cost;
        }
        double previousOutputValue = 0;
        foreach (var pair in new[] { (normalRecipe, component), (advancedRecipe, advanced), (mechaRecipe, mecha) })
        {
            var recipe = pair.Item1; var product = pair.Item2;
            double spent = CostFromSynthesizedInputs(recipe, 100);
            int output = 100 * recipe.products.Single().count * 2;
            double returned = MatterEconomy.RecycleYield(Cap(product) * output, 10, .05) * 2;
            check(returned < spent, "Native T5 ingredient rounding and bonus keep synthesis-craft-recycling negative: " + product.defName);
            double inputCap = 0;
            var bill = new Bill_Production(recipe, null); bench.BillStack.AddBill(bill);
            foreach (var input in recipe.ingredients)
            {
                var def = input.filter.AllowedThingDefs.Single();
                int actual = (int)Math.Ceiling(worker.GetIngredientCount(input, bill) / recipe.IngredientValueGetter.ValuePerUnitOf(def) - .00001);
                inputCap += actual * Cap(def);
            }
            check(Cap(product) * recipe.products.Single().count * 2 <= inputCap + .00001,
                "Conservative value is conserved across each full T5 crafting edge: " + product.defName);
            previousOutputValue += returned;
        }
        check(previousOutputValue > 0, "Cycle protection still allows useful positive recycling output");
        int synthSpent = (int)Math.Ceiling(MatterEconomy.SynthesisCost(prices[steel], 10) * .5);
        check(MatterEconomy.RecycleYield(Cap(steel) * 2, 10, .05) * 2 < synthSpent,
            "Direct synthesis-recycling remains strictly negative with unchanged market prices");
        var matter = Item("Matter", 1);
        var recycler = Recipe("Recycler", matter, 1, (component, 100));
        var recycleProps = new MatterRecipeProperties { recycle = true, output = matter, factor = 10, efficiency = .05f };
        recycler.modExtensions = new() { recycleProps };
        fixturePrices = prices;
        var priceHarmony = new Harmony("grechka.industry.balance-price-fixture");
        priceHarmony.Patch(AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.MarketValue)),
            prefix: new HarmonyMethod(typeof(BalanceChecks), nameof(MarketPrice)));
        MatterCraftValue.Rebuild(def => prices.TryGetValue(def, out double price) ? price : 0);
        var recycledInput = new Thing { def = component, stackCount = 100 };
        var recycled = GenRecipe.MakeRecipeProducts(recycler, null, new() { recycledInput }, recycledInput, bench).ToList();
        check(recycled.Sum(t => t.stackCount) == 2 * MatterEconomy.RecycleYield(Cap(component) * 100, 10, .05)
            && recycled.Sum(t => t.stackCount) < 2 * MatterEconomy.RecycleYield(prices[component] * 100, 10, .05)
            && recycledInput.stackCount == 100,
            "Actual native GenRecipe recycling uses production-value clamp and one T5 bonus before any ingredient consumption");
        recycleProps.efficiency = .1f; string unsafeSnapshot = MatterSnapshots.Capture(recycler); recycleProps.efficiency = .05f;
        bool unsafeRejected = false;
        using (MatterSnapshots.UseSnapshot(unsafeSnapshot))
            try { GenRecipe.MakeRecipeProducts(recycler, null, new() { recycledInput }, recycledInput, bench).ToList(); }
            catch (InvalidOperationException) { unsafeRejected = true; }
        check(unsafeRejected && recycledInput.stackCount == 100,
            "Unsafe legacy batch coefficients cannot bypass the strict limit or consume their inputs");

        var cycleItem = Item("Cycle", 1000);
        Recipe("CycleRecipe", cycleItem, 2, (cycleItem, 1));
        var custom = Item("CustomProducer", 1000);
        var customRecipe = Recipe("CustomRecipe", custom, 1, (steel, 1)); customRecipe.workerClass = typeof(UnknownRecipeWorker);
        MatterCraftValue.Rebuild(def => prices.TryGetValue(def, out double price) ? price : 0);
        check(Cap(cycleItem) == 0 && Cap(custom) == 0,
            "A gain cycle and an unknown custom producer cannot retain a speculative recycler price");
        var blockedInput = new IngredientCount(); blockedInput.filter.SetAllow(custom, true); blockedInput.SetBaseCount(3);
        var blockedRecipe = new RecipeDef { defName = "Grechka_Recycle_T5_GrechkaNativeTest_Blocked",
            ingredients = new() { blockedInput }, products = new() { new(component, 1) },
            modExtensions = new() { new MatterRecipeProperties { recycle = true, output = component, factor = 10, efficiency = .05f } } };
        DefDatabase<RecipeDef>.Add(blockedRecipe);
        var blockedBill = (Bill_Production)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Bill_Production));
        blockedBill.recipe = blockedRecipe;
        check(DefDatabase<RecipeDef>.GetNamed(blockedRecipe.defName) == blockedRecipe && !blockedRecipe.AvailableNow && !blockedBill.ShouldDoNow(),
            "Unsafe recycling keeps its stable recipe identity but pauses new and saved bills");
        var preserved = new Thing { def = custom, stackCount = 3 };
        var driver = (JobDriver_DoBill)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(JobDriver_DoBill));
        driver.job = new Job { bill = blockedBill };
        var guardHarmony = new Harmony("grechka.industry.balance-pause-fixture");
        guardHarmony.Patch(AccessTools.Method(typeof(JobDriver), nameof(JobDriver.EndJobWith)),
            prefix: new HarmonyMethod(typeof(BalanceChecks), nameof(ObserveInterruption)));
        interruptions = 0; driver.DriverTick(); driver.DriverTickInterval(1);
        check(interruptions == 2 && preserved.stackCount == 3 && !preserved.Destroyed,
            "Both native driver clocks interrupt an unsafe loaded job before its completion toil can consume reserved items");
        bool rejected = false;
        try { GenRecipe.MakeRecipeProducts(blockedRecipe, null, new() { preserved }, preserved, null).ToList(); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && preserved.stackCount == 3 && !preserved.Destroyed,
            "A forced unsafe output request fails before consumption so Storage can retain its buffered inputs");
        check(new MatterPolicyDef { recycleEfficiency = .05f }.Valid && !new MatterPolicyDef { recycleEfficiency = .0625f }.Valid,
            "Matter efficiency leaves a strict safety margin below the compounded T5 boundary");

        var building = new ThingDef { defName = "GrechkaNativeTest_BalanceBuilding", category = ThingCategory.Building,
            thingClass = typeof(Building), stackLimit = 1, comps = new() };
        var packed = new ThingDef { defName = "GrechkaNativeTest_BalancePacked", thingClass = typeof(MinifiedThing), stackLimit = 1 };
        var conversion = Recipe("PanelUpgrade", building, 1, (packed, 1), (steel, 10));
        var converted = new Building { def = building };
        check(bonus.TakeExtra(conversion, converted) == 0 && bonus.PreviewExtra(conversion, building) == 0,
            "One packed building upgrades to one building without bonus copies or preview promises");
        conversion.defName = "Grechka_UpgradeArmorT4";
        var upgradeBill = new Bill_Production(conversion, null); bench.BillStack.AddBill(upgradeBill);
        check(worker.GetIngredientCount(conversion.ingredients[1], upgradeBill) == 10 && bonus.TakeExtra(conversion, converted) == 0,
            "Named equipment upgrades receive neither an ingredient discount nor extra products");
    }
    private static int interruptions;
    private static Dictionary<ThingDef, double> fixturePrices;
    public static bool MarketPrice(Thing __instance, ref float __result)
    {
        if (fixturePrices == null || !fixturePrices.TryGetValue(__instance.def, out double price)) return true;
        __result = (float)price; return false;
    }
    public static bool ObserveInterruption(JobCondition condition)
    {
        if (condition != JobCondition.InterruptForced) throw new Exception("Unexpected pause condition");
        interruptions++; return false;
    }
    private sealed class UnknownRecipeWorker : RecipeWorker { }
}
