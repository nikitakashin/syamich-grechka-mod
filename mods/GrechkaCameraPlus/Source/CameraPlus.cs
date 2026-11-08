using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Grechka.CameraPlus
{
    public sealed class CameraPlusSettings : ModSettings
    {
        public const float DefaultFactor = 4f;
        public float closerFactor = DefaultFactor;
        public float fartherFactor = DefaultFactor;

        public static float ValidFactor(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return DefaultFactor;
            return Math.Clamp(value, 1f, 10f);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref closerFactor, "closerFactor", DefaultFactor);
            Scribe_Values.Look(ref fartherFactor, "fartherFactor", DefaultFactor);
            closerFactor = ValidFactor(closerFactor);
            fartherFactor = ValidFactor(fartherFactor);
        }
    }

    public sealed class GrechkaCameraPlusMod : Mod
    {
        public GrechkaCameraPlusMod(ModContentPack content) : base(content)
        {
            CameraLimits.Settings = GetSettings<CameraPlusSettings>();
            new Harmony("grechka.cameraplus").PatchAll(typeof(GrechkaCameraPlusMod).Assembly);
        }

        public override string SettingsCategory() { return Content.Name; }

        public override void DoSettingsWindowContents(Rect rect)
        {
            var listing = new Listing_Standard();
            listing.Begin(rect);
            listing.Label("GrechkaCameraPlus_Intro".Translate());
            listing.Gap();
            CameraPlusSettings settings = CameraLimits.Settings;
            listing.Label("GrechkaCameraPlus_Closer".Translate(settings.closerFactor.ToString("0.0")));
            settings.closerFactor = (float)Math.Round(listing.Slider(settings.closerFactor, 1f, 10f), 1);
            listing.Label("GrechkaCameraPlus_Farther".Translate(settings.fartherFactor.ToString("0.0")));
            settings.fartherFactor = (float)Math.Round(listing.Slider(settings.fartherFactor, 1f, 10f), 1);
            listing.Gap();
            listing.Label("GrechkaCameraPlus_Help".Translate());
            listing.Gap();
            if (listing.ButtonText("GrechkaCameraPlus_Reset".Translate()))
                settings.closerFactor = settings.fartherFactor = CameraPlusSettings.DefaultFactor;
            listing.End();
        }
    }

    public static class CameraLimits
    {
        public static CameraPlusSettings Settings = new CameraPlusSettings();

        // Only the ordinary map camera. Cinematic/developer camera configurations
        // retain their own bounds; no vanilla configuration object is modified.
        public static float Minimum(CameraMapConfig config)
        {
            return config.GetType() == typeof(CameraMapConfig_Normal)
                ? config.sizeRange.min / CameraPlusSettings.ValidFactor(Settings.closerFactor)
                : config.sizeRange.min;
        }

        public static float Maximum(CameraMapConfig config)
        {
            return config.GetType() == typeof(CameraMapConfig_Normal)
                ? config.sizeRange.max * CameraPlusSettings.ValidFactor(Settings.fartherFactor)
                : config.sizeRange.max;
        }
    }

    [HarmonyPatch]
    public static class MapCameraRangePatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            // Input clamp, movement scaling and altitude must use the same range.
            // Native altitude stays 15..65, inside the native far clipping plane.
            yield return AccessTools.Method(typeof(CameraDriver), "CameraDriverOnGUI");
            yield return AccessTools.Method(typeof(CameraDriver), "Update");
            yield return AccessTools.Method(typeof(CameraDriver), "ApplyPositionToGameObject");
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            FieldInfo range = AccessTools.Field(typeof(CameraMapConfig), "sizeRange");
            FieldInfo minimum = AccessTools.Field(typeof(FloatRange), "min");
            FieldInfo maximum = AccessTools.Field(typeof(FloatRange), "max");
            int replacements = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Ldflda && Equals(code[i].operand, range)
                    && i + 1 < code.Count && code[i + 1].opcode == OpCodes.Ldfld
                    && (Equals(code[i + 1].operand, minimum) || Equals(code[i + 1].operand, maximum)))
                {
                    string method = Equals(code[i + 1].operand, minimum) ? "Minimum" : "Maximum";
                    var replacement = new CodeInstruction(code[i]);
                    replacement.opcode = OpCodes.Call;
                    replacement.operand = AccessTools.Method(typeof(CameraLimits), method);
                    replacement.labels.AddRange(code[i + 1].labels);
                    replacement.blocks.AddRange(code[i + 1].blocks);
                    yield return replacement;
                    i++;
                    replacements++;
                }
                else yield return code[i];
            }
            if (replacements == 0)
                throw new InvalidOperationException("[GrechkaCameraPlus] Camera size range reads not found; game API or camera patch conflict.");
        }
    }
}
