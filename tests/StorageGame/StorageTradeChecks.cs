using Grechka.Storage;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

static class StorageTradeChecks
{
    static bool Skip() => false;
    static bool NotWorldPawn(ref bool __result) { __result = false; return false; }
    static bool NoPawnSales(ref IEnumerable<Pawn> __result) { __result = Array.Empty<Pawn>(); return false; }
    static bool BeaconFootprint(IntVec3 pos, ref List<IntVec3> __result) { __result = new() { pos }; return false; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static bool OriginalPosition(TradeDeal deal, Thing t, out string reason) => throw new NotImplementedException();

    internal static void Run(Map map, Pawn negotiator, Building_DiskRack rack, StorageDisk disk, TestHolder holder,
        Action<Thing, IntVec3> put, Action<bool, string> check)
    {
        var fixture = new Harmony("grechka.storage.trade-map-fixture");
        var oldSession = (TradeSession.trader, TradeSession.playerNegotiator, TradeSession.deal, TradeSession.giftMode);
        var oldSilver = ThingDefOf.Silver;
        var oldMarket = StatDefOf.MarketValue;
        try
        {
            // The headless map has no region graph, world pawns or DLC bootstrap.
            // The trader's listings, deal construction, position checks, quantities and transfers stay native.
            void Boundary(System.Reflection.MethodBase method, string prefix) =>
                fixture.Patch(method, prefix: new HarmonyMethod(typeof(StorageTradeChecks), prefix));
            Boundary(typeof(ModsConfig).TypeInitializer, nameof(Skip));
            Boundary(AccessTools.Method(typeof(WorldPawnsUtility), nameof(WorldPawnsUtility.IsWorldPawn)), nameof(NotWorldPawn));
            Boundary(AccessTools.Method(typeof(TradeUtility), nameof(TradeUtility.AllSellableColonyPawns)), nameof(NoPawnSales));
            Boundary(AccessTools.Method(typeof(Building_OrbitalTradeBeacon), nameof(Building_OrbitalTradeBeacon.TradeableCellsAround)), nameof(BeaconFootprint));
            map.mapPawns = new MapPawns(map);
            map.regionGrid = new RegionGrid(map);
            map.regionAndRoomUpdater = new RegionAndRoomUpdater(map);
            map.regionDirtyer = new RegionDirtyer(map);
            AccessTools.Field(typeof(RegionAndRoomUpdater), "initialized").SetValue(map.regionAndRoomUpdater, true);
            StatDefOf.MarketValue = new StatDef { defName = "TradeMarketValue", workerClass = typeof(StatWorker), defaultBaseValue = 1 };
            ThingDefOf.Silver = Fixture.Def("TradeSilver", typeof(Thing), 500);
            var steel = Fixture.Def("TradeSteel", typeof(Thing), 750);
            steel.tradeability = Tradeability.All;
            var physical = Fixture.Create(steel, 6102);
            put(physical, new IntVec3(27,0,27));
            var beacon = new Building_OrbitalTradeBeacon { def = new ThingDef { defName = "TradeBeacon", category = ThingCategory.Building,
                building = new BuildingProperties(), comps = new() }, thingIDNumber = 502000 };
            put(beacon, physical.Position);
            var stored = Fixture.Create(steel, 33258); holder.Contents.TryAdd(stored, false);
            disk.Accept(stored, stored.stackCount);
            var buyer = new StockGenerator_SingleDef();
            AccessTools.Field(typeof(StockGenerator_SingleDef), "thingDef").SetValue(buyer, steel);
            var ship = new TradeShip { def = new TraderKindDef { stockGenerators = new() { buyer } },
                passingShipManager = new PassingShipManager(map) };
            AccessTools.Field(typeof(TradeShip), "things").SetValue(ship, new ThingOwner<Thing>(ship));
            TradeSession.trader = ship; TradeSession.playerNegotiator = negotiator; TradeSession.giftMode = false;
            var deal = new TradeDeal(); TradeSession.deal = deal;
            Tradeable Line() => deal.AllTradeables.Single(t => t.ThingDef == steel);
            fixture.CreateReversePatcher(AccessTools.Method(typeof(TradeDeal), "InSellablePosition"),
                new HarmonyMethod(typeof(StorageTradeChecks), nameof(OriginalPosition))).Patch(HarmonyReversePatchType.Original);
            check(OriginalPosition(deal, physical, out _) && !OriginalPosition(deal, stored, out _),
                "The original deal position check reproduces the missing disk steel while accepting physical steel");
            var line = Line();
            check(line.TraderWillTrade && line.CountHeldBy(Transactor.Colony) == 39360 && line.GetMinimumToTransfer() == -39360
                && line.thingsColony.Count == 2 && line.thingsColony.Contains(physical) && line.thingsColony.Contains(stored),
                "Orbital trade dialog offers the full 39,360 steel: 6,102 on the map and 33,258 on disk, once each");
            disk.SealedContents = true; deal.Reset();
            check(Line().CountHeldBy(Transactor.Colony) == 6102, "A sealed disk leaves only the 6,102 physical steel in the deal");
            disk.SealedContents = false;
            var power = rack.GetComp<CompPowerTrader>();
            AccessTools.Field(typeof(CompPowerTrader), "powerOnInt").SetValue(power, false);
            deal.Reset();
            check(Line().CountHeldBy(Transactor.Colony) == 6102, "An unpowered rack cannot add its stock to the deal");
            AccessTools.Field(typeof(CompPowerTrader), "powerOnInt").SetValue(power, true);
            map.listerBuildings.allBuildingsColonist.Remove(beacon);
            deal.Reset(); line = Line();
            check(line.CountHeldBy(Transactor.Colony) == 33258 && line.thingsColony.Single() == stored,
                "Without a beacon the same ship still offers all of the disk steel, but no loose steel");
            long before = disk.Space.ItemCount;
            line.ForceToDestination(5000); line.ResolveTrade();
            check(stored.stackCount == 28258 && stored.ParentHolder == disk && disk.Space.ItemCount == before - 5000
                && ship.Goods.Where(t => t.def == steel).Sum(t => t.stackCount) == 5000 && physical.stackCount == 6102,
                "Resolving an orbital sale withdraws exactly 5,000 steel from the disk into the ship");
            deal.Reset(); line = Line();
            check(line.CountHeldBy(Transactor.Colony) == 28258, "Reopening trade shows only the unsold disk balance");
            line.ForceToDestination(28258); line.ResolveTrade(); deal.Reset();
            check(!disk.Items.Contains(stored) && disk.Space.ItemCount == before - 33258 && physical.stackCount == 6102
                && ship.Goods.Where(t => t.def == steel).Sum(t => t.stackCount) == 33258
                && !deal.AllTradeables.Any(t => t.ThingDef == steel && t.CountHeldBy(Transactor.Colony) > 0),
                "Selling the last disk stack clears the colony row and transfers the balance exactly once");

            map.areaManager = new AreaManager(map);
            map.areaManager.AllAreas.Add(new Area_Home(map.areaManager));
            var trader = new Pawn { def = negotiator.def, thingIDNumber = 502001 };
            trader.inventory = new Pawn_InventoryTracker(trader);
            trader.trader = new Pawn_TraderTracker(trader) { traderKind = ship.def };
            put(trader, new IntVec3(8,0,8));
            var fresh = Fixture.Create(steel, 90); holder.Contents.TryAdd(fresh, false); disk.Accept(fresh, 90);
            TradeSession.trader = trader; deal.Reset();
            line = Line();
            check(line.thingsColony.Single() == fresh && line.CountHeldBy(Transactor.Colony) == 90,
                "A ground trader's deal also includes installed disk stock outside a physical stockpile");
            line.ForceToDestination(90); line.ResolveTrade();
            check(!disk.Items.Contains(fresh) && trader.inventory.innerContainer.Contains(fresh) && fresh.stackCount == 90,
                "A ground sale moves the real stack from the disk to the trader's inventory");
        }
        finally
        {
            (TradeSession.trader, TradeSession.playerNegotiator, TradeSession.deal, TradeSession.giftMode) = oldSession;
            ThingDefOf.Silver = oldSilver; StatDefOf.MarketValue = oldMarket;
            fixture.UnpatchAll(fixture.Id);
        }
    }
}
