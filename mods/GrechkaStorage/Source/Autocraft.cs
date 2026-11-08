using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Grechka.Storage
{
    public static class StorageProduction
    {
        public static event Action<Thing, Thing, int> OutputProduced;
        internal static void Report(Thing machine, Thing product)
            => Report(machine, product, product?.stackCount ?? 0);
        internal static void Report(Thing machine, Thing product, int count)
        {
            if (OutputProduced == null || product == null || count <= 0) return;
            foreach (Action<Thing, Thing, int> subscriber in OutputProduced.GetInvocationList())
                try { subscriber(machine, product, count); }
                catch (Exception error) { Log.Error("[Storage] Production subscriber: " + error); }
        }
    }
    public sealed class CrafterProperties : DefModExtension
    {
        public int skill = 12;
        public float speed = 1;
    }
    public sealed class CraftOrder : IExposable
    {
        public int Id;
        public string ParentKey;
        public List<string> Ancestors = new List<string>();
        public Building_StorageCrafter Anchor;
        public Building_StorageController Controller;
        public RecipeDef Recipe;
        public int Remaining;
        public bool Maintain;
        public bool SelectedInputOrder;
        public Thing SelectedInput;
        public int SelectedCount;
        public bool Cancelled;
        public string Status = StorageText.Get("GrechkaStorage_Text094");
        public void ExposeData()
        {
            Scribe_Values.Look(ref ParentKey, "parent"); Scribe_Collections.Look(ref Ancestors, "ancestors", LookMode.Value); Scribe_References.Look(ref Anchor, "anchor");
            Ancestors ??= new List<string>();
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref Remaining, "remaining");
            Scribe_Values.Look(ref Maintain, "maintain"); Scribe_Values.Look(ref Cancelled, "cancelled");
            Scribe_Values.Look(ref SelectedInputOrder, "selectedInputOrder");
            Scribe_References.Look(ref SelectedInput, "selectedInput");
            Scribe_Values.Look(ref SelectedCount, "selectedCount");
            Scribe_Defs.Look(ref Recipe, "recipe"); Scribe_References.Look(ref Controller, "controller");
        }
    }
    public sealed class CraftMap : MapComponent
    {
        private List<CraftOrder> orders = new List<CraftOrder>();
        private int nextId = 1;
        internal readonly HashSet<Building_StorageCrafter> Crafters = new HashSet<Building_StorageCrafter>();
        private readonly Dictionary<int, CraftCatalogue> catalogues = new Dictionary<int, CraftCatalogue>();
        public CraftCatalogue Catalogue(Building_StorageController controller)
        {
            int network = controller.Node.Manager.NetworkOf(controller);
            int tick = Verse.Find.TickManager.TicksGame;
            if (catalogues.TryGetValue(network, out CraftCatalogue cached) && tick - cached.Tick < 250) return cached;
            var connected = controller.Node.Manager.ViewFor(controller.Node).Devices.OfType<Building_StorageCrafter>().ToHashSet();
            var tables = Crafters.Where(c => c.Spawned && connected.Contains(c)).SelectMany(c => c.Tables()).Distinct().ToList();
            var catalogue = new CraftCatalogue { Tick = tick };
            foreach (Building_WorkTable table in tables)
                foreach (RecipeDef recipe in table.def.AllRecipes.Where(CraftRecipes.Supported))
                {
                    if (!catalogue.Tables.TryGetValue(recipe, out List<Building_WorkTable> machines)) catalogue.Tables[recipe] = machines = new List<Building_WorkTable>();
                    machines.Add(table);
                    foreach (ThingDefCountClass product in recipe.products ?? new List<ThingDefCountClass>())
                    {
                        if (!catalogue.Products.TryGetValue(product.thingDef, out HashSet<RecipeDef> recipes)) catalogue.Products[product.thingDef] = recipes = new HashSet<RecipeDef>();
                        recipes.Add(recipe);
                    }
                }
            if (catalogues.Count > 64) catalogues.Clear();
            catalogues[network] = catalogue; return catalogue;
        }
        public CraftMap(Map map) : base(map) { }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref orders, "grechkaCraftOrders", LookMode.Deep);
            Scribe_Values.Look(ref nextId, "grechkaCraftNextId", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { orders ??= new List<CraftOrder>(); orders.RemoveAll(o => o == null || o.Recipe == null); }
        }
        internal bool SameNetwork(Building_StorageController a, Building_StorageController b)
            => a != null && b != null && a.Spawned && b.Spawned && a.Map == b.Map
                && a.Node.Manager.NetworkOf(a) == b.Node.Manager.NetworkOf(b);
        internal void Rebind(Building_StorageController old, Building_StorageController replacement)
        {
            if (old != null && old.Spawned && !old.Destroyed || replacement == null) return;
            foreach (var order in orders.Where(o => o.Controller == old && o.Anchor?.Spawned == true && SameNetwork(o.Anchor.Controller(), replacement))) order.Controller = replacement;
        }
        public List<CraftOrder> Orders(Building_StorageController controller)
        {
            foreach (var order in orders.ToList()) if (order.Controller == null || !order.Controller.Spawned || order.Controller.Destroyed) Rebind(order.Controller, controller);
            return orders.Where(o => !o.Cancelled && (o.Maintain || o.Remaining > 0) && SameNetwork(o.Controller, controller)).ToList();
        }
        public CraftOrder Add(Building_StorageController controller, RecipeDef recipe, int count, bool maintain)
        {
            if (!CraftRecipes.Supported(recipe) || DynamicCraftRecipes.Supported(recipe) || count <= 0) return null;
            count = Math.Min(count, 1000000);
            if (maintain)
            {
                CraftOrder existing = Orders(controller).FirstOrDefault(o => o.Maintain && o.Recipe == recipe);
                if (existing != null) { existing.Remaining = count; return existing; }
            }
            var added = new CraftOrder { Id = nextId++, Controller = controller, Recipe = recipe, Remaining = count, Maintain = maintain, Anchor = Crafters.FirstOrDefault(c => c.Spawned && SameNetwork(c.Controller(), controller)) };
            orders.Add(added); return added;
        }
        public CraftOrder AddSelected(Building_StorageController controller, RecipeDef recipe, Thing input)
        {
            if (!DynamicCraftRecipes.Supported(recipe) || input == null
                || Orders(controller).Any(o => o.SelectedInputOrder && o.SelectedInput == input)) return null;
            if (!Catalogue(controller).Tables.TryGetValue(recipe, out var tables)) return null;
            var selected = tables.Select(t => DynamicCraftRecipes.SelectSelected(controller, t, recipe, input, out _)).FirstOrDefault(s => s != null);
            if (selected == null) return null;
            var added = new CraftOrder { Id = nextId++, Controller = controller, Recipe = recipe,
                SelectedInputOrder = true, SelectedInput = input, SelectedCount = selected[0].count, Remaining = 1,
                Anchor = Crafters.FirstOrDefault(c => c.Spawned && SameNetwork(c.Controller(), controller)) };
            orders.Add(added); return added;
        }
        public void Cancel(CraftOrder order)
        {
            order.Cancelled = true;
            CraftDependencies.Cancel(map, "v:" + order.Id);
            foreach (Building_StorageCrafter crafter in Crafters.ToArray()) if (crafter.OrderId == order.Id) crafter.Cancel();
            orders.Remove(order);
        }
        internal List<CraftOrder> Children(string parent) => orders.Where(o => o.ParentKey == parent && !o.Cancelled).ToList();
        internal CraftOrder Find(int id) => orders.FirstOrDefault(o => o.Id == id && !o.Cancelled);
        internal bool Busy(Building_WorkTable table) => Crafters.Any(c => c.Spawned && c.ActiveTable == table);
        internal long Stock(Building_StorageController controller, ThingDef def)
            => controller.ActiveDisks().SelectMany(d => d.Items).Where(t => t.def == def).Sum(t => (long)t.stackCount);
        internal bool Needs(CraftOrder order, Building_StorageController controller)
        {
            if (order.SelectedInputOrder)
                return !order.Cancelled && !order.Maintain && order.Remaining > 0
                    && !Crafters.Any(c => c.OrderId == order.Id && c.ActiveTable != null && !c.Committed);
            if (order.Cancelled || order.Recipe.products.Count == 0) return false;
            long pending = Crafters.Where(c => c.OrderId == order.Id && c.ActiveTable != null && c.ActiveRecipe == order.Recipe && !c.Committed).Sum(c => (long)c.ExpectedOutput);
            return order.Maintain ? Stock(controller, order.Recipe.products[0].thingDef) + Crafters.Where(c => c.Committed && SameNetwork(c.Controller(), controller)).SelectMany(c => c.Contents).Where(t => t.def == order.Recipe.products[0].thingDef).Sum(t => (long)t.stackCount) + pending < order.Remaining : pending < order.Remaining;
        }
        internal void Complete(int id, int count)
        {
            CraftOrder order = Find(id);
            if (order == null || order.Maintain) return;
            order.Remaining = Math.Max(0, order.Remaining - count);
            if (order.Remaining == 0) { CraftDependencies.Cancel(map, "v:" + order.Id); orders.Remove(order); }
        }
    }
    public sealed class CraftCatalogue
    {
        public int Tick;
        public readonly Dictionary<RecipeDef, List<Building_WorkTable>> Tables = new Dictionary<RecipeDef, List<Building_WorkTable>>();
        public readonly Dictionary<ThingDef, HashSet<RecipeDef>> Products = new Dictionary<ThingDef, HashSet<RecipeDef>>();
    }
    internal static class CraftRecipes
    {
        internal static bool Supported(RecipeDef recipe) => DynamicCraftRecipes.Supported(recipe) || recipe != null && recipe.Worker.GetType() == typeof(RecipeWorker)
            && recipe.products != null && recipe.products.Count > 0 && recipe.ingredients != null
            && (recipe.specialProducts == null || recipe.specialProducts.Count == 0) && !recipe.ignoreIngredientCountTakeEntireStacks
            && recipe.products.All(p => p.thingDef.category == ThingCategory.Item && !p.thingDef.IsCorpse)
            && !recipe.defName.StartsWith("Grechka_Upgrade", StringComparison.Ordinal);
        internal static Bill_Production Bill(Building_WorkTable table, RecipeDef recipe)
        {
            var bill = new Bill_Production(recipe);
            // Deliberately not inserted into the table's pawn bill list.
            bill.billStack = new BillStack(table);
            return bill;
        }
        internal static List<(StorageDisk disk, Thing item, int count)> Select(Building_StorageController controller,
            Building_WorkTable table, RecipeDef recipe, out string problem, out IngredientCount missing)
        {
            return SelectDetailed(controller, table, recipe, out problem, out missing, out _, out _);
        }
        internal static List<(StorageDisk disk, Thing item, int count)> SelectDetailed(Building_StorageController controller,
            Building_WorkTable table, RecipeDef recipe, out string problem, out IngredientCount missing, out double shortage, out ThingDef dominantStuff)
        {
            missing = null; shortage = 0; dominantStuff = null;
            var available = controller.ActiveDisks().SelectMany(d => d.Items.Select(t => (disk: d, item: t))).ToList();
            var remaining = available.ToDictionary(x => x.item, x => x.item.stackCount);
            var selected = new List<(StorageDisk, Thing, int)>();
            Bill_Production bill = Bill(table, recipe);
            ThingDef stuff = null;
            // Narrow filters first prevents a generic ingredient from spending
            // the only item that satisfies a later specific ingredient.
            foreach (IngredientCount ingredient in recipe.ingredients.OrderBy(i => i.filter.AllowedThingDefs.Count()))
            {
                double required = recipe.Worker.GetIngredientCount(ingredient, bill);
                bool stuffProduct = recipe.products.Any(p => p.thingDef.MadeFromStuff) && !ingredient.IsFixedIngredient
                    && ingredient.filter.AllowedThingDefs.Any(d => d.IsStuff);
                var candidates = available.Where(candidate => remaining[candidate.item] > 0
                    && ingredient.filter.Allows(candidate.item) && recipe.fixedIngredientFilter.Allows(candidate.item)
                    && (!stuffProduct || candidate.item.def.IsStuff
                        && (stuff == null || candidate.item.def == stuff)
                        && recipe.products.Where(p => p.thingDef.MadeFromStuff).All(p => GenStuff.AllowedStuffsFor(p.thingDef).Contains(candidate.item.def))))
                    .ToList();
                // Vanilla NoMix selects one ThingDef, spanning its stacks if necessary. Never lock onto
                // the first small material pile: a later material may already satisfy the entire batch.
                if (!recipe.allowMixingIngredients || stuffProduct)
                {
                    var groups = candidates.GroupBy(c => c.item.def).ToList();
                    var complete = groups.FirstOrDefault(group =>
                        group.Sum(c => (long)remaining[c.item]) >= ingredient.CountRequiredOfFor(group.Key, recipe, bill));
                    if (complete == null && required > 0.00001)
                    {
                        missing = ingredient;
                        shortage = groups.Count == 0 ? required : groups.Min(group => Math.Max(0,
                            required - group.Sum(c => (long)remaining[c.item]) * recipe.IngredientValueGetter.ValuePerUnitOf(group.Key)));
                        problem = StorageText.Get("GrechkaStorage_Text109") + ingredient.Summary + " (" + shortage.ToString("0.##") + ")";
                        return null;
                    }
                    candidates = complete?.ToList() ?? candidates.Take(0).ToList();
                }
                foreach (var candidate in candidates)
                {
                    if (required <= 0.00001) break;
                    Thing thing = candidate.item;
                    if (!ingredient.filter.Allows(thing) || !recipe.fixedIngredientFilter.Allows(thing) || remaining[thing] <= 0) continue;
                    if (stuffProduct && thing.def.IsStuff && stuff != null && thing.def != stuff) continue;
                    double value = recipe.IngredientValueGetter.ValuePerUnitOf(thing.def);
                    if (value <= 0 || double.IsNaN(value)) continue;
                    int count = (int)Math.Min(remaining[thing], Math.Ceiling(required / value - 0.00001));
                    if (count <= 0) continue;
                    selected.Add((candidate.disk, thing, count)); remaining[thing] -= count; required -= count * value;
                    if (stuffProduct && thing.def.IsStuff) { stuff = thing.def; dominantStuff = stuff; }
                }
                if (required > 0.00001) { missing = ingredient; shortage = required; problem = StorageText.Get("GrechkaStorage_Text100") + ingredient.Summary + " (" + required.ToString("0.##") + ")"; return null; }
            }
            problem = null; return selected;
        }
    }

    public sealed class Building_StorageCrafter : Building, IThingHolder
    {
        private StorageBuffer buffer;
        private StorageBuffer outputStage;
        private ThingDef dominantStuff;
        private bool prepared, consumptionStarted;
        private string matterSnapshot;
        private long reservedQuantity;
        private Dictionary<string, int> reservedInputs = new Dictionary<string, int>();
        private Pawn worker;
        private Building_WorkTable table;
        private Building_StorageController controller;
        private RecipeDef recipe;
        private int orderId;
        private float workLeft;
        private int cursor;
        private bool committed;
        private bool faulted;
        public int OrderId => orderId;
        public bool Committed => committed;
        public int ExpectedOutput => DynamicCraftRecipes.Supported(recipe) ? 1 : CraftDependencies.Expected(recipe, table);
        public Building_WorkTable ActiveTable => table;
        public RecipeDef ActiveRecipe => recipe;
        public IEnumerable<Thing> Contents => buffer.Contents.Concat(committed ? outputStage.Contents : Enumerable.Empty<Thing>());
        public Building_StorageCrafter() { buffer = new StorageBuffer(this); outputStage = new StorageBuffer(this); }
        private CraftMap Manager => Map.GetComponent<CraftMap>();
        public override void PostMake() { base.PostMake(); buffer.Register(); outputStage.Register(); }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        { base.SpawnSetup(map, respawningAfterLoad); Manager.Crafters.Add(this); }
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        { if (Spawned) Manager.Crafters.Remove(this); base.DeSpawn(mode); }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref outputStage, "craftOutputStage", this); Scribe_Defs.Look(ref dominantStuff, "craftDominantStuff");
            Scribe_Values.Look(ref reservedQuantity, "craftReservedQuantity");
            Scribe_Collections.Look(ref reservedInputs, "craftReservedInputs", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref matterSnapshot, "craftMatterSnapshot");
            Scribe_Values.Look(ref prepared, "craftPrepared"); Scribe_Values.Look(ref consumptionStarted, "craftConsumptionStarted");
            Scribe_Deep.Look(ref buffer, "craftBuffer", this); Scribe_Deep.Look(ref worker, "craftWorker");
            Scribe_References.Look(ref table, "craftTable"); Scribe_References.Look(ref controller, "craftController");
            Scribe_Defs.Look(ref recipe, "craftRecipe"); Scribe_Values.Look(ref orderId, "craftOrder");
            Scribe_Values.Look(ref workLeft, "craftWork"); Scribe_Values.Look(ref cursor, "craftCursor");
            Scribe_Values.Look(ref committed, "craftCommitted"); Scribe_Values.Look(ref faulted, "craftFaulted");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { buffer ??= new StorageBuffer(this); outputStage ??= new StorageBuffer(this); reservedInputs ??= buffer.Contents.ToDictionary(t => t.GetUniqueLoadID(), t => t.stackCount); if (consumptionStarted && !committed) faulted = true; }
        }
        internal Building_StorageController Controller()
        {
            SignalNetworkMap network = Map.GetComponent<SignalNetworkMap>();
            return Map.listerBuildings.AllBuildingsColonistOfClass<Building_StorageController>()
                .Where(c => c.Online() && network.ViewFor(c.Node).Devices.Contains(this)).OrderBy(c => c.thingIDNumber).FirstOrDefault();
        }
        public List<Building> Neighbours() => GenAdj.CellsAdjacentCardinal(this).Where(c => c.InBounds(Map))
            .Select(c => c.GetEdifice(Map)).Where(b => b is Building_WorkTable || FactoryCraftAdapter.IsFactory(b))
            .Distinct().OrderBy(b => b.thingIDNumber).Take(4).ToList();
        public List<Building_WorkTable> Tables() => Neighbours().OfType<Building_WorkTable>().ToList();
        public override void TickRare()
        {
            base.TickRare();
            if (!Spawned || Faction != Faction.OfPlayer || !DeviceStatus.Ready(this) || faulted || FactoryCraftMap.Busy(this)) return;
            Building_StorageController connected = Controller();
            if (connected == null) return;
            if (table != null)
            {
                if (table.Destroyed || !table.Spawned || !Tables().Contains(table)) { Cancel(); return; }
                if (!Manager.SameNetwork(controller, connected))
                { if (controller != null && controller.Spawned && !controller.Destroyed) return; Manager.Rebind(controller, connected); }
                controller = connected;
                if (committed) { Flush(); return; }
                if (Manager.Find(orderId) == null) { Cancel(); return; }
                if (!DeviceStatus.Ready(table) || !table.CurrentlyUsableForBills() || !recipe.AvailableNow || !table.def.AllRecipes.Contains(recipe)) return;
                float speed = def.GetModExtension<CrafterProperties>()?.speed ?? 1;
                if (recipe.modExtensions?.Any(e => e.GetType().FullName == "Grechka.Industry.MatterRecipeProperties") == true) speed = 1;
                if (recipe.workSpeedStat != null) speed *= Worker().GetStatValue(recipe.workSpeedStat);
                if (recipe.workTableSpeedStat != null) speed *= table.GetStatValue(recipe.workTableSpeedStat);
                if (speed > 0) { table.UsedThisTick(); CraftDependencies.ReportWork(table);
                    CompStorageMachineArt.Worked(table, 250); CompStorageMachineArt.Worked(this, 250); }
                workLeft -= 250 * speed;
                if (workLeft <= 0) Finish();
                return;
            }
            if (buffer.Contents.Any || outputStage.Contents.Any) { Flush(); if (buffer.Contents.Any || outputStage.Contents.Any) return; }
            controller = connected;
            List<Building> tables = Neighbours();
            for (int offset = 0; offset < tables.Count; offset++)
            {
                int index = (cursor + offset) % tables.Count;
                Building neighbour = tables[index];
                if (FactoryCraftAdapter.IsFactory(neighbour))
                {
                    if (Map.GetComponent<FactoryCraftMap>().TryStart(this, neighbour))
                    { cursor = (index + 1) % tables.Count; return; }
                    continue;
                }
                Building_WorkTable next = (Building_WorkTable)neighbour;
                if (!DeviceStatus.Ready(next) || !next.CurrentlyUsableForBills() || Manager.Busy(next)
                    || Map.reservationManager.IsReserved(next)) continue;
                foreach (CraftOrder order in Manager.Orders(controller).Where(o => Manager.Needs(o, controller)))
                {
                    RecipeDef step = FindStep(order, next, out string planningProblem);
                    if (step == null) { order.Status = planningProblem ?? StorageText.Get("GrechkaStorage_Text110"); continue; }
                    ThingDef selectedStuff = null;
                    string problem;
                    var ingredients = order.SelectedInputOrder
                        ? DynamicCraftRecipes.SelectOrder(controller, next, order, out problem)
                        : CraftRecipes.SelectDetailed(controller, next, step, out problem, out _, out _, out selectedStuff);
                    order.Status = problem ?? StorageText.Get("GrechkaStorage_Text111");
                    if (ingredients == null) continue;
                    foreach (var part in ingredients)
                        if (part.disk.Withdraw(part.item, part.count, buffer.Contents) != part.count)
                        { Flush(); return; }
                    table = next; recipe = step; orderId = order.Id; order.Anchor = this; dominantStuff = selectedStuff; committed = prepared = consumptionStarted = false;
                    reservedInputs = buffer.Contents.ToDictionary(t => t.GetUniqueLoadID(), t => t.stackCount); reservedQuantity = buffer.Contents.Sum(t => (long)t.stackCount);
                    matterSnapshot = CraftDependencies.CaptureMatter(recipe);
                    workLeft = Math.Max(1, recipe.WorkAmountTotal(buffer.Contents.FirstOrDefault()));
                    cursor = (index + 1) % tables.Count;
                    return;
                }
            }
        }
        private RecipeDef FindStep(CraftOrder order, Building_WorkTable available, out string reason)
        {
            reason = null; RecipeDef target = order.Recipe;
            if (!CraftRecipes.Supported(target) || !target.AvailableNow || !available.def.AllRecipes.Contains(target)) return null;
            IngredientCount missing = null; double shortage = 0;
            var selection = order.SelectedInputOrder
                ? DynamicCraftRecipes.SelectOrder(controller, available, order, out reason)
                : CraftRecipes.SelectDetailed(controller, available, target, out reason, out missing, out shortage, out _);
            if (selection != null)
            {
                int skill = def.GetModExtension<CrafterProperties>()?.skill ?? 12;
                if (target.skillRequirements?.Any(r => r.minLevel > skill) == true) { reason = StorageText.Get("GrechkaStorage_Text112"); return null; }
                return target;
            }
            if (missing != null)
            {
                var ancestors = new List<string>(order.Ancestors) { "v:" + target.defName };
                foreach (ThingDef wanted in missing.filter.AllowedThingDefs.Where(target.fixedIngredientFilter.Allows).OrderBy(d => d.defName))
                {
                    double value = target.IngredientValueGetter.ValuePerUnitOf(wanted);
                    if (value > 0 && CraftDependencies.Request(Map, controller, wanted, (int)Math.Min(1000000, Math.Ceiling(shortage / value)), "v:" + order.Id, ancestors)) break;
                }
            }
            return null;
        }
        private Pawn Worker()
        {
            if (worker == null)
                worker = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, null,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false, allowFood: false, allowAddictions: false,
                    forceNoIdeo: true, forceNoBackstory: true, forbidAnyTitle: true, dontGiveWeapon: true, forceNoGear: true));
            foreach (SkillRecord skill in worker.skills.skills) skill.Level = def.GetModExtension<CrafterProperties>()?.skill ?? 12;
            return worker;
        }
        private void Finish()
        {
            (ThingComp comp, Dictionary<string, float> values) carrySnapshot = default;
            try
            {
                if (buffer.Contents.Sum(t => (long)t.stackCount) < reservedQuantity || reservedInputs.Any(p => !buffer.Contents.Any(t => !t.Destroyed && t.GetUniqueLoadID() == p.Key && t.stackCount >= p.Value)))
                    throw new InvalidOperationException("Reserved ingredient changed or spoiled during work");
                List<Thing> ingredients = buffer.Contents.ToList();
                Thing dominant = ingredients.FirstOrDefault(t => t.def == dominantStuff) ?? ingredients.FirstOrDefault();
                if (!prepared)
                {
                    carrySnapshot = CraftDependencies.CaptureTierCarry(table);
                    using (CraftDependencies.UseMatter(matterSnapshot))
                        foreach (Thing product in GenRecipe.MakeRecipeProducts(recipe, Worker(), ingredients, dominant, table))
                            if (!outputStage.Contents.TryAdd(product, false)) throw new InvalidOperationException("Cannot stage recipe output");
                    prepared = true;
                }
                consumptionStarted = true;
                foreach (Thing ingredient in ingredients) recipe.Worker.ConsumeIngredient(ingredient, recipe, Map);
                committed = true;
                int amount = DynamicCraftRecipes.Supported(recipe) ? 1
                    : outputStage.Contents.Where(t => t.def == recipe.products[0].thingDef).Sum(t => t.stackCount);
                if (Manager.Find(orderId)?.Recipe == recipe) Manager.Complete(orderId, amount);
                foreach (Thing product in outputStage.Contents) StorageProduction.Report(table, product);
                Flush();
            }
            catch (Exception error)
            {
                if (!committed) CraftDependencies.RestoreTierCarry(carrySnapshot);
                faulted = true;
                Log.Error("[Storage] Crafter paused after recipe error; buffered things retained: " + error);
            }
        }
        private void Flush()
        {
            if (!Spawned) return;
            Building_StorageController reachable = Controller();
            if (reachable == null || controller?.Spawned == true && !controller.Destroyed && !Manager.SameNetwork(controller, reachable)) return;
            if (controller != reachable) Manager.Rebind(controller, reachable);
            controller = reachable;
            foreach (Thing thing in Contents.ToList())
                foreach (StorageDisk disk in controller.ActiveDisks())
                {
                    if (thing.Destroyed || thing.holdingOwner != buffer.Contents && thing.holdingOwner != outputStage.Contents) break;
                    disk.Accept(thing, thing.stackCount);
                }
            if (!buffer.Contents.Any && !outputStage.Contents.Any) { table = null; recipe = null; orderId = 0; committed = prepared = consumptionStarted = false; dominantStuff = null; matterSnapshot = null; reservedInputs.Clear(); reservedQuantity = 0; }
        }
        private void DiscardUncommittedOutputs()
        { if (!committed) outputStage.Contents.ClearAndDestroyContents(); }
        internal void Cancel() { DiscardUncommittedOutputs(); committed = true; Flush(); }
        public override string GetInspectString() => base.GetInspectString() + "\n" + (faulted ? StorageText.Get("GrechkaStorage_Text113")
            : table == null ? StorageText.Get("GrechkaStorage_Text114") : committed ? StorageText.Get("GrechkaStorage_Text115") : recipe.LabelCap + StorageText.Get("GrechkaStorage_Text116") + Math.Max(0, workLeft).ToString("0"));
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (Faction == Faction.OfPlayer && (buffer.Contents.Any || outputStage.Contents.Any || faulted))
                yield return new Command_Action { defaultLabel = StorageText.Get("GrechkaStorage_Text117"), defaultDesc = StorageText.Get("GrechkaStorage_Text118"), action = () =>
                { DiscardUncommittedOutputs(); if (buffer.Contents.TryDropAll(Position, Map, ThingPlaceMode.Near) && outputStage.Contents.TryDropAll(Position, Map, ThingPlaceMode.Near)) { table = null; recipe = null; orderId = 0; committed = prepared = consumptionStarted = faulted = false; } } };
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (!FactoryCraftMap.PrepareRemoval(this)) return;
            DiscardUncommittedOutputs();
            if (outputStage.Contents.Any && (!Spawned || !outputStage.Contents.TryDropAll(Position, Map, ThingPlaceMode.Near))) return;
            if (buffer.Contents.Any && (!Spawned || !buffer.Contents.TryDropAll(Position, Map, ThingPlaceMode.Near))) return;
            base.Destroy(mode);
        }
        public ThingOwner GetDirectlyHeldThings() => buffer.Contents;
        public void GetChildHolders(List<IThingHolder> children) { children.Add(outputStage); ThingOwnerUtility.AppendThingHoldersFromThings(children, buffer.Contents); }
    }
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    internal static class CrafterPawnExclusionHook
    {
        private static bool Prefix(Thing thing, ref Job __result)
        {
            if (thing is Building_WorkTable table && table.Spawned && table.Map.GetComponent<CraftMap>().Busy(table))
            { __result = null; return false; }
            return true;
        }
    }
}

