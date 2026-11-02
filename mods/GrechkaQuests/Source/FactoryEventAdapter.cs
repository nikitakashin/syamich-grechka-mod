using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Grechka.Quests
{
    // Optional VFE Factory 1.6 assembly, loaded by its 1.7 loadFolders entry.
    // Reflecting PipeSystem keeps the quest book usable without Factory or VEF.
    internal static class FactoryEventAdapter
    {
        sealed class Attempt
        {
            internal int count;
        }

        [ThreadStatic] static Attempt currentAttempt;
        static FieldInfo parentField;
        static bool installed;
        static MethodInfo originalDefName;
        internal static bool Installed { get { return installed; } }

        internal static void TryInstall()
        {
            if (installed || !ModsConfig.IsActive("VanillaExpanded.VFEFactory")) return;
            Type process = AccessTools.TypeByName("PipeSystem.Process");
            Type result = AccessTools.TypeByName("PipeSystem.ProcessDef+Result");
            MethodInfo spawn = process == null || result == null ? null : AccessTools.Method(process, "TrySpawnAtCell",
                new[] { result, typeof(IntVec3), typeof(Map), typeof(Thing).MakeByRefType() });
            MethodInfo getCount = process == null || result == null ? null : AccessTools.Method(result, "GetCount", new[] { process });
            FieldInfo parent = process == null ? null : AccessTools.Field(process, "parent");
            if (spawn == null || spawn.ReturnType != typeof(bool) || getCount == null || getCount.ReturnType != typeof(int)
                || parent == null || !typeof(Thing).IsAssignableFrom(parent.FieldType))
            {
                Log.Warning("[Grechka Quests] VFE Factory: сигнатура PipeSystem изменилась; учёт фабричного выпуска отключён.");
                return;
            }

            try
            {
                var harmony = new Harmony("grechka.quests.factory");
                harmony.Patch(getCount, postfix: new HarmonyMethod(typeof(FactoryEventAdapter), nameof(CaptureCount)) { priority = Priority.Last });
                harmony.Patch(spawn,
                    prefix: new HarmonyMethod(typeof(FactoryEventAdapter), nameof(BeginAttempt)),
                    postfix: new HarmonyMethod(typeof(FactoryEventAdapter), nameof(EndAttempt)),
                    finalizer: new HarmonyMethod(typeof(FactoryEventAdapter), nameof(RestoreAttempt)));
                parentField = parent;
                originalDefName = AccessTools.Method(AccessTools.TypeByName("Grechka.WorldIntegration.WorldProfile"), "OriginalDefName", new[] { typeof(ThingDef) });
                installed = true;
            }
            catch (Exception e)
            {
                Log.Error("[Grechka Quests] Не удалось подключить VFE Factory: " + e);
            }
        }

        static void BeginAttempt(out Attempt __state)
        {
            __state = currentAttempt;
            currentAttempt = new Attempt();
        }

        static void CaptureCount(int __result)
        {
            if (currentAttempt != null) currentAttempt.count = __result;
        }

        static void EndAttempt(object __instance, bool __result, ref Thing __3, Attempt __state)
        {
            Attempt finished = currentAttempt;
            currentAttempt = __state;
            if (!__result || finished == null || finished.count <= 0 || __3 == null) return;
            Thing machine = parentField?.GetValue(__instance) as Thing;
            if (machine?.def == null) return;
            string sourceName = originalDefName?.Invoke(null, new object[] { machine.def }) as string ?? machine.def.defName;
            if (!sourceName.StartsWith("VFEFactory_", StringComparison.Ordinal)) return;
            string kind = sourceName == "VFEFactory_AutomatedDrillPlatform" ? "mine" : "produce";
            QuestGame.Current?.RecordVerifiedMachineOutput(kind, machine, __3, finished.count);
        }

        static Exception RestoreAttempt(Exception __exception, Attempt __state)
        {
            currentAttempt = __state;
            return __exception;
        }
    }
}
