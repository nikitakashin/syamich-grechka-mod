using System.Runtime.CompilerServices;
using Grechka.WorldIntegration;
using HarmonyLib;
using PipeSystem;
using UnityEngine;
using Verse;

void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}
var shaders = Path.Combine(Environment.GetEnvironmentVariable("RIMWORLD_MANAGED_DIR")!, "Rendering", "Shaders");
AccessTools.Method(Type.GetType("Waylith.Rendering.ShaderSourceLoader, Waylith", true), "SetOverrideShaderDirectories")
    .Invoke(null, new object[] { new[] { shaders } });
AccessTools.Field(typeof(UnityData), "mainThreadId").SetValue(null, Environment.CurrentManagedThreadId);
var harmony = new Harmony("grechka.test.productionstate");
harmony.Patch(AccessTools.Method(typeof(Graphic), nameof(Graphic.DrawFromDef)),
    prefix: new HarmonyMethod(typeof(NativeDrawProbe), nameof(NativeDrawProbe.Capture)));
harmony.CreateClassProcessor(typeof(ProductionStateProcessorHook)).Patch();

// Real PipeSystem state owners and native PostDraw. Only the final GPU draw is intercepted.
var props = new CompProperties_AdvancedResourceProcessor {
    inProgressTexture = "original-progress", finishedTexture = "original-finished",
    showResultItem = false, showProgressBar = false, showWastepackBar = false
};
var target = new ThingDef { defName = "TierTarget", graphicData = new GraphicData {
    graphicClass = typeof(Graphic_Single), drawSize = new Vector2(3, 2), ignoreThingDrawColor = true
}, comps = new List<CompProperties> { props } };
var building = new Building { def = target };
var processor = new CompAdvancedResourceProcessor { parent = building, props = props };
var stack = (ProcessStack)RuntimeHelpers.GetUninitializedObject(typeof(ProcessStack));
var process = (Process)RuntimeHelpers.GetUninitializedObject(typeof(Process));
var selected = AccessTools.Field(typeof(ProcessStack), "currentProcess");
var fraction = AccessTools.Field(typeof(Process), "progress");
var pickup = AccessTools.Field(typeof(Process), "pickUpReady");
AccessTools.Field(typeof(CompAdvancedResourceProcessor), "processStack").SetValue(processor, stack);
selected.SetValue(stack, process);
fraction.SetValue(process, .5f);
AccessTools.Field(typeof(CompAdvancedResourceProcessor), "cachedProgressGraphic").SetValue(processor, new Graphic_Single());
AccessTools.Field(typeof(CompAdvancedResourceProcessor), "cachedFinishedGraphic").SetValue(processor, new Graphic_Single());
var rack = new TierArtworkDef { packageId = "oskarpotocki.vfe.classical", sourceDefName = "VFEC_MeatDryingRack",
    kind = "Single", tier = 3, stateVariants = new List<string> { "finished", "in-progress" } };
var roots = new HashSet<string>();
bool Has(string path) { roots.Add(path); return true; }
processor.PostDraw();
Check(NativeDrawProbe.draws == 1, $"The unbound native processor draws its original in-progress overlay (observed {NativeDrawProbe.draws}, process {processor.Process != null}, progress {processor.Process?.Progress}, ready {processor.Process?.pickUpReady})");
Check(ProductionStateArtwork.TryBind(rack, target, Has, out string reason), "Bind both rack states using installed PipeSystem: " + reason);
var frame = ProductionStateArtwork.FrameFor(processor);
// Same bound processor on the loader and render threads. Only the final image is supplied by the fixture.
var normalGraphic = new Graphic_Single();
var workingGraphic = new Graphic_Single();
AccessTools.Field(typeof(GraphicData), "cachedGraphic").SetValue(frame, workingGraphic);
var useState = AccessTools.Method(typeof(ProductionStateArtwork), "UseState");
var updateMesh = AccessTools.Method(typeof(ProductionStateArtwork), "UpdateMesh");
var priorGame = Current.Game;
Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, new List<Map> { (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)) });
AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(building, (sbyte)0);
AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(building, new List<ThingComp> { processor });
try
{
    object[] draw = { building, normalGraphic };
    Task.Run(() => { useState.Invoke(null, draw); updateMesh.Invoke(null, new object[] { processor }); }).GetAwaiter().GetResult();
    Check(ReferenceEquals(draw[1], normalGraphic), "A loader-thread graphic query retains native output and skips processor mesh updates");
    useState.Invoke(null, draw);
    Check(ReferenceEquals(draw[1], workingGraphic), "The main-thread query of that same spawned processor selects the working image");
}
finally
{
    AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(building, (sbyte)-1);
    Current.Game = priorGame;
}
Check(frame.texPath.EndsWith("_T3__in-progress_single") && frame.drawSize == target.graphicData.drawSize
    && frame.ignoreThingDrawColor && roots.Count == 2, "Working rack selects its full single-view state at native size");
