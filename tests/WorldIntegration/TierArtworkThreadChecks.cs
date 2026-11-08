using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grechka.WorldIntegration;
using HarmonyLib;
using Verse;

internal static class TierArtworkThreadChecks
{
    private static readonly ConcurrentQueue<(string action, int thread)> observed = new();
    private static void Observe(MethodBase __originalMethod) => observed.Enqueue((__originalMethod.Name, Environment.CurrentManagedThreadId));
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }

    internal static void Run()
    {
        var harmony = new Harmony("grechka.tests.main-thread-artwork");
        var module = typeof(TierArtwork).Assembly;
        var owner = Current.Game;
        var enabled = new WorldProfileState(owner) { applied = true };
        var disabled = new WorldProfileState(owner);
        int main = Environment.CurrentManagedThreadId;
        void Expect(params string[] actions)
        {
            var calls = observed.ToArray();
            Check(calls.Select(c => c.action).SequenceEqual(actions) && calls.All(c => c.thread == main),
                "Latest load request executes on main thread in order: " + string.Join(", ", actions));
        }
        try
        {
            harmony.CreateClassProcessor(module.GetType("Grechka.WorldIntegration.TierArtworkLoadFinishedHook", true)).Patch();
            foreach (string name in new[] { "ApplyNow", "RestoreNow" })
                harmony.Patch(AccessTools.Method(typeof(TierArtwork), name), prefix: new HarmonyMethod(typeof(TierArtworkThreadChecks), nameof(Observe)));
            RunLongEvent(enabled.LoadedGame);
            Expect("RestoreNow", "ApplyNow");
            RunLongEvent(() => { enabled.LoadedGame(); disabled.LoadedGame(); });
            Expect("RestoreNow");
            RunLongEvent(() => { disabled.LoadedGame(); enabled.LoadedGame(); });
            Expect("RestoreNow", "ApplyNow");
            RunLongEvent(enabled.LoadedGame, () => Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game)));
            Expect("RestoreNow");
            RunLongEvent(enabled.LoadedGame);
            Expect(); // A stale component cannot submit its old settings under the new game's identity.
        }
        finally
        {
            Current.Game = owner;
            harmony.UnpatchAll(harmony.Id);
        }
    }

    // Use the shipped game's real async loader thread and real Root.Update pump. Hold the worker
    // across one display frame so a timing accident cannot disguise an early restore or an inline apply.
    private static void RunLongEvent(Action loaded, Action beforeFinish = null)
    {
        observed.Clear();
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Exception error = null;
        LongEventHandler.QueueLongEvent(() =>
        {
            try
            {
                if (UnityData.IsInMainThread) throw new Exception("Fixture did not enter the native async loader");
                loaded();
            }
            catch (Exception e) { error = e; }
            finally { reached.Set(); release.Wait(TimeSpan.FromSeconds(10)); }
        }, null, true, e => error = e, false, true);
        var clock = Stopwatch.StartNew();
        try
        {
            while (!reached.IsSet && clock.Elapsed < TimeSpan.FromSeconds(10))
            { LongEventHandler.LongEventsUpdate(out _); Thread.Yield(); }
            if (!reached.IsSet) throw new Exception("Native loader failed to start");
            beforeFinish?.Invoke();
            LongEventHandler.LongEventsUpdate(out _);
            if (!observed.IsEmpty) throw new Exception("Graphics or restoration ran before the load finished");
        }
        finally { release.Set(); }
        while (LongEventHandler.AnyEventNowOrWaiting && clock.Elapsed < TimeSpan.FromSeconds(15))
        { LongEventHandler.LongEventsUpdate(out _); Thread.Yield(); }
        if (LongEventHandler.AnyEventNowOrWaiting) throw new Exception("Native load timed out");
        if (error != null) throw new Exception("Native worker failed", error);
    }
}
