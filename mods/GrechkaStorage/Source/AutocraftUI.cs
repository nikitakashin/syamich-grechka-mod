using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Storage
{
    // One catalogue for the player; each row retains its native execution adapter.
    public sealed class AutocraftUI
    {
        private sealed class Row
        {
            public RecipeDef Recipe;
            public FactoryRecipe Factory;
            public string Title => Recipe?.LabelCap.ToString() ?? Factory.Title;
            public string Id => Recipe?.defName ?? Factory.Def.defName;
            public ThingDef Product => Recipe?.products[0].thingDef ?? Factory.Primary;
        }
        private sealed class OrderRow
        {
            public string Title, Status;
            public bool Maintain;
            public Action Cancel;
        }
        private Vector2 scroll;
        private string search = "", lastSearch;
        private bool showOrders;
        private CraftCatalogue catalogue;
        private int lastRefresh = -1000;
        private List<Row> filtered = new List<Row>();
        private readonly Dictionary<RecipeDef, string> problems = new Dictionary<RecipeDef, string>();
        private bool Matches(string title, string id = null) => (title ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
            || id != null && id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        public void Draw(Rect rect, Building_StorageController controller)
        {
            GUI.BeginGroup(rect);
            try
            {
                CraftMap manager = controller.Map.GetComponent<CraftMap>();
                FactoryCraftMap factory = controller.Map.GetComponent<FactoryCraftMap>();
                if (Widgets.ButtonText(new Rect(0, 0, 180, 30), showOrders ? "Каталог рецептов" : "Заказы и минимумы"))
                { showOrders = !showOrders; scroll = Vector2.zero; }
                search = Widgets.TextField(new Rect(190, 0, Math.Max(100, rect.width - 365), 30), search);
                if (Widgets.ButtonText(new Rect(rect.width - 165, 0, 165, 30), "Остатки фабрик")) factory.Recover(controller);
                if (showOrders) DrawOrders(rect, controller, manager, factory);
                else DrawCatalogue(rect, controller, manager, factory);
            }
            finally { GUI.EndGroup(); }
        }
        private void DrawOrders(Rect rect, Building_StorageController controller, CraftMap manager, FactoryCraftMap factory)
        {
            var orders = new List<OrderRow>();
            foreach (CraftOrder order in manager.Orders(controller))
            {
                if (!Matches(order.Recipe.LabelCap, order.Recipe.defName)) continue;
                orders.Add(new OrderRow { Title = order.Recipe.LabelCap + (order.Maintain ? " — минимум " : " — осталось ") + order.Remaining,
                    Status = order.Status, Maintain = order.Maintain, Cancel = () => manager.Cancel(order) });
            }
            foreach (FactoryOrder order in factory.Orders(controller))
            {
                if (!Matches(order.Title, order.ProcessName)) continue;
                orders.Add(new OrderRow { Title = (order.Title ?? order.ProcessName) + (order.Maintain ? " — минимум " : " — осталось ") + order.Remaining + (order.CountCycles ? " циклов" : " шт."),
                    Status = order.Status, Maintain = order.Maintain, Cancel = () => factory.Cancel(order) });
            }
            Rect outer = new Rect(0, 40, rect.width, rect.height - 40);
            Rect inner = new Rect(0, 0, rect.width - 20, Math.Max(outer.height, orders.Count * 82));
            Widgets.BeginScrollView(outer, ref scroll, inner);
            try
            {
                if (orders.Count == 0) Widgets.Label(new Rect(0, 0, inner.width, 60), "Нет активных заказов по этому поиску. Выберите рецепт в каталоге.");
                int first = Math.Max(0, (int)scroll.y / 82 - 1), last = Math.Min(orders.Count, first + (int)outer.height / 82 + 3);
                for (int i = first; i < last; i++)
                {
                    OrderRow order = orders[i]; float y = i * 82;
                    Widgets.Label(new Rect(0, y, inner.width - 120, 30), order.Title);
                    Widgets.Label(new Rect(0, y + 30, inner.width - 120, 48), order.Status);
                    if (Widgets.ButtonText(new Rect(inner.width - 115, y + 10, 110, 32), order.Maintain ? "Выключить" : "Отменить")) order.Cancel();
                }
            }
            finally { Widgets.EndScrollView(); }
        }
        private void DrawCatalogue(Rect rect, Building_StorageController controller, CraftMap manager, FactoryCraftMap factory)
        {
            CraftCatalogue current = manager.Catalogue(controller);
            int now = Find.TickManager.TicksGame;
            if (current != catalogue || lastSearch != search || now < lastRefresh || now - lastRefresh >= 250)
            {
                catalogue = current; lastSearch = search; lastRefresh = now; problems.Clear();
                filtered = catalogue.Tables.Keys.Select(r => new Row { Recipe = r })
                    .Concat(factory.Catalog(controller).GroupBy(r => r.Def.defName).Select(g => new Row { Factory = g.First() }))
                    .Where(r => Matches(r.Title, r.Id)).OrderBy(r => r.Title).ToList();
            }
            Widgets.Label(new Rect(0, 37, rect.width, 40), "Все рецепты подключённых станков и фабрик. Шаблоны не нужны.");
            Rect outer = new Rect(0, 80, rect.width, rect.height - 80);
            Rect inner = new Rect(0, 0, rect.width - 20, Math.Max(outer.height, filtered.Count * 82));
            Widgets.BeginScrollView(outer, ref scroll, inner);
            try
            {
                if (filtered.Count == 0) Widgets.Label(new Rect(0, 0, inner.width, 65), "Рецепты не найдены. Подключите крафтер к сети и поставьте вплотную к производственной машине.");
                int first = Math.Max(0, (int)scroll.y / 82 - 1), last = Math.Min(filtered.Count, first + (int)outer.height / 82 + 3);
                for (int i = first; i < last; i++)
                {
                    Row row = filtered[i]; float y = i * 82;
                    if (row.Product != null) Widgets.DefIcon(new Rect(0, y + 5, 34, 34), row.Product);
                    Widgets.Label(new Rect(43, y, inner.width - 270, 30), row.Title);
                    if (row.Recipe != null) DrawRecipe(row.Recipe, y, inner.width, controller, manager);
                    else DrawFactory(row.Factory, y, inner.width, controller, factory);
                }
            }
            finally { Widgets.EndScrollView(); }
        }
        private void DrawRecipe(RecipeDef recipe, float y, float width, Building_StorageController controller, CraftMap manager)
        {
            Building_WorkTable table = catalogue.Tables[recipe].First();
            if (!problems.TryGetValue(recipe, out string problem))
            { CraftRecipes.Select(controller, table, recipe, out problem, out _); problems[recipe] = problem; }
            Widgets.Label(new Rect(43, y + 30, width - 270, 48), problem ?? table.LabelCap + " · ингредиенты доступны");
            TooltipHandler.TipRegion(new Rect(0, y, width - 230, 78),
                string.Join("\n", recipe.ingredients.Select(ingredient => ingredient.SummaryFor(recipe))) + "\nСтанок: " + table.LabelCap);
            if (Widgets.ButtonText(new Rect(width - 220, y + 8, 105, 32), "Заказать…"))
                Find.WindowStack.Add(new Dialog_StorageCount(recipe.LabelCap, 1000000, recipe.products[0].count, count => manager.Add(controller, recipe, count, false)));
            if (Widgets.ButtonText(new Rect(width - 110, y + 8, 105, 32), "Минимум…"))
                Find.WindowStack.Add(new Dialog_StorageCount(recipe.LabelCap, 1000000, recipe.products[0].count, count => manager.Add(controller, recipe, count, true)));
        }
        private static void DrawFactory(FactoryRecipe recipe, float y, float width, Building_StorageController controller, FactoryCraftMap manager)
        {
            Widgets.Label(new Rect(43, y + 30, width - 270, 48), recipe.Problem ?? recipe.Machine.LabelCap + (recipe.Primary == null || recipe.NetOutput ? " · заказ в циклах" : " · заказ в штуках"));
            if (recipe.Problem == null && Widgets.ButtonText(new Rect(width - 220, y + 8, 105, 32), "Заказать…"))
                Find.WindowStack.Add(new FactoryOrderWindow(manager, controller, recipe, false));
            if (recipe.Problem == null && recipe.CanMaintain && Widgets.ButtonText(new Rect(width - 110, y + 8, 105, 32), "Минимум…"))
                Find.WindowStack.Add(new FactoryOrderWindow(manager, controller, recipe, true));
        }
    }
}