processor.PostDraw();
Check(NativeDrawProbe.draws == 1 && props.inProgressTexture == "original-progress"
    && props.finishedTexture == "original-finished", "Bound processor suppresses only its old state image without editing native properties");
pickup.SetValue(process, true);
Check(ProductionStateArtwork.FrameFor(processor).texPath.EndsWith("_T3__finished_single"),
    "Native pickup-ready selects finished even if a partial progress value was saved");
pickup.SetValue(process, false);
fraction.SetValue(process, 0f);
Check(ProductionStateArtwork.FrameFor(processor) == null, "Empty rack retains its base image");
fraction.SetValue(process, 1f);
Check(ProductionStateArtwork.FrameFor(processor) == null, "One without a pickup-ready result follows native display semantics");
selected.SetValue(stack, null);
Check(ProductionStateArtwork.FrameFor(processor) == null && selected.GetValue(stack) == null,
    "Rendering a cleared process neither schedules a new process nor fabricates progress");
selected.SetValue(stack, process);
ProductionStateArtwork.Unbind(target);
fraction.SetValue(process, .5f);
processor.PostDraw();
Check(NativeDrawProbe.draws == 2, "Profile rollback restores the real processor overlay on the same instance");

var press = new TierArtworkDef { packageId = "vanillaexpanded.vcooke", sourceDefName = "VCE_CheesePress",
    kind = "Multi", tier = 2, stateVariants = new List<string> { "full" } };
target.graphicData.graphicClass = typeof(Graphic_Multi);
props.inProgressTexture = props.finishedTexture = "";
Check(!ProductionStateArtwork.TryBind(press, target, path => !path.EndsWith("_west"), out _)
    && ProductionStateArtwork.FrameFor(processor) == null, "One missing west full view refuses the entire state binding");
roots.Clear();
Check(ProductionStateArtwork.TryBind(press, target, Has, out _) && roots.Count == 4,
    "All four cheese-press views are required before applying the set");
var workingPress = ProductionStateArtwork.FrameFor(processor);
pickup.SetValue(process, true);
Check(ReferenceEquals(workingPress, ProductionStateArtwork.FrameFor(processor))
    && workingPress.texPath.EndsWith("_T2__full") && workingPress.graphicClass == typeof(Graphic_Multi),
    "Cheese press shares the prepared full graphic during work and pickup even with empty upstream state paths");
ProductionStateArtwork.Unbind(target);
press.stateVariants.Clear();
Check(!ProductionStateArtwork.TryBind(press, target, Has, out _), "Stateful family cannot be registered as base-only");
press.stateVariants.Add("finished");
Check(!ProductionStateArtwork.TryBind(press, target, Has, out _), "Wrong finished slot cannot take the place of full");
press.sourceDefName = "Unreviewed";
Check(!ProductionStateArtwork.TryBind(press, target, Has, out _), "Unreviewed family cannot register processor states");

var shader = new ShaderTypeDef { defName = "MoteSubcoreEncoder" };
ThingDef Mote(string facing) => new ThingDef { defName = "Mote_SubcoreEncoder" + facing,
    graphicData = new GraphicData { shaderType = shader } };
var east = Mote("East");
var nativeMotes = new RimWorld.CompProperties_MoteEmitter
{
    perRotationMotes = new List<ThingDef> { Mote("North"), east, Mote("South"), east },
    useParentRotation = true,
    offsetNorth = new Vector3(0, 0, -.34f), offsetSouth = new Vector3(0, 0, .66f),
    offsetEast = new Vector3(-.575f, 0, .24f), offsetWest = new Vector3(.575f, 0, .24f)
};
var emitterTarget = new ThingDef { comps = new List<CompProperties> { nativeMotes } };
var encoder = new TierArtworkDef { packageId = "ludeon.rimworld.biotech", sourceDefName = "SubcoreEncoder", tier = 4 };
Check(ProductionStateArtwork.TryBind(encoder, emitterTarget, _ => false, out _)
    && ReferenceEquals(emitterTarget.comps.Single(), nativeMotes) && nativeMotes.useParentRotation,
    "Subcore hull-only binding keeps its exact native mote-emitter properties");
Check(nativeMotes.RotationMote(Rot4.West) == nativeMotes.RotationMote(Rot4.East)
    && nativeMotes.RotationOffset(Rot4.West).x == .575f && nativeMotes.RotationOffset(Rot4.East).x == -.575f
    && nativeMotes.RotationMote(Rot4.South).graphicData.shaderType == shader,
    "Native emitter still selects the shared east/west mote, opposed offsets and its own shader");
Console.WriteLine("Production state integration verified against installed libraries.");

public static class NativeDrawProbe
{
    public static int draws;
    public static bool Capture() { draws++; return false; }
}
