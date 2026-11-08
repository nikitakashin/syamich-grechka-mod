using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Storage
{
    // Input-selected operations never advertise an invented fixed output or enter
    // the dependency planner. One order executes one native recipe cycle.
    internal static class DynamicCraftRecipes
    {
        internal static bool Supported(RecipeDef recipe) => recipe != null
            && recipe.Worker.GetType() == typeof(RecipeWorker)
            && recipe.ingredients?.Count == 1 && recipe.ingredients[0].GetBaseCount() == 1
            && (recipe.products == null || recipe.products.Count == 0)
            && recipe.specialProducts?.Count == 1
            && (recipe.specialProducts[0] == SpecialProductType.StoneBlocks
                || recipe.specialProducts[0] == SpecialProductType.Smelted
                || recipe.specialProducts[0] == SpecialProductType.Butchery);

        internal static bool TryCount(RecipeDef recipe, Building_WorkTable table, Thing input, out int count, out string problem)
        {
            count = 0;
            problem = StorageText.Get("GrechkaStorage_Text160");
            if (!Supported(recipe) || input == null || input.Destroyed || input.stackCount <= 0
                || input is MinifiedThing || DiskItemPolicy.Problem(input) != null) return false;
            string method = recipe.specialProducts[0] == SpecialProductType.StoneBlocks ? "StoneBlockProducts"
                : recipe.specialProducts[0] == SpecialProductType.Smelted ? "SmeltProducts" : "ButcherProducts";
            // Modded overrides may require map/social context; they need their own adapter.
            if (AccessTools.Method(input.GetType(), method)?.DeclaringType != typeof(Thing)) return false;
            Bill_Production bill = CraftRecipes.Bill(table, recipe);
            IngredientCount ingredient = recipe.ingredients[0];
            if (!ingredient.filter.Allows(input) || !recipe.fixedIngredientFilter.Allows(input)
                || !bill.IsFixedOrAllowedIngredient(input)) return false;
            if (recipe.ignoreIngredientCountTakeEntireStacks) count = input.stackCount;
            else
            {
                float value = recipe.IngredientValueGetter.ValuePerUnitOf(input.def);
                if (!float.IsFinite(value) || value <= 0) return false;
                double required = ingredient.CountRequiredOfFor(input.def, recipe, bill);
                if (!double.IsFinite(required) || required <= 0 || required > input.stackCount) return false;
                count = (int)Math.Ceiling(required);
            }
            problem = null;
            return true;
        }

        internal static List<(StorageDisk disk, Thing item, int count)> SelectSelected(
            Building_StorageController controller, Building_WorkTable table, RecipeDef recipe, Thing input, out string problem)
        {
            if (!TryCount(recipe, table, input, out int count, out problem)) return null;
            StorageDisk disk = controller.ActiveDisks().FirstOrDefault(d => d.Items.Contains(input));
            if (disk == null) { problem = StorageText.Get("GrechkaStorage_Text161"); return null; }
            return new List<(StorageDisk, Thing, int)> { (disk, input, count) };
        }
        internal static List<(StorageDisk disk, Thing item, int count)> SelectOrder(
            Building_StorageController controller, Building_WorkTable table, CraftOrder order, out string problem)
        {
            var selection = SelectSelected(controller, table, order.Recipe, order.SelectedInput, out problem);
            if (selection == null) return null;
            if (order.SelectedCount != selection[0].count)
            {
                problem = StorageText.Get("GrechkaStorage_Text162");
                return null;
            }
            return selection;
        }
    }
}
