using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace Grechka.WorldIntegration
{
    // Optional PipeSystem adapter: no compile-time dependency or replacement of native processes.
    public static class FactoryTierAdapter
    {
        internal static readonly Type ProcessType = AccessTools.TypeByName("PipeSystem.Process");
        internal static readonly Type ResultType = AccessTools.TypeByName("PipeSystem.ProcessDef+Result");
        internal static readonly Type OwnerType = AccessTools.TypeByName("PipeSystem.ThingAndResourceOwner");
        private static readonly FieldInfo parent = Field(ProcessType, "parent"), owners = Field(ProcessType, "ingredientsOwners"),
            processDef = Field(ProcessType, "def"), wanted = Field(OwnerType, "wantedCount");
        internal static readonly FieldInfo TickLeft = Field(ProcessType, "tickLeft"), CurrentResult = Field(ProcessType, "currentResult");
        private static readonly ConditionalWeakTable<object, FactoryBatchState> states = new ConditionalWeakTable<object, FactoryBatchState>();
        private static FieldInfo Field(Type type, string name) => type == null ? null : AccessTools.Field(type, name);
        internal static bool Available => ProcessType != null && ResultType != null && parent != null && owners != null && wanted != null && TickLeft != null && CurrentResult != null;
        public static bool Supports(ThingDef def) => ProfileVariants.Entry(def) != null &&
            def.comps?.Any(c => c.compClass?.FullName == "PipeSystem.CompAdvancedResourceProcessor") == true;
        private static Thing Machine(object process) => parent?.GetValue(process) as Thing;
        private static int Tier(object process)
        {
            Thing machine = Machine(process);
            return machine != null && Supports(machine.def) ? WorldProfile.GetTier(machine) : 0;
        }
        internal static FactoryBatchState State(object process) => states.GetOrCreateValue(process);
        public static int PreviewCount(Thing machine, int baseCount)
        {
            int tier = machine != null && Supports(machine.def) ? WorldProfile.GetTier(machine) : 0;
            return baseCount <= 0 || tier <= 1 ? baseCount : Saturate(baseCount + Math.Floor(baseCount * (double)ProfileVariants.Output[tier] + .000001));
        }
        private static int Saturate(double value) => (int)Math.Min(int.MaxValue, Math.Max(0, value));
        internal static int ScaledTicks(object process, int ticks)
        {
            int tier = Tier(process);
            return tier <= 1 ? ticks : Saturate(ticks * (double)ProfileVariants.Speed[tier]);
        }
        internal static void SetInputs(object process, bool setup)
        {
            if (!Available || owners.GetValue(process) is not IList inputs) return;
            FactoryBatchState state = State(process);
            if (setup || state.baseline.Count != inputs.Count)
            {
                state.baseline.Clear();
                foreach (object input in inputs) state.baseline.Add((int)wanted.GetValue(input));
            }
            int tier = Tier(process);
            float discount = tier <= 1 ? 0 : ProfileVariants.Discount[tier];
            for (int i = 0; i < inputs.Count; i++)
            {
                int original = state.baseline[i];
                wanted.SetValue(inputs[i], original <= 0 ? original : Math.Max(1, (int)Math.Ceiling(original * (1d - discount) - .000001)));
            }
        }
        internal static int ResultIndex(object process, object result)
        {
            object def = processDef.GetValue(process);
            return (AccessTools.Field(def.GetType(), "results").GetValue(def) as IList)?.IndexOf(result) ?? -1;
        }
        internal static void Count(object process, object result, ref int amount)
        {
            int tier = Tier(process);
            if (tier <= 1 || amount <= 0) return;
            int index = ResultIndex(process, result);
            if (index < 0) return;
            FactoryBatchState state = State(process);
            float carry = state.carry.TryGetValue(index, out float saved) ? saved : 0;
            double bonus = carry + amount * (double)ProfileVariants.Output[tier];
            int extra = Saturate(Math.Floor(bonus + .000001));
            state.pending[index] = (float)Math.Max(0, bonus - extra);
            amount = Saturate((double)amount + extra);
        }
        // Called at native currentResult++ only: previews and failed/blocked deliveries do not consume carry.
        internal static void CommitOutput(object process)
        {
            FactoryBatchState state = State(process);
            int index = (int)CurrentResult.GetValue(process);
            if (!state.pending.TryGetValue(index, out float next)) return;
            state.carry[index] = next;
            state.pending.Remove(index);
        }
        internal static void Expose(object process)
        {
            FactoryBatchState state = State(process);
            Scribe_Deep.Look(ref state, "grechkaFactoryTiers");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                states.Remove(process);
                states.Add(process, state ?? new FactoryBatchState());
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit) SetInputs(process, false);
        }
        internal static IEnumerable<CodeInstruction> WorkInstructions(IEnumerable<CodeInstruction> source)
        {
            var code = source.ToList();
            int matches = 0;
            for (int i = 0; i + 3 < code.Count; i++)
                if (code[i].LoadsField(TickLeft) && code[i + 1].opcode == OpCodes.Ldarg_1 && code[i + 2].opcode == OpCodes.Sub && code[i + 3].StoresField(TickLeft))
                {
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                    code.Insert(i + 3, CodeInstruction.Call(typeof(FactoryTierAdapter), nameof(ScaledTicks)));
                    matches++; i += 5;
                }
            if (matches != 1) throw new InvalidOperationException("Unsupported PipeSystem.Process.Tick IL: expected one work-clock subtraction, got " + matches);
            return code;
        }
        internal static IEnumerable<CodeInstruction> DeliveryInstructions(IEnumerable<CodeInstruction> source)
        {
            var code = source.ToList();
            int matches = 0;
            for (int i = 0; i + 5 < code.Count; i++)
                if (code[i].opcode == OpCodes.Ldarg_0 && code[i + 1].opcode == OpCodes.Ldarg_0 && code[i + 2].LoadsField(CurrentResult)
                    && code[i + 3].LoadsConstant(1) && code[i + 4].opcode == OpCodes.Add && code[i + 5].StoresField(CurrentResult))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(code[i].labels); code[i].labels.Clear();
                    load.blocks.AddRange(code[i].blocks); code[i].blocks.Clear();
                    code.Insert(i, load); code.Insert(i + 1, CodeInstruction.Call(typeof(FactoryTierAdapter), nameof(CommitOutput)));
                    matches++; i += 7;
                }
            if (matches != 1) throw new InvalidOperationException("Unsupported PipeSystem output IL: expected one committed result increment, got " + matches);
            return code;
        }
    }
    public sealed class FactoryBatchState : IExposable
    {
        public List<int> baseline = new List<int>();
        public Dictionary<int, float> carry = new Dictionary<int, float>();
        public Dictionary<int, float> pending = new Dictionary<int, float>();
        public void ExposeData()
        {
            Scribe_Collections.Look(ref baseline, "baseline", LookMode.Value);
            Scribe_Collections.Look(ref carry, "carry", LookMode.Value, LookMode.Value);
            baseline ??= new List<int>(); carry ??= new Dictionary<int, float>();
        }
    }
    [HarmonyPatch]
    public static class FactoryTierSetupHook
    {
        public static bool Prepare() => FactoryTierAdapter.Available;
        public static MethodBase TargetMethod() => AccessTools.Method(FactoryTierAdapter.ProcessType, "Setup");
        public static void Postfix(object __instance) => FactoryTierAdapter.SetInputs(__instance, true);
    }
    [HarmonyPatch]
    public static class FactoryTierSaveHook
    {
        public static bool Prepare() => FactoryTierAdapter.Available;
        public static MethodBase TargetMethod() => AccessTools.Method(FactoryTierAdapter.ProcessType, "ExposeData");
        public static void Postfix(object __instance) => FactoryTierAdapter.Expose(__instance);
    }
    [HarmonyPatch]
    public static class FactoryTierResetHook
    {
        public static bool Prepare() => FactoryTierAdapter.Available;
        public static MethodBase TargetMethod() => AccessTools.Method(FactoryTierAdapter.ProcessType, "ResetProcess");
        public static void Postfix(object __instance)
        { FactoryTierAdapter.State(__instance).pending.Clear(); FactoryTierAdapter.SetInputs(__instance, false); }
    }
    [HarmonyPatch]
    public static class FactoryTierCountHook
    {
        public static bool Prepare() => FactoryTierAdapter.Available;
        public static MethodBase TargetMethod() => AccessTools.Method(FactoryTierAdapter.ResultType, "GetCount");
        [HarmonyPriority(Priority.First)]
        public static void Postfix(object __instance, object __0, ref int __result) => FactoryTierAdapter.Count(__0, __instance, ref __result);
    }
    [HarmonyPatch]
    public static class FactoryTierClockHook
    {
        public static bool Prepare() => FactoryTierAdapter.Available;
        public static MethodBase TargetMethod() => AccessTools.Method(FactoryTierAdapter.ProcessType, "Tick");
        public static void Prefix(object __instance) => FactoryTierAdapter.SetInputs(__instance, false);
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => FactoryTierAdapter.WorkInstructions(instructions);
    }
    [HarmonyPatch]
    public static class FactoryTierDeliveryHook
    {
        public static bool Prepare() => FactoryTierAdapter.Available;
        public static MethodBase TargetMethod() => AccessTools.Method(FactoryTierAdapter.ProcessType, "SpawnOrPushToNet");
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => FactoryTierAdapter.DeliveryInstructions(instructions);
    }
}
