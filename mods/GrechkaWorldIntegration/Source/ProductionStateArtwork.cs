using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.WorldIntegration
{
    // Five production families have authored full images for the native processor phases.
    public static class ProductionStateArtwork
    {
        private sealed class Frames
        {
            public GraphicData working, finished;
        }
        private sealed class LastFrame { public GraphicData frame; }
        private static readonly Dictionary<ThingDef, Frames> bindings = new Dictionary<ThingDef, Frames>();
        private static readonly ConditionalWeakTable<ThingComp, LastFrame> last = new ConditionalWeakTable<ThingComp, LastFrame>();
        private static readonly string[] directions = { "north", "east", "south", "west" };
        internal static readonly Type Processor = AccessTools.TypeByName("PipeSystem.CompAdvancedResourceProcessor");
        private static readonly FieldInfo stack = Processor?.GetField("processStack", AccessTools.all);
        private static readonly FieldInfo selected = stack?.FieldType.GetField("currentProcess", AccessTools.all);
        private static readonly FieldInfo progress = selected?.FieldType.GetField("progress", AccessTools.all);
        private static readonly FieldInfo ready = selected?.FieldType.GetField("pickUpReady", AccessTools.all);
        internal static bool OverlayHookReady;

        private static string[] Expected(TierArtworkDef art)
        {
            switch (art.packageId + "/" + art.sourceDefName)
            {
                case "oskarpotocki.vfe.classical/VFEC_MeatDryingRack":
                case "oskarpotocki.vfe.classical/VFEC_TyrianFarm":
                    return new[] { "in-progress", "finished" };
                case "vanillaexpanded.vcooke/VCE_CheesePress":
                case "vanillaexpanded.vcooke/VCE_ElectricPot":
                case "vanillaexpanded.vcookestews/VCE_StewPot":
                    return new[] { "full" };
                default: return Array.Empty<string>();
            }
        }

        public static bool TryBind(TierArtworkDef art, ThingDef target, Func<string, bool> hasTexture, out string reason)
        {
            reason = null;
            string[] expected = Expected(art);
            var variants = art.stateVariants ?? new List<string>();
            if (variants.Count != expected.Length || !new HashSet<string>(variants).SetEquals(expected))
            {
                reason = WorldText.Get("GrechkaWorldIntegration_02a251680dcd", "incomplete or unknown set of operating states");
                return false;
            }
            if (expected.Length == 0) return true;
            if (!OverlayHookReady || progress?.FieldType != typeof(float) || ready?.FieldType != typeof(bool)
                || target.comps?.Count(p => Processor.IsAssignableFrom(p.compClass)) != 1)
            {
                reason = WorldText.Get("GrechkaWorldIntegration_2b74ee1ff61c", "compatible PipeSystem state source not found");
                return false;
            }
            // The base Single name ends in _single. The state token always precedes the view.
            string prefix = "TierSprites/" + art.packageId + "/" + art.sourceDefName + "/" + art.sourceDefName + "_T" + art.tier;
            var frames = new Frames();
            foreach (string variant in expected)
            {
                string path = prefix + "__" + variant;
                if (art.kind == "Single") path += "_single";
                bool present = art.kind == "Single" ? hasTexture(path) : directions.All(d => hasTexture(path + "_" + d));
                if (!present)
                {
                    reason = WorldText.Get("GrechkaWorldIntegration_17543b7a088a", "missing state frames for ") + variant;
                    return false;
                }
                GraphicData data = TierArtwork.CopyData(target.graphicData);
                data.texPath = path;
                data = TierArtwork.ReuseData(target.graphicData, data);
                if (variant != "finished") frames.working = data;
                if (variant != "in-progress") frames.finished = data;
            }
            bindings[target] = frames;
            return true;
        }
        public static void Unbind(ThingDef target) => bindings.Remove(target);

        public static GraphicData FrameFor(ThingComp processor)
        {
            ThingDef def = processor?.parent?.def;
            if (def == null || !bindings.TryGetValue(def, out Frames frames) || !Processor.IsInstanceOfType(processor)) return null;
            // ProcessStack.FirstCanDo can select a new recipe. Drawing observes the selected process directly.
            object currentStack = stack.GetValue(processor);
            object current = currentStack == null ? null : selected.GetValue(currentStack);
            if (current == null) return null;
            if ((bool)ready.GetValue(current)) return frames.finished;
            float fraction = (float)progress.GetValue(current);
            return fraction > 0 && fraction < 1 ? frames.working : null;
        }

        internal static void UpdateMesh(ThingComp comp)
        {
            if (!UnityData.IsInMainThread) return;
            ThingWithComps parent = comp.parent;
            if (parent?.Spawned != true || !bindings.ContainsKey(parent.def)) return;
            GraphicData frame = FrameFor(comp);
            LastFrame previous = last.GetValue(comp, _ => new LastFrame());
            if (ReferenceEquals(previous.frame, frame)) return;
            previous.frame = frame;
            parent.Map.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlagDefOf.Things);
        }
        internal static void UseState(Thing thing, ref Graphic result)
        {
            if (!UnityData.IsInMainThread || bindings.Count == 0 || !(thing is ThingWithComps machine) || !machine.Spawned || !bindings.ContainsKey(machine.def)) return;
            var comp = machine.AllComps.Find(c => Processor.IsInstanceOfType(c));
            GraphicData frame = FrameFor(comp);
            if (frame != null) result = frame.Graphic;
        }

        // PipeSystem draws its state image over the previously printed building. Our complete state
        // replaces the base mesh instead. Leave its progress bars, result icon and every unrelated comp alone.
        public static string NativeOverlay(string nativePath, ThingComp comp) =>
            comp.parent?.def != null && bindings.ContainsKey(comp.parent.def) ? string.Empty : nativePath;
    }

    [HarmonyPatch(typeof(Thing), "get_DefaultGraphic")]
    internal static class ProductionStateGraphicHook
    {
        private static void Postfix(Thing __instance, ref Graphic __result) => ProductionStateArtwork.UseState(__instance, ref __result);
    }

    [HarmonyPatch]
    public static class ProductionStateProcessorHook
    {
        public static MethodBase TargetMethod() => ProductionStateArtwork.Processor?.GetMethod("PostDraw", AccessTools.allDeclared);
        public static bool Prepare() => TargetMethod() != null;

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var body = instructions.ToList();
            bool TextureGate(CodeInstruction c, string name) => c.opcode == OpCodes.Ldfld && c.operand is FieldInfo f
                && f.DeclaringType?.FullName == "PipeSystem.CompProperties_AdvancedResourceProcessor"
                && f.FieldType == typeof(string) && f.Name == name;
            int[] indexes = new[] { "inProgressTexture", "finishedTexture" }.Select(name =>
            {
                var reads = Enumerable.Range(0, body.Count).Where(i => TextureGate(body[i], name)).ToList();
                if (reads.Count != 1) return -1;
                int i = reads[0];
                // Substitute only the predicate guarding state textures, never an arbitrary future read.
                if (i + 3 >= body.Count || body[i + 1].opcode != OpCodes.Ldstr || !Equals(body[i + 1].operand, "")
                    || !body[i + 2].Calls(AccessTools.Method(typeof(string), "op_Inequality", new[] { typeof(string), typeof(string) }))
                    || (body[i + 3].opcode != OpCodes.Brfalse && body[i + 3].opcode != OpCodes.Brfalse_S)) return -1;
                return i;
            }).ToArray();
            if (indexes.Contains(-1))
            {
                ProductionStateArtwork.OverlayHookReady = false;
                return body;
            }
            foreach (int i in indexes.OrderByDescending(value => value))
                body.InsertRange(i + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0),
                    CodeInstruction.Call(typeof(ProductionStateArtwork), nameof(ProductionStateArtwork.NativeOverlay)) });
            ProductionStateArtwork.OverlayHookReady = true;
            return body;
        }
        // Native PostDraw has already chosen its process. Only phase transitions invalidate the map mesh.
        public static void Postfix(ThingComp __instance) => ProductionStateArtwork.UpdateMesh(__instance);
    }
}
