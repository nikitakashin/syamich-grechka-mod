using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Grechka.WorldIntegration;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

public static class TierArtworkChecks
{
    private static void Check(bool value, string label)
    { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
    public static void Run(ModContentPack pack)
    {
        Check(new ModContentHolder<Texture2D>(pack).Get("TierSprites/fixture/missing") == null, "Native content holder returns null for absent artwork without loading a bad graphic");
        var original = new GraphicData { graphicClass = typeof(Graphic_Multi), texPath = "Original/Machine", drawSize = new Vector2(2, 1),
            drawOffset = new Vector3(.1f, .2f, .3f), drawOffsetEast = new Vector3(.4f, 0, .5f), color = Color.red,
            colorTwo = Color.blue, maskPath = "Original/Mask", ignoreThingDrawColor = false, renderQueue = 3010, damageData = new DamageGraphicData { cornerTL = "Damage/Corner" }, overlayOpacity = .75f };
        var baseDef = new ThingDef { defName = "TierArtFixture", modContentPack = pack, graphicData = original, thingClass = typeof(Building),
            uiIconPath = "Original/Icon", uiIconColor = Color.red, uiIconColorTwo = Color.blue, defaultPlacingRot = Rot4.South };
        var laterDef = new ThingDef { defName = "GrechkaWorld_TierArtFixture_T3_Building", modContentPack = pack, graphicData = original,
            thingClass = typeof(Building), defaultPlacingRot = Rot4.South };
        baseDef.label = "fixture machine"; baseDef.building = new BuildingProperties(); baseDef.category = ThingCategory.Building;
        var blueprintFactory = AccessTools.Method(typeof(ThingDefGenerator_Buildings), "NewBlueprintDef_Thing");
        baseDef.blueprintDef = (ThingDef)blueprintFactory.Invoke(null, new object[] { baseDef, false, null, false });
        baseDef.installBlueprintDef = (ThingDef)blueprintFactory.Invoke(null, new object[] { baseDef, true, baseDef.blueprintDef, false });
        var blueprintData = baseDef.blueprintDef.graphicData;
        var installData = baseDef.installBlueprintDef.graphicData;
        var edgeShader = new ShaderTypeDef { defName = "FixtureEdgeShader" };
        blueprintData.shaderType = edgeShader;
        var frameData = new GraphicData { texPath = "Generic/Frame" };
        baseDef.frameDef = new ThingDef { defName = "FixtureFrame", graphicData = frameData };
        var oldUiMaterial = (Material)RuntimeHelpers.GetUninitializedObject(typeof(Material));
        baseDef.uiIconMaterial = oldUiMaterial;
        baseDef.uiIconAngle = 177;
        var baseEntry = new VariantEntry { sourceName = baseDef.defName, name = baseDef.defName, tier = 2, minimum = 2 };
        var laterEntry = new VariantEntry { sourceName = baseDef.defName, name = laterDef.defName, tier = 3, minimum = 2 };
        var art = new TierArtworkDef { defName = "TierArtFixtureBinding", packageId = pack.PackageIdPlayerFacing, sourceDefName = baseDef.defName,
            tier = 2, kind = "Multi", texPath = "TierSprites/" + pack.PackageIdPlayerFacing + "/TierArtFixture/TierArtFixture_T2" };
        bool All(string path) => true;
        bool changed = TierArtwork.TryPrepare(art, baseDef, baseDef, baseEntry, All, out _, out _);
        Check(!changed && ReferenceEquals(baseDef.graphicData, original), "Unapproved profile cannot acquire new tier artwork");
        WorldProfile.AppliedTiers[baseDef.defName] = 2; WorldProfile.AppliedTiers[laterDef.defName] = 3;
        Check(!TierArtwork.TryPrepare(art, baseDef, baseDef, baseEntry, path => !path.EndsWith("_west"), out _, out _), "Partial Multi artwork keeps the original in all directions");
        art.packageId = "another.package";
        Check(!TierArtwork.TryPrepare(art, baseDef, baseDef, baseEntry, All, out _, out _), "An identical DefName in a different package cannot be rebound");
        art.packageId = pack.PackageIdPlayerFacing;
        art.texPath += "/../stolen";
        Check(!TierArtwork.TryPrepare(art, baseDef, baseDef, baseEntry, All, out _, out _), "Tier artwork paths cannot escape their addressed package/family/tier");
        art.texPath = art.texPath.Replace("/../stolen", "");
        art.tier = 1;
        Check(!TierArtwork.TryPrepare(art, baseDef, baseDef, baseEntry, All, out _, out _), "A T2 family never gains a fabricated T1 artwork binding");
        art.tier = 2;
        Check(TierArtwork.TryApply(art, baseDef, baseDef, baseEntry, All, def =>
        {
            if (def == baseDef) Check(ReferenceEquals(def.uiIconMaterial, null) && def.uiIconAngle == 0,
                "A tier icon cannot accidentally keep the previous shader material or rotation");
        }, out _), "Complete addressed tier artwork can be applied without touching gameplay fields");
        var replacement = baseDef.graphicData;
        Check(ReferenceEquals(replacement.damageData, original.damageData), "Native damage scratches remain attached to an intact tiered building graphic");
        Check(baseDef.blueprintDef.graphicData.texPath == art.texPath && baseDef.installBlueprintDef.graphicData.texPath == art.texPath
            && baseDef.blueprintDef.graphicData.color == blueprintData.color && baseDef.blueprintDef.graphicData.color != Color.white
            && ReferenceEquals(baseDef.blueprintDef.graphicData.shaderType, edgeShader) && baseDef.blueprintDef.graphicData.renderQueue == blueprintData.renderQueue,
            "Native generated build/install blueprints gain the new silhouette while preserving their edge shader, blue color and render queue");
        Check(ReferenceEquals(baseDef.frameDef.graphicData, frameData), "The native generic construction frame is not replaced by a finished full-color machine");
        var existingBlueprint = new Blueprint_Build { def = baseDef.blueprintDef };
        AccessTools.Field(typeof(Thing), "graphicInt").SetValue(existingBlueprint, new FixtureGraphic());
        TierArtwork.RefreshInstance(existingBlueprint);
        Check(AccessTools.Field(typeof(Thing), "graphicInt").GetValue(existingBlueprint) == null, "Existing native blueprint instances discard their old silhouette cache");
        Check(!ReferenceEquals(replacement, original) && ReferenceEquals(laterDef.graphicData, original) && original.texPath == "Original/Machine",
            "Changing base-tier artwork cannot leak through a shared GraphicData into higher tiers");
        Check(replacement.drawSize == original.drawSize && replacement.drawOffset == original.drawOffset && replacement.drawOffsetEast == original.drawOffsetEast
            && replacement.renderQueue == 3010 && replacement.overlayOpacity == .75f,
            "Full-sprite replacement preserves native geometry, directional offsets and rendering metadata");
        Check(replacement.color == Color.white && replacement.colorTwo == Color.white && replacement.maskPath == null && replacement.ignoreThingDrawColor,
            "Full authored tier art is never globally recolored by inherited masks or stuff colors");
        Check(baseDef.uiIconPath.EndsWith("_T2_south") && baseDef.uiIconColor == Color.white,
            "Architect/info icon points at the new tier and native default placing direction");
        var held = new Thing { def = baseDef }; var staleGraphic = new FixtureGraphic();
        AccessTools.Field(typeof(Thing), "graphicInt").SetValue(held, staleGraphic);
        TierArtwork.RefreshInstance(held);
        Check(AccessTools.Field(typeof(Thing), "graphicInt").GetValue(held) == null,
            "Already-existing held/minified/caravan objects invalidate their stale graphical cache lazily");
        AccessTools.Field(typeof(Thing), "graphicInt").SetValue(held, staleGraphic);
        TierArtwork.RefreshInstance(held);
        Check(ReferenceEquals(AccessTools.Field(typeof(Thing), "graphicInt").GetValue(held), staleGraphic), "Unchanged artwork does not repeatedly flush instance graphics");
        if (Current.Game.Maps == null) AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, new List<Map>());
        var packed = new MinifiedThing { def = new ThingDef { defName = "FixturePacked", category = ThingCategory.Item }, InnerThing = held };
        FieldInfo packedCache = AccessTools.Field(typeof(MinifiedThing), "cachedGraphic");
        packedCache.SetValue(packed, staleGraphic);
        TierArtwork.RefreshMinified(packed);
        Check(packedCache.GetValue(packed) == null, "Native MinifiedThing wrapper invalidates its own separate cachedGraphic on artwork apply");
        packedCache.SetValue(packed, staleGraphic);
        TierArtwork.RefreshMinified(packed);
        Check(ReferenceEquals(packedCache.GetValue(packed), staleGraphic), "Unchanged packaged artwork stays cached");
        var ghosts = (System.Collections.IDictionary)AccessTools.Field(typeof(GhostUtility), "ghostGraphics").GetValue(null);
        ghosts[17521] = new FixtureGraphic();
        TierArtwork.Restore();
        Check(ReferenceEquals(baseDef.blueprintDef.graphicData, blueprintData) && ReferenceEquals(baseDef.installBlueprintDef.graphicData, installData)
            && ghosts.Count == 0, "Restore recovers original generated blueprints and clears placement-ghost cache");
        Check(ReferenceEquals(baseDef.graphicData, original) && baseDef.uiIconPath == "Original/Icon" && baseDef.uiIconColor == Color.red
            && baseDef.uiIconColorTwo == Color.blue && !TierArtwork.IsBound(baseDef), "Switching away restores original map and UI artwork exactly");
        TierArtwork.RefreshMinified(packed);
        Check(packedCache.GetValue(packed) == null && ReferenceEquals(baseDef.uiIconMaterial, oldUiMaterial) && baseDef.uiIconAngle == 177,
            "Restore refreshes the minified wrapper and recovers the exact original UI material/angle");
        Check(TierArtwork.TryPrepare(art, baseDef, baseDef, baseEntry, All, out var again, out _) && ReferenceEquals(again, replacement),
            "Loading the same world again reuses the prepared GraphicData and native graphic cache key");
        TierArtwork.RefreshInstance(held);
        Check(AccessTools.Field(typeof(Thing), "graphicInt").GetValue(held) == null, "Restoration also invalidates a held item's tier artwork cache");
        Check(!TierArtwork.TryApply(art, baseDef, baseDef, baseEntry, All, _ => throw new InvalidOperationException("fixture decoder failure"), out _)
            && ReferenceEquals(baseDef.graphicData, original) && !TierArtwork.IsBound(baseDef), "A graphics load exception restores the original binding without leaving half-updated UI");
        art.stateVariants.Add("unknown-state");
        Check(!TierArtwork.TryApply(art, baseDef, baseDef, baseEntry, All, _ => {}, out var stateReason) && stateReason != null
            && ReferenceEquals(baseDef.graphicData, original) && ReferenceEquals(baseDef.blueprintDef.graphicData, blueprintData)
            && ReferenceEquals(baseDef.uiIconMaterial, oldUiMaterial), "A rejected state set rolls back the already-staged base, blueprint and UI together");
        art.stateVariants.Clear();
        original.attachments = new() { new GraphicData() };
        Check(!TierArtwork.TryPrepare(art, baseDef, baseDef, baseEntry, All, out _, out _), "Multilayer artwork cannot be silently flattened by the simple sprite adapter");
        original.attachments = null; original.graphicClass = typeof(Graphic_Random);
        Check(!TierArtwork.TryPrepare(art, baseDef, baseDef, baseEntry, All, out _, out _), "Custom/random native renderers require a separate explicit adapter");
        original.graphicClass = typeof(Graphic_Multi);
        baseDef.thingClass = typeof(FixtureExternalBuilding);
        Check(!TierArtwork.TryPrepare(art, baseDef, baseDef, baseEntry, All, out _, out _), "Third-party building render classes remain native until an addressed adapter is supplied");
        baseDef.thingClass = typeof(Building);
        WorldProfile.AppliedTiers.Remove(baseDef.defName); WorldProfile.AppliedTiers.Remove(laterDef.defName);
        CheckPackagedCatalogue();
    }
    // Optional native-library fixture. Uses the current exported catalogue plus installed source definitions.
    // No game/GPU is started. Missing optional authoring inputs are reported explicitly.
    private static void CheckPackagedCatalogue()
    {
        string audit = "work/machine-audit/data.json";
        if (!File.Exists(audit)) { Console.WriteLine("SKIP installed production art source audit (no local source catalogue)"); return; }
        using var document = JsonDocument.Parse(File.ReadAllText(audit));
        JsonElement things = document.RootElement.GetProperty("things");
        var textureRoot = "mods/GrechkaWorldIntegration/Textures";
        var records = XDocument.Load("mods/GrechkaWorldIntegration/Defs/TierArtwork.xml").Root!.Elements().ToList();
        var sources = new Dictionary<string, XDocument>();
        var minima = XDocument.Load("mods/GrechkaWorldIntegration/Defs/MachineProfile.xml").Root!.Elements()
            .Where(n => (string)n.Element("addHousing") == "true").ToDictionary(n => (string)n.Element("packageId") + "/" + (string)n.Element("targetDefName"), n => (int)n.Element("minimumTier")!);
        int checkedForms = 0, checkedExternal = 0;
        var families = new HashSet<string>();
        var optional = new[] { "SPECIAL_FORTIFIED_DLL", "SPECIAL_VNPE_DLL" }.Select(Environment.GetEnvironmentVariable)
            .Where(path => path != null && File.Exists(path)).Select(path => Assembly.LoadFrom(path!)).ToList();
        if (optional.Count != 2 && records.Any(n => n.Element("sourceDefName")?.Value == "FT_ArcFurnace"))
        { Console.WriteLine("SKIP installed production art source audit (set SPECIAL_FORTIFIED_DLL and SPECIAL_VNPE_DLL)"); return; }
        foreach (var row in records)
        {
            string sourceName = row.Element("sourceDefName")!.Value;
            var sourceInfo = things.GetProperty(sourceName);
            string xmlFile = sourceInfo.GetProperty("file").GetString()!;
            if (!sources.TryGetValue(xmlFile, out var xml)) sources.Add(xmlFile, xml = XDocument.Load(xmlFile));
            var native = xml.Root!.Elements("ThingDef").First(n => (string)n.Element("defName") == sourceName);
            var graphic = native.Element("graphicData")!;
            string className = sourceInfo.GetProperty("class").GetString()!;
            var type = typeof(Thing).Assembly.GetType("Verse." + className) ?? typeof(Thing).Assembly.GetType("RimWorld." + className)
                ?? optional.Select(a => a.GetType(className)).First(t => t != null);
            var pack = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
            string package = row.Element("packageId")!.Value;
            if (package != sourceInfo.GetProperty("mod").GetString()) throw new Exception("Art package/source mismatch: " + sourceName);
            AccessTools.Field(typeof(ModContentPack), "packageIdPlayerFacingInt").SetValue(pack, package);
            int tier = (int)row.Element("tier")!;
            string graphicClass = graphic.Element("graphicClass")?.Value ?? "Graphic_Single";
            var original = new GraphicData { texPath = graphic.Element("texPath")!.Value, graphicClass = typeof(Thing).Assembly.GetType("Verse." + graphicClass, true),
                damageData = graphic.Element("damageData") == null ? null : new DamageGraphicData() };
            var def = new ThingDef { defName = sourceName, modContentPack = pack, thingClass = type, graphicData = original,
                rotatable = !string.Equals((string)native.Element("rotatable"), "false", StringComparison.OrdinalIgnoreCase) };
            int minimum = minima[package + "/" + sourceName];
            string targetName = minimum == tier ? sourceName : "GrechkaWorld_" + sourceName + "_T" + tier + "_Building";
            var target = minimum == tier ? def : new ThingDef { defName = targetName, modContentPack = pack, thingClass = type, graphicData = original, rotatable = def.rotatable };
            var entry = new VariantEntry { sourceName = sourceName, name = targetName, minimum = minimum, tier = tier };
            var binding = new TierArtworkDef { sourceDefName = sourceName, packageId = package, tier = tier, kind = (string)row.Element("kind"), texPath = (string)row.Element("texPath") };
            WorldProfile.AppliedTiers[targetName] = tier;
            bool exists(string path) => File.Exists(Path.Combine(textureRoot, path + ".png"));
            if (!TierArtwork.TryPrepare(binding, def, target, entry, exists, out var prepared, out var reason))
                throw new Exception("Installed production binding rejected: " + sourceName + " T" + tier + " (" + className + ", " + graphicClass + "): " + reason);
            if (!ReferenceEquals(original.damageData, prepared.damageData)) throw new Exception("Damage graphics lost: " + sourceName);
            if (sourceName == "VFEC_TyrianFarm" && tier == minimum)
            {
                target.rotatable = true;
                Check(!TierArtwork.TryPrepare(binding, def, target, entry, exists, out _, out _), "The single-view Tyrian farm exception rejects a changed, freely rotatable source");
            }
            WorldProfile.AppliedTiers.Remove(targetName);
            families.Add(sourceName); checkedForms++; if (type.Assembly != typeof(Thing).Assembly) checkedExternal++;
        }
        Check(checkedForms == records.Count, $"All {checkedForms} packaged base tier bindings across {families.Count} families accept actual installed class/XML and packaged PNGs ({checkedExternal} external class forms)");
    }
    private sealed class FixtureGraphic : Graphic { }
    private sealed class FixtureExternalBuilding : Building { }
}
