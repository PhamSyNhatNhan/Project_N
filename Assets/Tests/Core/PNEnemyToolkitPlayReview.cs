#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class PNEnemyMoveSkillPlayReview
{
    private IEnumerator ToolkitMotion()
    {
        bool captured = false;
        var context = enemy.GetComponent<PNEnemyReviewProbeBehavior>().Context;
        target.transform.position = new Vector3(100, 0, 0); Physics.SyncTransforms();
        Reposition(move, new Vector3(-4, .02f, -4));
        var start = move.Body.position;
        Check(context.TryCast(PNAgentToolkitContent.Curve, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt) == PNEnemyCastAdmission.Started,
            "EB6 selected curve Action is admitted through motion interface");
        float peak = -4; float deadline = Time.realtimeSinceStartup + 3;
        while (!receipt.IsTerminal && Time.realtimeSinceStartup < deadline) { peak = Mathf.Max(peak, move.Body.position.z); yield return null; }
        Check(receipt.Status == PNEnemyCastStatus.Completed && peak > -3.6f && Mathf.Abs(move.Body.position.x + 2.5f) < .1f && Mathf.Abs(move.Body.position.z + 4) < .1f,
            "EB6 actual body follows curved XZ path and reaches finite endpoint");
        Check(!move.HasActiveMotion && move.CanMove, "EB6 curve releases exclusive motion and movement lock");
        Reposition(move, new Vector3(-4, .02f, -4)); var anchor = new Vector3(-2.5f, .02f, -4);
        context.TryCast(PNAgentToolkitContent.Orbit, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out receipt, position: anchor);
        yield return Until(() => receipt.IsTerminal, 3, "EB6 orbit segment finishes");
        Check(receipt.Status == PNEnemyCastStatus.Completed && Mathf.Abs(PNAgentGeometry.CenterDistance(move.Body.position, anchor) - 1.5f) < .1f && move.Body.position.z < -5.3f,
            "EB6 orbit uses explicit snapshot anchor and preserves radius");
        Reposition(move, new Vector3(-4, .02f, -4));
        context.TryCast(PNAgentToolkitContent.Hop, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out receipt);
        float height = 0; deadline = Time.realtimeSinceStartup + 3;
        while (!receipt.IsTerminal && Time.realtimeSinceStartup < deadline) { height = Mathf.Max(height, move.Body.position.y); if (height > .4f) CaptureOnce(); yield return null; }
        Check(receipt.Status == PNEnemyCastStatus.Completed && height > .5f && move.Body.position.y < .12f && move.Body.useGravity,
            "EB6 short hop raises real body and restores gravity on landing");
        Reposition(move, new Vector3(-4, .02f, -4));
        context.TryCast(PNAgentToolkitContent.Hop, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out receipt);
        yield return new WaitForSeconds(.2f); var clock = enemy.GetComponent<EveTimeScale>();
        long frozen = clock.AddOwnedModifier(EveId.Parse("project_n:review_hop_freeze"), EveTimeModifierKind.Scale, -1, this);
        yield return new WaitForFixedUpdate(); var held = move.Body.position; enemy.TryInterrupt(EveId.Parse("project_n:review_hop_cancel"));
        yield return new WaitForSeconds(.12f);
        Check(receipt.Status == PNEnemyCastStatus.Cancelled && Vector3.Distance(held, move.Body.position) < .01f, "EB6 cancelling frozen hop preserves freeze owner");
        clock.RemoveModifier(frozen); yield return new WaitForSeconds(.3f);
        Check(move.Body.useGravity && !move.HasActiveMotion && move.Body.position.y < held.y, "EB6 thaw restores pre-hop gravity after cancelled physics lease");
        Reposition(move, new Vector3(-4, .02f, -5.8f));
        context.TryCast(PNAgentToolkitContent.Hop, Vector3.back, PNEnemyActionExecution.AuxiliaryTimeline, out receipt);
        yield return Until(() => receipt.IsTerminal, 2, "EB6 unsafe landing returns terminal result");
        Check(receipt.Status == PNEnemyCastStatus.Failed && receipt.Reason == PNMotionOutcome.InvalidLanding.ToString(), "EB6 hop cannot silently cross unsupported terrain");
        Reposition(move, new Vector3(-2, .02f, -4));
        context.TryCast(PNAgentToolkitContent.Curve, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out receipt);
        yield return Until(() => receipt.IsTerminal, 3, "EB6 trajectory reaches blocking wall");
        Check(receipt.Status == PNEnemyCastStatus.Failed && receipt.Reason == PNMotionOutcome.Blocked.ToString() && move.Body.position.x < -.6f,
            "EB6 curve wall collision reports blocked and never teleports to endpoint");
        Reposition(move, new Vector3(-4, .02f, -4)); target.transform.position = new Vector3(-3, 0, -4); Physics.SyncTransforms();
        double hp = target.GetCurrent(PNContentIds.Health);
        context.TryCast(PNAgentToolkitContent.ContactMotion, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out receipt);
        yield return Until(() => receipt.IsTerminal, 3, "EB6 contact motion completes");
        Check(receipt.Status == PNEnemyCastStatus.Completed && Math.Abs(hp - target.GetCurrent(PNContentIds.Health) - 1000) < .01,
            "EB6 swept contact hits queryable player exactly once despite physical pair ignore");
        double after = target.GetCurrent(PNContentIds.Health); yield return new WaitForSeconds(.15f);
        Check(target.GetCurrent(PNContentIds.Health) == after && !move.HasActiveMotion && move.CanMove, "EB6 completed contact performs no later hits and releases owned resources");
        Reposition(move, new Vector3(-4, .02f, -4));
        Action<PNDamageResult> cancelOnHit = _ => enemy.TryInterrupt(EveId.Parse("project_n:review_contact_cancel"));
        target.DamageReceived += cancelOnHit;
        try
        {
            context.TryCast(PNAgentToolkitContent.ContactMotion, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out receipt);
            yield return Until(() => receipt.IsTerminal, 3, "EB6 receiver callback can cancel the currently scanning contact Action");
            yield return null;
            Check(receipt.Status == PNEnemyCastStatus.Cancelled && enemy.PendingCleanupScopes == 0 && !move.HasActiveMotion && move.CanMove && enemy.Failure == null,
                "EB6 deferred scan cleanup remains owned until safe point and drains without fault");
        }
        finally { target.DamageReceived -= cancelOnHit; }
        void CaptureOnce() { if (!captured) { Capture("eb6-hop-peak"); captured = true; } }
    }
    private IEnumerator ToolkitBehaviors()
    {
        var context = enemy.GetComponent<PNEnemyReviewProbeBehavior>().Context;
        var agent = new PNEnemyAgentContext(context, actor => context.Stat.Targets(actor));
        var slot = context.Own(new PNAgentOwnedSlot<PNMovementTask>());
        target.transform.position = new Vector3(-2, 0, -4); Physics.SyncTransforms();
        var sensor = context.Own(new PNAgentPerception(agent, new PNPerceptionSettings(interval: .05f, reactionDelay: 0)));
        foreach (PNMovementTaskKind kind in Enum.GetValues(typeof(PNMovementTaskKind)))
        {
            Reposition(move, new Vector3(kind == PNMovementTaskKind.Flee ? -3.2f : -4, .02f, -4)); sensor.Tick(.1f); yield return null;
            var seen = sensor.NearestVisible();
            Check(seen != null, "EB3 target observation available for " + kind);
            var settings = new PNMovementTaskSettings(kind, new PNLocomotionOptions(.15f), minimumRange: 1, maximumRange: 1.5f,
                timeout: 12, radius: kind == PNMovementTaskKind.Orbit ? 2 : .4f, loop: false,
                offset: new Vector3(-.6f, 0, 0), patrolPoints: new[] { new Vector3(-3.4f, 0, -4), new Vector3(-3, 0, -4) });
            var home = kind == PNMovementTaskKind.Guard || kind == PNMovementTaskKind.Search ? new Vector3(-3, 0, -4) : agent.Self.Position;
            var task = slot.Replace(() => new PNMovementTask(agent, settings, home,
                kind == PNMovementTaskKind.Patrol || kind == PNMovementTaskKind.Guard || kind == PNMovementTaskKind.Search ? null : seen.Actor,
                kind == PNMovementTaskKind.Pursue ? seen : null));
            var before = move.Body.position; float deadline = Time.realtimeSinceStartup + (kind == PNMovementTaskKind.Orbit ? 1.5f : 10);
            while (!task.IsTerminal && task.Status != PNAgentTaskStatus.Holding && Time.realtimeSinceStartup < deadline)
            { sensor.Tick(Time.deltaTime); if (kind == PNMovementTaskKind.Pursue) task.UpdateObservation(seen); task.Tick(Time.deltaTime); yield return null; }
            if (kind == PNMovementTaskKind.Orbit)
                Check(!task.IsTerminal && PNAgentGeometry.CenterDistance(before, move.Body.position) > .2f, "EB3 orbit traverses validated segment without owning pose");
            else Check(task.Status == PNAgentTaskStatus.Succeeded || task.Status == PNAgentTaskStatus.Holding, "EB3 native task completes/holds: " + kind + " diagnostic=" + task.Diagnostic);
            task.Cancel(); Check(move.NavigationRequest == null || move.NavigationRequest.IsTerminal, "EB3 task releases exact navigation handle: " + kind);
        }
        slot.Dispose(); sensor.Dispose();
        float attack = .4f, support = .3f; bool supportAllowed = true;
        var selector = new PNUtilitySelector(new[] { new PNUtilityCandidate("attack", () => true, () => attack), new PNUtilityCandidate("support", () => supportAllowed, () => support) }, .1f, .5f, .1);
        Check(selector.Tick(.1f) == "attack", "EB4 utility chooses highest eligible score"); support = .8f;
        Check(selector.Tick(.2f) == "attack" && selector.Tick(.4f) == "support", "EB4 utility minimum hold prevents rapid switching then permits stronger candidate");
        supportAllowed = false; Check(selector.Tick(.01f) == "attack", "EB4 invalidation bypasses hold and cadence");
        var tie = new PNUtilitySelector(new[] { new PNUtilityCandidate("z", () => true, () => .5f), new PNUtilityCandidate("a", () => true, () => .5f) });
        Check(tie.Tick(.1f) == "a", "EB4 utility tie-break uses stable ID");
        Reject(() => new PNUtilitySelector(new[] { new PNUtilityCandidate("bad", () => true, () => float.NaN) }).Tick(.1f), "EB4 non-finite score rejected");
        int executions = 0;
        using (var goal = new PNCompositeGoal(Enumerable.Range(0, 40).Select(_ => (PNAgentGoal)new PNDelegateGoal(delta => { executions++; return PNGoalStatus.Succeeded; }))))
        {
            goal.Tick(.1f); Check(executions <= 15 && goal.Status == PNGoalStatus.Running, "EB4 nested goal tree shares finite tick fuel");
            for (int i = 0; i < 5 && !goal.IsTerminal; i++) goal.Tick(.1f);
            Check(goal.Status == PNGoalStatus.Succeeded && executions == 40, "EB4 sequence resumes bounded work without replaying completed leaves");
        }
        using (var goal = new PNCompositeGoal(new PNAgentGoal[] { new PNDelegateGoal(_ => PNGoalStatus.Failed), new PNDelegateGoal(_ => PNGoalStatus.Succeeded) }, true))
            Check(goal.Tick(.1f) == PNGoalStatus.Succeeded, "EB4 fallback tries next failed strategy");
        int attempts = 0;
        using (var retry = new PNRetryGoal(() => new PNDelegateGoal(_ => { attempts++; return PNGoalStatus.Failed; }), retries: 2, backoff: .01f))
        { for (int i = 0; i < 8 && !retry.IsTerminal; i++) retry.Tick(.02f); Check(retry.Status == PNGoalStatus.Failed && attempts == 3, "EB4 retry uses fresh goals and bounded backoff attempts"); }
        var shared = new PNDelegateGoal(_ => PNGoalStatus.Running);
        using (var owner = new PNCompositeGoal(new[] { shared }))
            Reject(() => new PNCompositeGoal(new[] { shared }), "EB4 mutable goal cannot be owned by two trees");
        Reposition(move, new Vector3(-3, .02f, -4));
        var admission = context.TryCast(PNEnemyGameplayContent.Strike, Vector3.right, PNEnemyActionExecution.Instant, out var receipt, new[] { target });
        Check(admission == PNEnemyCastAdmission.Started && receipt.Status == PNEnemyCastStatus.Completed && receipt.InvocationId > 0, "EB4 instant cast receipt preserves completed invocation");
        admission = context.TryCast(PNEnemyGameplayContent.Charge, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out receipt);
        Check(admission == PNEnemyCastAdmission.Started && !receipt.IsTerminal, "EB4 timeline cast receipt begins running");
        Check(context.TryCast(PNEnemyGameplayContent.Strike, Vector3.right, PNEnemyActionExecution.Instant, out var denied, new[] { target }) == PNEnemyCastAdmission.Busy && denied == null,
            "EB4 busy cast does not fabricate completion receipt");
        enemy.TryInterrupt(EveId.Parse("project_n:review_interrupt"));
        Check(receipt.Status == PNEnemyCastStatus.Cancelled, "EB4 interrupted cast is distinct from successful completion");
        var other = Enemy(a, new Vector3(-3.5f, .02f, -3));
        yield return Until(() => other.ActiveBehaviorId == "probe", 3, "EB5 peer enters authoritative behavior");
        var otherContext = other.GetComponent<PNEnemyReviewProbeBehavior>().Context;
        var peer = new PNEnemyAgentContext(otherContext, actor => otherContext.Stat.Targets(actor));
        var groups = new PNAgentGroupService(session); var kindId = EveId.Parse("project_n:review_pack");
        var member = context.Own(groups.Join(agent, kindId, "encounter-a", (x, y) => x.Layer == y.Layer, priority: 1));
        var peerMember = otherContext.Own(groups.Join(peer, kindId, "encounter-a", (x, y) => x.Layer == y.Layer));
        var separate = otherContext.Own(groups.Join(peer, kindId, "encounter-b", (x, y) => x.Layer == y.Layer));
        int slotId = peerMember.Slot; Check(groups.GroupCount == 2 && member.Leader.Id == agent.Self.Id, "EB5 same-kind encounter groups remain separate with stable leader");
        member.Send(PNAgentSignalKind.Alert, agent.Self.Position);
        int received = 0; peerMember.Drain(_ => received++); int leaked = separate.Drain(_ => { });
        Check(received == 1 && leaked == 0, "EB5 signal is scoped to explicit group generation");
        var reservation = member.Reserve("target-slot", PNActorHandle.Capture(target), .1f);
        Check(reservation != null && peerMember.Reserve("target-slot") == null, "EB5 shared reservation prevents simultaneous role ownership");
        yield return new WaitForSeconds(.15f); groups.Tick();
        using (var next = peerMember.Reserve("target-slot")) Check(next != null && !reservation.IsCurrent, "EB5 reservation expires using world gameplay time");
        var clock = enemy.GetComponent<EveTimeScale>(); long frozen = clock.AddOwnedModifier(EveId.Parse("project_n:review_group_freeze"), EveTimeModifierKind.Scale, -1, this);
        int groupTicks = groups.Ticks; yield return null; groups.Tick();
        Check(groups.Ticks > groupTicks && peerMember.Leader.Id == agent.Self.Id, "EB5 frozen leader does not own or stall group clock"); clock.RemoveModifier(frozen);
        member.Send(PNAgentSignalKind.Alert, agent.Self.Position, ttl: .05f); yield return new WaitForSeconds(.1f);
        Check(peerMember.Drain(_ => { }) == 0, "EB5 expired advisory signal is not executed after delay");
        member.Dispose(); yield return null; groups.Tick();
        Check(peerMember.Leader.Id == peer.Self.Id && peerMember.Slot == slotId, "EB5 leader replacement preserves surviving formation slot");
        peerMember.Dispose(); separate.Dispose(); yield return null; groups.Tick(); Check(groups.GroupCount == 0, "EB5 empty group releases memberships and subscriptions"); groups.Dispose();
        var supportActor = Enemy(a, new Vector3(-4, .02f, -2), supportExample: true);
        var ally = other.GetComponent<PNStat>(); ally.ReceiveDamage(ally.CreateDamage(50000, false, 0, EveId.Parse("base:physical")));
        double hp = ally.GetCurrent(PNContentIds.Health);
        yield return Until(() => supportActor.ActiveBehaviorId == "probe", 3, "EB5 support example enrolled through ordinary actor lifecycle");
        supportActor.GetComponent<PNToolkitSupportBehavior>().Configure(PNActorHandle.Capture(ally)); supportActor.Switch("support", PNEnemySwitchPolicy.CancelCurrent);
        yield return Until(() => ally.GetCurrent(PNContentIds.Health) > hp && supportActor.CastCount > 0, 4, "EB5 utility support chooses ally heal through registered Skill/Operation");
        Check(supportActor.Failure == null, "EB5 support requires no summon lifecycle or hardcoded player actor");
        supportActor.Switch("probe", PNEnemySwitchPolicy.CancelCurrent); Capture("eb345-toolkit");
        target.transform.position = new Vector3(-2, 0, -4); Physics.SyncTransforms();
        var packService = new PNAgentGroupService(session);
        var packA = Enemy(a, new Vector3(-4, .02f, -4), packExample: true);
        var packB = Enemy(a, new Vector3(-4, .02f, -3), packExample: true);
        yield return Until(() => packA.ActiveBehaviorId == "probe" && packB.ActiveBehaviorId == "probe", 3, "EB5 same-kind pack actors enrolled");
        packA.GetComponent<PNToolkitPackBehavior>().Configure(packService, kindId, "pack-example");
        packB.GetComponent<PNToolkitPackBehavior>().Configure(packService, kindId, "pack-example");
        packA.Switch("pack", PNEnemySwitchPolicy.CancelCurrent); packB.Switch("pack", PNEnemySwitchPolicy.CancelCurrent);
        float packDeadline = Time.realtimeSinceStartup + 8;
        while ((packA.CastCount == 0 || packB.CastCount == 0) && Time.realtimeSinceStartup < packDeadline) { packService.Tick(); yield return null; }
        Check(packService.GroupCount == 1 && packA.CastCount > 0 && packB.CastCount > 0 && packA.Failure == null && packB.Failure == null,
            "EB5 same-kind pack uses shared focus and finite attack reservations through independent actor Skills");
        packA.Switch("probe", PNEnemySwitchPolicy.CancelCurrent); packB.Switch("probe", PNEnemySwitchPolicy.CancelCurrent);
        yield return null; packService.Tick(); Check(packService.GroupCount == 0, "EB5 pack exit releases group membership and reservations"); packService.Dispose();
    }
    private IEnumerator ToolkitSpatial()
    {
        var context = enemy.GetComponent<PNEnemyReviewProbeBehavior>().Context;
        Reposition(move, new Vector3(-4, .02f, -4)); target.transform.position = new Vector3(-2, 0, -4); Physics.SyncTransforms();
        var agent = new PNEnemyAgentContext(context, value => context.Stat.Targets(value));
        Check(agent.IsCurrent && agent.CanTarget(target), "EB2 agent adapter resolves live self and policy target");
        var otherWorld = Target(b, new Vector3(-2, 0, -4));
        Check(!agent.CanTarget(otherWorld), "EB2 actor at same coordinates in another world is not a target");
        var point = agent.FindPoint(new PNPointQuerySettings(new Vector3(-2, 0, -4), 0, 0, candidates: 1));
        float deadline = Time.realtimeSinceStartup + 4;
        while (!point.IsTerminal && Time.realtimeSinceStartup < deadline) { point.Tick(); yield return null; }
        Check(point.Status == PNPointQueryStatus.Success && PNAgentGeometry.CenterDistance(point.Point, new Vector3(-2, 0, -4)) < .01f,
            "EB2 point query confirms a real full path in its world");
        var impossible = agent.FindPoint(new PNPointQuerySettings(new Vector3(100, 0, 100), 0, 0, candidates: 1)); impossible.Tick();
        Check(impossible.Status == PNPointQueryStatus.NoCandidate, "EB2 distant projection is rejected without straight-line fallback");
        var cancelled = agent.FindPoint(new PNPointQuerySettings(new Vector3(-2, 0, -4), 0, 0, candidates: 1)); cancelled.Tick(); cancelled.Dispose();
        yield return Until(() => PNWorldNavigation.ForSession(session).PendingRequests == 0, 4, "EB2 cancelled point probe drains native path callback");
        Check(cancelled.Status == PNPointQueryStatus.Cancelled, "EB2 late path callback cannot revive cancelled point query");
        var perception = new PNAgentPerception(agent, new PNPerceptionSettings(interval: .05f, reactionDelay: .1f, memoryDuration: .7f));
        perception.Tick(.02f); Check(perception.NearestVisible() == null, "EB2 first sight does not bypass reaction delay");
        deadline = Time.realtimeSinceStartup + 2;
        while (perception.NearestVisible() == null && Time.realtimeSinceStartup < deadline) { perception.Tick(Time.deltaTime); yield return null; }
        var seen = perception.NearestVisible();
        Check(seen != null && seen.Actor.TryResolve(out var resolved) && resolved == target, "EB2 LOS sensor reacts to live target after delay");
        var last = seen.LastPosition; target.transform.position = new Vector3(2, 0, -4); Physics.SyncTransforms();
        float until = Time.time + .25f; while (Time.time < until) { perception.Tick(Time.deltaTime); yield return null; }
        Check(!seen.Visible && seen.LastPosition == last && perception.NearestVisible() == null, "EB2 occluded target preserves last-known position without live tracking");
        int queries = perception.SightQueries;
        using (var hold = a.AcquireLease("toolkit-review"))
        { a.SetPaused(true); perception.Tick(.4f); Check(perception.SightQueries == queries && perception.Records.Any(), "EB2 world pause preserves memory and stops sensor work"); a.SetPaused(false); }
        until = Time.time + 1; while (Time.time < until) { perception.Tick(Time.deltaTime); yield return null; }
        Check(!perception.Records.Any(), "EB2 unseen target memory expires on entity clock"); perception.Dispose();
        var retiredTarget = Target(a, new Vector3(-2, 0, -4)); var handle = PNActorHandle.Capture(retiredTarget);
        retiredTarget.GetComponent<EveEntityLoader>().Unload(); retiredTarget.GetComponent<EveEntityIdentity>().ResetIdentity();
        Check(!handle.TryResolve(out _), "EB2 actor handle rejects unload and identity reuse");
        point = agent.FindPoint(new PNPointQuerySettings(new Vector3(-2, 0, -4), 0, 0, candidates: 1));
        enemy.Switch("charge", PNEnemySwitchPolicy.CancelCurrent); point.Tick();
        Check(!agent.IsCurrent && point.Status == PNPointQueryStatus.Cancelled, "EB2 retired behavior adapter cancels pending spatial work");
    }
    private IEnumerator ToolkitLocomotion()
    {
        Reject(() => new PNLocomotionOptions(float.NaN), "EB1 NaN stop rejected");
        Reject(() => new PNLocomotionOptions(0, speedFraction: 1.1f), "EB1 speed multiplier cannot exceed stat speed");
        Reject(() => new PNLocomotionOptions(0, acceleration: 0), "EB1 invalid acceleration rejected before mutation");
        Check(PNLocomotionOptions.LegacyImmediate(.2f).IsImmediate, "EB1 legacy request keeps immediate settings");
        Check(Mathf.Abs(PNAgentGeometry.Distance(Vector3.zero, .2f, Vector3.right, .3f, PNDistanceMetric.FootprintGapXZ) - .5f) < .0001f,
            "EB1 footprint gap subtracts both radii once");
        Check(PNAgentGeometry.CenterDistance(Vector3.zero, new Vector3(3, 50, 4)) == 5, "EB1 distance ignores height on XZ");
        var probe = enemy.GetComponent<PNEnemyReviewProbeBehavior>(); var context = probe.Context;
        var legacy = context.GoTo(new Vector3(-3, 0, -4), .2f);
        Check(legacy.Options == null && PNAgentPerception.ActiveInstances == 0 && PNAgentGroupService.ActiveInstances == 0,
            "EB1 opt-out uses legacy request without creating options/sensor/group runtime"); move.CancelNavigation(legacy);
        target.transform.position = new Vector3(100, 0, 0); Physics.SyncTransforms();
        Reposition(move, new Vector3(-4, .02f, -4));
        var options = new PNLocomotionOptions(.2f, 1, 1, true, 4, true, 8, true, 360);
        var route = context.GoTo(new Vector3(-1, 0, -4), options);
        yield return Until(() => route.Status == PNEnemyNavigationStatus.Following, 4, "EB1 optional locomotion route ready");
        float maxAcceleration = 0, previousSpeed = 0; bool slowed = false;
        float deadline = Time.realtimeSinceStartup + 8;
        while (!route.IsTerminal && Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForFixedUpdate();
            float speed = move.MoveDirection.magnitude * move.CurrentMoveSpeed;
            maxAcceleration = Mathf.Max(maxAcceleration, (speed - previousSpeed) / Time.fixedDeltaTime);
            if (move.Body.position.x > -1.8f && speed > .02f && speed < move.CurrentMoveSpeed * .8f) slowed = true;
            previousSpeed = speed;
        }
        Check(route.Status == PNEnemyNavigationStatus.Arrived && Mathf.Abs(move.Body.position.x + 1.2f) < .08f,
            "EB1 soft arrival stops at configured range using real body");
        Check(slowed && maxAcceleration <= 4.3f, "EB1 real movement accelerates within limit and slows near destination");
        var lookTask = context.LookAt(move.Body.position + Vector3.left);
        var lookSkill = context.LookAt(move.Body.position + Vector3.right, PNEnemyLookPriority.Skill);
        yield return new WaitForFixedUpdate(); Check(move.LookDirection.x > .9f, "EB1 skill look has priority over task look");
        lookSkill.Dispose(); yield return new WaitForFixedUpdate();
        Check(move.LookDirection.x < -.9f, "EB1 releasing exact skill look restores surviving task look");
        using (var denial = enemy.Controls.Acquire("toolkit-look-denied", new[] { PNControlIds.FaceControl }))
        { lookTask.UpdatePoint(move.Body.position + Vector3.right); yield return new WaitForFixedUpdate(); Check(move.LookDirection.x < -.9f, "EB1 task look obeys FaceControl"); }
        lookTask.Dispose(); Check(!lookTask.UpdatePoint(Vector3.zero), "EB1 disposed look cannot update later selection");
        Reposition(move, new Vector3(-3, .02f, -4));
        route = context.GoTo(new Vector3(-4, 0, -4), new PNLocomotionOptions(.1f, turnLimitEnabled: true, turnRate: 90));
        var start = move.Body.position; yield return new WaitForSeconds(.15f);
        Check(Vector3.Distance(start, move.Body.position) < .08f, "EB1 reverse heading turns in place before moving");
        yield return Until(() => route.Status == PNEnemyNavigationStatus.Arrived, 7, "EB1 limited heading turns through reversal and arrives");
        Reposition(move, new Vector3(-4, .02f, -4));
        route = context.GoTo(new Vector3(-1, 0, -4), options);
        yield return Until(() => move.MoveDirection.magnitude > .3f, 3, "EB1 smoothing moves before freeze");
        var clock = enemy.GetComponent<EveTimeScale>();
        long freeze = clock.AddOwnedModifier(EveId.Parse("project_n:toolkit_freeze"), EveTimeModifierKind.Scale, -1, this);
        yield return new WaitForFixedUpdate(); using (move.SuspendNavigation())
        { var frozen = move.Body.position; yield return new WaitForSeconds(.15f); Check(Vector3.Distance(frozen, move.Body.position) < .01f, "EB1 freeze owns the body during suspension"); clock.RemoveModifier(freeze); yield return new WaitForSeconds(.12f); Check(Vector3.Distance(frozen, move.Body.position) < .01f, "EB1 thaw with suspension does not restore stale velocity"); }
        yield return Until(() => route.Status == PNEnemyNavigationStatus.Arrived, 8, "EB1 release resumes smoother from real body and arrives");
        var other = Enemy(a, new Vector3(-2.5f, .02f, -4)); var otherMove = other.GetComponent<PNEnemyMove>();
        yield return Until(() => other.ActiveBehaviorId == "probe", 3, "EB1 second enemy ready");
        enemy.gameObject.layer = other.gameObject.layer = 7;
        Check(Physics.GetIgnoreLayerCollision(7, 7), "EB1 project already disables Enemy7 physical pair");
        Reposition(move, new Vector3(-4, .02f, -4));
        route = context.GoTo(new Vector3(-1, 0, -4), .1f);
        yield return Until(() => route.Status == PNEnemyNavigationStatus.Arrived, 5, "EB1 actual body crosses another enemy without collision");
        var hits = new Collider[16];
        int count = enemy.gameObject.scene.GetPhysicsScene().OverlapSphere(otherMove.Body.position + Vector3.up * .7f, .3f, hits, 1 << 7, QueryTriggerInteraction.Collide);
        bool found = false; for (int i = 0; i < count; i++) if (hits[i].GetComponentInParent<PNEnemyStat>() == other.GetComponent<PNEnemyStat>()) found = true;
        Check(found, "EB1 enemy hurt collider remains discoverable by combat query mask");
        var staleLook = context.LookAt(move.Body.position + Vector3.right);
        enemy.Switch("charge", PNEnemySwitchPolicy.CancelCurrent); yield return null;
        Check(!staleLook.UpdatePoint(Vector3.zero), "EB1 retired behavior look cannot mutate a new selection");
        Capture("eb1-locomotion");
    }
}
#endif
