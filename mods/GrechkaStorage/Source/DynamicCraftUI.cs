using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Storage
{
    public sealed class Dialog_DynamicCraftInput : Window
    {
        private readonly Building_StorageController controller;
        private readonly RecipeDef recipe;
        private readonly List<Building_WorkTable> tables;
        private readonly List<Thing> items;
        private List<Thing> filtered;
        private string search = "", previousSearch;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(740, 570);
        public Dialog_DynamicCraftInput(Building_StorageController controller, RecipeDef recipe, List<Building_WorkTable> tables)
        {
            this.controller = controller; this.recipe = recipe; this.tables = tables.ToList();
            doCloseX = true; absorbInputAroundWindow = true; preventCameraMotion = false;
            items = controller.ActiveDisks().SelectMany(d => d.Items)
                .Where(t => tables.Any(table => DynamicCraftRecipes.TryCount(recipe, table, t, out _, out _)))
                .OrderBy(t => t.LabelCapNoCount.ToString()).ThenBy(t => t.thingIDNumber).ToList();
        }
        public override void DoWindowContents(Rect rect)
        {
            if (!controller.Spawned || controller.Faction != Faction.OfPlayer) { Close(); return; }
            Widgets.Label(new Rect(0, 0, rect.width - 32, 32), recipe.LabelCap);
            Widgets.Label(new Rect(0, 36, rect.width, 64),
                StorageText.Get("GrechkaStorage_Text155"));
            search = Widgets.TextField(new Rect(0, 104, rect.width, 30), search);
            if (search != previousSearch)
            {
                previousSearch = search; scroll = Vector2.zero;
                filtered = items.Where(t => !t.Destroyed && t.LabelCapNoCount.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            Rect outer = new Rect(0, 144, rect.width, rect.height - 144);
            Rect inner = new Rect(0, 0, outer.width - 20, Math.Max(outer.height, filtered.Count * 70));
            Widgets.BeginScrollView(outer, ref scroll, inner);
            try
            {
                if (filtered.Count == 0) Widgets.Label(new Rect(0, 0, inner.width, 60), StorageText.Get("GrechkaStorage_Text156"));
                int first = Math.Max(0, (int)scroll.y / 70 - 1), last = Math.Min(filtered.Count, first + (int)outer.height / 70 + 3);
                for (int i = first; i < last; i++)
                {
                    Thing item = filtered[i]; float y = i * 70;
                    if (item.Destroyed) continue;
                    Building_WorkTable table = tables.FirstOrDefault(t => t.Spawned && DynamicCraftRecipes.TryCount(recipe, t, item, out _, out _));
                    if (table == null) continue;
                    DynamicCraftRecipes.TryCount(recipe, table, item, out int count, out _);
                    Widgets.ThingIcon(new Rect(0, y + 4, 42, 42), item);
                    Widgets.Label(new Rect(50, y, inner.width - 200, 30), item.LabelCapNoCount);
                    Widgets.Label(new Rect(50, y + 30, inner.width - 200, 32), StorageText.Get("GrechkaStorage_Text157") + count.ToString("N0")
                        + (item.def.useHitPoints ? " · HP " + item.HitPoints + "/" + item.MaxHitPoints : ""));
                    if (StorageArt.Button(new Rect(inner.width - 140, y + 8, 136, 32), StorageText.Get("GrechkaStorage_Text158"), "StorageCraftOrder"))
                    {
                        if (controller.Map.GetComponent<CraftMap>().AddSelected(controller, recipe, item) != null) { Close(); return; }
                        Messages.Message(StorageText.Get("GrechkaStorage_Text159"), MessageTypeDefOf.RejectInput);
                    }
                }
            }
            finally { Widgets.EndScrollView(); }
        }
    }
}
