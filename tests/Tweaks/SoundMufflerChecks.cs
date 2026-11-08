using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Grechka.Tweaks;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

static class SoundMufflerChecks
{
    public static int Run(Harmony fixture, string repo)
    {
        int checks = 0;
        void Check(bool ok, string message)
        {
            if (!ok) throw new Exception(message);
            checks++; Console.WriteLine("PASS muffler: " + message);
        }
        var mapA = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        var mapB = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        var zoneA = new SoundMufflerMapComponent(mapA);
        var zoneB = new SoundMufflerMapComponent(mapB);
        mapA.components = new List<MapComponent> { zoneA };
        mapB.components = new List<MapComponent> { zoneB };
        AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, new List<Map> { mapA, mapB });
        void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
        var def = new ThingDef { defName = "FixtureSoundMuffler", label = "muffler",
            category = ThingCategory.Building, thingClass = typeof(Building),
            comps = new List<CompProperties> { new CompProperties_Power { compClass = typeof(CompPowerTrader) },
                new CompProperties_Flickable(), new CompProperties_Breakdownable(), new CompProperties_SoundMuffler() } };
        CompSoundMuffler Make(int x, int z, sbyte mapIndex = 0)
        {
            var building = new Building { def = def };
            AccessTools.Method(typeof(ThingWithComps), "InitializeComps").Invoke(building, null);
            Field(building, "positionInt", new IntVec3(x, 0, z));
            Field(building, "mapIndexOrState", mapIndex);
            Field(building.GetComp<CompPowerTrader>(), "powerOnInt", true);
            var comp = building.GetComp<CompSoundMuffler>();
            comp.PostSpawnSetup(false);
            return comp;
        }
        var muffler = Make(20, 20);
        SoundInfo At(int x, int z, Map map = null) => SoundInfo.InMap(new TargetInfo(new IntVec3(x, 0, z), map ?? mapA));
        Check(muffler.Radius == 12 && muffler.Active && SoundMufflerVolume.Mutes(At(20, 20))
            && SoundMufflerVolume.Mutes(At(32, 20)) && !SoundMufflerVolume.Mutes(At(33, 20))
            && !SoundMufflerVolume.Mutes(At(32, 21)), "default circle includes the exact edge, excludes outside cells and square corners");
        Check(!SoundMufflerVolume.Mutes(At(20, 20, mapB)), "identical coordinates on another map remain audible");
        Check(!SoundMufflerVolume.Mutes(SoundInfo.OnCamera()) && !SoundMufflerVolume.Mutes(default),
            "camera music/UI and invalid sources are unaffected");
        var forced = At(20, 20); forced.forcedPlayOnCamera = true;
        Check(!SoundMufflerVolume.Mutes(forced), "explicit camera playback remains audible");
        var power = muffler.parent.GetComp<CompPowerTrader>();
        Field(power, "powerOnInt", false);
        Check(!SoundMufflerVolume.Mutes(At(20, 20)), "power failure immediately restores sound");
        Field(power, "powerOnInt", true);
        // Set backing fields to avoid the fixture needing real electrical overlays and map meshes.
        var flick = muffler.parent.GetComp<CompFlickable>();
        Field(flick, "switchOnInt", false);
        Check(!SoundMufflerVolume.Mutes(At(20, 20)), "manual switch disables suppression");
        Field(flick, "switchOnInt", true);
        var breakdown = muffler.parent.GetComp<CompBreakdownable>();
        Field(breakdown, "brokenDownInt", true);
        Check(!SoundMufflerVolume.Mutes(At(20, 20)), "breakdown disables suppression");
        Field(breakdown, "brokenDownInt", false);
        muffler.Radius = 5;
        Check(SoundMufflerVolume.Mutes(At(23, 24)) && !SoundMufflerVolume.Mutes(At(24, 24)),
            "editing the radius immediately changes a circular zone");
        var moving = new Thing { def = new ThingDef { defName = "FixtureMovingSource" } };
        Field(moving, "mapIndexOrState", (sbyte)0);
        Field(moving, "positionInt", new IntVec3(20, 0, 20));
        SoundInfo movingInfo = SoundInfo.InMap(new TargetInfo(moving));
        Check(SoundMufflerVolume.Mutes(movingInfo), "thing-bound pawn or machine source is muted inside");
        Field(moving, "positionInt", new IntVec3(40, 0, 20));
        Check(!SoundMufflerVolume.Mutes(movingInfo), "the same source becomes audible after moving out");
        Field(moving, "mapIndexOrState", (sbyte)-1);
        Check(!SoundMufflerVolume.Mutes(movingInfo), "a despawned source is safe");
        var overlap = Make(20, 20);
        overlap.Radius = 3;
        Field(power, "powerOnInt", false);
        Check(SoundMufflerVolume.Mutes(At(20, 20)), "one powered overlapping muffler keeps the zone muted");
        overlap.PostDeSpawn(mapA);
        Check(!SoundMufflerVolume.Mutes(At(20, 20)), "despawn unregisters the remaining active muffler");
        Field(power, "powerOnInt", true);
        muffler.PostSpawnSetup(true); // Registration must be idempotent on a repeated load notification.
        muffler.PostDeSpawn(mapA);
        Check(!SoundMufflerVolume.Mutes(At(20, 20)), "repeated registration leaves no stale duplicate after despawn");
        Field(muffler.parent, "mapIndexOrState", (sbyte)1);
        muffler.PostSpawnSetup(true);
        Check(!SoundMufflerVolume.Mutes(At(20, 20)) && SoundMufflerVolume.Mutes(At(20, 20, mapB)),
            "loading or moving the building registers only its current map");
        Field(muffler.parent, "mapIndexOrState", (sbyte)-1);
        Check(!SoundMufflerVolume.Mutes(At(20, 20, mapB)), "unspawned buildings cannot mute a stale registry entry");
        Field(muffler.parent, "mapIndexOrState", (sbyte)1);
        string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        try
        {
            muffler.Radius = 17;
            Scribe.saver.InitSaving(temp, "muffler"); muffler.PostExposeData(); Scribe.saver.FinalizeSaving();
            var restored = new CompSoundMuffler();
            Scribe.loader.InitLoading(temp); restored.PostExposeData(); Scribe.loader.FinalizeLoading();
            Check(restored.Radius == 17, "per-building radius survives native Scribe round trip");
            foreach (var (xml, expected) in new[] { ("<muffler/>", 12),
                ("<muffler><grechkaSoundMufflerRadius>-4</grechkaSoundMufflerRadius></muffler>", 1),
                ("<muffler><grechkaSoundMufflerRadius>300</grechkaSoundMufflerRadius></muffler>", 30) })
            {
                File.WriteAllText(temp, xml);
                Scribe.loader.InitLoading(temp); restored.PostExposeData(); Scribe.loader.FinalizeLoading();
                Check(restored.Radius == expected, "legacy or out-of-range saved radius loads as " + expected);
            }
        }
        finally { Scribe.mode = LoadSaveMode.Inactive; File.Delete(temp); }
        muffler.Radius = int.MaxValue; Check(muffler.Radius == 30, "radius edits clamp the upper limit");
        muffler.Radius = int.MinValue; Check(muffler.Radius == 1, "radius edits clamp the lower limit");

