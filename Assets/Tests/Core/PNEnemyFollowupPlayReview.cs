#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Opt-in fixtures only: registered variants exercise public production factories.
public static class PNEnemyFollowupContent
{
    public static readonly EveId Fast = EveId.Parse("review:fast_contact");
    public static readonly EveId Budget = EveId.Parse("review:budget_curve");
    public static EveId Fault(string phase) => EveId.Parse("review:fault_" + phase);
    public static IEnumerable<EveId> Ids => new[] { Fast, Budget, Fault("start"), Fault("perform"), Fault("tick"), Fault("dispose"), Fault("none") };
    public static void Register(EveRegistryHub hub)
    {
        string fixtureRoot = Environment.GetCommandLineArgs().FirstOrDefault(value => value.StartsWith("--pn-followup-root="))?.Substring("--pn-followup-root=".Length);
        if (fixtureRoot != null && File.Exists(fixtureRoot + "/Disconnected/world.bytes"))
            hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(EveId.Parse("review:disconnected"), PNNavigationCache.Hash("disconnected-followup"),
                typeof(PNEnemyDisconnectedProvider), () => new PNEnemyDisconnectedProvider(fixtureRoot)));
        var selected = EveId.Parse("review:trajectory_override");
        hub.Actions().Register(new PNSkillActionDefinition(selected, typeof(PNEnemyFollowupTrajectory),
            (data, registry) => PNAgentTrajectoryAction.Validate(data, registry, false), PNSkillContextRequirements.Direction));
        hub.Actions().RequestOverride(EveId.Parse("base:trajectory_action"), selected, "review", "1.0.0", new[] { "project_n" });
        hub.Actions().SelectOverride(EveId.Parse("base:trajectory_action"), "review");
        var contact = EveId.Parse("review:contact_override");
        hub.Actions().Register(new PNSkillActionDefinition(contact, typeof(PNEnemyFollowupContact),
            (data, registry) => PNAgentTrajectoryAction.Validate(data, registry, true), PNSkillContextRequirements.Direction));
        hub.Actions().RequestOverride(EveId.Parse("base:contact_motion_action"), contact, "review", "1.0.0", new[] { "project_n" });
        hub.Actions().SelectOverride(EveId.Parse("base:contact_motion_action"), "review");
        var heal = EveId.Parse("review:heal_override");
        hub.Operations().Register(new PNSkillOperationDefinition(heal, typeof(PNEnemyFollowupHeal), (data, content) => PNHealSettings.Parse(data).ValidateContent(content)));
        hub.Operations().RequestOverride(EveId.Parse("base:heal"), heal, "review", "1.0.0", new[] { "project_n" });
        hub.Operations().SelectOverride(EveId.Parse("base:heal"), "review");
        hub.Actions().Register(new PNSkillActionDefinition(EveId.Parse("review:fault_action"), typeof(PNEnemyFollowupFaultAction)));
        foreach (var phase in new[] { "start", "perform", "tick", "dispose", "none" })
            Add(Fault(phase), "review:fault_action", "{'phase':'" + phase + "'}");
        Add(Fast, "base:contact_motion_action", @"{'kind':'CurveXZ','distance':2,'lateralOffset':0,'duration':0.02,
          'permissionId':'base:motion_voluntary','actorCollision':'IgnoreEnemyBodies','maxSegmentLength':0.12,'maxChordError':0.015,'maxSubsteps':64,
          'hitRadius':0.25,'activeStart':0,'activeEnd':1,'repeatInterval':0,'canCrit':false,'value':{'amount':1000},'tags':['base:physical','base:skill']}");
        Add(Budget, "base:trajectory_action", @"{'kind':'CurveXZ','distance':2,'lateralOffset':0.5,'duration':0.01,
          'permissionId':'base:motion_voluntary','actorCollision':'IgnoreEnemyBodies','maxSegmentLength':0.005,'maxChordError':0.001,'maxSubsteps':1}");
        void Add(EveId id, string action, string payload) => hub.Skills.Register(new EveSkillDefinition(id, new JObject {
            ["categoryIds"] = new JArray(), ["affectedByAttackSpeed"] = false,
            ["action"] = new JObject { ["id"] = action, ["start"] = new JObject(), ["perform"] = JObject.Parse(payload), ["exit"] = new JObject(), ["cancel"] = new JObject() }
        }));
    }
}

