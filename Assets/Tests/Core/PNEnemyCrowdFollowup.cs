#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Unity.Profiling;
using UnityEngine;

public sealed partial class PNEnemyMoveSkillPlayReview
{
    private static EveContentDocument[] CrowdDocuments(EveContentDocument[] documents)
        => documents.Select(document => {
            var json = Newtonsoft.Json.Linq.JObject.Parse(document.Json);
            var characters = json["characters"] as Newtonsoft.Json.Linq.JArray;
            if (characters == null) return document;
            foreach (var character in characters)
                if ((string)character["id"] == "project_n:sentinel")
                    foreach (var stat in character["stats"])
                        if ((string)stat["statId"] == "base:health") { stat["base"] = 10000000; stat["initialCurrent"] = 10000000; }
            return new EveContentDocument(document.Name, json.ToString(), document.ContentNamespace, document.IsMetadata, document.Dependencies);
        }).ToArray();
    private sealed class CrowdActor : IDisposable
    {
        internal PNEnemySkill Skill;
        internal PNEnemyMove Move;
        internal PNEnemyBehaviorContext Context;
        internal PNEnemyAgentContext Agent;
        internal PNAgentPerception Sensor;
        internal PNMovementTask Task;
        internal PNAgentGroupMember Member;
        internal PNEnemyNavigationRequest Observed;
        internal Vector3 Goal;
        internal float IssuedAt, StartedAt = -1;
        internal int Attempts, Arrivals, Phases, Hits, Failures;
        internal int ObservedHits;
        internal bool ReadyRecorded;
        internal long TerminalRequest = -1;
        internal bool FirstArrived;
        public void Dispose() { Task?.Dispose(); Sensor?.Dispose(); Member?.Dispose(); }
    }
    private struct CrowdFrame
    {
        internal double T, Milliseconds;
        internal long MainNs, GcBytes;
        internal int Pending, Starts, Sight, Evictions, Drops;
        public override string ToString() => FormattableString.Invariant($"{T:F5},{Milliseconds:F5},{MainNs},{GcBytes},{Pending},{Starts},{Sight},{Evictions},{Drops}");
    }
    private IEnumerator FollowupCrowd()
    {
        string[] parts = stage.Split('-'); int count = int.Parse(parts[1]), workload = int.Parse(parts[2]);
        Check((count == 32 || count == 64) && workload >= 0 && workload <= 2, "F2 explicit population/workload");
        Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
        trace.Dispose(); trace = null; // No per-frame file IO during measured windows.
        foreach (var actor in actors.ToArray())
        { actor.GetComponent<EveEntityLoader>().Unload(); actor.GetComponent<EveEntityIdentity>().ResetIdentity(); Destroy(actor); }
        actors.Clear(); enemy = null; move = null; target = null;
        yield return null;
        var nav = PNWorldNavigation.ForSession(session);
        Check(nav.PathStartsPerFrame == 4 && Physics.GetIgnoreLayerCollision(7, 7), "F2 production path budget4 and Enemy pair ignored");
        var frames = new List<CrowdFrame>(24000); var waits = new List<float>(); var latencies = new List<float>();
        var readyTimes = new List<float>();
        var losDiagnostics = new List<string>();
        var occlusionHolds = new List<IDisposable>();
        var reacquired = new HashSet<CrowdActor>();
        var crowd = new List<CrowdActor>(count); var groupService = workload == 2 ? new PNAgentGroupService(session) : null;
        var byId = new Dictionary<EveEntityId, CrowdActor>();
        var targets = new[] { Target(a, new Vector3(4, 0, -1.3f)), Target(a, new Vector3(4, 0, 1.3f)) };
        yield return Until(() => targets.All(t => t.IsReady), 3, "F2 targets published");
        double initialHp = targets.Sum(t => t.GetCurrent(PNContentIds.Health));
        Action<PNDamageResult> observedHit = result => { if (result.AppliedDamage > 0 && byId.TryGetValue(result.Damage.SourceId, out var source)) source.ObservedHits++; };
        foreach (var victim in targets) victim.DamageReceived += observedHit;
        try
        {
            using (var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1))
            using (var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1))
            {
                float idleUntil = Time.realtimeSinceStartup + 3;
                var idle = new List<float>(2000);
                while (Time.realtimeSinceStartup < idleUntil) { idle.Add(Time.unscaledDeltaTime * 1000); yield return null; }
                File.WriteAllLines(Path.Combine(output, "idle-ms.csv"), idle.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                for (int i = 0; i < count; i++)
                {
                    var skill = Enemy(a, new Vector3(-4.6f + (i % 8) * .18f, .02f, -2.3f + (i / 8) * .6f));
                    crowd.Add(new CrowdActor { Skill = skill, Move = skill.GetComponent<PNEnemyMove>(), Goal = new Vector3(2.8f, 0, (i / 32 == 0 ? -1.3f : 1.3f) + (i % 8 - 3.5f) * .08f) });
                }
                yield return Until(() => crowd.All(c => c.Skill.ActiveBehaviorId == "probe"), 5, "F2 all actors admitted");
                for (int i = 0; i < count; i++)
                {
                    var c = crowd[i]; c.Context = c.Skill.GetComponent<PNEnemyReviewProbeBehavior>().Context;
                    byId.Add(c.Context.Stat.EntityId, c);
                    if (workload > 0)
                    {
                        int groupIndex = i / 32;
                        c.Agent = new PNEnemyAgentContext(c.Context, value => value == targets[groupIndex]);
                        c.Sensor = new PNAgentPerception(c.Agent, new PNPerceptionSettings(interval: .1f, reactionDelay: .1f));
                        c.Task = new PNMovementTask(c.Agent, new PNMovementTaskSettings(PNMovementTaskKind.Patrol, new PNLocomotionOptions(.15f), timeout: 20,
                            loop: false, patrolPoints: new[] { c.Goal }), c.Goal);
                        if (groupService != null) c.Member = groupService.Join(c.Agent, EveId.Parse("review:crowd"), "encounter-" + groupIndex, (_, __) => true);
                    }
                    else c.Context.GoTo(c.Goal, .15f);
                    c.IssuedAt = Time.realtimeSinceStartup;
                }
                if (groupService != null)
                {
                    Check(groupService.GroupCount == count / 32, "F2 explicit groups of32");
                    Reject(() => groupService.Join(crowd[count == 64 ? 32 : 0].Agent, EveId.Parse("review:crowd"), "encounter-0", (_, __) => true), "F2 full/duplicate enrollment rejected");
                }
                var gcCollections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
                double start = Time.realtimeSinceStartupAsDouble, previous = start, nextSignal = 0; bool captured = false, paused = false, resumed = false;
                int phase = 0, peakPending = 0;
                bool dynamic = Environment.GetCommandLineArgs().Contains("--pn-crowd-dynamic"), shifted = false, hidden = false, restored = false, unseen = true;
                while (Time.realtimeSinceStartupAsDouble - start < 40)
                {
                    double now = Time.realtimeSinceStartupAsDouble, elapsed = now - start; int starts = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var c = crowd[i]; c.Sensor?.Tick(Time.deltaTime);
                        c.Task?.Tick(Time.deltaTime);
                        var request = c.Move.NavigationRequest;
                        if (request != null && !ReferenceEquals(request, c.Observed)) { c.Observed = request; c.Attempts = 0; c.StartedAt = -1; c.IssuedAt = (float)now; c.ReadyRecorded = false; }
                        if (request != null)
                        {
                            starts += Math.Max(0, request.PathAttempts - c.Attempts); c.Attempts = request.PathAttempts;
                            if (request.PathAttempts > 0 && c.StartedAt < 0) { c.StartedAt = (float)now; waits.Add((float)now - c.IssuedAt); }
                            if (!c.ReadyRecorded && request.Status == PNEnemyNavigationStatus.Following) { readyTimes.Add((float)now - c.IssuedAt); c.ReadyRecorded = true; }
                            if (request.Status == PNEnemyNavigationStatus.Arrived && c.TerminalRequest != request.Id)
                            {
                                c.TerminalRequest = request.Id; c.Arrivals++;
                                if (!c.FirstArrived) { Check(PNAgentGeometry.CenterDistance(c.Move.Body.position, c.Goal) <= .2f, "F2 actual arrival actor" + i); c.FirstArrived = true; latencies.Add((float)elapsed); }
                            }
                            if ((request.Status == PNEnemyNavigationStatus.NoPath || request.Status == PNEnemyNavigationStatus.Stuck) && c.Failures == 0)
                            {
                                c.Failures++;
                                var bounds = c.Move.GetComponent<CapsuleCollider>().bounds; float r = Mathf.Min(bounds.extents.x, bounds.extents.z) * .95f;
                                float half = Mathf.Max(0, bounds.extents.y - r - .03f); var direction = c.Goal - c.Move.Body.position; direction.y = 0; direction.Normalize();
                                var small = new RaycastHit[32]; var large = new RaycastHit[256]; var physics = c.Move.gameObject.scene.GetPhysicsScene();
                                int n = physics.CapsuleCast(bounds.center + Vector3.up * half, bounds.center - Vector3.up * half, r, direction, small, .1f, ~(1 << 13), QueryTriggerInteraction.Ignore);
                                int m = physics.CapsuleCast(bounds.center + Vector3.up * half, bounds.center - Vector3.up * half, r, direction, large, .1f, ~(1 << 13), QueryTriggerInteraction.Ignore);
                                File.AppendAllText(Path.Combine(output, "stuck-query.txt"), "actor=" + i + " status=" + request.Status + " diagnostic=" + request.Diagnostic + " count32=" + n + " count256=" + m + " actorHits=" + large.Take(m).Count(h => h.collider.GetComponentInParent<PNStat>() != null) + "\n");
                            }
                        }
                        if (workload == 2 && c.FirstArrived && !c.Skill.IsExecutingEnemyAction && c.Sensor.NearestVisible() != null)
                        {
                            var seen = c.Sensor.NearestVisible();
                            if (seen.Actor.TryResolve(out var victim) && PNAgentGeometry.CenterDistance(c.Move.Body.position, seen.LastPosition) <= 1.65f)
                            {
                                var cooldown = c.Context.Abilities.GetCooldown(PNEnemyGameplayContent.Recovery);
                                if (cooldown.CanConsume())
                                using (var reservation = c.Member.Reserve("attack/" + i, seen.Actor, .3f))
                                {
                                    var direction = seen.LastPosition - c.Move.Body.position;
                                    if (reservation != null && c.Context.TryCast(PNEnemyGameplayContent.Strike, direction, PNEnemyActionExecution.Instant, new[] { victim }, () => cooldown.TryConsume())) c.Hits++;
                                }
                            }
                        }
                    }
                    groupService?.Tick();
                    if (!paused && elapsed >= 20) { a.SetPaused(true); paused = true; }
                    if (paused && !resumed && elapsed >= 20.4) { a.SetPaused(false); resumed = true; }
                    if (workload == 1 && crowd.All(c => c.FirstArrived) && phase < 2 && elapsed >= (phase == 0 ? 14 : 27))
                    {
                        phase++;
                        for (int i = 0; i < count; i++)
                        {
                            var c = crowd[i]; c.Task.Dispose();
                            c.Task = new PNMovementTask(c.Agent, new PNMovementTaskSettings(phase == 1 ? PNMovementTaskKind.MaintainRange : PNMovementTaskKind.Orbit,
                                new PNLocomotionOptions(.15f), minimumRange: 1, maximumRange: 1.6f, radius: 1.3f, timeout: 30), c.Goal, PNActorHandle.Capture(targets[i / 32])); c.Phases++;
                        }
                    }
                    if (workload == 2 && a.CanExecute && elapsed >= nextSignal)
                    { nextSignal = elapsed + .5; foreach (var c in crowd.Where(c => c.Member.Leader.SameActor(c.Agent.Self))) c.Member.Send(PNAgentSignalKind.Regroup, c.Agent.Self.Position); }
                    foreach (var c in crowd) c.Member?.Drain(_ => { });
                    if (dynamic)
                    {
                        if (!shifted && elapsed >= 12) { foreach (var victim in targets) victim.transform.position += Vector3.forward * .25f; Physics.SyncTransforms(); shifted = true; }
                        if (!hidden && elapsed >= 24) { foreach (var c in crowd) occlusionHolds.Add(c.Skill.Controls.Acquire("followup-los-hold", new[] { PNControlIds.Move })); foreach (var victim in targets) victim.transform.position += Vector3.left * 8; Physics.SyncTransforms(); hidden = true; }
                        if (!restored && elapsed >= 26) { foreach (var victim in targets) victim.transform.position += Vector3.right * 8; Physics.SyncTransforms(); foreach (var hold in occlusionHolds) hold.Dispose(); occlusionHolds.Clear(); restored = true; }
                        if (restored && elapsed >= 28) foreach (var c in crowd) if (c.Sensor?.NearestVisible() != null) reacquired.Add(c);
                        if (elapsed >= 24.5 && elapsed < 25.5)
                        {
                            foreach (var c in crowd)
                            {
                                var visible = c.Sensor?.NearestVisible();
                                if (visible != null && losDiagnostics.Count < 32 && visible.Actor.TryResolve(out var observed))
                                {
                                    var buffer = new RaycastHit[256];
                                    bool actual = PNAgentSpatial.HasSight(c.Context.Stat, c.Agent.Self.Position + Vector3.up * .6f, observed, PNActorHandle.Pose(observed) + Vector3.up * .6f, ref buffer);
                                    losDiagnostics.Add($"time={elapsed:F3} actor={crowd.IndexOf(c)} body={c.Move.Body.position} target={PNActorHandle.Pose(observed)} actualLOS={actual} seen={visible.LastPosition} lastTest={visible.LastTested}");
                                }
                            }
                            unseen &= crowd.All(c => c.Sensor == null || c.Sensor.NearestVisible() == null);
                        }
                    }
                    int pending = nav.PendingRequests; peakPending = Math.Max(peakPending, pending);
                    // Counters are sampled once/frame. Queue-start times have one-frame quantization.
                    if (elapsed >= 10) frames.Add(new CrowdFrame { T = elapsed, Milliseconds = (now - previous) * 1000, MainNs = main.Valid ? main.LastValue : -1,
                        GcBytes = gc.Valid ? gc.LastValue : -1, Pending = pending, Starts = starts, Sight = crowd.Sum(c => c.Sensor?.SightQueries ?? 0),
                        Evictions = crowd.Sum(c => c.Sensor?.Evictions ?? 0), Drops = crowd.Sum(c => c.Member?.DroppedSignals ?? 0) });
                    if (!captured && elapsed > 9) { Capture("crowd-" + count + "-c" + workload); captured = true; }
                    if (elapsed > 20 && crowd.Any(c => !c.FirstArrived)) Check(false, "F2 all reachable routes must arrive by20s: " + string.Join(",", crowd.Select((c, i) => i + ":" + c.Move.NavigationRequest?.Status)));
                    previous = now; yield return null;
                }
                Check(crowd.All(c => c.FirstArrived && c.Failures == 0 && c.Skill.Failure == null), "F2 every actor arrived without path/owner faults");
                if (workload == 2) Check(crowd.All(c => c.Hits > 0 && c.ObservedHits > 0) && targets.All(t => !t.IsDead) && targets.Sum(t => t.GetCurrent(PNContentIds.Health)) < initialHp,
                    "F2 every group actor delivers actual registered damage; target stays alive through sampling");
                if (workload == 1) Check(crowd.All(c => c.Phases == 2 && c.Arrivals >= 2 && c.Task.Status != PNAgentTaskStatus.Failed), "F2 every task actor progresses through range/orbit phases");
                if (dynamic) Check(shifted && hidden && restored && unseen && reacquired.Count == count, "F5 moving target occluded during Move hold, then reacquired by every sensor");
                Check(frames.All(f => f.Starts <= 4), "F2 movement starts never exceed shared budget4");
                File.WriteAllLines(Path.Combine(output, "frames.csv"), new[] { "t,frameMs,mainNs,gcBytes,pending,movementStarts,sight,evictions,drops" }.Concat(frames.Select(f => f.ToString())));
                File.WriteAllLines(Path.Combine(output, "actors.csv"), new[] { "index,arrivals,phases,hits,failures,casts" }.Concat(crowd.Select((c, i) => $"{i},{c.Arrivals},{c.Phases},{c.Hits},{c.Failures},{c.Skill.CastCount}")));
                File.WriteAllLines(Path.Combine(output, "wait-seconds.csv"), waits.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                File.WriteAllLines(Path.Combine(output, "arrival-seconds.csv"), latencies.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                File.WriteAllText(Path.Combine(output, "measurement.txt"), $"population={count} workload={workload} device={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType} resolution={Screen.width}x{Screen.height} quality={QualitySettings.GetQualityLevel()} mainValid={main.Valid} gcValid={gc.Valid} pendingPeak={peakPending} gcCollections={GC.CollectionCount(0)-gcCollections[0]}/{GC.CollectionCount(1)-gcCollections[1]}/{GC.CollectionCount(2)-gcCollections[2]}\nQueue timing is frame-quantized; Main Thread overlaps frame time, not additive. Movement-start samples exclude point probes; no solve-CPU claim.");
            }
        }
        finally
        {
            foreach (var hold in occlusionHolds) hold.Dispose();
            File.WriteAllLines(Path.Combine(output, "los-diagnostics.txt"), losDiagnostics);
            foreach (var victim in targets) if (victim != null) victim.DamageReceived -= observedHit;
            File.WriteAllLines(Path.Combine(output, "frames.csv"), new[] { "t,frameMs,mainNs,gcBytes,pending,movementStarts,sight,evictions,drops" }.Concat(frames.Select(f => f.ToString())));
            File.WriteAllLines(Path.Combine(output, "actors.csv"), new[] { "index,arrivals,phases,hits,failures,casts,task,request,bodyX,bodyZ" }.Concat(crowd.Select((c, i) => FormattableString.Invariant($"{i},{c.Arrivals},{c.Phases},{c.ObservedHits},{c.Failures},{c.Skill.CastCount},{c.Task?.Status},{c.Move.NavigationRequest?.Status},{c.Move.Body.position.x},{c.Move.Body.position.z}"))));
            File.WriteAllLines(Path.Combine(output, "first-follow-ready-seconds.csv"), readyTimes.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            a.SetPaused(false);
            foreach (var c in crowd) c.Dispose(); groupService?.Dispose();
        }
        yield return Until(() => nav.PendingRequests == 0, 10, "F2 cancelled callbacks drain before teardown");
        Check(PNAgentPerception.ActiveInstances == 0 && PNAgentGroupService.ActiveInstances == 0, "F2 optional runtimes released");
    }
}
#endif


