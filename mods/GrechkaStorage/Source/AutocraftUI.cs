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
            public ThingDef Product => Recipe != null ? Recipe.products?.FirstOrDefault()?.thingDef : Factory.Primary;
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
        private List<(StorageDisk Disk, long Version)> stockVersion = new List<(StorageDisk, long)>();
        private List<Row> filtered = new List<Row>();
        private sealed class Availability
        {
            internal Building_WorkTable Table;
            internal string Text, Icon;
        }
        private readonly Dictionary<RecipeDef, Availability> problems = new Dictionary<RecipeDef, Availability>();
        private bool Matches(string title, string id = null) => (title ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
            || id != null && id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        public void Draw(Rect rect, Building_StorageController controller)
        {
            GUI.BeginGroup(rect);
            try
            {
                CraftMap manager = controller.Map.GetComponent<CraftMap>();
                FactoryCraftMap factory = controller.Map.GetComponent<FactoryCraftMap>();
                if (StorageArt.Button(new Rect(0, 0, 180, 30), showOrders ? StorageText.Get("GrechkaStorage_Text066") : StorageText.Get("GrechkaStorage_Text067"), showOrders ? "StorageCraftOrder" : "StorageCraftQueue"))
                { showOrders = !showOrders; scroll = Vector2.zero; }
                search = Widgets.TextField(new Rect(190, 0, Math.Max(100, rect.width - 365), 30), search);
                if (Widgets.ButtonText(new Rect(rect.width - 165, 0, 165, 30), StorageText.Get("GrechkaStorage_Text068"))) factory.Recover(controller);
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
                orders.Add(new OrderRow { Title = order.Recipe.LabelCap + (order.SelectedInputOrder
                        ? " — " + (order.SelectedInput?.LabelCapNoCount.ToString() ?? StorageText.Get("GrechkaStorage_Text069"))
                        : (order.Maintain ? StorageText.Get("GrechkaStorage_Text070") : StorageText.Get("GrechkaStorage_Text071")) + order.Remaining),
                    Status = order.Status, Maintain = order.Maintain, Cancel = () => manager.Cancel(order) });
            }
            foreach (FactoryOrder order in factory.Orders(controller))
            {
                if (!Matches(order.Title, order.ProcessName)) continue;
                orders.Add(new OrderRow { Title = (order.Title ?? order.ProcessName) + (order.Maintain ? StorageText.Get("GrechkaStorage_Text070") : StorageText.Get("GrechkaStorage_Text071")) + order.Remaining + (order.CountCycles ? StorageText.Get("GrechkaStorage_Text072") : StorageText.Get("GrechkaStorage_Text073")),
                    Status = order.Status, Maintain = order.Maintain, Cancel = () => factory.Cancel(order) });
            }
            Rect outer = new Rect(0, 40, rect.width, rect.height - 40);
            Rect inner = new Rect(0, 0, rect.width - 20, Math.Max(outer.height, orders.Count * 82));
            Widgets.BeginScrollView(outer, ref scroll, inner);
            try
            {
                if (orders.Count == 0) Widgets.Label(new Rect(0, 0, inner.width, 60), StorageText.Get("GrechkaStorage_Text074"));
                int first = Math.Max(0, (int)scroll.y / 82 - 1), last = Math.Min(orders.Count, first + (int)outer.height / 82 + 3);
                for (int i = first; i < last; i++)
                {
                    OrderRow order = orders[i]; float y = i * 82;
                    Widgets.Label(new Rect(0, y, inner.width - 120, 30), order.Title);
                    Widgets.Label(new Rect(0, y + 30, inner.width - 120, 48), order.Status);
                    if (StorageArt.Button(new Rect(inner.width - 115, y + 10, 110, 32), order.Maintain ? StorageText.Get("GrechkaStorage_Text075") : StorageText.Get("GrechkaStorage_Text032"), "StorageCraftCancel")) order.Cancel();
                }
            }
            finally { Widgets.EndScrollView(); }
        }
        private void DrawCatalogue(Rect rect, Building_StorageController controller, CraftMap manager, FactoryCraftMap factory)
        {
            CraftCatalogue current = manager.Catalogue(controller);
            int now = Find.TickManager.TicksGame;
            List<StorageDisk> disks = controller.ActiveDisks();
            if (stockVersion.Count != disks.Count || stockVersion.Where((v, i) => v.Disk != disks[i] || v.Version != disks[i].Version).Any())
            {
                stockVersion = disks.Select(d => (d, d.Version)).ToList();
                problems.Clear();
            }
            if (current != catalogue || lastSearch != search || now < lastRefresh || now - lastRefresh >= 250)
            {
                catalogue = current; lastSearch = search; lastRefresh = now; problems.Clear();
                filtered = catalogue.Tables.Keys.Select(r => new Row { Recipe = r })
                    .Concat(factory.Catalog(controller).GroupBy(r => r.Def.defName).Select(g => new Row { Factory = g.First() }))
                    .Where(r => Matches(r.Title, r.Id)).OrderBy(r => r.Title).ToList();
            }
            Widgets.Label(new Rect(0, 37, rect.width, 40), StorageText.Get("GrechkaStorage_Text076"));
            Rect outer = new Rect(0, 80, rect.width, rect.height - 80);
            Rect inner = new Rect(0, 0, rect.width - 20, Math.Max(outer.height, filtered.Count * 82));
            Widgets.BeginScrollView(outer, ref scroll, inner);
            try
            {
                if (filtered.Count == 0) Widgets.Label(new Rect(0, 0, inner.width, 65), StorageText.Get("GrechkaStorage_Text077"));
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
            if (DynamicCraftRecipes.Supported(recipe))
            {
                Widgets.Label(new Rect(43, y + 30, width - 270, 48), StorageText.Get("GrechkaStorage_Text078"));
                if (StorageArt.Button(new Rect(width - 220, y + 8, 215, 32), StorageText.Get("GrechkaStorage_Text079"), "StorageInput"))
                    Find.WindowStack.Add(new Dialog_DynamicCraftInput(controller, recipe, catalogue.Tables[recipe]));
                return;
            }
            if (!problems.TryGetValue(recipe, out Availability state))
            {
                state = new Availability { Table = catalogue.Tables[recipe].First(), Text = StorageText.Get("GrechkaStorage_Text080"), Icon = "StorageMachineUnavailable" };
                if (!controller.Online()) { state.Text = StorageText.Get("GrechkaStorage_Text063"); state.Icon = "StorageNoPower"; }
                else
                {
                    var readyTables = controller.Node.Manager.ViewFor(controller.Node).Devices.OfType<Building_StorageCrafter>()
                        .Where(c => c.Spawned && DeviceStatus.Ready(c)).SelectMany(c => c.Tables()).ToHashSet();
                    foreach (Building_WorkTable candidate in catalogue.Tables[recipe])
                    {
                        if (!readyTables.Contains(candidate) || !DeviceStatus.Ready(candidate) || !candidate.CurrentlyUsableForBills()) continue;
                        CraftRecipes.Select(controller, candidate, recipe, out string problem, out _);
                        state.Table = candidate; state.Text = problem ?? candidate.LabelCap + StorageText.Get("GrechkaStorage_Text081");
                        state.Icon = problem == null ? "StorageNetworkOnline" : "StorageMissingIngredients";
                        if (problem == null) break;
                    }
                }
                problems[recipe] = state;
            }
            StorageArt.Status(new Rect(43, y + 30, width - 270, 48), state.Text, state.Icon, state.Text);
            TooltipHandler.TipRegion(new Rect(0, y, width - 230, 78),
                string.Join("\n", recipe.ingredients.Select(ingredient => ingredient.SummaryFor(recipe))) + StorageText.Get("GrechkaStorage_Text082") + state.Table.LabelCap);
            if (StorageArt.Button(new Rect(width - 220, y + 8, 105, 32), StorageText.Get("GrechkaStorage_Text083"), "StorageCraftOrder"))
                Find.WindowStack.Add(new Dialog_StorageCount(recipe.LabelCap, 1000000, recipe.products[0].count, count => manager.Add(controller, recipe, count, false)));
            if (StorageArt.Button(new Rect(width - 110, y + 8, 105, 32), StorageText.Get("GrechkaStorage_Text084"), "StorageMaintainStock"))
                Find.WindowStack.Add(new Dialog_StorageCount(recipe.LabelCap, 1000000, recipe.products[0].count, count => manager.Add(controller, recipe, count, true)));
        }
        private static void DrawFactory(FactoryRecipe recipe, float y, float width, Building_StorageController controller, FactoryCraftMap manager)
        {
            Widgets.Label(new Rect(43, y + 30, width - 270, 48), recipe.Problem ?? recipe.Machine.LabelCap + (recipe.Primary == null || recipe.NetOutput ? StorageText.Get("GrechkaStorage_Text085") : StorageText.Get("GrechkaStorage_Text086")));
            if (recipe.Problem == null && StorageArt.Button(new Rect(width - 220, y + 8, 105, 32), StorageText.Get("GrechkaStorage_Text083"), "StorageCraftOrder"))
                Find.WindowStack.Add(new FactoryOrderWindow(manager, controller, recipe, false));
            if (recipe.Problem == null && recipe.CanMaintain && StorageArt.Button(new Rect(width - 110, y + 8, 105, 32), StorageText.Get("GrechkaStorage_Text084"), "StorageMaintainStock"))
                Find.WindowStack.Add(new FactoryOrderWindow(manager, controller, recipe, true));
        }
    }
}