        // Supply only a deterministic dry volume instead of running the native audio/graphics backend.
        // Info getters and the actual patched SanitizedVolume getter still execute on both game sample types.
        var dryMethods = new[] { AccessTools.PropertyGetter(typeof(Sample), "Volume"),
            AccessTools.PropertyGetter(typeof(SampleSustainer), "Volume") };
        foreach (var method in dryMethods)
            fixture.Patch(method, prefix: new HarmonyMethod(typeof(SoundMufflerChecks), nameof(DryVolume)));
        try
        {
            var shot = (SampleOneShot)RuntimeHelpers.GetUninitializedObject(typeof(SampleOneShot));
            var loop = (SampleSustainer)RuntimeHelpers.GetUninitializedObject(typeof(SampleSustainer));
            var sustainer = (Sustainer)RuntimeHelpers.GetUninitializedObject(typeof(Sustainer));
            var sub = (SubSustainer)RuntimeHelpers.GetUninitializedObject(typeof(SubSustainer));
            Field(sub, "parent", sustainer); Field(loop, "subSustainer", sub);
            Field(shot, "subDef", new SubSoundDef { parentDef = new SoundDef { defName = "FixtureShot" } });
            Field(loop, "subDef", new SubSoundDef { parentDef = new SoundDef { defName = "FixtureLoop" } });
            Field(shot, "info", At(20, 20, mapB)); Field(sustainer, "info", At(20, 20, mapB));
            Check(shot.SanitizedVolume == 0 && loop.SanitizedVolume == 0,
                "real one-shot and sustained sample getters are both muted");
            Field(power, "powerOnInt", false);
            Check(Math.Abs(shot.SanitizedVolume - .65f) < .0001f && Math.Abs(loop.SanitizedVolume - .65f) < .0001f,
                "same existing sample objects regain their original volume after power loss");
            Field(power, "powerOnInt", true);
            Field(shot, "info", SoundInfo.OnCamera()); Field(sustainer, "info", SoundInfo.OnCamera());
            Check(shot.SanitizedVolume > 0 && loop.SanitizedVolume > 0,
                "real camera sample getters preserve volume");
        }
        finally
        {
            foreach (var method in dryMethods) fixture.Unpatch(method, HarmonyPatchType.Prefix, fixture.Id);
        }
        var volumeGetter = AccessTools.PropertyGetter(typeof(Sample), "SanitizedVolume");
        foreach (var method in new[] { AccessTools.Method(typeof(SampleOneShot), "TryMakeAndPlay"),
            AccessTools.Method(typeof(SampleSustainer), "TryMakeAndPlay"), AccessTools.Method(typeof(Sample), "Update") })
            Check(PatchProcessor.GetOriginalInstructions(method).Any(i => i.Calls(volumeGetter)),
                "native " + method.DeclaringType.Name + "." + method.Name + " routes through the muted getter");