public sealed class PNEnemyDisconnectedProvider : IPNWorldProvider
{
    private readonly string rootPath;
    public PNEnemyDisconnectedProvider(string path) { rootPath = path; }
    public IEnumerator Load(PNWorldBuildContext build)
    {
        var cache = ScriptableObject.CreateInstance<PNNavigationCache>(); build.Own(() => UnityEngine.Object.Destroy(cache));
        cache.SetBakedData(File.ReadAllBytes(rootPath + "/Disconnected/world.bytes"), File.ReadAllText(rootPath + "/Disconnected/world.hash"), "ground-small", .25f, 1.5f);
        var root = new GameObject("Disconnected fixture"); SceneManager.MoveGameObjectToScene(root, build.PhysicsScene);
        build.Own(() => { if (root != null) UnityEngine.Object.Destroy(root); });
        foreach (float x in new[] { -3f, 3f })
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(root.transform);
            floor.transform.position = new Vector3(x, -.5f, 0); floor.transform.localScale = new Vector3(4, 1, 12); floor.layer = 13;
        }
        root.AddComponent<PNWorldNavigationSource>().Configure(cache, cache.Fingerprint);
        build.AddSlot("main", build.PhysicsScene, new[] { root }); yield break;
    }
}

public sealed class PNEnemyFollowupTrajectory : PNAgentTrajectoryAction
{
    public static int Starts;
    public override void Start(PNSkillActionContext context) { Starts++; base.Start(context); }
}
public sealed class PNEnemyFollowupContact : PNAgentContactMotionAction
{
    public static int Starts;
    public override void Start(PNSkillActionContext context) { Starts++; base.Start(context); }
}
public sealed class PNEnemyFollowupHeal : PNHealOperation
{
    public static int Executions;
    public override void Execute(PNSkillActionContext context, JToken payload) { Executions++; base.Execute(context, payload); }
}

public sealed class PNEnemyFollowupFaultAction : PNSkillAction
{
    private PNControlLease lease;
    private string phase;
    private bool failedDispose;
    public override bool HasAuxiliaryTimeline => true;
    public override void Start(PNSkillActionContext context)
    {
        context.BeginAction(); phase = context.Data.Action.Perform["phase"].Value<string>();
        lease = context.Skill.Controls.Acquire("followup fault", new[] { PNControlIds.Move });
        if (phase == "start") throw new InvalidOperationException("Intentional followup start");
    }
    public override void Perform(PNSkillActionContext context)
    { if (phase == "perform") throw new InvalidOperationException("Intentional followup perform"); }
    public override bool AdvanceAuxiliary(float delta)
    { if (phase == "tick") throw new InvalidOperationException("Intentional followup tick"); return delta > 0; }
    public override void Exit(PNSkillActionContext context) { context.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext context) { context.CancelEffects(); Dispose(); }
    public override void Dispose()
    {
        if (phase == "dispose" && !failedDispose) { failedDispose = true; throw new InvalidOperationException("Intentional followup dispose"); }
        lease?.Dispose(); lease = null;
    }
}

