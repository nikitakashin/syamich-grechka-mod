using System.Runtime.CompilerServices;
using System.Xml;
using Grechka.Industry;
using HarmonyLib;
using RimWorld;
using Verse;

internal static class EmitterChecks
{
    private static IntVec3 ringCenter;
    private static float ringRadius;
    public static bool CaptureRing(IntVec3 __0, float __1)
    { ringCenter = __0; ringRadius = __1; return false; }
    public static bool SkipHealthNotifications() => false;

    internal static void Run(Game game)
    {
        void Check(bool ok, string label)
        { if (!ok) throw new Exception(label); Console.WriteLine("PASS emitter: " + label); }
        void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
        var xml = new XmlDocument(); xml.Load("mods/GrechkaIndustry/Defs/ArchoEmitterHediffs.xml");
        foreach (XmlNode node in xml.DocumentElement.ChildNodes)
            if (node is XmlElement) DefDatabase<HediffDef>.Add(DirectXmlToObject.ObjectFromXml<HediffDef>(node, false));
        DefDatabase<PawnCapacityDef>.Add(new PawnCapacityDef { defName = "EmitterFixtureCapacity" });
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        var previousMaps = game.Maps.ToList();
        int previousTick = game.tickManager.TicksGame;
        Field(game, "maps", new List<Map> { map });
        map.mapPawns = new MapPawns(map);
        var field = new ArchoEmitterMap(map); map.components = new List<MapComponent> { field };
        var faction = new Faction { def = new FactionDef { defName = "EmitterFixtureFaction" } };
        var patch = new Harmony("grechka.industry.emitter-fixture");
        patch.Patch(AccessTools.Method(typeof(GenDraw), nameof(GenDraw.DrawRadiusRing), new[] { typeof(IntVec3), typeof(float) }),
            prefix: new HarmonyMethod(typeof(EmitterChecks), nameof(CaptureRing)));
        // Only full-game health notifications are suppressed; native AddHediff/RemoveHediff,
        // visibility, lifetime, needs, map tick and range/faction/power selection execute normally.
        patch.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.Notify_HediffChanged)),
            prefix: new HarmonyMethod(typeof(EmitterChecks), nameof(SkipHealthNotifications)));
        patch.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "CheckForStateChange"),
            prefix: new HarmonyMethod(typeof(EmitterChecks), nameof(SkipHealthNotifications)));
        CompArchoEmitter Device(string role, int tier)
        {
            var building = new Building { def = new ThingDef { defName = "FixtureEmitter" + role + tier,
                category = ThingCategory.Building, thingClass = typeof(Building),
                comps = new() { new CompProperties_Power { compClass = typeof(CompPowerTrader) },
                    new CompProperties_ArchoEmitter { role = role, tier = tier, radius = tier == 5 ? 32 : 20 } } } };
            AccessTools.Method(typeof(ThingWithComps), "InitializeComps").Invoke(building, null);
            Field(building, "mapIndexOrState", (sbyte)0); Field(building, "positionInt", new IntVec3(40, 0, 40));
            Field(building, "factionInt", faction); Field(building.GetComp<CompPowerTrader>(), "powerOnInt", true);
            return building.GetComp<CompArchoEmitter>();
        }
        var body = new BodyDef { defName = "EmitterFixtureBody",
            corePart = new BodyPartRecord { def = new BodyPartDef { defName = "EmitterFixtureTorso" } } };
        var pawn = new Pawn { def = new ThingDef { defName = "EmitterFixturePawn", category = ThingCategory.Pawn,
            race = new RaceProperties { intelligence = Intelligence.Humanlike, body = body } } };
        Field(pawn, "factionInt", faction); Field(pawn, "positionInt", new IntVec3(40, 0, 40));
        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.needs = (Pawn_NeedsTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_NeedsTracker));
        pawn.needs.rest = new Need_Rest(pawn);
        pawn.needs.rest.CurLevel = .4f;
        ((List<Pawn>)AccessTools.Field(typeof(MapPawns), "pawnsSpawned").GetValue(map.mapPawns)).Add(pawn);
        void Tick() { Field(game.tickManager, "ticksGameInt", game.tickManager.TicksGame + 60 - game.tickManager.TicksGame % 60); field.MapComponentTick(); }
        Hediff Effect() => pawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("Grechka_ArchoWellbeing"));
        try
        {
            foreach (string role in new[] { "Acceleration", "Suppression", "Wellbeing" })
                foreach (int tier in new[] { 4, 5 })
                {
                    var emitter = Device(role, tier); ringRadius = -1;
                    emitter.PostDrawExtraSelectionOverlays();
                    Check(ringCenter == emitter.parent.Position && ringRadius == emitter.Settings.radius,
                        role + " T" + tier + " selection draws the native ring at its real radius");
                }
            var t4 = Device("Wellbeing", 4); t4.PostSpawnSetup(false); Tick();
            Check(Effect()?.Visible == true && Effect().Severity == .5f && Math.Abs(pawn.needs.rest.CurLevel - .402f) < .00001f,
                "T4 restores rest and creates a visible Health effect during the same map tick");
            Check(Effect().TryGetComp<HediffComp_Disappears>().ticksToDisappear == 120,
                "Health effect has the native bounded lifetime");
            var t5 = Device("Wellbeing", 5); t5.PostSpawnSetup(false); float before = pawn.needs.rest.CurLevel; Tick();
            Check(pawn.health.hediffSet.hediffs.Count(h => h.def.defName == "Grechka_ArchoWellbeing") == 1
                && Effect().Severity == 1f && Math.Abs(pawn.needs.rest.CurLevel - before - .008f) < .00001f,
                "Overlapping T4/T5 show one strongest effect without stacking restoration");
            Field(t5.parent.GetComp<CompPowerTrader>(), "powerOnInt", false); Tick();
            Check(Effect()?.Severity == .5f, "Losing T5 power downgrades the displayed effect to active T4");
            Field(pawn, "positionInt", new IntVec3(61, 0, 40)); before = pawn.needs.rest.CurLevel; Tick();
            Check(Effect() == null && pawn.needs.rest.CurLevel == before, "Leaving the active radius removes the Health effect and restoration");
            Field(pawn, "positionInt", new IntVec3(40, 0, 40)); Field(pawn, "factionInt", new Faction()); Tick();
            Check(Effect() == null, "Foreign pawns receive no wellbeing effect");
            Field(pawn, "factionInt", faction); Tick();
            Field(t4.parent.GetComp<CompPowerTrader>(), "powerOnInt", false); before = pawn.needs.rest.CurLevel; Tick();
            Check(Effect() == null && pawn.needs.rest.CurLevel == before, "Power loss removes the Health effect and stops restoration");
            Field(t4.parent.GetComp<CompPowerTrader>(), "powerOnInt", true); pawn.needs.rest = null; Tick();
            Check(Effect() == null, "Pawns without food/rest needs do not receive a misleading wellbeing effect");
        }
        finally { patch.UnpatchAll(patch.Id); Field(game, "maps", previousMaps); Field(game.tickManager, "ticksGameInt", previousTick); }
    }
}
