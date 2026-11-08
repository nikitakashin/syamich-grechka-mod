using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.Storage
{
    public sealed class Dialog_StorageCount : Window
    {
        private int count;
        private string buffer;
        private readonly int maximum;
        private readonly Action<int> accept;
        private readonly string label;
        public override Vector2 InitialSize { get { return new Vector2(430, 200); } }
        public Dialog_StorageCount(string label, int maximum, int initial, Action<int> accept)
        {
            this.label = label; this.maximum = Math.Max(1, maximum); count = Math.Min(this.maximum, Math.Max(1, initial));
            buffer = count.ToString(); this.accept = accept; doCloseX = true; absorbInputAroundWindow = true;
        }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width - 25, 40), label);
            Widgets.Label(new Rect(0, 45, rect.width, 25), StorageText.Get("GrechkaStorage_Text001") + maximum.ToString("N0"));
            Widgets.TextFieldNumeric(new Rect(0, 77, rect.width, 32), ref count, ref buffer, 1, maximum);
            if (Widgets.ButtonText(new Rect(0, rect.height - 38, rect.width, 34), StorageText.Get("GrechkaStorage_Text002")))
            { accept(Math.Min(maximum, Math.Max(1, count))); Close(); }
        }
    }

    public sealed class Dialog_StorageImport : Window
    {
        private readonly Building_StorageController controller;
        private readonly List<Thing> sources;
        private readonly Dictionary<Thing, string> problems;
        private string search = "";
        private string previousSearch;
        private List<Thing> filtered = new List<Thing>();
        private Vector2 scroll;
        public override Vector2 InitialSize { get { return new Vector2(800, 600); } }
        public Dialog_StorageImport(Building_StorageController controller)
        {
            this.controller = controller; doCloseX = true; draggable = true;
            sources = controller.Map.listerThings.AllThings.Where(t => t.Spawned && t.def.category == ThingCategory.Item
                && !t.Position.Fogged(controller.Map) && (t.Faction == null || t.Faction == Faction.OfPlayer)
                && !t.IsForbidden(Faction.OfPlayer) && !t.IsBurning())
                .OrderBy(t => t.LabelCapNoCount.ToString()).ThenBy(t => t.thingIDNumber).ToList();
            problems = sources.ToDictionary(t => t, DiskItemPolicy.Problem);
        }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width - 25, 30), StorageText.Get("GrechkaStorage_Text003"));
            search = Widgets.TextField(new Rect(0, 38, rect.width - 225, 30), search);
            if (previousSearch != search)
            { previousSearch = search; filtered = sources.Where(t => t.LabelCapNoCount.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList(); }
            if (Widgets.ButtonText(new Rect(rect.width - 215, 38, 215, 30), StorageText.Get("GrechkaStorage_Text004")))
            {
                if (controller.Spawned)
                    foreach (Thing thing in filtered.Where(t => !t.Destroyed && t.Spawned && problems[t] == null))
                        controller.Queue(thing, thing.stackCount, StorageOrderKind.Import);
            }
            Rect outer = new Rect(0, 80, rect.width, rect.height - 85);
            Rect inner = new Rect(0, 0, outer.width - 20, Math.Max(outer.height, filtered.Count * 42));
            Widgets.BeginScrollView(outer, ref scroll, inner);
            try
            {
                int first = Math.Max(0, (int)(scroll.y / 42) - 1), last = Math.Min(filtered.Count, first + (int)(outer.height / 42) + 3);
                for (int i = first; i < last; i++)
                {
                    Thing thing = filtered[i]; float y = i * 42;
                    if (thing.Destroyed || !thing.Spawned) continue;
                    Widgets.ThingIcon(new Rect(0, y, 34, 34), thing);
                    Widgets.Label(new Rect(42, y + 5, inner.width - 170, 30), thing.LabelCapNoCount + " ×" + thing.stackCount.ToString("N0"));
                    string problem = problems[thing];
                    Rect button = new Rect(inner.width - 122, y + 3, 120, 32);
                    bool enabled = GUI.enabled;
                    GUI.enabled = enabled && problem == null;
                    bool clicked = Widgets.ButtonText(button, problem == null ? StorageText.Get("GrechkaStorage_Text005") : StorageText.Get("GrechkaStorage_Text006"));
                    GUI.enabled = enabled;
                    if (problem != null) TooltipHandler.TipRegion(button, problem);
                    if (clicked)
                        Find.WindowStack.Add(new Dialog_StorageCount(thing.LabelCapNoCount, thing.stackCount, thing.stackCount,
                            count => { if (controller.Spawned) controller.Queue(thing, count, StorageOrderKind.Import); }));
                }
            }
            finally { Widgets.EndScrollView(); }
        }
    }

    public sealed class StorageTerminalUI
    {
        private sealed class Entry
        {
            internal Thing Thing;
            internal StorageDisk Disk;
            internal StorageBuffer Buffer;
            internal string Label;
        }
        private Vector2 scroll;
        private Vector2 diskScroll;
        private Vector2 orderScroll;
        private string search = "";
        private string previousSearch;
        private int filter;
        private int previousFilter = -1;
        private List<(StorageDisk Disk, long Version)> version = new List<(StorageDisk, long)>();
        private long recoveryVersion = -1, coldVersion = -1;
        private List<Entry> entries = new List<Entry>();
        private List<Entry> filtered = new List<Entry>();
        public void Draw(Rect rect, Building_StorageController controller, int tab)
        {
            GUI.BeginGroup(rect);
            try
            {
            Rect local = new Rect(0, 0, rect.width, rect.height);
            if (tab == 0) DrawItems(local, controller);
            else if (tab == 1) DrawDisks(local, controller);
            else DrawOrders(local, controller);
            }
            finally { GUI.EndGroup(); }
        }
        private void Rebuild(Building_StorageController controller, List<StorageDisk> disks)
        {
            bool same = version.Count == disks.Count && version.Select((v, i) => v.Disk == disks[i] && v.Version == disks[i].Version).All(v => v)
                && recoveryVersion == controller.Recovery.Version && coldVersion == controller.ColdRecovery.Version;
            if (same) return;
            version = disks.Select(d => (d, d.Version)).ToList();
            recoveryVersion = controller.Recovery.Version; coldVersion = controller.ColdRecovery.Version;
            entries = disks.SelectMany(d => d.Items.Select(t => new Entry { Thing = t, Disk = d, Label = t.LabelCapNoCount.ToString() })).ToList();
            foreach (StorageBuffer buffer in new[] { controller.Recovery, controller.ColdRecovery })
                entries.AddRange(buffer.Contents.Select(t => new Entry { Thing = t, Buffer = buffer, Label = t.LabelCapNoCount.ToString() }));
            entries = entries.OrderBy(e => e.Label).ThenBy(e => e.Thing.thingIDNumber).ToList();
            previousSearch = null;
        }
        private bool Matches(Entry entry)
        {
            Thing inner = entry.Thing.GetInnerIfMinified();
            bool food = (inner as ThingWithComps)?.GetComp<CompRottable>() != null;
            bool gear = inner is Apparel || inner.def.IsWeapon;
            return entry.Label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                && (filter == 0 || filter == 1 && food || filter == 2 && gear || filter == 3 && !food && !gear);
        }
        private void DrawItems(Rect rect, Building_StorageController controller)
        {
            List<StorageDisk> disks = controller.ActiveDisks();
            if (StorageArt.Button(new Rect(0, 0, 185, 32), StorageText.Get("GrechkaStorage_Text007"), "StorageInput") && controller.Online())
                Find.WindowStack.Add(new Dialog_StorageImport(controller));
            if (StorageArt.Button(new Rect(195, 0, 155, 32), StorageText.Get("GrechkaStorage_Text008"), "StorageOptimize"))
                Messages.Message(StorageOptimization.Optimize(controller), MessageTypeDefOf.NeutralEvent);
            if (controller.HasRecovery && Widgets.ButtonText(new Rect(360, 0, rect.width - 360, 32), StorageText.Get("GrechkaStorage_Text009")))
                StorageOptimization.RestoreRecovery(controller);
            long total = disks.Sum(d => d.Space.ItemCount);
            var crafters = controller.Node.Manager.ViewFor(controller.Node).Devices.OfType<Building_StorageCrafter>();
            long held = crafters.SelectMany(c => c.Contents.Concat(FactoryCraftMap.HeldContents(c))).Sum(t => (long)t.stackCount);
            Widgets.Label(new Rect(0, 39, rect.width, 26), StorageText.Get("GrechkaStorage_Text010") + total.ToString("N0")
                + StorageText.Get("GrechkaStorage_Text011") + controller.Output.Contents.Sum(t => (long)t.stackCount).ToString("N0"));
            StorageArt.Status(new Rect(0, 67, rect.width, 26), StorageText.Get("GrechkaStorage_Text012") + held.ToString("N0"), "StorageReserved",
                StorageText.Get("GrechkaStorage_Text013"));
            search = Widgets.TextField(new Rect(0, 101, rect.width - 155, 30), search);
            string[] filters = { StorageText.Get("GrechkaStorage_Text014"), StorageText.Get("GrechkaStorage_Text015"), StorageText.Get("GrechkaStorage_Text016"), StorageText.Get("GrechkaStorage_Text017") };
            if (Widgets.ButtonText(new Rect(rect.width - 145, 101, 145, 30), filters[filter])) filter = (filter + 1) % filters.Length;
            Rebuild(controller, disks);
            if (previousSearch != search || previousFilter != filter)
            { previousSearch = search; previousFilter = filter; filtered = entries.Where(Matches).ToList(); }
            Rect outer = new Rect(0, 141, rect.width, rect.height - 146);
            Rect inner = new Rect(0, 0, outer.width - 20, Math.Max(outer.height, filtered.Count * 62));
            Widgets.BeginScrollView(outer, ref scroll, inner);
            try
            {
                int first = Math.Max(0, (int)(scroll.y / 62) - 1), last = Math.Min(filtered.Count, first + (int)(outer.height / 62) + 3);
                for (int i = first; i < last; i++)
                {
                    Entry entry = filtered[i]; Thing thing = entry.Thing; float y = i * 62;
                    if (thing.Destroyed) continue;
                    Widgets.ThingIcon(new Rect(0, y + 7, 38, 38), thing);
                    Widgets.Label(new Rect(46, y + 2, inner.width - 265, 27), entry.Label);
                    string detail = entry.Buffer != null ? StorageText.Get("GrechkaStorage_Text018") : entry.Disk.LabelCap.ToString();
                    if (thing.def.useHitPoints) detail += "; HP " + thing.HitPoints + "/" + thing.MaxHitPoints;
                    CompRottable rot = (thing as ThingWithComps)?.GetComp<CompRottable>();
                    if (rot != null) detail += "; " + rot.Stage;
                    Widgets.Label(new Rect(46, y + 28, inner.width - 265, 28), detail);
                    Widgets.Label(new Rect(inner.width - 210, y + 14, 95, 30), thing.stackCount.ToString("N0"));
                    if (StorageArt.Button(new Rect(inner.width - 110, y + 11, 108, 32), StorageText.Get("GrechkaStorage_Text019"), "StorageOutput"))
                    {
                        int max = (int)Math.Min(thing.stackCount, (long)(Building_StorageController.OutputStacks - controller.Output.Contents.Count) * Math.Max(1, thing.def.stackLimit));
                        if (max <= 0) Messages.Message(StorageText.Get("GrechkaStorage_Text020"), MessageTypeDefOf.RejectInput);
                        else Find.WindowStack.Add(new Dialog_StorageCount(entry.Label, max, Math.Min(max, thing.def.stackLimit), count =>
                        {
                            if (!controller.Spawned) return;
                            int moved = entry.Buffer == null ? controller.Withdraw(entry.Disk, thing, count) : controller.WithdrawRecovery(entry.Buffer, thing, count);
                            if (moved == 0) Messages.Message(StorageText.Get("GrechkaStorage_Text021"), MessageTypeDefOf.RejectInput);
                        }));
                    }
                }
            }
            finally { Widgets.EndScrollView(); }
        }
        private void DrawDisks(Rect rect, Building_StorageController controller)
        {
            List<Building_DiskRack> racks = controller.Node.Manager.ViewFor(controller.Node).Devices.OfType<Building_DiskRack>().ToList();
            Rect inner = new Rect(0, 0, rect.width - 20, Math.Max(rect.height, racks.Sum(r => 55 + r.Disks.Count() * 48)));
            Widgets.BeginScrollView(rect, ref diskScroll, inner);
            try
            {
                float y = 0;
                foreach (Building_DiskRack rack in racks) { y = DrawRack(inner.width, y, rack); }
                if (racks.Count == 0) Widgets.Label(new Rect(0, 0, inner.width, 55), StorageText.Get("GrechkaStorage_Text022"));
            }
            finally { Widgets.EndScrollView(); }
        }
        internal static float DrawRack(float width, float y, Building_DiskRack rack)
        {
            Widgets.Label(new Rect(0, y + 5, width - 165, 35), rack.LabelCap + " — " + rack.Disks.Count() + "/" + Building_DiskRack.Slots
                + (DeviceStatus.Ready(rack) ? "" : StorageText.Get("GrechkaStorage_Text023")));
            if (StorageArt.Button(new Rect(width - 160, y, 158, 32), StorageText.Get("GrechkaStorage_Text024"), "StorageEmptySlot"))
            {
                var options = rack.Map.listerThings.AllThings.OfType<StorageDisk>().Where(d => d.Spawned && d.Map == rack.Map
                    && !d.Position.Fogged(rack.Map) && !d.IsForbidden(Faction.OfPlayer)).OrderBy(d => d.Capacity)
                    .Select(d => new FloatMenuOption(d.LabelCap + StorageText.Get("GrechkaStorage_Text025") + d.Space.ItemCount.ToString("N0"), () => rack.RequestInstall(d))).ToList();
                if (options.Count == 0) options.Add(new FloatMenuOption(StorageText.Get("GrechkaStorage_Text026"), null));
                Find.WindowStack.Add(new FloatMenu(options));
            }
            y += 48;
            foreach (StorageDisk disk in rack.Disks.ToList())
            {
                Widgets.ThingIcon(new Rect(0, y + 3, 34, 34), disk);
                Widgets.Label(new Rect(44, y, width - 165, 24), disk.LabelCap + StorageText.Get("GrechkaStorage_Text027") + disk.Space.Types + "/63");
                bool full = disk.Space.IsFull;
                StorageArt.Status(new Rect(44, y + 23, width - 165, 25), disk.Space.ItemCount.ToString("N0") + StorageText.Get("GrechkaStorage_Text028")
                    + disk.Space.UsedBytes.ToString("N0") + "/" + disk.Capacity.ToString("N0") + StorageText.Get("GrechkaStorage_Text029"), full ? "StorageDiskFull" : null,
                    full ? StorageText.Get("GrechkaStorage_Text030")
                        : disk.Space.Types >= DiskSpace.TypeLimit ? StorageText.Get("GrechkaStorage_Text031") : null);
                bool requested = rack.RemoveRequests.Contains(disk);
                if (StorageArt.Button(new Rect(width - 110, y + 5, 108, 32), requested ? StorageText.Get("GrechkaStorage_Text032") : StorageText.Get("GrechkaStorage_Text033"), requested ? "StorageCraftCancel" : "StorageOutput"))
                { if (requested) rack.CancelRequests(disk); else rack.RequestRemove(disk); }
                y += 48;
            }
            return y + 7;
        }
        private void DrawOrders(Rect rect, Building_StorageController controller)
        {
            controller.Prune(); List<StorageOrder> orders = controller.Orders.ToList();
            Widgets.Label(new Rect(0, 0, rect.width, 48), StorageText.Get("GrechkaStorage_Text034"));
            Rect outer = new Rect(0, 56, rect.width, rect.height - 61), inner = new Rect(0, 0, rect.width - 20, Math.Max(rect.height - 61, orders.Count * 42));
            Widgets.BeginScrollView(outer, ref orderScroll, inner);
            try
            {
                float y = 0;
                foreach (StorageOrder order in orders)
                {
                    Widgets.Label(new Rect(0, y + 5, inner.width - 115, 32), order.Source.LabelCapNoCount + " ×" + order.Count.ToString("N0")
                        + (order.Source.Spawned ? "" : StorageText.Get("GrechkaStorage_Text035")));
                    if (StorageArt.Button(new Rect(inner.width - 110, y + 2, 108, 32), StorageText.Get("GrechkaStorage_Text032"), "StorageCraftCancel")) controller.Cancel(order.Id);
                    y += 42;
                }
            }
            finally { Widgets.EndScrollView(); }
        }
    }

    public sealed class Window_DiskRack : Window
    {
        private readonly Building_DiskRack rack;
        private Vector2 scroll;
        public override Vector2 InitialSize { get { return new Vector2(760, 620); } }
        public Window_DiskRack(Building_DiskRack rack) { this.rack = rack; doCloseX = true; draggable = true; }
        public override void DoWindowContents(Rect rect)
        {
            if (!rack.Spawned || rack.Faction != Faction.OfPlayer) { Widgets.Label(rect, StorageText.Get("GrechkaStorage_Text036")); return; }
            Rect inner = new Rect(0, 0, rect.width - 20, Math.Max(rect.height, 55 + rack.Disks.Count() * 48));
            Widgets.BeginScrollView(rect, ref scroll, inner);
            try
            {
                StorageTerminalUI.DrawRack(inner.width, 0, rack);
            }
            finally { Widgets.EndScrollView(); }
        }
    }
}