namespace Grechka.Storage
{
    internal static class CraftDependencies
    {
        private static readonly System.Reflection.MethodInfo reportWork = AccessTools.Method(AccessTools.TypeByName("Grechka.Industry.OperationalWork"), "ReportWork", new[] { typeof(Thing), typeof(int) });
        private static readonly System.Reflection.MethodInfo captureMatter = AccessTools.Method(AccessTools.TypeByName("Grechka.Industry.MatterSnapshots"), "Capture", new[] { typeof(RecipeDef) });
        private static readonly System.Reflection.MethodInfo useMatter = AccessTools.Method(AccessTools.TypeByName("Grechka.Industry.MatterSnapshots"), "UseSnapshot", new[] { typeof(string) });
        internal static string CaptureMatter(RecipeDef recipe) => captureMatter?.Invoke(null, new object[] { recipe }) as string;
        internal static IDisposable UseMatter(string payload) => payload == null ? null : useMatter?.Invoke(null, new object[] { payload }) as IDisposable;
        internal static void ReportWork(Thing table) { reportWork?.Invoke(null, new object[] { table, 250 }); }
        internal static (ThingComp comp, Dictionary<string, float> values) CaptureTierCarry(Building_WorkTable table)
        {
            ThingComp comp = table?.AllComps?.FirstOrDefault(c => c.GetType().FullName == "Grechka.Industry.CompTierBonuses");
            return (comp, comp == null ? null : AccessTools.Method(comp.GetType(), "CaptureCarry")?.Invoke(comp, null) as Dictionary<string, float>);
        }
        internal static void RestoreTierCarry((ThingComp comp, Dictionary<string, float> values) snapshot)
        {
            if (snapshot.comp == null || snapshot.values == null) return;
            try { AccessTools.Method(snapshot.comp.GetType(), "RestoreCarry")?.Invoke(snapshot.comp, new object[] { snapshot.values }); }
            catch (Exception error) { Log.Error("[Storage] Cannot restore failed recipe carry: " + error); }
        }
        internal static int Expected(RecipeDef recipe, Building_WorkTable table)
        {
            var product = recipe?.products?.FirstOrDefault(); if (product == null) return 0;
            object comp = table?.AllComps?.FirstOrDefault(c => c.GetType().FullName == "Grechka.Industry.CompTierBonuses");
            float bonus = FactoryCraftAdapter.Get<float>(FactoryCraftAdapter.Get(comp, "Props"), "outputBonus");
            // Material/quality are unknown before input selection. A lower bound
            // avoids promising an extra item from another variant's fractional carry.
            int extra = comp != null && AccessTools.Method(comp.GetType(), "PreviewExtra", new[] { typeof(RecipeDef), typeof(ThingDef) })
                ?.Invoke(comp, new object[] { recipe, product.thingDef }) is int preview
                ? preview : (int)Math.Floor(product.count * bonus + 0.00001f);
            return (int)Math.Min(int.MaxValue, (long)product.count + extra);
        }
        internal static bool Request(Map map, Building_StorageController controller, ThingDef product, int missing, string parent, List<string> ancestors)
        {
            if (missing <= 0 || ancestors.Count >= 32) return false;
            var vanilla = map.GetComponent<CraftMap>(); var factory = map.GetComponent<FactoryCraftMap>();
            // One live child per parent and concrete output. Parent re-evaluates the actual deficit after completion.
            if (vanilla.Orders(controller).Any(o => !o.SelectedInputOrder && o.ParentKey == parent && o.Recipe.products[0].thingDef == product)
                || factory.Orders(controller).Any(o => o.ParentKey == parent && o.Product == product)) return true;
            var catalogue = vanilla.Catalogue(controller);
            if (catalogue.Products.TryGetValue(product, out HashSet<RecipeDef> options))
                foreach (RecipeDef recipe in options.Where(r => r.AvailableNow && r.products[0].thingDef == product).OrderBy(r => r.defName))
                {
                    if (ancestors.Contains("v:" + recipe.defName)) continue;
                    CraftOrder child = vanilla.Add(controller, recipe, missing, false);
                    child.ParentKey = parent; child.Ancestors = new List<string>(ancestors); return true;
                }
            foreach (FactoryRecipe recipe in factory.Catalog(controller).Where(r => r.Primary == product && !r.NetOutput && r.Problem == null).OrderBy(r => r.Def.defName))
            {
                if (ancestors.Contains("f:" + recipe.Def.defName)) continue;
                FactoryOrder child = factory.Add(controller, recipe, missing, false);
                child.ParentKey = parent; child.Ancestors = new List<string>(ancestors); return true;
            }
            return false;
        }
        internal static void Cancel(Map map, string parent)
        {
            var vanilla = map.GetComponent<CraftMap>(); var factory = map.GetComponent<FactoryCraftMap>();
            foreach (var child in vanilla.Children(parent)) vanilla.Cancel(child);
            foreach (var child in factory.Children(parent)) factory.Cancel(child);
        }
    }
}
