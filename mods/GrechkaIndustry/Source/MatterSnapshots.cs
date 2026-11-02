using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grechka.Industry
{
    public sealed class MatterBatchSnapshot : IExposable
    {
        public string recipeName;
        public float factor;
        public float efficiency;
        public float workAmount;
        public List<float> ingredientCounts = new List<float>();
        public void ExposeData()
        {
            Scribe_Values.Look(ref recipeName, "recipe");
            Scribe_Values.Look(ref factor, "factor");
            Scribe_Values.Look(ref efficiency, "efficiency");
            Scribe_Values.Look(ref workAmount, "workAmount");
            Scribe_Collections.Look(ref ingredientCounts, "ingredients", LookMode.Value);
            if (ingredientCounts == null) ingredientCounts = new List<float>();
        }
        public static MatterBatchSnapshot Capture(RecipeDef recipe)
        {
            var p = recipe?.GetModExtension<MatterRecipeProperties>();
            if (p == null) return null;
            var s = new MatterBatchSnapshot { recipeName = recipe.defName, factor = p.factor, efficiency = p.efficiency, workAmount = recipe.workAmount };
            foreach (var ingredient in recipe.ingredients) s.ingredientCounts.Add(ingredient.GetBaseCount());
            return s;
        }
    }
    public static class MatterSnapshots
    {
        private static readonly ConditionalWeakTable<Bill, MatterBatchSnapshot> bills = new ConditionalWeakTable<Bill, MatterBatchSnapshot>();
        private static readonly JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true };
        [ThreadStatic] private static MatterBatchSnapshot active;
        public static string Capture(RecipeDef recipe)
        {
            var snapshot = MatterBatchSnapshot.Capture(recipe);
            return snapshot == null ? null : JsonSerializer.Serialize(snapshot, json);
        }
        public static IDisposable UseSnapshot(string payload)
        {
            MatterBatchSnapshot next = string.IsNullOrEmpty(payload) ? null : JsonSerializer.Deserialize<MatterBatchSnapshot>(payload, json);
            return new Scope(next);
        }
        private sealed class Scope : IDisposable
        {
            private readonly MatterBatchSnapshot previous;
            public Scope(MatterBatchSnapshot next) { previous = active; active = next; }
            public void Dispose() { active = previous; }
        }
        internal static MatterBatchSnapshot For(RecipeDef recipe, Pawn worker)
        {
            if (active?.recipeName == recipe.defName) return active;
            Bill bill = worker?.CurJob?.bill;
            return bill != null && bills.TryGetValue(bill, out var saved) && saved.recipeName == recipe.defName ? saved : null;
        }
        internal static MatterBatchSnapshot For(Bill bill)
            => bill != null && bills.TryGetValue(bill, out var saved) ? saved : null;
        internal static void Begin(Bill bill)
        {
            if (bill?.recipe == null || bills.TryGetValue(bill, out _)) return;
            var snapshot = MatterBatchSnapshot.Capture(bill.recipe);
            if (snapshot != null) bills.Add(bill, snapshot);
        }
        internal static void End(Bill bill) { if (bill != null) bills.Remove(bill); }
        internal static void Expose(Bill bill)
        {
            bills.TryGetValue(bill, out var snapshot);
            Scribe_Deep.Look(ref snapshot, "grechkaMatterBatch");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                bills.Remove(bill);
                if (snapshot != null) bills.Add(bill, snapshot);
            }
        }
    }
    [HarmonyPatch(typeof(Bill), nameof(Bill.Notify_DoBillStarted))]
    internal static class MatterBatchStartHook
    {
        private static void Postfix(Bill __instance) => MatterSnapshots.Begin(__instance);
    }
    [HarmonyPatch(typeof(Bill), nameof(Bill.GetWorkAmount))]
    internal static class MatterBatchWorkHook
    {
        private static void Postfix(Bill __instance, ref float __result)
        {
            var snapshot = MatterSnapshots.For(__instance);
            if (snapshot != null) __result = snapshot.workAmount;
        }
    }
    [HarmonyPatch(typeof(Bill), nameof(Bill.Notify_BillWorkStarted))]
    internal static class MatterBatchWorkStartHook
    {
        private static void Postfix(Bill __instance) => MatterSnapshots.Begin(__instance);
    }
    [HarmonyPatch(typeof(Bill), nameof(Bill.ExposeData))]
    internal static class MatterBatchSaveHook
    {
        private static void Postfix(Bill __instance) => MatterSnapshots.Expose(__instance);
    }
    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.Notify_IterationCompleted))]
    internal static class MatterBatchFinishHook
    {
        private static void Postfix(Bill_Production __instance) => MatterSnapshots.End(__instance);
    }
    [HarmonyPatch(typeof(RecipeWorker), nameof(RecipeWorker.GetIngredientCount))]
    internal static class MatterBatchIngredientsHook
    {
        private static bool Prefix(RecipeWorker __instance, IngredientCount __0, Bill bill, ref float __result)
        {
            var snapshot = MatterSnapshots.For(bill);
            if (snapshot == null || bill.recipe != __instance.recipe) return true;
            int index = bill.recipe.ingredients.IndexOf(__0);
            if (index < 0 || index >= snapshot.ingredientCounts.Count) return true;
            __result = snapshot.ingredientCounts[index];
            return false;
        }
    }
}
