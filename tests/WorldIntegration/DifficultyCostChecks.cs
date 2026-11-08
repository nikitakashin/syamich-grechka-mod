using Grechka.WorldIntegration;
using RimWorld;
using Verse;

internal static class DifficultyCostChecks
{
    private static void Check(bool value, string label)
    { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }

    internal static void Run(ModContentPack core, ModContentPack industry)
    {
        ThingDef Resource(string name, ModContentPack pack)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
            if (def != null) return def;
            def = new ThingDef { defName = name, label = name, category = ThingCategory.Item,
                modContentPack = pack, stackLimit = 75 };
            DefDatabase<ThingDef>.Add(def); return def;
        }
        var steel = Resource("Steel", core);
        var component = Resource("DifficultyCostComponent", core);
        var barrel = Resource("DifficultyCostBarrel", core);
        var baseline = new CostListForDifficulty { difficultyVar = "classicMortars", invert = true,
            costStuffCount = 50, costList = new() { new(component, 6), new(barrel, 1) } };
        var source = new ThingDef { defName = "DifficultyCostMortar", label = "mortar", modContentPack = core,
            category = ThingCategory.Building, thingClass = typeof(Building_TurretGun),
            costList = new() { new(steel, 150), new(component, 6) }, costStuffCount = 75,
            stuffCategories = new() { new StuffCategoryDef { defName = "DifficultyCostMetallic" } },
            costListForDifficulty = baseline };
        DefDatabase<ThingDef>.Add(source);
        var rule = new MachineProfileRuleDef { defName = "difficulty_cost_rule", packageId = "ludeon.rimworld",
            targetDefName = source.defName, minimumTier = 2, addHousing = true };
        DefDatabase<MachineProfileRuleDef>.Add(rule);
        var forms = new List<ThingDef> { source };
        for (int tier = 2; tier <= 5; tier++)
        {
            Resource("Grechka_HousingT" + tier + "_Item", industry);
            if (tier == 2) continue;
            var def = new ThingDef { defName = "GrechkaWorld_" + source.defName + "_T" + tier + "_Building",
                label = "mortar T" + tier, modContentPack = core, category = ThingCategory.Building,
                thingClass = source.thingClass, costList = source.costList, costStuffCount = source.costStuffCount,
                stuffCategories = source.stuffCategories, costListForDifficulty = baseline };
            DefDatabase<ThingDef>.Add(def); forms.Add(def);
            ProfileVariants.Entries[def.defName] = new VariantEntry { sourceName = source.defName, name = def.defName,
                ruleName = rule.defName, tier = tier, minimum = 2 };
        }
        WorldProfile.Capture();
        var state = new WorldProfileState(null);
        WorldProfile.Apply(state, false);
        Check(forms.All(d => d.costListForDifficulty.costList.Count == 2),
            "Unapproved colony retains every original difficulty cost without a housing");
        WorldProfile.Apply(state, true);
        void Verify(string phase)
        {
            foreach (bool classic in new[] { false, true })
            {
                // The native conditional cost caches the Difficulty identity, as in the game.
                Current.Game.storyteller.difficulty = new Difficulty { classicMortars = classic };
                CostListCalculator.Reset();
                for (int i = 0; i < forms.Count; i++)
                {
                    var def = forms[i]; int tier = i + 2;
                    var cost = CostListCalculator.CostListAdjusted(def, steel);
                    var housings = cost.Where(c => c.thingDef.defName.StartsWith("Grechka_HousingT")).ToList();
                    Check(housings.Count == 1 && housings[0].thingDef.defName == "Grechka_HousingT" + tier + "_Item"
                        && housings[0].count == 1,
                        phase + ": native construction price includes exactly its T" + tier + " housing, classicMortars=" + classic);
                    Check(cost.Single(c => c.thingDef == steel).count == (classic ? 225 : 50)
                        && cost.Single(c => c.thingDef == component).count == 6
                        && cost.Any(c => c.thingDef == barrel) == !classic
                        && def.CostStuffCount == (classic ? 75 : 50),
                        phase + ": T" + tier + " retains the native barrel, components and stuff price for classicMortars=" + classic);
                }
            }
            Check(forms.Select(d => d.costListForDifficulty).Distinct().Count() == forms.Count
                && baseline.costList.Count == 2,
                phase + ": tier prices never share or mutate the baseline difficulty object");
        }
        Verify("Approved profile");
        WorldProfile.Apply(state, false); Verify("Profile reload");
        WorldProfile.RestoreBaseline();
        foreach (bool classic in new[] { false, true })
        {
            Current.Game.storyteller.difficulty = new Difficulty { classicMortars = classic };
            CostListCalculator.Reset();
            Check(forms.All(d => !CostListCalculator.CostListAdjusted(d, steel)
                .Any(c => c.thingDef.defName.StartsWith("Grechka_HousingT"))),
                "Removing profile restores all native prices, classicMortars=" + classic);
        }
    }
}
