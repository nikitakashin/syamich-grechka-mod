using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Grechka.Tweaks
{
    public sealed class TweaksSettings : ModSettings
    {
        public const int DefaultSize = 750;
        // The collection has extractors with fixed output batches of up to ten.
        public const int MinSize = 10;
        public const int MaxSize = 10000;
        public int stackSize = DefaultSize;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref stackSize, "stackSize", DefaultSize);
            stackSize = Math.Clamp(stackSize, MinSize, MaxSize);
        }
    }

    public sealed class TweaksMod : Mod
    {
        internal static TweaksSettings Settings = new TweaksSettings();
        private string sizeBuffer;

        public TweaksMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<TweaksSettings>();
            var harmony = new Harmony("grechka.tweaks");
            harmony.PatchAll(typeof(TweaksMod).Assembly);
            Editors.Install();
        }

        public override string SettingsCategory() => Content.Name;

        public override void DoSettingsWindowContents(Rect rect)
        {
            var listing = new Listing_Standard();
            listing.Begin(rect);
            sizeBuffer ??= Settings.stackSize.ToString();
            listing.TextFieldNumericLabeled("GrechkaTweaks_StackSize".Translate(),
                ref Settings.stackSize, ref sizeBuffer, TweaksSettings.MinSize, TweaksSettings.MaxSize);
            listing.Gap();
            listing.Label("GrechkaTweaks_StackHelp".Translate(TweaksSettings.MinSize, TweaksSettings.MaxSize));
            if (listing.ButtonText("GrechkaTweaks_ResetStack".Translate(TweaksSettings.DefaultSize)))
            {
                Settings.stackSize = TweaksSettings.DefaultSize;
                sizeBuffer = Settings.stackSize.ToString();
            }
            listing.End();
        }

        public override void WriteSettings()
        {
            Settings.stackSize = Math.Clamp(Settings.stackSize, TweaksSettings.MinSize, TweaksSettings.MaxSize);
            sizeBuffer = null;
            base.WriteSettings();
            StackLimits.Apply(Settings.stackSize);
        }
    }

    [StaticConstructorOnStartup]
    static class StackLimits
    {
        static StackLimits() { Apply(TweaksMod.Settings.stackSize); }

        private static bool Eligible(ThingDef def) => def.category == ThingCategory.Item && def.stackLimit > 1;

        internal static void Apply(int size)
        {
            bool changed = false;
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!Eligible(def) || def.stackLimit == size) continue;
                def.stackLimit = size;
                changed = true;
            }
            // A formerly full stack can now receive more items. Refresh the hauling index once per edit.
            if (!changed || Current.ProgramState != ProgramState.Playing) return;
            foreach (var map in Find.Maps)
                foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
                    if (Eligible(thing.def)) map.listerMergeables.Notify_ThingStackChanged(thing);
        }
    }

    // Normal placement splits new piles at ThingDef.stackLimit. The low-level spawn fallback
    // truncates an oversized pile, including already-owned items restored or moved after a reduction.
    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    static class PreserveStackCount
    {
        static bool KeepStack(Thing thing) => thing.def.category == ThingCategory.Item && thing.def.stackLimit > 1;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // PipeSystem rewrites the comparison at fixed offsets from the first stackCount read.
            // Leave that sequence intact and guard only the following warning/truncation block.
            var matcher = new CodeMatcher(instructions).MatchStartForward(
                new CodeMatch(i => i.opcode.FlowControl == FlowControl.Cond_Branch && i.operand is Label),
                new CodeMatch(OpCodes.Ldc_I4_7),
                new CodeMatch(OpCodes.Newarr, typeof(string)),
                new CodeMatch(OpCodes.Dup),
                new CodeMatch(OpCodes.Ldc_I4_0),
                new CodeMatch(OpCodes.Ldstr, "Spawned "));
            matcher.ThrowIfInvalid("Grechka Tweaks: missing spawn truncation block");
            var afterTruncation = (Label)matcher.Operand;
            matcher.Advance(1).Insert(
                new CodeInstruction(OpCodes.Ldarg_0),
                CodeInstruction.Call(typeof(PreserveStackCount), nameof(KeepStack)),
                new CodeInstruction(OpCodes.Brtrue, afterTruncation));
            return matcher.InstructionEnumeration();
        }
    }

    [HarmonyPatch(typeof(ThingUtility), nameof(ThingUtility.TryAbsorbStackNumToTake))]
    static class OverfullMerge
    {
        static void Postfix(ref int __result)
        {
            if (__result < 0) __result = 0;
        }
    }
}
