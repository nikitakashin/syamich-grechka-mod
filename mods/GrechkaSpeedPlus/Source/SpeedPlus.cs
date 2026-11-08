using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Grechka.SpeedPlus
{
    public sealed class GrechkaSpeedPlusMod : Mod
    {
        public GrechkaSpeedPlusMod(ModContentPack content) : base(content)
        {
            new Harmony("grechka.speedplus").PatchAll(typeof(GrechkaSpeedPlusMod).Assembly);
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
    public static class FourthSpeedRate
    {
        // Fourth is an ordinal button, not a multiplier: native Superfast is already
        // x6/x12 on maps and x18 without maps. Retain native Ultrafast rates, excluding
        // the developer-only x20000 boost, so this button is always the faster mode.
        public static bool Prefix(TickManager __instance, ref float __result)
        {
            if (__instance.CurTimeSpeed != TimeSpeed.Ultrafast) return true;
            __result = __instance.slower.ForcedNormalSpeed ? 1f : Find.Maps.Count == 0 ? 150f : 15f;
            return false;
        }
    }

    [HarmonyPatch(typeof(TimeControls), nameof(TimeControls.DoTimeControlsGUI))]
    [StaticConstructorOnStartup]
    public static class FourthSpeedControls
    {
        private static Texture2D icon;
        private static readonly Action<TimeSpeed> PlaySpeedSound = AccessTools.MethodDelegate<Action<TimeSpeed>>(
            AccessTools.Method(typeof(TimeControls), "PlaySoundOf"));

        public static Rect ExpandedRect(Rect rect)
        {
            // Grow to the left: the rightmost button stays inside the existing screen edge.
            float width = TimeControls.TimeButSize.x;
            rect.x -= width;
            rect.width += width;
            return rect;
        }

        public static Rect ButtonRect(Rect expandedRect)
        {
            return new Rect(expandedRect.x + 4f * TimeControls.TimeButSize.x,
                expandedRect.y, TimeControls.TimeButSize.x, TimeControls.TimeButSize.y);
        }

        public static bool TrySelect(TickManager manager, bool requested)
        {
            if (!requested || manager.ForcePaused) return false;
            manager.CurTimeSpeed = TimeSpeed.Ultrafast;
            return manager.CurTimeSpeed == TimeSpeed.Ultrafast;
        }

        public static void Prefix(ref Rect timerRect)
        {
            timerRect = ExpandedRect(timerRect);
            TickManager manager = Find.TickManager;
            KeyBindingDef binding = KeyBindingDefOf.TimeSpeed_Ultrafast;
            Event current = Event.current;
            // Consume before the native debug handler; the same binding remains remappable.
            if (current.type == EventType.KeyDown &&
                (binding.KeyDownEvent || manager.CurTimeSpeed == TimeSpeed.Superfast &&
                    KeyBindingDefOf.TimeSpeed_Faster.KeyDownEvent))
            {
                Select(manager);
                current.Use();
            }

            Rect button = ButtonRect(timerRect);
            if (icon == null) icon = ContentFinder<Texture2D>.Get("UI/TimeControls/GrechkaSpeedPlus_Four");
            string key = KeyPrefs.KeyPrefsData.GetBoundKeyCode(binding, KeyPrefs.BindingSlot.A).ToStringReadable();
            string tip = "GrechkaSpeedPlus_FourTip".Translate() + "\n" + "HotKeyTip".Translate() + ": " + key;
            if (Widgets.ButtonImage(button, icon, true, tip)) Select(manager);
            if (!manager.ForcePaused && manager.CurTimeSpeed == TimeSpeed.Ultrafast)
                GUI.DrawTexture(button, TexUI.HighlightTex);
            if (manager.ForcePaused || manager.slower.ForcedNormalSpeed)
                Widgets.DrawLineHorizontal(button.x, button.y + button.height / 2f, button.width);
        }

        private static void Select(TickManager manager)
        {
            if (TrySelect(manager, true)) PlaySpeedSound(manager.CurTimeSpeed);
        }
    }
}
