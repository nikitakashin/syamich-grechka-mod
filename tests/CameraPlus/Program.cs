using System.Reflection;
using System.Reflection.Emit;
using Grechka.CameraPlus;
using HarmonyLib;
using Verse;

static void Check(bool ok, string label)
{
    if (!ok) throw new Exception(label);
    Console.WriteLine("PASS " + label);
}

var config = new CameraMapConfig_Normal();
var vanilla = config.sizeRange;
Console.WriteLine($"Installed vanilla camera range: {vanilla.min}..{vanilla.max}");
Check(PatchProcessor.GetOriginalInstructions(AccessTools.Constructor(typeof(CameraDriver)))
    .Any(i => i.opcode == OpCodes.Newobj && i.operand is ConstructorInfo c && c.DeclaringType == typeof(CameraMapConfig_Normal)),
    "Installed map camera creates the supported normal configuration by default");
CameraLimits.Settings = new CameraPlusSettings();
Check(CameraLimits.Minimum(config) == vanilla.min / 4f && CameraLimits.Maximum(config) == vanilla.max * 4f,
    "Default zoom reaches four times closer and farther than installed vanilla limits");
CameraLimits.Settings.closerFactor = 10f;
CameraLimits.Settings.fartherFactor = 10f;
Check(CameraLimits.Minimum(config) > 0 && CameraLimits.Minimum(config) < CameraLimits.Maximum(config),
    "Maximum settings preserve a positive, nonzero altitude normalization range");
CameraLimits.Settings.closerFactor = CameraLimits.Settings.fartherFactor = 1f;
Check(CameraLimits.Minimum(config) == vanilla.min && CameraLimits.Maximum(config) == vanilla.max,
    "Factors of one restore the exact native range");
foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
    Check(CameraPlusSettings.ValidFactor(invalid) == 4f, "Invalid saved factor recovers: " + invalid);
Check(CameraPlusSettings.ValidFactor(-10f) == 1f && CameraPlusSettings.ValidFactor(1000f) == 10f,
    "Out of range saved settings are bounded");
CameraLimits.Settings = new CameraPlusSettings();
foreach (CameraMapConfig special in new CameraMapConfig[]
    { new CameraMapConfig_ContinuousPan(), new CameraMapConfig_ContinuousPanAndZoom(),
      new CameraMapConfig_Car(), new CameraMapConfig_CarWithContinuousZoom(), new CameraMapConfig_SmoothZoom() })
    Check(CameraLimits.Minimum(special) == special.sizeRange.min && CameraLimits.Maximum(special) == special.sizeRange.max,
        "Special camera keeps its own range: " + special.GetType().Name);
Check(config.sizeRange.min == vanilla.min && config.sizeRange.max == vanilla.max,
    "Reading extended limits never mutates or persists vanilla camera configuration");

var harmony = new Harmony("grechka.cameraplus.isolated-check");
harmony.PatchAll(typeof(CameraLimits).Assembly);
var targets = MapCameraRangePatch.TargetMethods().ToArray();
Check(targets.Length == 3 && targets.All(m => m.DeclaringType == typeof(CameraDriver)),
    "Only map input, map update and map positioning are patched");
int total = 0;
foreach (var target in targets)
{
    var original = PatchProcessor.GetOriginalInstructions(target);
    int reads = original.Count(i => i.opcode == OpCodes.Ldflda && Equals(i.operand, AccessTools.Field(typeof(CameraMapConfig), "sizeRange")));
    var patched = PatchProcessor.GetCurrentInstructions(target);
    var calls = patched.Where(i => i.opcode == OpCodes.Call && i.operand is MethodInfo m && m.DeclaringType == typeof(CameraLimits)).ToArray();
    Check(reads > 0 && calls.Length == reads && !patched.Any(i => i.opcode == OpCodes.Ldflda && Equals(i.operand, AccessTools.Field(typeof(CameraMapConfig), "sizeRange"))),
        target.Name + ": all " + reads + " native range reads replaced by actual Harmony patch");
    Check(Harmony.GetPatchInfo(target).Transpilers.Any(p => p.owner == harmony.Id), "Patch binds to native method: " + target.Name);
    // Each replacement removes one field instruction and otherwise preserves the
    // native input/smoothing/zoom-to-mouse/rendering implementation.
    Check(patched.Count == original.Count - reads, "Native camera behavior retained around range reads: " + target.Name);
    total += reads;
}
bool rejected = false;
try { MapCameraRangePatch.Transpiler(new[] { new CodeInstruction(OpCodes.Ret) }).ToList(); }
catch (InvalidOperationException) { rejected = true; }
Check(rejected, "Missing game API pattern fails explicitly instead of silently doing nothing");
harmony.UnpatchAll(harmony.Id);
Check(targets.All(m => Harmony.GetPatchInfo(m)?.Transpilers.All(p => p.owner != harmony.Id) != false),
    "Removing hooks restores native code without configuration changes");
Console.WriteLine("PASS CameraPlus: " + total + " native range reads verified. No game launched; live visual acceptance remains pending.");
