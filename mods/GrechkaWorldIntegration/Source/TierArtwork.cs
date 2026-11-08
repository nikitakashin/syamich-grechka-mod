using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.WorldIntegration
{
    // Only reviewed, explicitly packaged full sprites opt into replacement. TierMasks are never read.
    public sealed class TierArtworkDef : Def
    {
        public string packageId, sourceDefName, texPath, kind;
        public int tier;
        public List<string> stateVariants = new List<string>();
    }

    public static class TierArtwork
    {
        private sealed class Change
        {
            public readonly Game game;
            public readonly bool apply;
            public Change(Game game, bool apply) { this.game = game; this.apply = apply; }
        }
        private static Change pending;

        private sealed class Baseline
        {
            public GraphicData data;
            public Graphic graphic;
            public object[] icons;
        }
        private sealed class InstanceRevision { public GraphicData data; }
        private static readonly Dictionary<ThingDef, Baseline> baselines = new Dictionary<ThingDef, Baseline>();
        private static readonly Dictionary<ThingDef, Baseline> blueprintBaselines = new Dictionary<ThingDef, Baseline>();
        private static readonly HashSet<ThingDef> pendingDirty = new HashSet<ThingDef>();
        private static readonly ConditionalWeakTable<GraphicData, Dictionary<string, GraphicData>> preparedByOriginal = new();
        private static readonly HashSet<ThingDef> touched = new HashSet<ThingDef>();
        private static readonly ConditionalWeakTable<Thing, InstanceRevision> revisions = new ConditionalWeakTable<Thing, InstanceRevision>();
        private static readonly FieldInfo instanceGraphic = AccessTools.Field(typeof(Thing), "graphicInt");
        private static readonly FieldInfo minifiedGraphic = AccessTools.Field(typeof(MinifiedThing), "cachedGraphic");
        private static readonly ConditionalWeakTable<MinifiedThing, InstanceRevision> minifiedRevisions = new();
        private static readonly FieldInfo[] dataFields = typeof(GraphicData).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.Name != "cachedGraphic").ToArray();
        private static readonly MethodInfo resolveIcon = AccessTools.Method(typeof(ThingDef), "ResolveIcon");
        private static readonly FieldInfo stuffUiIcons = AccessTools.Field(typeof(BuildableDef), "stuffUiIcons");
        private static readonly FieldInfo[] iconFields = new[] { "uiIconPath", "uiIconPathsStuff", "uiIconColor", "uiIconColorTwo", "uiIcon", "uiIconMaterial", "stuffUiIcons", "uiIconAngle" }
            .Select(n => AccessTools.Field(typeof(BuildableDef), n)).ToArray();
        private static readonly string[] directions = { "north", "east", "south", "west" };
        public static bool IsBound(ThingDef def) => UnityData.IsInMainThread && def != null && baselines.ContainsKey(def);

        public static bool TryPrepare(TierArtworkDef art, ThingDef source, ThingDef target, VariantEntry entry,
            Func<string, bool> hasTexture, out GraphicData prepared, out string reason)
        {
            prepared = null; reason = null;
            if (!UnityData.IsInMainThread) { reason = WorldText.Get("GrechkaWorldIntegration_612fba43775b", "Graphics can only be prepared on the main thread"); return false; }
            if (art == null || entry == null || source == null || target == null || !WorldProfile.AppliedTiers.TryGetValue(target.defName, out int appliedTier) || appliedTier != art.tier
                || art.tier != entry.tier || art.tier < entry.minimum || art.tier < 1 || art.tier > 5
                || entry.sourceName != art.sourceDefName || source.defName != art.sourceDefName || entry.name != target.defName
                || !MaterialDictionary.BelongsTo(source, art.packageId) || !MaterialDictionary.BelongsTo(target, art.packageId))
            { reason = WorldText.Get("GrechkaWorldIntegration_f44e79ed8807", "source, tier or applied profile does not match"); return false; }
            string expected = "TierSprites/" + art.packageId + "/" + art.sourceDefName + "/" + art.sourceDefName + "_T" + art.tier;
            if (string.IsNullOrWhiteSpace(art.texPath) || !art.texPath.StartsWith(expected, StringComparison.Ordinal)
                || (art.texPath != expected && art.texPath != expected + "_single")
                || art.texPath.Contains("..") || art.texPath.Contains('\\') || art.texPath.Contains(':'))
            { reason = WorldText.Get("GrechkaWorldIntegration_3d1f3680ec10", "path is outside the targeted TierSprites contract"); return false; }
            GraphicData original = baselines.TryGetValue(target, out Baseline saved) ? saved.data : target.graphicData;
            Type kind = art.kind == "Single" ? typeof(Graphic_Single) : art.kind == "Multi" ? typeof(Graphic_Multi) : null;
            bool fixedFarm = art.packageId == "oskarpotocki.vfe.classical" && art.sourceDefName == "VFEC_TyrianFarm"
                && !target.rotatable && original?.graphicClass == typeof(Graphic_Multi) && kind == typeof(Graphic_Single);
            if (original == null || kind == null || (original.graphicClass != kind && !fixedFarm) || original.Linked
                || original.attachments?.Count > 0 || original.attachPoints?.Count > 0
                || !string.IsNullOrEmpty(original.cornerOverlayPath) || original.shaderParameters?.Count > 0
                || target.building?.turretGunDef != null
                || (target.thingClass != null && target.thingClass.Assembly != typeof(Thing).Assembly && !ReviewedExternalClass(art, target.thingClass))
                || (target.thingClass != null && AccessTools.PropertyGetter(target.thingClass, "Graphic")?.DeclaringType != typeof(Thing)))
            { reason = WorldText.Get("GrechkaWorldIntegration_2782147b6efd", "custom rendering requires a separate adapter"); return false; }
            // White full sprites intentionally replace old material masks/tints, never inherit them.
            if (hasTexture == null || (art.kind == "Multi" ? directions.Any(d => !hasTexture(art.texPath + "_" + d)) : !hasTexture(art.texPath)))
            { reason = WorldText.Get("GrechkaWorldIntegration_103ace0bbdaf", "incomplete PNG set. Original graphics preserved"); return false; }
            prepared = CopyData(original);
            prepared.texPath = art.texPath;
            prepared.graphicClass = kind;
            prepared.maskPath = null;
            prepared.shaderType = ShaderTypeDefOf.Cutout;
            prepared.color = prepared.colorTwo = Color.white;
            prepared.ignoreThingDrawColor = true;
            prepared = ReuseData(original, prepared);
            return true;
        }

        private static bool ReviewedExternalClass(TierArtworkDef art, Type type)
        {
            bool reviewed = (art.packageId == "aoba.fortress.industrial.citadel"
                && (art.sourceDefName == "FT_ArcFurnace" || art.sourceDefName == "FT_HeavyChem")
                && type.FullName == "Fortified.Building_WorkTableAutonomous") ||
                (art.packageId == "vanillaexpanded.vnutriente" && art.sourceDefName == "VNPE_NutrientPasteGrinder"
                && type.FullName == "VNPE.Building_NutrientGrinder");
            // These exact installed classes inherit both map render routes. A future override must be reviewed again.
            return reviewed && AccessTools.Method(type, "DrawAt", new[] { typeof(Vector3), typeof(bool) })?.DeclaringType == typeof(ThingWithComps)
                && AccessTools.Method(type, "Print", new[] { typeof(SectionLayer) })?.DeclaringType == typeof(ThingWithComps);
        }

        internal static GraphicData ReuseData(GraphicData original, GraphicData next)
        {
            // GraphicDatabase keys include the data identity. Reapplying a world should reuse its prepared material set.
            var byPath = preparedByOriginal.GetValue(original, _ => new Dictionary<string, GraphicData>());
            if (byPath.TryGetValue(next.texPath, out GraphicData cached) && dataFields.All(f => Equals(f.GetValue(next), f.GetValue(cached))))
                return cached;
            return byPath[next.texPath] = next;
        }

        internal static GraphicData CopyData(GraphicData original)
        {
            var copy = new GraphicData();
            foreach (FieldInfo field in dataFields) field.SetValue(copy, field.GetValue(original));
            return copy;
        }
        private static Baseline Capture(ThingDef def) => new Baseline { data = def.graphicData, graphic = def.graphic,
            icons = iconFields.Select(f => f.GetValue(def)).ToArray() };

        public static bool TryApply(TierArtworkDef art, ThingDef source, ThingDef target, VariantEntry entry,
            Func<string, bool> hasTexture, Action<ThingDef> refresh, out string reason)
        {
            if (!TryPrepare(art, source, target, entry, hasTexture, out GraphicData prepared, out reason)) return false;
            bool captured = baselines.ContainsKey(target);
            if (!captured) baselines[target] = Capture(target);
            var changedBlueprints = new List<ThingDef>();
            try
            {
                target.graphicData = prepared;
                target.uiIconPath = art.kind == "Multi" ? art.texPath + "_" + directions[target.defaultPlacingRot.AsInt] : art.texPath;
                target.uiIconColor = target.uiIconColorTwo = Color.white;
                target.uiIconPathsStuff = null;
                stuffUiIcons.SetValue(target, null);
                target.uiIconMaterial = null;
                target.uiIconAngle = 0;
                refresh(target);
                target.uiIconColor = target.uiIconColorTwo = Color.white;
                // Default generated construction/install blueprints use the same silhouette with native edge/transparent rendering.
                // Deliberately authored blueprint graphics and the generic construction Frame stay untouched.
                if (target.building?.blueprintGraphicData == null)
                    foreach (ThingDef blueprint in new[] { target.blueprintDef, target.installBlueprintDef }.Where(d => d != null).Distinct())
                    {
                        GraphicData previous = blueprintBaselines.TryGetValue(blueprint, out Baseline savedBlueprint) ? savedBlueprint.data : blueprint.graphicData;
                        if (blueprint.entityDefToBuild != target || previous == null || previous.graphicClass != baselines[target].data.graphicClass
                            || previous.texPath != baselines[target].data.texPath || previous.Linked || previous.attachments?.Count > 0) continue;
                        if (!blueprintBaselines.ContainsKey(blueprint)) blueprintBaselines[blueprint] = Capture(blueprint);
                        changedBlueprints.Add(blueprint);
                        var next = CopyData(previous); next.texPath = prepared.texPath; next.maskPath = null;
                        next.graphicClass = prepared.graphicClass;
                        blueprint.graphicData = ReuseData(previous, next);
                        blueprint.uiIconMaterial = null;
                        blueprint.uiIconAngle = 0;
                        refresh(blueprint);
                        MarkChanged(blueprint);
                    }
                if (!ProductionStateArtwork.TryBind(art, target, hasTexture, out reason))
                    throw new InvalidOperationException(reason);
                MarkChanged(target);
                return true;
            }
            catch (Exception ex)
            {
                ProductionStateArtwork.Unbind(target);
                RestoreOne(target, baselines[target]);
                baselines.Remove(target);
                foreach (ThingDef blueprint in changedBlueprints)
                { RestoreOne(blueprint, blueprintBaselines[blueprint]); blueprintBaselines.Remove(blueprint); }
                reason ??= WorldText.Get("GrechkaWorldIntegration_cb2bf0b7a02a", "graphics loading failed. Original restored: ") + ex.GetType().Name;
                return false;
            }
        }

        // LoadedGame runs on the long-event worker. Publish only the latest desired world state there.
        // ExecuteWhenFinished may invoke its argument immediately on the caller when no active event exists.
        public static void Apply() => Request(new Change(Current.Game, true));
        public static void Restore() => Request(new Change(Current.Game, false));

        private static void Request(Change change)
        {
            Interlocked.Exchange(ref pending, change);
            if (UnityData.IsInMainThread) FinishPending();
        }

        internal static void FinishPending()
        {
            if (Volatile.Read(ref pending) == null || !UnityData.IsInMainThread || LongEventHandler.AnyEventNowOrWaiting) return;
            Change change = Interlocked.Exchange(ref pending, null);
            if (change == null) return;
            // Restore may have been superseded by Apply while loading. Always restore before binding the active world.
            RestoreNow();
            if (change.apply && change.game != null && ReferenceEquals(change.game, Current.Game)) ApplyNow();
            else DirtyMaps();
        }

        private static void ApplyNow()
        {
            var groups = DefDatabase<TierArtworkDef>.AllDefsListForReading.Where(a => MaterialDictionary.BelongsTo(a, "grechka.world.integration")).GroupBy(a => (a.packageId ?? "").ToLowerInvariant() + "/" + a.sourceDefName + "/" + a.tier);
            foreach (var group in groups)
            {
                if (group.Count() != 1) { WorldProfile.Report.Add(WorldText.Get("GrechkaWorldIntegration_231347d8fac7", "Graphics has duplicate bindings for ") + group.Key + WorldText.Get("GrechkaWorldIntegration_a32e0f503e68", ". Original preserved.")); continue; }
                TierArtworkDef art = group.Single();
                if (string.IsNullOrWhiteSpace(art.sourceDefName) || string.IsNullOrWhiteSpace(art.packageId))
                { WorldProfile.Report.Add(WorldText.Get("GrechkaWorldIntegration_734e11a1f21e", "Graphics ") + art.defName + WorldText.Get("GrechkaWorldIntegration_b5e53db9cf59", ": source is missing")); continue; }
                ProfileVariants.Entries.TryGetValue(art.sourceDefName, out VariantEntry entry);
                if (entry?.tier != art.tier)
                    ProfileVariants.Entries.TryGetValue("GrechkaWorld_" + art.sourceDefName + "_T" + art.tier + "_Building", out entry);
                ThingDef source = DefDatabase<ThingDef>.GetNamedSilentFail(art.sourceDefName);
                ThingDef target = entry == null ? null : DefDatabase<ThingDef>.GetNamedSilentFail(entry.name);
                bool Has(string path) => art.modContentPack.GetContentHolder<Texture2D>()?.Get(path) != null;
                if (!TryApply(art, source, target, entry, Has, def => { def.graphic = def.graphicData.Graphic; resolveIcon.Invoke(def, null); }, out string reason))
                    WorldProfile.Report.Add(WorldText.Get("GrechkaWorldIntegration_734e11a1f21e", "Graphics ") + art.defName + ": " + reason);
            }
            DirtyMaps();
        }

        private static void RestoreOne(ThingDef def, Baseline saved)
        {
            def.graphicData = saved.data; def.graphic = saved.graphic;
            for (int i = 0; i < iconFields.Length; i++) iconFields[i].SetValue(def, saved.icons[i]);
            MarkChanged(def);
        }
        private static void RestoreNow()
        {
            foreach (var pair in baselines) { ProductionStateArtwork.Unbind(pair.Key); RestoreOne(pair.Key, pair.Value); }
            baselines.Clear();
            foreach (var pair in blueprintBaselines) RestoreOne(pair.Key, pair.Value);
            blueprintBaselines.Clear();
        }
        private static void MarkChanged(ThingDef def) { touched.Add(def); pendingDirty.Add(def); }
        private static void DirtyMaps()
        {
            if (pendingDirty.Count == 0) return;
            if (AccessTools.Field(typeof(GhostUtility), "ghostGraphics").GetValue(null) is System.Collections.IDictionary ghosts) ghosts.Clear();
            if (Current.Game?.Maps != null)
            {
                foreach (Map map in Current.Game.Maps)
                {
                    if (map?.listerThings == null || map.mapDrawer == null) continue;
                    foreach (Thing thing in map.listerThings.AllThings)
                    {
                        ThingDef innerDef = (thing as MinifiedThing)?.InnerThing?.def;
                        if (pendingDirty.Contains(thing.def) || (innerDef != null && pendingDirty.Contains(innerDef)))
                            map.mapDrawer.MapMeshDirty(thing.Position, MapMeshFlagDefOf.Things);
                    }
                }
            }
            pendingDirty.Clear();
        }
        public static void RefreshMinified(MinifiedThing packed)
        {
            if (!UnityData.IsInMainThread) return;
            var def = packed?.InnerThing?.def;
            if (def == null || !touched.Contains(def)) return;
            var revision = minifiedRevisions.GetValue(packed, _ => new InstanceRevision());
            if (ReferenceEquals(revision.data, def.graphicData)) return;
            minifiedGraphic.SetValue(packed, null);
            revision.data = def.graphicData;
        }
        // Covers map, minified, caravan and disk-held instances without an expensive inventory traversal.
        public static void RefreshInstance(Thing thing)
        {
            if (!UnityData.IsInMainThread || thing?.def == null || !touched.Contains(thing.def)) return;
            InstanceRevision revision = revisions.GetValue(thing, _ => new InstanceRevision());
            if (ReferenceEquals(revision.data, thing.def.graphicData)) return;
            instanceGraphic.SetValue(thing, null);
            revision.data = thing.def.graphicData;
        }
    }

    // Root.Update calls this on Unity's main thread, including while loading or paused.
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsUpdate))]
    internal static class TierArtworkLoadFinishedHook
    { private static void Postfix() => TierArtwork.FinishPending(); }

    [HarmonyPatch(typeof(Thing), "get_DefaultGraphic")]
    internal static class TierArtworkInstanceHook
    { private static void Prefix(Thing __instance) => TierArtwork.RefreshInstance(__instance); }

    [HarmonyPatch(typeof(MinifiedThing), "get_Graphic")]
    internal static class TierArtworkMinifiedHook
    { private static void Prefix(MinifiedThing __instance) => TierArtwork.RefreshMinified(__instance); }

    [HarmonyPatch(typeof(BuildableDef), nameof(BuildableDef.GetColorForStuff))]
    internal static class TierArtworkIconColorHook
    {
        private static bool Prefix(BuildableDef __instance, ref Color __result)
        {
            if (!(__instance is ThingDef def) || !TierArtwork.IsBound(def)) return true;
            __result = Color.white; return false;
        }
    }
}
