using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine.Experimental.Lifecycle;

public static class EveLifecycleCleanupChecks
{
    public static string Run()
    {
        int checks = 0, seededFrames = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("Cleanup model: " + label); checks++; }
        var key = new PresenceKey(1, 1); double time = 0;
        var policy = new CleanupPolicy(4, 4, 2, 8, 8, 2, 2, 10);
        var cleanup = new CleanupModel(policy, () => time); var order = new List<string>(); int returned = 0;
        Check(cleanup.TryAcquire(key, new[] {
            new CleanupStep("module-unload", () => { order.Add("module"); time += 3; }),
            new CleanupStep("resource-release", () => { order.Add("resource"); time += 1; }) }, () => { order.Add("pool"); returned++; }, out var first), "acquire tracked resident");
        cleanup.Retire(first);
        Check(!first.IsVisible && !first.CanRunGameplay && cleanup.ResidentCount == 1 && cleanup.BacklogCount == 1, "retire hides/invalidate immediately, keeps resident");
        Check(cleanup.AdvanceFrame(0) == 1 && cleanup.LastFrameMs == 3 && returned == 0, "zero remaining budget still runs first step and measures overrun");
        Check(cleanup.AdvanceFrame(2) == 2 && returned == 1 && cleanup.ResidentCount == 0 && order.SequenceEqual(new[] { "module", "resource", "pool" }), "pool return follows successful cleanup only");

        var leases = new List<CleanupLease>();
        for (int i = 0; i < 4; i++) { cleanup.TryAcquire(new PresenceKey(i + 1, 1), new[] { new CleanupStep("unload", () => time++) }, () => returned++, out var lease); leases.Add(lease); }
        Check(cleanup.ResidentCount == 4 && !cleanup.CanBeginPreparation, "resident cap admits no extra object");
        cleanup.Retire(leases[0]); cleanup.Retire(leases[1]);
        Check(!cleanup.CanBeginPreparation && cleanup.BacklogCount == 2, "backlog threshold causes preparation backpressure");
        cleanup.Retire(leases[2]); cleanup.Retire(leases[3]);
        Check(cleanup.BacklogCount == 4 && leases.All(x => !x.IsVisible), "full backlog can retire every existing lease without lost despawn");
        Check(!cleanup.TryAcquire(key, new[] { new CleanupStep("unload", () => {}) }, () => {}, out _), "full backlog cannot allocate another preparation");
        cleanup.BeginStop();
        int limit = 32;
        while (cleanup.ResidentCount > 0 && limit-- > 0) cleanup.AdvanceFrame(0);
        Check(cleanup.StopState == CleanupStopState.Stopped && cleanup.ResidentCount == 0 && returned == 5, "Stopping cleanup progresses despite exhausted gameplay budget");
        Check(!cleanup.TryAcquire(key, new[] { new CleanupStep("unload", () => {}) }, () => {}, out _), "stopped scheduler does not admit new objects");

        time = 0; bool fail = true; int continued = 0, pooled = 0;
        cleanup = new CleanupModel(policy, () => time);
        cleanup.TryAcquire(key, new[] { new CleanupStep("faulty-unload", () => { if (fail) throw new InvalidOperationException("fixture"); }),
            new CleanupStep("other-resource", () => continued++) }, () => pooled++, out var failed);
        cleanup.BeginStop(); cleanup.AdvanceFrame(2);
        Check(continued == 1 && pooled == 0 && failed.State == CleanupLeaseState.RetryWaiting && cleanup.ResidentCount == 1, "continue cleanup after failure and retain object before retry");
        Check(cleanup.AdvanceFrame(0) == 0 && failed.Attempts == 1, "retry interval uses fake monotonic clock");
        time = 10; cleanup.AdvanceFrame(2);
        Check(failed.State == CleanupLeaseState.Quarantined && cleanup.StopState == CleanupStopState.Blocked && cleanup.QuarantineCount == 1 && cleanup.ResidentCount == 1 && pooled == 0, "exhausted failure keeps resident and blocks session completion");
        fail = false; cleanup.RetryCleanup(failed); cleanup.AdvanceFrame(2);
        Check(pooled == 1 && continued == 1 && cleanup.StopState == CleanupStopState.Stopped, "explicit retry recovers only failed steps and returns pool once");