public sealed partial class PNEnemyMoveSkillPlayReview
{
    private bool followupFaultWindow;
    private int followupFaultLogs;
    private GameObject FollowupBox(string name, Vector3 center, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.layer = 31;
        SceneManager.MoveGameObjectToScene(go, a.PhysicsOwner); go.transform.position = center; go.transform.localScale = scale;
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor", new Color(.6f, .3f, .4f));
        materials.Add(material); go.GetComponent<Renderer>().sharedMaterial = material;
        Physics.SyncTransforms(); return go;
    }
    private IEnumerator FollowupDisconnected()
    {
        if (!session.Registry.Get<PNWorldDefinition>().TryGet(EveId.Parse("review:disconnected"), out _)) yield break;
        var island = worlds.Create(EveId.Parse("review:disconnected"));
        yield return Until(() => island.CanExecute, 10, "P05 disconnected baked world ready");
        var npc = Enemy(island, new Vector3(-3, .02f, 0));
        yield return Until(() => npc.ActiveBehaviorId == "probe", 3, "P05 disconnected actor ready");
        var route = npc.GetComponent<PNEnemyMove>().GoTo(new Vector3(3, 0, 0), .15f);
        yield return Until(() => route.IsTerminal, 20, "P05 disconnected route terminates");
        Check(route.Status == PNEnemyNavigationStatus.NoPath && npc.GetComponent<Rigidbody>().position.x < -1,
            "P05 disconnected result status=" + route.Status + " x=" + npc.GetComponent<Rigidbody>().position.x + " diagnostic=" + route.Diagnostic);
        var agent = new PNEnemyAgentContext(npc.GetComponent<PNEnemyReviewProbeBehavior>().Context, _ => true);
        using (var query = agent.FindPoint(new PNPointQuerySettings(new Vector3(3, 0, 0), 0, 0, candidates: 1)))
        {
            float until = Time.realtimeSinceStartup + 5;
            while (!query.IsTerminal && Time.realtimeSinceStartup < until) { query.Tick(); yield return null; }
            Check(query.Status == PNPointQueryStatus.NoCandidate && query.Diagnostic == "NoFullPath", "P05 point probe rejects nearest reachable endpoint on wrong island");
        }
        var npcRoot = npc.gameObject;
        npc.GetComponent<EveEntityLoader>().Unload(); npc.GetComponent<EveEntityIdentity>().ResetIdentity();
        yield return Until(() => worlds.Unload(island), 10, "P05 disconnected world drains"); actors.Remove(npcRoot);
    }
    private IEnumerator FollowupCoverage()
    {
        var context = enemy.GetComponent<PNEnemyReviewProbeBehavior>().Context;
        target.transform.position = new Vector3(-2.9f, 0, -4); Physics.SyncTransforms();
        var wall = FollowupBox("P01 thin wall", new Vector3(-3.35f, 1, -4), new Vector3(.025f, 2, 2));
        try
        {
            foreach (var skill in new[] { PNAgentToolkitContent.ContactMotion, PNEnemyFollowupContent.Fast })
            {
                Reposition(move, new Vector3(-4, .02f, -4)); double hp = target.GetCurrent(PNContentIds.Health);
                Check(context.TryCast(skill, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt) == PNEnemyCastAdmission.Started, "P01 contact admitted " + skill);
                yield return Until(() => receipt.IsTerminal, 4, "P01 contact finite " + skill);
                Check(receipt.Status == PNEnemyCastStatus.Failed && receipt.Reason == "Blocked" && move.Body.position.x < -3.5f,
                    "P01 normal/high-speed sweep stops before 0.025m wall " + skill);
                Check(target.GetCurrent(PNContentIds.Health) == hp, "P01 no damage beyond wall " + skill);
                Capture("p01-" + (skill == PNEnemyFollowupContent.Fast ? "fast" : "normal"));
            }
        }
        finally { Destroy(wall); }
        yield return null;
        target.transform.position = new Vector3(100, 0, 0); Physics.SyncTransforms();
        Reposition(move, new Vector3(-4, .02f, -4)); var before = move.Body.position;
        context.TryCast(PNEnemyFollowupContent.Budget, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out var budget);
        yield return Until(() => budget.IsTerminal, 4, "P02 budget termination");
        Check(budget.Status == PNEnemyCastStatus.Failed && budget.Reason == "BudgetExceeded" && PNAgentGeometry.CenterDistance(before, move.Body.position) < .04f && !move.HasActiveMotion,
            "P02 exhausted subdivision has no partial teleport or retained motion");
        var ceiling = FollowupBox("P03 low ceiling", new Vector3(-3.25f, 1.65f, -4), new Vector3(3, .1f, 2));
        try
        {
            Reposition(move, before); context.TryCast(PNAgentToolkitContent.Hop, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out var hop);
            yield return Until(() => hop.IsTerminal, 4, "P03 ceiling hop termination");
            Check(hop.Status == PNEnemyCastStatus.Failed && hop.Reason == "Blocked" && move.Body.position.y < .25f && move.Body.useGravity && !move.HasActiveMotion,
                "P03 ceiling blocks real body and returns gravity lease"); Capture("p03-ceiling");
        }
        finally { Destroy(ceiling); }
        yield return null;
        foreach (float cancelAt in new[] { .08f, .3f, .5f })
        {
            Reposition(move, before); context.TryCast(PNAgentToolkitContent.Hop, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out var hop);
            float until = Time.time + cancelAt; while (Time.time < until && !hop.IsTerminal) yield return null;
            Check(!hop.IsTerminal, "P04 valid hop still active at cancel phase " + cancelAt);
            var position = move.Body.position; enemy.TryInterrupt(EveId.Parse("review:cancel"));
            Check(hop.Status == PNEnemyCastStatus.Cancelled && Vector3.Distance(position, move.Body.position) < .04f && move.Body.useGravity && !move.HasActiveMotion,
                "P04 rising/apex/falling cancellation returns physics without warp " + cancelAt);
            yield return Until(() => move.Body.position.y < .12f, 4, "P04 gravity returns to floor " + cancelAt);
        }
        var capsule = move.GetComponent<CapsuleCollider>(); float radius = capsule.radius;
        try { capsule.radius = .4f; Physics.SyncTransforms(); Reject(() => context.GoTo(new Vector3(-2, 0, -4), .1f), "P06 oversized footprint rejected before route admission"); }
        finally { capsule.radius = radius; Physics.SyncTransforms(); }
        foreach (string phase in new[] { "start", "perform", "tick", "dispose", "none" })
        {
            var faultActor = Enemy(a, new Vector3(-4, .02f, -2));
            yield return Until(() => faultActor.ActiveBehaviorId == "probe", 3, "O02 fault actor ready " + phase);
            var faultContext = faultActor.GetComponent<PNEnemyReviewProbeBehavior>().Context;
            var faultMove = faultActor.GetComponent<PNEnemyMove>();
            followupFaultWindow = true; int logsBefore = followupFaultLogs;
            try
            {
                if (phase == "start" || phase == "perform")
                {
                    Reject(() => faultContext.TryCast(PNEnemyFollowupContent.Fault(phase), Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline), "O02 synchronous custom failure observed " + phase);
                    Check(faultMove.CanMove && !faultActor.IsExecutingEnemyAction, "O02 partial acquire released " + phase);
                }
                else
                {
                    faultContext.TryCast(PNEnemyFollowupContent.Fault(phase), Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt);
                    yield return Until(() => receipt.IsTerminal, 4, "O02 custom timeline terminal " + phase);
                    yield return Until(() => faultActor.PendingCleanupScopes == 0, 4, "O02 retained disposal retried " + phase);
                    Check(faultMove.CanMove && !faultActor.IsExecutingEnemyAction && receipt.Status == (phase == "none" ? PNEnemyCastStatus.Completed : PNEnemyCastStatus.Failed),
                        "O02/O03 non-motion timeline result and resource cleanup " + phase);
                    Check(phase == "none" ? followupFaultLogs == logsBefore : faultActor.Failure != null && followupFaultLogs == logsBefore + 1,
                        "O02 exact injected owner fault count " + phase);
                }
            }
            finally { followupFaultWindow = false; }
            faultActor.GetComponent<EveEntityLoader>().Unload(); faultActor.GetComponent<EveEntityIdentity>().ResetIdentity(); actors.Remove(faultActor.gameObject); Destroy(faultActor.gameObject);
            yield return null;
        }
        foreach (string timing in new[] { "before", "receiver-death", "after" })
        {
            var npc = Enemy(a, new Vector3(-4, .02f, -4));
            yield return Until(() => npc.ActiveBehaviorId == "probe", 3, "L01 contact actor ready " + timing);
            var npcContext = npc.GetComponent<PNEnemyReviewProbeBehavior>().Context;
            yield return Until(() => target.IFrameRemaining == 0, 3, "L01 receiver iframe expired before independent case " + timing);
            target.transform.position = new Vector3(-3, 0, -4); Physics.SyncTransforms(); double hp = target.GetCurrent(PNContentIds.Health);
            Action<PNDamageResult> diedInReceiver = _ => {
                var stat = npc.GetComponent<PNStat>(); stat.ReceiveDamage(stat.CreateDamage(10000000, false, 0, EveId.Parse("base:physical")));
            };
            if (timing == "receiver-death") target.DamageReceived += diedInReceiver;
            try
            {
                npcContext.TryCast(PNAgentToolkitContent.ContactMotion, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt);
                if (timing == "before") npc.enabled = false;
                if (timing == "after")
                { yield return Until(() => target.GetCurrent(PNContentIds.Health) < hp, 4, "L01 first hit before cancellation"); npc.TryInterrupt(EveId.Parse("review:after_hit")); }
                yield return Until(() => receipt.IsTerminal && npc.PendingCleanupScopes == 0, 4, "L01 contact cancellation drains scan ownership " + timing);
                double heldHp = target.GetCurrent(PNContentIds.Health); yield return new WaitForSeconds(.2f);
                Check(receipt.Status == PNEnemyCastStatus.Cancelled && !npc.GetComponent<PNEnemyMove>().HasActiveMotion && target.GetCurrent(PNContentIds.Health) == heldHp &&
                    Math.Abs(hp - heldHp - (timing == "before" ? 0 : 1000)) < .01,
                    "L01 no late/duplicate hit across disable, receiver death and after-hit cancel " + timing +
                    " status=" + receipt.Status + " reason=" + receipt.Reason + " damage=" + (hp - heldHp) + " late=" + (heldHp - target.GetCurrent(PNContentIds.Health)) + " motion=" + npc.GetComponent<PNEnemyMove>().HasActiveMotion);
            }
            finally { target.DamageReceived -= diedInReceiver; }
            npc.GetComponent<EveEntityLoader>().Unload(); npc.GetComponent<EveEntityIdentity>().ResetIdentity(); actors.Remove(npc.gameObject); Destroy(npc.gameObject);
            yield return null;
        }
        foreach (var actionId in new[] { PNAgentToolkitContent.Hop, PNAgentToolkitContent.ContactMotion })
        {
            var temporary = worlds.Create(EveId.Parse("project_n:enemy_review_a"));
            yield return Until(() => temporary.CanExecute, 10, "L02 temporary world ready");
            var npc = Enemy(temporary, new Vector3(-4, .02f, -4));
            yield return Until(() => npc.ActiveBehaviorId == "probe", 3, "L02 unload actor ready");
            var oldContext = npc.GetComponent<PNEnemyReviewProbeBehavior>().Context;
            var adapter = new PNEnemyAgentContext(oldContext, _ => true);
            var point = adapter.FindPoint(new PNPointQuerySettings(new Vector3(-2, 0, -4), 0, 0, candidates: 1)); point.Tick();
            oldContext.TryCast(actionId, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt);
            npc.GetComponent<EveTimeScale>().AddOwnedModifier(EveId.Parse("review:unload_freeze"), EveTimeModifierKind.Scale, -1, this);
            var npcRoot = npc.gameObject;
            npc.GetComponent<EveEntityLoader>().Unload(); npc.GetComponent<EveEntityIdentity>().ResetIdentity();
            yield return Until(() => worlds.Unload(temporary), 10, "L02 unload frozen motion with point callback pending " + actionId);
            point.Tick(); yield return null;
            Check(receipt.IsTerminal && !oldContext.IsCurrent && !adapter.IsCurrent && point.IsTerminal && PNWorldNavigation.Find(temporary) == null,
                "L02 stale motion/context/query cannot mutate retired world " + actionId);
            point.Dispose(); actors.Remove(npcRoot); if (npcRoot != null) Destroy(npcRoot);
        }
        yield return FollowupDisconnected();
        // Same reusable ID, a new loader generation must never revive a previous handle.
        var reusable = Target(a, new Vector3(-3, 0, -2)); yield return Until(() => reusable.IsReady, 3, "L03 reusable target published");
        var old = PNActorHandle.Capture(reusable); var loader = reusable.GetComponent<EveEntityLoader>();
        loader.Unload(); loader.Load(session.Registry); yield return Until(() => reusable.IsReady, 3, "L03 same identity republished");
        Check(!old.TryResolve(out _) && PNActorHandle.Capture(reusable).TryResolve(out _), "L03 stale handle rejected after same EntityId reload");
        // Game policy changes are evaluated by adapter, without implicit faction polling.
        bool hostile = true;
        var agent = new PNEnemyAgentContext(context, stat => stat == reusable && hostile);
        using (var sensor = new PNAgentPerception(agent, new PNPerceptionSettings(interval: .02f, reactionDelay: 0)))
        {
            float deadline = Time.realtimeSinceStartup + 3;
            while (sensor.NearestVisible() == null && Time.realtimeSinceStartup < deadline) { sensor.Tick(Time.deltaTime); yield return null; }
            Check(sensor.NearestVisible() != null && agent.CanTarget(reusable), "R01 hostile relation admits observed target");
            hostile = false; sensor.Tick(.04f);
            Check(!agent.CanTarget(reusable) && sensor.NearestVisible() == null, "R01 changed relation cannot select old observed hostile target");
        }
        var observed = new List<PNStat>(); var visited = new HashSet<EveEntityId>();
        for (int i = 0; i < 6; i++) observed.Add(Target(a, new Vector3(-3.5f + (i % 3) * .45f, 0, -3 + (i / 3) * .6f)));
        yield return Until(() => observed.All(stat => stat.IsReady), 3, "R03 bounded-memory targets ready");
        var fairAgent = new PNEnemyAgentContext(context, observed.Contains);
        using (var sensor = new PNAgentPerception(fairAgent, new PNPerceptionSettings(interval: .02f, reactionDelay: 0, candidateBudget: 2, sightBudget: 1, memoryCapacity: 2)))
        {
            float until = Time.realtimeSinceStartup + 3; bool capped = true;
            while (Time.realtimeSinceStartup < until)
            {
                sensor.Tick(Time.deltaTime);
                foreach (var record in sensor.Records) if (record.Visible) visited.Add(record.Actor.Id);
                capped &= sensor.Records.Count() <= 2; yield return null;
            }
            Check(capped && visited.Count == 6 && sensor.Evictions >= 4, "R03 cap2 and bounded candidate/sight cursor serve all6 targets despite memory eviction");
        }
        foreach (var actor in observed)
        { actor.GetComponent<EveEntityLoader>().Unload(); actor.GetComponent<EveEntityIdentity>().ResetIdentity(); actors.Remove(actor.gameObject); Destroy(actor.gameObject); }
        // Explicit retire/rejoin is the Game-owned response to affinity changes.
        var peer = Enemy(a, new Vector3(-3, .02f, -2)); yield return Until(() => peer.ActiveBehaviorId == "probe", 3, "R02 peer published");
        var peerAgent = new PNEnemyAgentContext(peer.GetComponent<PNEnemyReviewProbeBehavior>().Context, _ => true);
        using (var groups = new PNAgentGroupService(session))
        {
            var first = groups.Join(agent, EveId.Parse("review:group"), "a", (_, __) => true, 1);
            var second = groups.Join(peerAgent, EveId.Parse("review:group"), "a", (_, __) => true);
            using (var reservation = first.Reserve("role", ttl: .1f))
            using (var hold = a.AcquireLease("followup-pause"))
            {
                a.SetPaused(true); double now = a.GameplayTime; yield return new WaitForSeconds(.15f); groups.Tick();
                Check(a.GameplayTime == now && reservation.IsCurrent, "R02 reservation TTL pauses with world clock"); a.SetPaused(false);
                yield return new WaitForSeconds(.15f); groups.Tick(); Check(!reservation.IsCurrent, "R02 reservation expires after thaw");
            }
            first.Send(PNAgentSignalKind.Alert, agent.Self.Position); first.Dispose(); yield return null; groups.Tick();
            Check(second.Leader.SameActor(peerAgent.Self) && second.Drain(_ => { }) == 0, "R02 retired leader signals rejected and successor selected");
            second.Dispose(); groups.Tick(); Check(groups.GroupCount == 0, "R02 explicit affinity retirement releases old group");
            using (var rejoined = groups.Join(peerAgent, EveId.Parse("review:group"), "new-affinity", (_, __) => true))
                Check(rejoined.IsCurrent, "R02 explicit new-affinity rejoin accepted");
        }
        foreach (var actor in new[] { reusable.gameObject, peer.gameObject })
        { actor.GetComponent<EveEntityLoader>().Unload(); actor.GetComponent<EveEntityIdentity>().ResetIdentity(); actors.Remove(actor); Destroy(actor); }
        yield return null;
        Reject(() => context.TryCast(PNAgentToolkitContent.Orbit, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline), "O03 missing snapshot orbit anchor rejects without leaked lease");
        Check(PNEnemyFollowupTrajectory.Starts > 0 && !move.HasActiveMotion && move.CanMove, "O01 selected trajectory override factory executed through base alias");
        Reposition(move, new Vector3(-4, .02f, 0)); target.transform.position = new Vector3(4, 0, 0); Physics.SyncTransforms();
        Check(PNAgentGroupService.ActiveInstances == 0 && PNAgentPerception.ActiveInstances == 0 && enemy.PendingCleanupScopes == 0,
            "F1 extra coverage releases optional scopes before legacy regression");
    }
}
#endif