        var node = XDocument.Load(Path.Combine(repo, "mods/GrechkaTweaks/Defs/SoundMuffler.xml")).Root.Element("ThingDef");
        Check(node.Element("defName").Value == "Grechka_SoundMuffler_Building"
            && node.Element("designationCategory").Value == "Grechka_IndustryCategory"
            && node.Element("comps").Elements().Count(e => e.Attribute("Class")?.Value == typeof(CompProperties_SoundMuffler).FullName) == 1,
            "shipping building has a stable ID, Grechka category and exactly one muffler comp");
        Check(File.Exists(Path.Combine(repo, "mods/GrechkaTweaks/Textures", node.Element("graphicData").Element("texPath").Value + ".png")),
            "shipping graphic resolves to its own prepared PNG");
        var patchXml = new XmlDocument();
        patchXml.Load(Path.Combine(repo, "mods/GrechkaTweaks/Patches/ArchitectCategory.xml"));
        var categoryPatch = DirectXmlToObject.ObjectFromXml<PatchOperation>(patchXml.SelectSingleNode("Patch/Operation"), false);
        foreach (bool existingCategory in new[] { false, true })
        {
            var defs = new XmlDocument();
            defs.LoadXml(existingCategory ? "<Defs><DesignationCategoryDef><defName>Grechka_IndustryCategory</defName><label>Гречка</label></DesignationCategoryDef></Defs>" : "<Defs/>");
            Check(categoryPatch.Apply(defs) && categoryPatch.Apply(defs)
                && defs.SelectNodes("Defs/DesignationCategoryDef[defName='Grechka_IndustryCategory']").Count == 1,
                "native category patch works twice " + (existingCategory ? "with the collection" : "standalone"));
        }
        return checks;
    }

    public static bool DryVolume(ref float __result) { __result = .65f; return false; }
}
