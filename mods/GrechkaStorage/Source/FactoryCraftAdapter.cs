using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Storage
{
    // Optional bridge: never substitutes RecipeDef for PipeSystem.ProcessDef.
    [StaticConstructorOnStartup]
    public static class FactoryCraftAdapter
    {
        internal static readonly Type ProcessorType = AccessTools.TypeByName("PipeSystem.CompAdvancedResourceProcessor");
        internal static readonly Type ProcessType = AccessTools.TypeByName("PipeSystem.Process");
        internal static readonly Type ResultType = AccessTools.TypeByName("PipeSystem.ProcessDef+Result");
        internal static bool Installed;
        public static string Status { get; private set; } = "PipeSystem не установлен";
        [ThreadStatic] static Capture attempt;
        sealed class Capture { internal FactoryTask task; internal int amount; internal List<(Thing thing, int count)> placements = new List<(Thing, int)>(); }
        static FactoryCraftAdapter()
        {
            if (ProcessorType == null || ProcessType == null || ResultType == null) return;
            try
            {
                MethodInfo spawn = AccessTools.Method(ProcessType, "TrySpawnAtCell", new[] { ResultType, typeof(IntVec3), typeof(Map), typeof(Thing).MakeByRefType() });
                MethodInfo count = AccessTools.Method(ResultType, "GetCount", new[] { ProcessType });
                MethodInfo reset = AccessTools.Method(ProcessType, "ResetProcess", new[] { typeof(bool) });
                MethodInfo hopper = AccessTools.Method(ProcessType, "FactoryHopperDetected");
                if (spawn == null || count == null || reset == null || hopper == null) { Status = "Версия PipeSystem не поддержана: сигнатуры изменились"; return; }
                var harmony = new Harmony("grechka.storage.factory");
                harmony.Patch(spawn, prefix: new HarmonyMethod(typeof(FactoryCraftAdapter), nameof(Begin)), postfix: new HarmonyMethod(typeof(FactoryCraftAdapter), nameof(End)) { after = new[] { "grechka.quests.factory" } }, finalizer: new HarmonyMethod(typeof(FactoryCraftAdapter), nameof(FinalizeAttempt)));
                MethodInfo placement = AccessTools.Method(typeof(GenPlace), nameof(GenPlace.TryPlaceThing), new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Thing).MakeByRefType(), typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int) });
                if (placement != null) harmony.Patch(placement, prefix: new HarmonyMethod(typeof(FactoryCraftAdapter), nameof(ObservePlacements)));
                harmony.Patch(count, postfix: new HarmonyMethod(typeof(FactoryCraftAdapter), nameof(Count)) { priority = Priority.Last });
                harmony.Patch(reset, postfix: new HarmonyMethod(typeof(FactoryCraftAdapter), nameof(Reset)));
                harmony.Patch(hopper, prefix: new HarmonyMethod(typeof(FactoryCraftAdapter), nameof(HasInterface)));
                Installed = true; Status = "PipeSystem: штатные процессы и физические выходы";
            }
            catch (Exception e) { Status = "Ошибка подключения Factory: " + e.Message; Log.Error("[Storage] " + Status); }
        }
        internal static object Get(object instance, string name)
        {
            if (instance == null) return null;
            Type type = instance.GetType();
            PropertyInfo property = AccessTools.Property(type, name);
            return property != null ? property.GetValue(instance) : AccessTools.Field(type, name)?.GetValue(instance);
        }
        internal static T Get<T>(object instance, string name, T fallback = default) => Get(instance, name) is T value ? value : fallback;
        internal static object Call(object instance, string method, params object[] args) => AccessTools.Method(instance.GetType(), method).Invoke(instance, args);
        internal static IEnumerable<object> List(object value) => value is IEnumerable list ? list.Cast<object>() : Enumerable.Empty<object>();
        internal static object Comp(ThingWithComps machine) => ProcessorType == null ? null : machine?.AllComps?.FirstOrDefault(c => ProcessorType.IsInstanceOfType(c));
        public static bool IsFactory(ThingWithComps machine) => Installed && Comp(machine) != null;
        public static IEnumerable<FactoryRecipe> Recipes(ThingWithComps machine)
        {
            object comp = Comp(machine); if (!Installed || comp == null) yield break;
            object props = Get(comp, "Props");
            foreach (object process in List(Get(props, "processes")))
            {
                if (!(process is Def def)) continue;
                string reason = null;
                try
                {
                    MethodInfo visible = AccessTools.Method(comp.GetType(), "<get_ProcessesOptions>b__47_0");
                    if (visible != null && !(bool)visible.Invoke(comp, new[] { process })) continue;
                    if (List(Get(process, "researchPrerequisites")).OfType<ResearchProjectDef>().Any(r => !r.IsFinished)) reason = "Нужно исследование";
                    if (!List(Get(process, "results")).Any()) reason = "У процесса нет результата";
                }
                catch (Exception e) { reason = "Не удалось проверить условия: " + e.GetBaseException().Message; }
                var first = List(Get(process, "results")).FirstOrDefault();
                var primary = Get<ThingDef>(first, "thing");
                bool dynamic = Get<Type>(first, "workerClass") != null;
                string title = Get<string>(process, "labelOverride");
                if (string.IsNullOrEmpty(title)) title = !string.IsNullOrEmpty(def.label) ? def.LabelCap.ToString() : primary?.LabelCap.ToString() ?? def.defName;
                yield return new FactoryRecipe { Def = def, Machine = machine, Title = title, Problem = reason,
                    Primary = dynamic ? null : primary, Quantity = dynamic ? 1 : PreviewQuantity(machine, Math.Max(1, Get<int>(first, "count", 1))),
                    NetOutput = Get(first, "pipeNet") != null };
            }
        }
        static int PreviewQuantity(Thing machine, int count) => AccessTools.Method(AccessTools.TypeByName("Grechka.WorldIntegration.FactoryTierAdapter"), "PreviewCount", new[] { typeof(Thing), typeof(int) })?.Invoke(null, new object[] { machine, count }) is int result ? result : count;
        internal static bool Allowed(object ingredient, Thing thing, ThingDef sameKind = null)
        {
            if (thing == null || thing.Destroyed || thing.stackCount <= 0 || thing.IsForbidden(Faction.OfPlayer)) return false;
            ThingDef exact = Get<ThingDef>(ingredient, "thing");
            ThingCategoryDef category = Get<ThingCategoryDef>(ingredient, "thingCategory");
            if (exact != null && thing.def != exact || category != null && !thing.def.IsWithinCategory(category)) return false;
            if (exact == null && category == null) return false;
            if (List(Get(ingredient, "disallowedThingDefs")).Contains(thing.def)) return false;
            if (Get<bool>(ingredient, "onlySmeltable") && !thing.Smeltable) return false;
            if (Get<bool>(ingredient, "onlyFreshCorpses") && thing.TryGetComp<CompRottable>()?.Stage != RotStage.Fresh) return false;
            return sameKind == null || category == null || sameKind == thing.def;
        }
        internal static FactoryTask TaskFor(object process)
        {
            ThingWithComps machine = Get<ThingWithComps>(process, "parent");
            return machine?.Map?.GetComponent<FactoryCraftMap>()?.Task(machine, Get<string>(process, "id"));
        }
        static void Begin(object __instance, out Capture __state) { __state = attempt; var task = TaskFor(__instance); attempt = task == null ? null : new Capture { task = task }; }
        static void ObservePlacements(ref Action<Thing, int> __5)
        {
            Capture capture = attempt; if (capture == null) return;
            Action<Thing, int> existing = __5;
            __5 = (thing, count) => { existing?.Invoke(thing, count); if (thing != null && count > 0) capture.placements.Add((thing, count)); };
        }
        static void Count(int __result) { if (attempt != null) attempt.amount = __result; }
        static void End(bool __result, Thing __3, Capture __state)
        {
            Capture finished = attempt; attempt = __state;
            if (__result && __3 != null && finished?.amount > 0)
                {
                    finished.task.Record(__3, finished.amount);
                    if (finished.task.InterfaceReady)
                    {
                        if (finished.placements.Count == 0) CaptureOutput(finished.task, __3, finished.amount);
                        else foreach (var group in finished.placements.GroupBy(p => p.thing)) CaptureOutput(finished.task, group.Key, group.Sum(p => p.count));
                    }
                }
        }
        internal static bool CaptureOutput(FactoryTask task, Thing thing, int amount)
        {
            if (thing == null || thing.Destroyed || amount <= 0) return false;
            Map map = thing.Map; IntVec3 cell = thing.Position;
            ThingOwner previous = thing.holdingOwner;
            Thing split = thing.SplitOff(Math.Min(amount, thing.stackCount));
            if (split.Spawned) split.DeSpawn();
            if (task.Buffer.Contents.TryAddOrTransfer(split, false)) return true;
            // Keep the real split instance if an external ownership hook rejects insertion.
            if (previous != null && previous.TryAddOrTransfer(split, false)) return false;
            if (!thing.Destroyed && thing != split && thing.CanStackWith(split) && thing.TryAbsorbStack(split, false)) return false;
            if (map != null && GenPlace.TryPlaceThing(split, cell, map, ThingPlaceMode.Near)) return false;
            // This owner has no filter/capacity; a second refusal is an external mod failure.
            if (!task.Buffer.Contents.TryAdd(split, false)) throw new InvalidOperationException("Factory output ownership rejected; result must be recovered: " + split);
            return true;
        }
        static Exception FinalizeAttempt(Exception __exception, Capture __state) { attempt = __state; return __exception; }
        static void Reset(object __instance, bool __0)
        { if (__0) { FactoryTask task = TaskFor(__instance); if (task != null) { task.Finished = true; task.Completed = true; } } }
        static bool HasInterface(object __instance, ref bool __result)
        {
            FactoryTask task = TaskFor(__instance);
            if (task == null || !task.InterfaceReady) return true;
            __result = true; return false;
        }
    }
    public sealed class FactoryRecipe
    {
        public Def Def;
        public ThingWithComps Machine;
        public ThingDef Primary;
        public int Quantity;
        public bool NetOutput;
        public string Title, Problem;
        public bool CanMaintain => Primary != null && !NetOutput;
    }
    public sealed class FactoryOrder : IExposable
    {
        public int Id, Remaining;
        public string ParentKey;
        public List<string> Ancestors = new List<string>();
        public Building_StorageCrafter Anchor;
        public string ProcessName, Title;
        public ThingDef Product;
        public bool Maintain, Cancelled, CountCycles;
        public Building_StorageController Controller;
        public string Status = "Ожидание";
        public void ExposeData()
        {
            Scribe_Values.Look(ref ParentKey, "parent"); Scribe_Collections.Look(ref Ancestors, "ancestors", LookMode.Value); Scribe_References.Look(ref Anchor, "anchor"); Ancestors ??= new List<string>();
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref Remaining, "remaining"); Scribe_Values.Look(ref ProcessName, "process"); Scribe_Values.Look(ref Title, "title");
            Scribe_Values.Look(ref Maintain, "maintain"); Scribe_Values.Look(ref Cancelled, "cancelled"); Scribe_Values.Look(ref CountCycles, "cycles");
            Scribe_Defs.Look(ref Product, "product"); Scribe_References.Look(ref Controller, "controller");
        }
    }
    public sealed class FactoryTask : IExposable, IThingHolder
    {
        public int OrderId;
        public string ProcessId;
        public Building Machine;
        public Building_StorageCrafter Crafter;
        public Building_StorageController Controller;
        public bool Finished, Fed, Faulted, Completed, Accounted;
        public int Produced;
        public StorageBuffer Buffer;
        public FactoryTask() { Buffer = new StorageBuffer(this); }
        public IThingHolder ParentHolder => Crafter;
        public ThingOwner GetDirectlyHeldThings() => Buffer.Contents;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, Buffer.Contents);
        public bool InterfaceReady => Crafter != null && Machine != null && Crafter.Spawned && Machine.Spawned && Crafter.Map == Machine.Map
            && GenAdj.CellsAdjacentCardinal(Crafter).Any(c => c.InBounds(Machine.Map) && c.GetEdifice(Machine.Map) == Machine)
            && DeviceStatus.Ready(Crafter) && Controller?.Spawned == true && Controller.Online()
            && Crafter.Map.GetComponent<CraftMap>().SameNetwork(Controller, Crafter.Controller());
        internal object Process
        {
            get
            {
                object comp = FactoryCraftAdapter.Comp(Machine); object stack = FactoryCraftAdapter.Get(comp, "ProcessStack");
                return FactoryCraftAdapter.List(FactoryCraftAdapter.Get(stack, "Processes")).Concat(new[] { FactoryCraftAdapter.Get(stack, "currentProcess") })
                    .FirstOrDefault(p => p != null && FactoryCraftAdapter.Get<string>(p, "id") == ProcessId);
            }
        }
        public void Record(Thing thing, int amount)
        {
            // Placement is captured synchronously; delayed map pickup could steal a changed stack.
            FactoryOrder order = Machine?.Map?.GetComponent<FactoryCraftMap>().Find(OrderId);
            if (order != null && !order.CountCycles && order.Product == thing.def) Produced += amount;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref OrderId, "order"); Scribe_Values.Look(ref ProcessId, "process"); Scribe_Values.Look(ref Finished, "finished");
            Scribe_Values.Look(ref Accounted, "accounted"); Scribe_Values.Look(ref Completed, "completed"); Scribe_Values.Look(ref Fed, "fed"); Scribe_Values.Look(ref Faulted, "faulted"); Scribe_Values.Look(ref Produced, "produced");
            Scribe_References.Look(ref Machine, "machine"); Scribe_References.Look(ref Crafter, "crafter"); Scribe_References.Look(ref Controller, "controller");
            Scribe_Deep.Look(ref Buffer, "buffer", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { Buffer ??= new StorageBuffer(this); }
        }
    }
    public sealed class FactoryCraftMap : MapComponent
    {
        List<FactoryOrder> orders = new List<FactoryOrder>();
        List<FactoryTask> tasks = new List<FactoryTask>();
        int nextId = 1;
        public FactoryCraftMap(Map map) : base(map) { }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref orders, "grechkaFactoryOrders", LookMode.Deep); Scribe_Collections.Look(ref tasks, "grechkaFactoryTasks", LookMode.Deep); Scribe_Values.Look(ref nextId, "grechkaFactoryNextId", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { orders ??= new List<FactoryOrder>(); tasks ??= new List<FactoryTask>(); }
        }
        public static IEnumerable<Thing> HeldContents(Thing carrier) => carrier is Building_StorageCrafter crafter && crafter.MapHeld != null
            ? crafter.MapHeld.GetComponent<FactoryCraftMap>().tasks.Where(t => t.Crafter == crafter).SelectMany(t => t.Buffer.Contents) : Enumerable.Empty<Thing>();
        public static IEnumerable<Thing> MapContents(Map map) => map?.GetComponent<FactoryCraftMap>()?.tasks.SelectMany(t => t.Buffer.Contents) ?? Enumerable.Empty<Thing>();
        internal FactoryTask Task(ThingWithComps machine, string id) => tasks.FirstOrDefault(t => t.Machine == machine && t.ProcessId == id);
        internal List<FactoryOrder> Children(string parent) => orders.Where(o => o.ParentKey == parent && !o.Cancelled).ToList();
        internal FactoryOrder Find(int id) => orders.FirstOrDefault(o => o.Id == id);
        public List<FactoryOrder> Orders(Building_StorageController controller)
        {
            foreach (FactoryOrder order in orders)
                if ((order.Controller == null || order.Controller.Destroyed || !order.Controller.Spawned) && order.Anchor?.Spawned == true && map.GetComponent<CraftMap>().SameNetwork(order.Anchor.Controller(), controller))
                { order.Controller = controller; foreach (FactoryTask task in tasks.Where(t => t.OrderId == order.Id)) task.Controller = controller; }
            return orders.Where(o => !o.Cancelled && map.GetComponent<CraftMap>().SameNetwork(o.Controller, controller)).ToList();
        }
        public IEnumerable<FactoryRecipe> Catalog(Building_StorageController controller) => map.GetComponent<CraftMap>().Crafters
            .Where(c => c.Spawned && map.GetComponent<CraftMap>().SameNetwork(controller, c.Controller()))
            .SelectMany(c => GenAdj.CellsAdjacentCardinal(c).Where(p => p.InBounds(map)).Select(p => p.GetEdifice(map)))
            .Where(b => b != null && b.Faction == Faction.OfPlayer && FactoryCraftAdapter.IsFactory(b)).Distinct()
            .SelectMany(FactoryCraftAdapter.Recipes);
        public FactoryOrder Add(Building_StorageController controller, FactoryRecipe recipe, int count, bool maintain)
        {
            if (count <= 0 || recipe.Problem != null || maintain && !recipe.CanMaintain) return null;
            FactoryOrder previous = maintain ? Orders(controller).FirstOrDefault(o => o.Maintain && o.ProcessName == recipe.Def.defName) : null;
            if (previous != null) { previous.Remaining = Math.Min(1000000, count); return previous; }
            var added = new FactoryOrder { Anchor = map.GetComponent<CraftMap>().Crafters.FirstOrDefault(c => c.Spawned && map.GetComponent<CraftMap>().SameNetwork(controller, c.Controller())), Id = nextId++, Controller = controller, ProcessName = recipe.Def.defName, Title = recipe.Title,
                Remaining = Math.Min(1000000, count), Maintain = maintain, Product = recipe.Primary, CountCycles = recipe.Primary == null || recipe.NetOutput }; orders.Add(added); return added;
        }
        public void Cancel(FactoryOrder order) { order.Cancelled = true; CraftDependencies.Cancel(map, "f:" + order.Id); order.Status = "Оставленная штатная партия завершится без новых запусков"; if (!tasks.Any(t => t.OrderId == order.Id)) orders.Remove(order); }
        public static bool Busy(Building_StorageCrafter crafter) => crafter?.Map?.GetComponent<FactoryCraftMap>().tasks.Any(t => t.Crafter == crafter && !t.Fed && !t.Finished && !t.Faulted) == true;
        public bool TryStart(Building_StorageCrafter crafter, ThingWithComps building)
        {
            if (!FactoryCraftAdapter.IsFactory(building) || building is not Building machine || tasks.Any(t => t.Machine == machine)) return false;
            // Let the current native batch finish, then leave the machine idle for its paid upgrade.
            if (building.AllComps.Any(c => c.GetType().FullName == "Grechka.WorldIntegration.CompFactoryUpgrade"
                && FactoryCraftAdapter.Get<bool>(c, "Requested"))) return false;
            var controller = crafter.Controller(); if (controller == null || !DeviceStatus.Ready(machine)) return false;
            object comp = FactoryCraftAdapter.Comp(machine), stack = FactoryCraftAdapter.Get(comp, "ProcessStack");
            if (stack == null || FactoryCraftAdapter.Get(comp, "Process") != null) return false;
            foreach (FactoryRecipe recipe in FactoryCraftAdapter.Recipes(machine).Where(r => r.Problem == null))
                foreach (FactoryOrder order in Orders(controller).Where(o => o.ProcessName == recipe.Def.defName))
                {
                    long pending = tasks.Where(t => t.OrderId == order.Id && !t.Accounted).Sum(_ => (long)(order.CountCycles ? 1 : recipe.Quantity));
                    long stock = order.Product == null ? 0 : controller.ActiveDisks().SelectMany(d => d.Items).Where(t => t.def == order.Product).Sum(t => (long)t.stackCount);
                    if (order.Maintain && order.Product != null) stock += tasks.Where(t => t.Controller == controller && t.Accounted).SelectMany(t => t.Buffer.Contents).Where(t => t.def == order.Product).Sum(t => (long)t.stackCount);
                    if ((order.Maintain ? stock : 0) + pending >= order.Remaining) continue;
                    try
                    {
                        FactoryCraftAdapter.Call(stack, "AddProcess", recipe.Def, machine, BillRepeatModeDefOf.RepeatCount, 1, QualityCategory.Normal);
                        object process = FactoryCraftAdapter.Get(comp, "Process");
                        if (process == null) { order.Status = "Штатный процесс не активировался"; return false; }
                        var task = new FactoryTask { Crafter = crafter, Machine = machine, Controller = controller, OrderId = order.Id, ProcessId = FactoryCraftAdapter.Get<string>(process, "id") };
                        order.Anchor = crafter; tasks.Add(task); task.Buffer.Register();
                        if (!Feed(task, process, out string reason))
                        {
                            order.Status = reason;
                            // Before native consumption every reserved item is still a real Thing.
                            if (!task.Faulted) { task.Finished = true; FactoryCraftAdapter.Call(stack, "Delete", process); Flush(task); }
                            return false;
                        }
                        order.Status = "Фабрика выполняет штатный процесс"; return true;
                    }
                    catch (Exception e) { order.Status = "Ошибка Factory: " + e.GetBaseException().Message; Log.Error("[Storage Factory] " + e); return false; }
                }
            return false;
        }
        bool Feed(FactoryTask task, object process, out string problem)
        {
            var defs = FactoryCraftAdapter.List(FactoryCraftAdapter.Get(FactoryCraftAdapter.Get(process, "Def"), "ingredients")).ToList();
            var owners = FactoryCraftAdapter.List(FactoryCraftAdapter.Get(process, "IngredientsOwners")).ToList();
            var available = task.Controller.ActiveDisks().SelectMany(d => d.Items.Select(item => (disk: d, item))).ToList();
            var left = available.ToDictionary(x => x.item, x => x.item.stackCount);
            var selections = new List<(StorageDisk disk, Thing item, int count)>();
            ThingDef sameKind = null;
            bool mixing = FactoryCraftAdapter.Get<bool>(FactoryCraftAdapter.Get(process, "Def"), "disallowMixing");
            for (int i = 0; i < owners.Count && i < defs.Count; i++)
            {
                if (FactoryCraftAdapter.Get(owners[i], "PipeNetDef") != null) continue; // Native pipe network supplies fluid; never invent items for it.
                int need = FactoryCraftAdapter.Get<int>(owners[i], "Required");
                foreach (var candidate in available)
                {
                    if (need <= 0) break;
                    if (left[candidate.item] <= 0 || !FactoryCraftAdapter.Allowed(defs[i], candidate.item, mixing ? sameKind : null)) continue;
                    int count = Math.Min(need, left[candidate.item]); selections.Add((candidate.disk, candidate.item, count)); left[candidate.item] -= count; need -= count;
                    if (mixing && FactoryCraftAdapter.Get(defs[i], "thingCategory") != null) sameKind ??= candidate.item.def;
                }
                if (need > 0) {
                    FactoryOrder order = Find(task.OrderId);
                    if (order != null)
                    {
                        var ancestors = new List<string>(order.Ancestors) { "f:" + order.ProcessName };
                        ThingDef exact = FactoryCraftAdapter.Get<ThingDef>(defs[i], "thing");
                        ThingCategoryDef category = FactoryCraftAdapter.Get<ThingCategoryDef>(defs[i], "thingCategory");
                        IEnumerable<ThingDef> choices = exact != null ? new[] { exact } : DefDatabase<ThingDef>.AllDefs.Where(d => category != null && d.IsWithinCategory(category));
                        foreach (ThingDef wanted in choices.Where(d => !FactoryCraftAdapter.List(FactoryCraftAdapter.Get(defs[i], "disallowedThingDefs")).Contains(d)).OrderBy(d => d.defName))
                            if (CraftDependencies.Request(map, task.Controller, wanted, need, "f:" + order.Id, ancestors)) break;
                    }
                    problem = "Не хватает: " + (FactoryCraftAdapter.Get<ThingDef>(defs[i], "thing")?.LabelCap.ToString() ?? FactoryCraftAdapter.Get<ThingCategoryDef>(defs[i], "thingCategory")?.LabelCap.ToString() ?? "ингредиента") + " ×" + need; return false; }
            }
            foreach (var part in selections)
                if (part.disk.Withdraw(part.item, part.count, task.Buffer.Contents) != part.count) { problem = "Резервирование прервано; исходные вещи сохранены"; return false; }
            try
            {
                object manager = FactoryCraftAdapter.Get(process, "advancedProcessorsManager");
                object comp = FactoryCraftAdapter.Comp(task.Machine);
                foreach (Thing item in task.Buffer.Contents.ToList()) FactoryCraftAdapter.Call(manager, "AddIngredient", comp, item);
                task.Fed = true; problem = null; return true;
            }
            catch (Exception e) { task.Faulted = true; problem = "Подача приостановлена: " + e.GetBaseException().Message; return false; }
        }
        void Flush(FactoryTask task)
        {
            if (!task.InterfaceReady) return;
            foreach (Thing item in task.Buffer.Contents.ToList())
                foreach (StorageDisk disk in task.Controller.ActiveDisks())
                { if (item.Destroyed || item.holdingOwner != task.Buffer.Contents) break; disk.Accept(item, item.stackCount); }
        }
        public override void MapComponentTick()
        {
            if (Verse.Find.TickManager.TicksGame % 60 != 0) return;
            foreach (FactoryTask task in tasks.ToList())
            {
                if ((task.Controller == null || task.Controller.Destroyed || !task.Controller.Spawned) && task.Crafter?.Spawned == true)
                {
                    var replacement = task.Crafter.Controller();
                    if (replacement != null) { task.Controller = replacement; FactoryOrder existing = Find(task.OrderId); if (existing != null) existing.Controller = replacement; }
                }
                Flush(task);
                object process = task.Process;
                if (!task.Finished && (task.Machine == null || task.Machine.Destroyed || process == null)) task.Finished = true;
                if (!task.Finished) continue;
                FactoryOrder order = Find(task.OrderId);
                if (process != null)
                {
                    object stack = FactoryCraftAdapter.Get(FactoryCraftAdapter.Comp(task.Machine), "ProcessStack");
                    FactoryCraftAdapter.Call(stack, "Delete", process);
                }
                if (!task.Accounted)
                {
                    if (order != null && !order.Maintain)
                    {
                        order.Remaining = Math.Max(0, order.Remaining - (order.CountCycles ? task.Completed ? 1 : 0 : task.Produced));
                        if (order.Remaining == 0 || order.Cancelled) { CraftDependencies.Cancel(map, "f:" + order.Id); orders.Remove(order); }
                    }
                    task.Accounted = true;
                }
                if (task.Buffer.Contents.Any) continue;
                tasks.Remove(task);
                if (order?.Cancelled == true && !tasks.Any(t => t.OrderId == order.Id)) orders.Remove(order);
            }
        }
        public static bool PrepareRemoval(Building_StorageCrafter crafter)
        {
            if (!crafter.Spawned) return true;
            var manager = crafter.Map.GetComponent<FactoryCraftMap>();
            bool success = true;
            foreach (FactoryTask task in manager.tasks.Where(t => t.Crafter == crafter))
                if (task.Buffer.Contents.Any && !task.Buffer.Contents.TryDropAll(crafter.Position, crafter.Map, ThingPlaceMode.Near)) success = false;
            if (!success) Messages.Message("Сначала освободите место для выгрузки фабричного буфера.", crafter, MessageTypeDefOf.RejectInput, false);
            return success;
        }
        public void Recover(Building_StorageController controller)
        {
            foreach (FactoryTask task in tasks.Where(t => t.Controller == controller))
                if (task.Buffer.Contents.Any) task.Buffer.Contents.TryDropAll(controller.Position, map, ThingPlaceMode.Near);
        }
    }
    [HarmonyPatch(typeof(Building_StorageCrafter), nameof(Building_StorageCrafter.DeSpawn))]
    internal static class FactoryCrafterRemovalHook
    {
        static bool Prefix(Building_StorageCrafter __instance) => FactoryCraftMap.PrepareRemoval(__instance);
    }
    [HarmonyPatch(typeof(MinifyUtility), nameof(MinifyUtility.MakeMinified))]
    internal static class FactoryCrafterMinifyHook
    {
        static bool Prefix(Thing __0, ref MinifiedThing __result)
        {
            if (__0 is not Building_StorageCrafter crafter || FactoryCraftMap.PrepareRemoval(crafter)) return true;
            __result = null; return false;
        }
    }

}