        time = 0; bool failPool = true; int successfulUnloads = 0;
        cleanup = new CleanupModel(new CleanupPolicy(1, 1, 1, 1, 4, 1, 1, 10), () => time);
        cleanup.TryAcquire(key, new[] { new CleanupStep("unload", () => successfulUnloads++) }, () => { if (failPool) throw new Exception("pool fault"); }, out failed);
        cleanup.Retire(failed); cleanup.AdvanceFrame(1); cleanup.AdvanceFrame(1);
        Check(failed.State == CleanupLeaseState.Quarantined && cleanup.ResidentCount == 1, "pool callback failure cannot falsely release resident");
        failPool = false; cleanup.RetryCleanup(failed); cleanup.AdvanceFrame(1);
        Check(successfulUnloads == 1 && cleanup.ResidentCount == 0, "retry pool return does not rerun successful module cleanup");

        bool badPolicy = false; try { new CleanupPolicy(4, 3, 2, 1, 1, 1, 1, 1); } catch (ArgumentException) { badPolicy = true; }
        Check(badPolicy, "one retirement slot per possible resident required before commit/teardown");
        cleanup = new CleanupModel(policy, () => time);
        bool guarded = false;
        cleanup.TryAcquire(key, new[] { new CleanupStep("reentrant", () => { try { cleanup.BeginStop(); } catch (InvalidOperationException) { guarded = true; } }) }, () => {}, out first);
        cleanup.Retire(first); cleanup.AdvanceFrame(2);
        Check(guarded && cleanup.ResidentCount == 0, "reentrant cleanup owner call cannot corrupt queue");

        for (int seed = 0; seed < 16; seed++)
        {
            var random = new Random(seed); time = 0; int created = 0, released = 0;
            cleanup = new CleanupModel(new CleanupPolicy(8, 8, 3, 3, 8, 1, 1, 1), () => time);
            var active = new List<CleanupLease>();
            for (int frame = 0; frame < 160; frame++)
            {
                if (cleanup.CanBeginPreparation && random.Next(2) == 0)
                {
                    var steps = Enumerable.Range(0, random.Next(1, 5)).Select(i => new CleanupStep("step-" + i, () => time += .6)).ToArray();
                    if (cleanup.TryAcquire(new PresenceKey(++created, 1), steps, () => released++, out var lease)) active.Add(lease);
                }
                if (active.Count > 0 && random.Next(3) != 0) { var selected = random.Next(active.Count); cleanup.Retire(active[selected]); active.RemoveAt(selected); }
                var before = cleanup.BacklogCount; var executed = cleanup.AdvanceFrame(random.Next(2) == 0 ? 0 : 1);
                Check(executed <= 3 && (before == 0 || executed >= 1), "seed cleanup progress/step cap");
                Check(cleanup.ResidentCount == created - released && cleanup.ResidentCount <= 8 && cleanup.BacklogCount <= 8 && active.All(x => x.IsVisible), "seed accounting/visibility invariant");
                time++; seededFrames++;
            }
            cleanup.BeginStop(); limit = 1000;
            while (cleanup.ResidentCount > 0 && limit-- > 0) cleanup.AdvanceFrame(0);
            Check(created == released && cleanup.ResidentCount == 0 && cleanup.StopState == CleanupStopState.Stopped && active.All(x => !x.IsVisible && !x.CanRunGameplay), "seed stop hides all and drains to zero");
        }
        return "PASS " + checks + " cleanup model checks; " + seededFrames + " fake-clock seeded frames (16 seeds); Unity hiding/pool integration deferred";
    }
}
