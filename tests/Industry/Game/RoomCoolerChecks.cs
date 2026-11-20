using System.Reflection;
using UnityEngine;
using System.Runtime.CompilerServices;
using System.Xml;
using Grechka.Industry;
using HarmonyLib;
using RimWorld;
using Verse;

internal static class RoomCoolerChecks
{
    private static Room servedRoom;
    private static Room otherRoom;

    internal static void Run(Game game, string root)
    {
        void Check(bool condition, string text)
        {
            if (!condition) throw new Exception(text);
            Console.WriteLine("PASS room cooler: " + text);
        }

        var xml = new XmlDocument();
        xml.Load(Path.Combine(root, "mods/GrechkaIndustry/Defs/ThingDefs/Buildings_RoomCooler.xml"));
        var node = xml.SelectSingleNode("Defs/ThingDef");
        var components = node.SelectNodes("comps/li").Cast<XmlNode>()
            .Select(n => DirectXmlToObject.ObjectFromXml<CompProperties>(n, false)).ToList();
        var definition = new ThingDef
        {
            defName = node.SelectSingleNode("defName").InnerText, thingClass = typeof(Building_RoomCooler),
            category = ThingCategory.Building, building = new BuildingProperties(), comps = components
        };
        Check(components.OfType<CompProperties_TempControl>().Single().energyPerSecond == -22,
            "Shipping XML sets twice the passive cooler's -11 cooling rate");
        Check(node.SelectSingleNode("placeWorkers/li").InnerText == nameof(PlaceWorker_CoolerSimple)
            && node.SelectSingleNode("designationCategory").InnerText == "Grechka_IndustryCategory",
            "Construction uses the room cooling preview in the Grechka category");
        const string heaterPath = "Things/Building/Misc/TempControl/Heater";
        Check(node.SelectSingleNode("graphicData/texPath").InnerText == heaterPath
            && node.SelectSingleNode("graphicData/graphicClass").InnerText == typeof(Graphic_RoomCooler).FullName
            && node.SelectSingleNode("uiIconPath") == null,
            "Map graphic and automatic build icon use the recolored vanilla heater");
        string sourcePath = Environment.GetEnvironmentVariable("GRECHKA_HEATER_TEXTURE");
        if (!string.IsNullOrEmpty(sourcePath))
        {
            string temporary = Path.GetDirectoryName(sourcePath);
            if (sourcePath.EndsWith(".split", StringComparison.Ordinal))
            {
                if (!Waylith.Graphics.Textures.DdsFile.TryJoin(File.ReadAllBytes(sourcePath), out var joined, out int length))
                    throw new Exception("Cannot reconstruct native heater DDS");
                sourcePath = Path.Combine(temporary, "Heater.dds");
                File.WriteAllBytes(sourcePath, joined.AsSpan(0, length).ToArray());
                Waylith.Assets.AssetPack.Return(joined);
            }
            if (!Waylith.Graphics.Textures.DdsFile.TryDecode(File.ReadAllBytes(sourcePath), out var native, out _))
                throw new Exception("Cannot decode native heater DDS");
            var source = new Texture2D(native.Width, native.Height, TextureFormat.BC7, false);
            source.SetPixelData(native.Levels[0], 0);
            var recolor = AccessTools.Method(typeof(Graphic_RoomCooler), "Recolor");
            var blue = (Texture2D)recolor.Invoke(null, new object[] { source });
            Color32[] original = source.GetPixels32();
            Color32[] changed = blue.GetPixels32();
            int modified = 0;
            for (int i = 0; i < original.Length; i++)
            {
                Color32 a = original[i], b = changed[i];
                if (a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a) continue;
                if (a.a != b.a || a.g != b.g || a.r != b.b || a.b != b.r)
                    throw new Exception("Vanilla heater changed beyond red/blue channels");
                modified++;
            }
            File.WriteAllBytes(Path.Combine(temporary, "vanilla.png"), ImageConversion.EncodeToPNG(source));
            File.WriteAllBytes(Path.Combine(temporary, "cooler.png"), ImageConversion.EncodeToPNG(blue));
            Console.WriteLine("NATIVE HEATER " + source.width + "x" + source.height + " changed=" + modified);
            Check(source.width == blue.width && source.height == blue.height && modified > 0,
                "Actual vanilla DDS retains its dimensions, silhouette, alpha and every non-red pixel");
            Check(source.GetPixels32().Select(p => (p.r, p.g, p.b, p.a)).SequenceEqual(original.Select(p => (p.r, p.g, p.b, p.a)))
                && ReferenceEquals(blue, recolor.Invoke(null, new object[] { source })),
                "Vanilla heater texture stays intact and its blue copy is reused");
            Console.WriteLine("COOLER PREVIEW " + Path.Combine(temporary, "cooler.png"));
        }
        else Console.WriteLine("SKIP native DDS recolor check: set GRECHKA_HEATER_TEXTURE to the extracted vanilla heater");

        var previousMaps = game.Maps.ToList();
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(20, 1, 20) };
        game.Maps.Clear();
        game.Maps.Add(map);
        var cooler = new Building_RoomCooler { def = definition, thingIDNumber = 91347 };
        AccessTools.Method(typeof(ThingWithComps), "InitializeComps").Invoke(cooler, null);
        Set(cooler, "mapIndexOrState", (sbyte)0);
        Set(cooler, "positionInt", new IntVec3(5, 0, 5));
        cooler.compPowerTrader = cooler.GetComp<CompPowerTrader>();
        cooler.compTempControl = cooler.GetComp<CompTempControl>();
        Set(cooler.compPowerTrader, "powerOnInt", true);
        cooler.compTempControl.PostSpawnSetup(false);
        servedRoom = MakeRoom(100, 30);
        otherRoom = MakeRoom(100, 35);
        var patch = new Harmony("grechka.room-cooler.native-checks");
        patch.Patch(AccessTools.Method(typeof(RegionAndRoomQuery), nameof(RegionAndRoomQuery.RoomAt)),
            prefix: new HarmonyMethod(typeof(RoomCoolerChecks), nameof(RoomAt)));
        patch.Patch(AccessTools.Method(typeof(RegionAndRoomQuery), nameof(RegionAndRoomQuery.GetRoom)),
            prefix: new HarmonyMethod(typeof(RoomCoolerChecks), nameof(GetRoom)));
        try
        {
            Check(cooler.compTempControl.TargetTemperature == 21, "Native thermostat defaults to 21 C");
            cooler.TickRare();
            Check(Math.Abs(servedRoom.Temperature - (30 - 22 * 250f / 60 / 100)) < .0001,
                "Normal TickRare cools its room through the real GenTemperature algorithm");
            Check(otherRoom.Temperature == 35 && cooler.compTempControl.operatingAtHighPower
                && cooler.compPowerTrader.PowerOutput == -250,
                "Cooling draws 250 W and acts on the device's room");

            servedRoom.Temperature = 21.01f;
            cooler.TickRare();
            Check(servedRoom.Temperature == 21, "Cooling stops exactly at the setpoint without overshoot");
            cooler.TickRare();
            Check(!cooler.compTempControl.operatingAtHighPower && cooler.compPowerTrader.PowerOutput == -25,
                "The reached setpoint switches consumption to 25 W");

            servedRoom.Temperature = 15;
            cooler.TickRare();
            Check(servedRoom.Temperature == 15 && cooler.compPowerTrader.PowerOutput == -25,
                "A room colder than the setpoint remains unchanged");
            cooler.compTempControl.TargetTemperature = -10;
            servedRoom.Temperature = -5;
            cooler.TickRare();
            Check(servedRoom.Temperature < -5, "The adjustable setpoint supports refrigeration below zero");

            Set(cooler.compPowerTrader, "powerOnInt", false);
            float before = servedRoom.Temperature;
            cooler.TickRare();
            Check(servedRoom.Temperature == before && !cooler.compTempControl.operatingAtHighPower,
                "Power loss stops cooling");
            Set(cooler.compPowerTrader, "powerOnInt", true);
            Set(cooler.GetComp<CompFlickable>(), "switchOnInt", false);
            cooler.TickRare();
            Check(servedRoom.Temperature == before, "Switching the device off stops cooling");
            Set(cooler.GetComp<CompFlickable>(), "switchOnInt", true);

            Set(servedRoom, "cachedOpenRoofCount", 100);
            cooler.TickRare();
            Check(servedRoom.Temperature == before && cooler.compPowerTrader.PowerOutput == -25,
                "The native room algorithm rejects an outdoor room");
            Set(servedRoom, "cachedOpenRoofCount", 0);

            string path = Path.Combine(Path.GetTempPath(), "grechka-room-cooler-" + Guid.NewGuid() + ".xml");
            try
            {
                cooler.compTempControl.TargetTemperature = 7;
                Scribe.saver.InitSaving(path, "fixture");
                cooler.compTempControl.PostExposeData();
                Scribe.saver.FinalizeSaving();
                var restored = new CompTempControl { props = cooler.compTempControl.props };
                Scribe.loader.InitLoading(path);
                restored.PostExposeData();
                Scribe.loader.FinalizeLoading();
                Check(restored.TargetTemperature == 7, "Native Scribe preserves the custom temperature");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        finally
        {
            patch.UnpatchAll(patch.Id);
            game.Maps.Clear();
            game.Maps.AddRange(previousMaps);
        }
    }

    private static Room MakeRoom(int cells, float temperature)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        Set(room, "districts", new List<District>());
        Set(room, "cachedCellCount", cells);
        Set(room, "cachedOpenRoofCount", 0);
        var tracker = (RoomTempTracker)RuntimeHelpers.GetUninitializedObject(typeof(RoomTempTracker));
        Set(room, "tempTracker", tracker);
        room.Temperature = temperature;
        return room;
    }

    private static void Set(object target, string field, object value)
        => AccessTools.Field(target.GetType(), field).SetValue(target, value);

    public static bool RoomAt(IntVec3 c, ref Room __result)
    {
        __result = c.x < 10 ? servedRoom : otherRoom;
        return false;
    }

    public static bool GetRoom(Thing thing, ref Room __result)
    {
        __result = thing.Position.x < 10 ? servedRoom : otherRoom;
        return false;
    }
}
