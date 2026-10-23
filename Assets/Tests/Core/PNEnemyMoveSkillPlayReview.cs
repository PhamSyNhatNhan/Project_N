#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PNEnemyReviewProbeBehavior : PNEnemyBehavior
{
    public int Ticks, Enters, Exits;
    public override IEnumerable<EveId> SkillIds => new[] { PNEnemyGameplayContent.Strike, PNEnemyGameplayContent.Charge,
        PNAgentToolkitContent.Curve, PNAgentToolkitContent.Hop, PNAgentToolkitContent.Orbit, PNAgentToolkitContent.ContactMotion,
        EveId.Parse("project_n:enemy_review_empty"), EveId.Parse("project_n:enemy_review_start_failure") }.Concat(PNEnemyFollowupContent.Ids);
    public override void Enter() { Enters++; }
    public override void Tick(float delta) { Ticks++; }
    public override void Exit(PNEnemyBehaviorExit reason) { Exits++; }
}

public sealed class PNEnemyReviewDamageAction : PNDamageAction
{
    public static int Starts;
    public override void Start(PNSkillActionContext context) { Starts++; base.Start(context); }
}
public sealed class PNEnemyReviewThrowAction : PNSkillAction
{
    private PNControlLease lease;
    public override void Start(PNSkillActionContext context)
    { context.BeginAction(); lease = context.Skill.Controls.Acquire("Intentional partial start", new[] { PNControlIds.Move }); throw new InvalidOperationException("Intentional enemy Start failure"); }
    public override void Perform(PNSkillActionContext context) { throw new InvalidOperationException("Failed Start cannot perform."); }
    public override void Exit(PNSkillActionContext context) { context.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext context) { context.CancelEffects(); Dispose(); }
    public override void Dispose() { lease?.Dispose(); lease = null; }
}
public sealed class PNEnemyReviewRuntime : EveRuntime
{
    public EveIContentModule ExtraModule;
    [NonSerialized] public EveContentDocument[] Documents;
    public override string RulesVersion => EveGameRuntime.CurrentRulesVersion;
    protected override void Awake() { } // Configure fixture inputs before the public BeginSession API.
    protected override IEnumerable<EveIContentModule> CreateContentModules()
        => new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), ExtraModule };
    protected override IEnumerable<EveContentDocument> ReadContentDocuments() => Documents;
}

// Fixture only. Uses the production world provider, scheduler, loader, physics, Move and Skill paths.
public sealed partial class PNEnemyMoveSkillPlayReview : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartPlayerReview()
    {
#if !UNITY_EDITOR
        var args = Environment.GetCommandLineArgs();
        var destination = args.FirstOrDefault(x => x.StartsWith("--pn-enemy-review="))?.Substring(18);
        if (destination == null) return;
        Finished += (ok, _) => Application.Quit(ok ? 0 : 1);
        StartReview(destination, args.FirstOrDefault(x => x.StartsWith("--pn-toolkit-stage="))?.Substring("--pn-toolkit-stage=".Length) ?? "e5");
#endif
    }
    public static event Action<bool, string> Finished;
    private readonly List<string> evidence = new List<string>();
    private readonly List<GameObject> actors = new List<GameObject>();
    private readonly List<PNNavigationCache> caches = new List<PNNavigationCache>();
    private EveEntityPreparationScheduler scheduler;
    private PNWorldRuntime worlds;
    private EveSessionContext session;
    private PNEnemyReviewRuntime reviewRuntime;
    private EveWorldContext a, b;
    private PNEnemySkill enemy;
    private PNEnemyMove move;
    private PNStat target;
    private int errors, searchErrors;
    private int expectedCleanupErrors;
    private bool injectingCleanupFailure;
    private string output;
    private StreamWriter trace;
    private string stage;
    private long nextEntity = 992100;
    private Camera reviewCamera;
    private readonly List<Material> materials = new List<Material>();
    private int previousFrameRate, previousVSync;
    private float previousTimeScale, nextTrace;
    private bool previousBackground;
    public static void StartReview(string directory, string throughStage = "e5")
    {
        var root = new GameObject("Enemy Move + Skill Review"); DontDestroyOnLoad(root);
        root.AddComponent<PNEnemyMoveSkillPlayReview>().Begin(directory, throughStage);
    }
    private void Begin(string directory, string throughStage)
    {
        output = directory; stage = throughStage; Directory.CreateDirectory(output);
        previousFrameRate = Application.targetFrameRate; previousVSync = QualitySettings.vSyncCount;
        previousTimeScale = Time.timeScale; previousBackground = Application.runInBackground;
        Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0; Time.timeScale = 1; Application.runInBackground = true;
        trace = new StreamWriter(Path.Combine(output, "trace.csv")); trace.WriteLine("time,world,x,y,z,nav,request,behavior,casts,hp");
        Application.logMessageReceived += Logged;
        var cameraObject = new GameObject("Enemy review camera"); cameraObject.transform.SetParent(transform);
        reviewCamera = cameraObject.AddComponent<Camera>(); reviewCamera.orthographic = true; reviewCamera.orthographicSize = 8;
        reviewCamera.transform.position = new Vector3(8, 14, -12); reviewCamera.transform.LookAt(Vector3.zero);
        reviewCamera.clearFlags = CameraClearFlags.SolidColor; reviewCamera.backgroundColor = new Color(.07f, .09f, .13f);
        StartCoroutine(Drive(Run()));
    }
    private void Check(bool ok, string name)
    { evidence.Add((ok ? "PASS " : "FAIL ") + name); if (!ok) throw new Exception(name); }
    private void Reject(Action action, string name)
    { bool rejected = false; try { action(); } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { rejected = true; } Check(rejected, name); }
    private void Logged(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception) return;
        bool search = message.StartsWith("ArgumentOutOfRangeException") && stack.Contains("UnityEditor.Search.SearchDatabase");
        bool injected = injectingCleanupFailure && message.StartsWith("InvalidOperationException: Intentional retained-resource release failure") &&
            stack.Contains("RetainedResource.Dispose") && stack.Contains("PNEnemySkill:Fault");
        if (followupFaultWindow && message.Contains("Intentional followup ") && stack.Contains("PNEnemySkill:Fault"))
        { followupFaultLogs++; File.AppendAllText(Path.Combine(output, "errors.txt"), "EXPECTED_FOLLOWUP_FAULT " + message + "\n" + stack + "\n"); return; }
        if (search) searchErrors++; else if (injected) expectedCleanupErrors++; else errors++;
        File.AppendAllText(Path.Combine(output, "errors.txt"), (search ? "EDITOR_SEARCH " : injected ? "EXPECTED_INJECTED_CLEANUP " : "FEATURE_OR_OTHER ") + message + "\n" + stack + "\n");
    }
    private void Update()
    {
        if (scheduler != null && (session?.IsActive == true || EveRegistrySession.State == EveSessionState.Stopping)) scheduler.AdvanceFrame();
        if (move?.Body != null && trace != null && Time.time >= nextTrace)
        {
            nextTrace = Time.time + .02f;
            var p = move.Body.position;
            trace.WriteLine(FormattableString.Invariant($"{Time.time:F3},{move.GetComponent<EveEntityIdentity>()?.World?.InstanceId},{p.x:F4},{p.y:F4},{p.z:F4},{move.NavigationRequest?.Status},{move.NavigationRequest?.Id},{enemy?.ActiveBehaviorId},{enemy?.CastCount},{target?.GetCurrent(PNContentIds.Health)}"));
        }
    }
    private IEnumerator Drive(IEnumerator routine)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(routine); Exception failure = null;
        while (stack.Count > 0)
        {
            object yielded = null; bool more = false;
            try { more = stack.Peek().MoveNext(); if (more) yielded = stack.Peek().Current; }
            catch (Exception e) { failure = e; break; }
            if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (yielded is IEnumerator nested) stack.Push(nested); else yield return yielded;
        }
        if (failure != null) { evidence.Add("FAIL " + failure); Debug.LogException(failure); }
        Time.timeScale = previousTimeScale;
        foreach (var actor in actors.Where(x => x != null))
        { try { actor.GetComponent<EveEntityLoader>()?.Unload(); actor.GetComponent<EveEntityIdentity>()?.ResetIdentity(); } catch (Exception e) { failure = failure ?? e; evidence.Add("CLEANUP " + e); } Destroy(actor); }
        actors.Clear(); enemy = null; move = null; target = null;
        try { reviewRuntime?.EndSession(); } catch (Exception e) { failure = failure ?? e; evidence.Add("STOP " + e); }
        float deadline = Time.realtimeSinceStartup + 10;
        while (EveRegistrySession.State != EveSessionState.Stopped && Time.realtimeSinceStartup < deadline)
        { EveRegistrySession.PumpCleanup(); yield return null; }
        try { if (EveRegistrySession.State == EveSessionState.Stopped) reviewRuntime?.UnloadContent(); } catch (Exception e) { failure = failure ?? e; evidence.Add("CONTENT CLEANUP " + e); }
        foreach (var cache in caches) if (cache != null) Destroy(cache);
        foreach (var material in materials) if (material != null) Destroy(material);
        trace?.Dispose(); trace = null; Application.logMessageReceived -= Logged;
        Application.targetFrameRate = previousFrameRate; QualitySettings.vSyncCount = previousVSync; Application.runInBackground = previousBackground;
        bool pass = failure == null && errors == 0 && EveRegistrySession.State == EveSessionState.Stopped;
        evidence.Add($"RESULT {(pass ? "PASS" : "FAIL")} checks={evidence.Count(x => x.StartsWith("PASS "))} featureOrOtherErrors={errors} expectedInjectedCleanupErrors={expectedCleanupErrors} editorSearchErrors={searchErrors} session={EveRegistrySession.State}");
        File.WriteAllLines(Path.Combine(output, "results.txt"), evidence);
        Finished?.Invoke(pass, output); Destroy(gameObject);
    }
    private IEnumerator Until(Func<bool> condition, float seconds, string name)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), name);
    }
    private PNEnemySkill Enemy(EveWorldContext world, Vector3 position, bool sentinelPolicy = false, bool supportExample = false, bool packExample = false)
    {
        var root = new GameObject("Sentinel review " + nextEntity); root.layer = 7; root.SetActive(false); actors.Add(root);
        SceneManager.MoveGameObjectToScene(root, world.PhysicsOwner); root.transform.position = position;
        var identity = root.AddComponent<EveEntityIdentity>(); identity.Assign(new EveEntityId(nextEntity++), EveId.Parse("project_n:sentinel"), 0); identity.BindWorld(world, "main");
        root.AddComponent<PNEnemyStat>(); var body = root.AddComponent<Rigidbody>(); body.constraints = RigidbodyConstraints.FreezeRotation;
        var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .2f; capsule.height = 1.4f; capsule.center = Vector3.up * .7f;
        var clock = root.AddComponent<EveTimeScale>(); clock.MinScale = 0;
        Visual(root, new Color(.95f, .55f, .25f));
        var walking = root.AddComponent<PNEnemyMove>(); walking.GroundLayers = 1 << 13; walking.CanJump = false;
        var owner = sentinelPolicy ? (PNEnemySkill)root.AddComponent<PNSentinelSkill>() : root.AddComponent<PNEnemySkill>(); owner.ConfigureInitialBehavior("probe");
        root.AddComponent<PNEnemyReviewProbeBehavior>().ConfigureId("probe");
        root.AddComponent<PNEnemyMeleeBehavior>().ConfigureId("melee");
        root.AddComponent<PNEnemyChargeBehavior>().ConfigureId("charge");
        if (supportExample) root.AddComponent<PNToolkitSupportBehavior>().ConfigureId("support");
        if (packExample) root.AddComponent<PNToolkitPackBehavior>().ConfigureId("pack");
        root.AddComponent<EveEntityLoader>().Load(session.Registry); root.SetActive(true); return owner;
    }
    private PNStat Target(EveWorldContext world, Vector3 position)
    {
        var root = new GameObject("Review target"); root.layer = 6; root.SetActive(false); actors.Add(root);
        SceneManager.MoveGameObjectToScene(root, world.PhysicsOwner); root.transform.position = position;
        var identity = root.AddComponent<EveEntityIdentity>(); identity.Assign(new EveEntityId(nextEntity++), EveId.Parse("project_n:sentinel"), 0); identity.BindWorld(world, "main");
        var collider = root.AddComponent<CapsuleCollider>(); collider.radius = .2f; collider.height = 1.4f; collider.center = Vector3.up * .7f;
        var body = root.AddComponent<Rigidbody>(); body.isKinematic = true;
        Visual(root, new Color(.25f, .75f, 1));
        var stat = root.AddComponent<PNPlayerStat>(); root.AddComponent<EveEntityLoader>().Load(session.Registry); root.SetActive(true); return stat;
    }
    private void Visual(GameObject root, Color color)
    {
        var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule); visual.name = "Review body"; visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.up * .7f; visual.transform.localScale = new Vector3(.4f, .7f, .4f); Destroy(visual.GetComponent<Collider>());
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor", color); materials.Add(material);
        visual.GetComponent<Renderer>().sharedMaterial = material;
    }
    private void Capture(string name)
    {
        var texture = RenderTexture.GetTemporary(960, 640, 24); var previous = RenderTexture.active; var targetTexture = reviewCamera.targetTexture;
        Texture2D image = null;
        try
        {
            reviewCamera.targetTexture = texture; reviewCamera.Render(); RenderTexture.active = texture;
            image = new Texture2D(960, 640, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); image.Apply();
            var pixels = image.GetPixels32(); int visible = pixels.Count(x => x.r > 80 || x.g > 80 || x.b > 100);
            Check(visible > 500, "Rendered scene has visible geometry: " + name);
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        }
        finally { if (image != null) Destroy(image); reviewCamera.targetTexture = targetTexture; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(texture); }
    }
    private void Reposition(PNEnemyMove walking, Vector3 position)
    {
        if (walking.NavigationRequest != null) walking.CancelNavigation(walking.NavigationRequest);
        walking.Body.position = position; walking.transform.position = position; walking.Body.linearVelocity = Vector3.zero; Physics.SyncTransforms();
    }
    private IEnumerator Run()
    {
        Check(Application.isPlaying && EveRegistrySession.State == EveSessionState.Stopped, "Review starts in empty native Play without a running session");
        var cacheA = ScriptableObject.CreateInstance<PNNavigationCache>(); var cacheB = ScriptableObject.CreateInstance<PNNavigationCache>(); caches.Add(cacheA); caches.Add(cacheB);
        // Historical backend bytes are fixture inputs, not shipped map assets. E4 also exercises the production baker.
        var input = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith("--pn-enemy-cache-path="))?.Substring(22);
        if (input == null) input = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../AstarWorldProbe20261022/Worker"));
        if (!Directory.Exists(input)) throw new DirectoryNotFoundException("Supply --pn-enemy-cache-path=<review caches directory>");
        string GeometryHash(string name) => File.Exists(Path.Combine(input, name + ".hash")) ? File.ReadAllText(Path.Combine(input, name + ".hash")) : PNNavigationCache.Hash("review-" + name);
        cacheA.SetBakedData(File.ReadAllBytes(Path.Combine(input, "worldA.bytes")), GeometryHash("worldA"), "ground-small", .25f, 1.5f);
        cacheB.SetBakedData(File.ReadAllBytes(Path.Combine(input, "worldB.bytes")), GeometryHash("worldB"), "ground-small", .25f, 1.5f);
        Reject(() => new PNNavigationSettings { RepathInterval = float.NaN }.Validate(), "E0 invalid navigation tuning rejected");
        var documents = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        if (stage.StartsWith("crowd-", StringComparison.Ordinal)) documents = CrowdDocuments(documents);
        reviewRuntime = gameObject.AddComponent<PNEnemyReviewRuntime>(); reviewRuntime.ExtraModule = new ReviewContent(cacheA, cacheB); reviewRuntime.Documents = documents;
        reviewRuntime.BeginSession(EveSessionRole.Offline); session = EveRegistrySession.Context;
        scheduler = new EveEntityPreparationScheduler(session, PNGameBootstrap.Config.Preparation); worlds = new PNWorldRuntime(session, scheduler);
        gameObject.AddComponent<PNWorldPhysicsLoop>().Bind(session);
        a = worlds.Create(EveId.Parse("project_n:enemy_review_a")); b = worlds.Create(EveId.Parse("project_n:enemy_review_b"));
        yield return Until(() => a.CanExecute && b.CanExecute, 15, "E0 real PNWorldRuntime prepares both local worlds and navigation before Ready");
        Check(a.PhysicsOwner.GetPhysicsScene() != b.PhysicsOwner.GetPhysicsScene(), "E0 worlds have distinct native physics scenes");
        Check(PNWorldNavigation.ForSession(session).GraphCount == 2, "E0 one navigation owner has two graph leases");
        enemy = Enemy(a, new Vector3(-4, .02f, 0)); move = enemy.GetComponent<PNEnemyMove>(); target = Target(a, new Vector3(4, 0, 0));
        yield return Until(() => enemy.ActiveBehaviorId == "probe", 3, "E0 behavior enters through Ready owner tick");
        if (stage.StartsWith("crowd-", StringComparison.Ordinal)) { yield return FollowupCrowd(); yield break; }
        var probe = enemy.GetComponent<PNEnemyReviewProbeBehavior>(); var context = probe.Context;
        Check(probe.Enters == 1 && context.IsCurrent && !enemy.IsExecutingEnemyAction, "E0 active selection context belongs to the live owner");
        var old = context.GoTo(target.transform.position, .4f); int ended = 0; old.Completed += _ => ended++;
        var replacement = context.GoTo(new Vector3(-3, 0, 0), .1f);
        Check(old.Status == PNEnemyNavigationStatus.Cancelled && old.EndReason == PNEnemyNavigationEndReason.Replaced && ended == 1, "E0 replacing a request ends the old handle exactly once");
        Check(!move.CancelNavigation(old) && !move.UpdateGoal(old, Vector3.zero), "E0 stale navigation handle cannot affect replacement");
        move.CancelNavigation(replacement); Check(replacement.IsTerminal, "E0 owner cancellation ends the selected navigation request");
        if (stage == "e0") yield break;
        if (stage == "disconnected") { yield return FollowupDisconnected(); yield break; }
        if (stage == "toolkit1") { yield return ToolkitLocomotion(); yield break; }
        if (stage == "toolkit2") { yield return ToolkitSpatial(); yield break; }
        if (stage == "toolkit345") { yield return ToolkitBehaviors(); yield break; }
        if (stage == "toolkit6") { yield return ToolkitMotion(); yield break; }
        if (stage == "followup") { yield return FollowupCoverage(); }
        if (stage == "toolkit-all" || stage == "followup")
        {
            yield return ToolkitLocomotion();
            enemy.Switch("probe", PNEnemySwitchPolicy.CancelCurrent); yield return null;
            yield return ToolkitSpatial();
            enemy.Switch("probe", PNEnemySwitchPolicy.CancelCurrent); yield return null;
            yield return ToolkitBehaviors();
            enemy.Switch("probe", PNEnemySwitchPolicy.CancelCurrent); yield return null;
            yield return ToolkitMotion();
            enemy.Switch("probe", PNEnemySwitchPolicy.CancelCurrent); yield return null;
            if (stage == "followup") Check(PNEnemyFollowupContact.Starts > 0 && PNEnemyFollowupHeal.Executions > 0,
                "O01 contact Action and heal Operation use selected override factories");
            foreach (var extra in actors.Where(root => root != null && root != enemy.gameObject && root != target.gameObject).ToArray())
            {
                extra.GetComponent<EveEntityLoader>()?.Unload(); extra.GetComponent<EveEntityIdentity>()?.ResetIdentity();
                actors.Remove(extra); Destroy(extra);
            }
            yield return null;
            Check(PNAgentPerception.ActiveInstances == 0 && PNAgentGroupService.ActiveInstances == 0,
                "EB7 toolkit fixtures release optional runtimes before legacy world-retirement checks");
            Reposition(move, new Vector3(-4, .02f, 0)); target.transform.position = new Vector3(4, 0, 0); Physics.SyncTransforms();
        }

        var initialHp = target.GetCurrent(PNContentIds.Health);
        long beforeMeleeCasts = enemy.CastCount;
        enemy.Switch("melee", PNEnemySwitchPolicy.CancelCurrent);
        Reject(() => context.GoTo(Vector3.zero, 0), "E1 retired behavior context cannot issue commands");
        yield return Until(() => enemy.CastCount > beforeMeleeCasts, 15, "E1 real body follows Recast route around wall and performs registered attack");
        Check(target.GetCurrent(PNContentIds.Health) < initialHp && move.Body.position.x > 2 && enemy.Failure == null, "E1 attack damages the live target after reaching its side of the wall");
        Check(PNEnemyReviewDamageAction.Starts > 0, "E1 enemy execution uses the selected registered Action override");
        Capture("e1-chase-attack");
        target.transform.position = new Vector3(4, 0, -3); Physics.SyncTransforms(); long casts = enemy.CastCount;
        yield return Until(() => enemy.CastCount > casts, 10, "E1 target moves: enemy resumes chase and attacks again");
        if (stage == "e1") yield break;

        enemy.Switch("probe", PNEnemySwitchPolicy.CancelCurrent); target.transform.position = new Vector3(100, 0, 0);
        Reposition(move, new Vector3(-4, .02f, -4));
        var incremental = move.GoTo(new Vector3(-1, 0, -4), .1f);
        yield return Until(() => incremental.Status == PNEnemyNavigationStatus.Following, 4, "E2 continuous-goal route ready");
        for (int i = 0; i < 20; i++) { move.UpdateGoal(incremental, new Vector3(-1, 0, -4 + (i + 1) * .04f)); yield return new WaitForSeconds(.03f); }
        Check(incremental.PathAttempts >= 2 && !incremental.IsTerminal, "E2 small goal updates accumulate against the submitted path goal and trigger repath");
        yield return Until(() => incremental.Status == PNEnemyNavigationStatus.Arrived, 4, "E2 continuous-goal request reaches its updated destination without waiting for stuck");
        if (stage == "stream") yield break;
        Reposition(move, new Vector3(-4, .02f, 0));
        var request = move.GoTo(new Vector3(-1, 0, -4), .1f);
        yield return Until(() => request.Status == PNEnemyNavigationStatus.Following, 4, "E2 path becomes ready for control/time checks");
        var controls = enemy.Controls; using (var first = controls.Acquire("review1", new[] { PNControlIds.Move }))
        {
            var second = controls.Acquire("review2", new[] { PNControlIds.Move });
            yield return new WaitForFixedUpdate(); var held = move.Body.position; yield return new WaitForSeconds(.25f);
            Check(Vector3.Distance(held, move.Body.position) < .02f && request.Status == PNEnemyNavigationStatus.Suspended, "E2 move permission suspends navigation without stuck");
            second.Dispose(); Check(!move.CanMove, "E2 releasing one move lease preserves the other denial");
        }
        yield return Until(() => request.Status == PNEnemyNavigationStatus.Arrived, 6, "E2 releasing final move lease repaths and arrives");
        var clock = enemy.GetComponent<EveTimeScale>(); var cooldown = enemy.Abilities.GetCooldown(PNEnemyGameplayContent.Recovery); cooldown.TryConsume();
        long freeze = clock.AddOwnedModifier(EveId.Parse("project_n:enemy_review_freeze"), EveTimeModifierKind.Scale, -1, this);
        var frozen = move.Body.position; double remaining = cooldown.CooldownRemaining; int ticks = probe.Ticks;
        yield return new WaitForSeconds(.3f);
        Check(Vector3.Distance(frozen, move.Body.position) < .02f && Math.Abs(cooldown.CooldownRemaining - remaining) < .001 && probe.Ticks == ticks, "E2 TimeStop freezes body, behavior and Entity cooldown");
        clock.RemoveModifier(freeze);
        Check(!move.TryJump(), "E2 grounded enemy has no ordinary jump locomotion");
        context = probe.Context;
        int beforeSwitchEnters = probe.Enters, beforeSwitchExits = probe.Exits;
        using (controls.Acquire("review-skill-all", new[] { PNControlIds.SkillAll }))
            Check(!context.TryCast(PNEnemyGameplayContent.Charge, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline), "E2 skill_all denies admission without starting motion");
        using (controls.Acquire("review-exact", skillIds: new[] { PNEnemyGameplayContent.Charge }))
            Check(!context.TryCast(PNEnemyGameplayContent.Charge, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline), "E2 exact skill lease denies admission");
        using (controls.Acquire("review-category", new[] { PNControlIds.Skill }))
        {
            Check(!context.TryCast(PNEnemyGameplayContent.Charge, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline), "E2 category permission denies categorized charge");
            Check(context.TryCast(EveId.Parse("project_n:enemy_review_empty"), Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline), "E2 explicit empty category list bypasses category permission");
            enemy.TryInterrupt(EveId.Parse("project_n:review_interrupt"));
        }
        int controlLeases = controls.Snapshot().Count;
        Reject(() => context.TryCast(EveId.Parse("project_n:enemy_review_start_failure"), Vector3.right, PNEnemyActionExecution.Instant), "E2 partial Action Start failure is observable");
        Check(controls.Snapshot().Count == controlLeases && move.NavigationSuspensions == 0 && !enemy.IsExecutingEnemyAction && enemy.PendingCleanupScopes == 0,
            "E2 failed Start releases its Control, Action and navigation resources");
        var left = move.SuspendNavigation(); var right = move.SuspendNavigation();
        left.Dispose(); Check(move.NavigationSuspensions == 1, "E2 releasing one navigation lease keeps the other suspension"); right.Dispose();
        Reposition(move, new Vector3(-4, .02f, -4)); var full = move.GoTo(new Vector3(-.8f, 0, -4), .1f);
        yield return Until(() => full.Status == PNEnemyNavigationStatus.Following, 4, "E2 full speed route ready");
        var beforeFull = move.Body.position; yield return new WaitForSeconds(.35f); float fullTravel = Vector3.Distance(beforeFull, move.Body.position);
        Reposition(move, new Vector3(-4, .02f, -4)); long slow = clock.AddOwnedModifier(EveId.Parse("project_n:review_slow"), EveTimeModifierKind.Scale, -.5f, this);
        var half = move.GoTo(new Vector3(-.8f, 0, -4), .1f); yield return Until(() => half.Status == PNEnemyNavigationStatus.Following, 4, "E2 half speed route ready");
        var beforeHalf = move.Body.position; yield return new WaitForSeconds(.35f); float halfTravel = Vector3.Distance(beforeHalf, move.Body.position);
        Check(fullTravel > .7f && halfTravel / fullTravel > .35f && halfTravel / fullTravel < .65f, "E2 half scale applies once to actual Rigidbody movement"); clock.RemoveModifier(slow);
        long resumeFreeze = clock.AddOwnedModifier(EveId.Parse("project_n:review_resume_freeze"), EveTimeModifierKind.Scale, -1, this);
        yield return new WaitForFixedUpdate();
        var heldWhileFrozen = move.SuspendNavigation(); clock.RemoveModifier(resumeFreeze); var heldResumePosition = move.Body.position;
        yield return new WaitForSeconds(.15f);
        Check(Vector3.Distance(heldResumePosition, move.Body.position) < .03f, "E2 suspension acquired while frozen does not restore stale walking velocity on thaw"); heldWhileFrozen.Dispose();
        var beforePause = move.Body.position; double cooldownPause = cooldown.CooldownRemaining;
        a.SetPaused(true); yield return new WaitForSeconds(.2f);
        Check(Vector3.Distance(beforePause, move.Body.position) < .02f && Math.Abs(cooldownPause - cooldown.CooldownRemaining) < .001 &&
            !half.IsTerminal && half.Status == PNEnemyNavigationStatus.Suspended, "E2 world pause suspends intent, movement and entity cooldown"); a.SetPaused(false);
        float oldTimeScale = Time.timeScale; beforePause = move.Body.position; Time.timeScale = 0;
        yield return new WaitForSecondsRealtime(.2f);
        Check(Vector3.Distance(beforePause, move.Body.position) < .02f, "E2 global pause stops physical stepping"); Time.timeScale = oldTimeScale;
        Reposition(move, new Vector3(-1.2f, .02f, 0)); var resumeAfterCharge = move.GoTo(new Vector3(-4, 0, 0), .1f);
        Check(context.TryCast(PNEnemyGameplayContent.Charge, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline), "E2 registered charge takes over a live navigation intent");
        yield return Until(() => !enemy.IsExecutingEnemyAction, 3, "E2 blocked charge completes its timeline");
        Check(move.Body.position.x < -.65f && move.NavigationSuspensions == 0, "E2 charge clips against its own world wall and releases takeover");
        yield return Until(() => resumeAfterCharge.Status == PNEnemyNavigationStatus.Arrived, 6, "E2 follower repaths and resumes from the actual post-charge body position");
        Reposition(move, new Vector3(-4, .02f, -4));
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); actors.Add(blocker); SceneManager.MoveGameObjectToScene(blocker, a.PhysicsOwner);
        blocker.transform.position = new Vector3(-2.5f, 1.5f, 0); blocker.transform.localScale = new Vector3(.5f, 3, 12); blocker.layer = 31;
        blocker.GetComponent<Renderer>().enabled = false; Physics.SyncTransforms();
        var stuck = move.GoTo(new Vector3(-1, 0, -4), .1f);
        yield return Until(() => stuck.IsTerminal, 10, "E2 blocked cached route finishes within bounded retry policy");
        Check(stuck.Status == PNEnemyNavigationStatus.Stuck, "E2 no progress becomes Stuck rather than infinite repath"); Destroy(blocker); yield return null;
        Reposition(move, new Vector3(-4, .02f, 0));
        var outside = move.GoTo(new Vector3(100, 0, 0), 0);
        yield return Until(() => outside.IsTerminal, 4, "E2 unreachable goal finishes"); Check(outside.Status == PNEnemyNavigationStatus.NoPath, "E2 endpoint projection does not fake arrival");
        if (stage == "e2") yield break;

        yield return Until(() => cooldown.CanConsume(), 3, "E3 shared cooldown recovers through its existing Ability manager");
        context = probe.Context;
        Check(context.TryCast(PNEnemyGameplayContent.Charge, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline, pay: () => cooldown.TryConsume()), "E3 behavior creates registered motion action");
        var finish = enemy.Switch("melee", PNEnemySwitchPolicy.FinishCurrent);
        Check(finish.Status == PNEnemySwitchStatus.Pending && enemy.IsExecutingEnemyAction, "E3 FinishCurrent retains committed motion");
        Check(enemy.Switch("charge", PNEnemySwitchPolicy.CancelCurrent).Status == PNEnemySwitchStatus.Busy, "E3 a second pending switch returns Busy");
        yield return Until(() => finish.Status == PNEnemySwitchStatus.Applied, 3, "E3 FinishCurrent enters new set after committed action finishes");
        Check(!context.IsCurrent && cooldown.Current == 0 && move.NavigationSuspensions == 0, "E3 old context retired; cooldown persists; motion lease released");
        enemy.Switch("probe", PNEnemySwitchPolicy.CancelCurrent);
        Check(probe.Enters == beforeSwitchEnters + 1 && probe.Exits == beforeSwitchExits + 1, "E3 switching back reuses component but creates a new selection");
        Check(probe.Context.TryCast(PNEnemyGameplayContent.Charge, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline), "E3 second motion starts without duplicated Action ownership");
        var cancel = enemy.Switch("charge", PNEnemySwitchPolicy.CancelCurrent);
        Check(cancel.Status == PNEnemySwitchStatus.Applied && !enemy.IsExecutingEnemyAction && move.NavigationSuspensions == 0, "E3 CancelCurrent disposes motion before entering next set");
        Capture("e3-phase-motion");
        if (stage == "e3") yield break;

        enemy.Switch("probe", PNEnemySwitchPolicy.CancelCurrent);
        var secondEnemy = Enemy(b, new Vector3(-4, .02f, 0)); var secondMove = secondEnemy.GetComponent<PNEnemyMove>();
        yield return Until(() => secondEnemy.ActiveBehaviorId == "probe", 3, "E4 second world enemy enters independently");
        var bRequest = secondMove.GoTo(new Vector3(4, 0, 0), .15f);
        Reposition(move, new Vector3(-4, .02f, 0));
        var aRequest = move.GoTo(new Vector3(4, 0, 0), .15f);
        yield return new WaitForFixedUpdate();
        Check(move.HasPendingNavigationPath, "E4 A has an actual pending worker request at retirement");
        var aId = a.InstanceId; enemy.GetComponent<EveEntityLoader>().Unload(); enemy.GetComponent<EveEntityIdentity>().ResetIdentity(); Destroy(enemy.gameObject); enemy = null; move = null;
        target.GetComponent<EveEntityLoader>().Unload(); target.GetComponent<EveEntityIdentity>().ResetIdentity(); Destroy(target.gameObject); target = null;
        yield return Until(() => worlds.Unload(a), 8, "E4 unload A drains actor/graph resources while B remains live");
        Check(aRequest.IsTerminal && PNWorldNavigation.ForSession(session).GraphCount == 1, "E4 old navigation handle retired and only B graph remains");
        yield return Until(() => bRequest.Status == PNEnemyNavigationStatus.Arrived, 12, "E4 B follows its opposite wall opening after A unloads");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            a = worlds.Create(EveId.Parse("project_n:enemy_review_a"), aId, (ulong)(cycle + 2));
            yield return Until(() => a.CanExecute, 8, "E4 reload A incarnation " + a.Incarnation);
            Check(PNWorldNavigation.ForSession(session).GraphCount == 2 && PNWorldNavigation.Find(a).Generation != PNWorldNavigation.Find(b).Generation, "E4 reload has separate graph lease generation");
            yield return Until(() => worlds.Unload(a), 8, "E4 unload reload cycle " + cycle);
        }
        a = worlds.Create(EveId.Parse("project_n:enemy_review_a"), aId, 5);
        var twin = worlds.Create(EveId.Parse("project_n:enemy_review_a"));
        yield return Until(() => a.CanExecute && twin.CanExecute, 8, "E4 two simultaneous instances load the exact same cache");
        Check(PNWorldNavigation.ForSession(session).GraphCount == 3 && PNWorldNavigation.Find(a).Generation != PNWorldNavigation.Find(twin).Generation, "E4 same-cache instances own independent graph leases");
        yield return Until(() => worlds.Unload(a), 8, "E4 unloading one same-cache instance retains the other");
        var twinEnemy = Enemy(twin, new Vector3(-4, .02f, 0));
        yield return Until(() => twinEnemy.ActiveBehaviorId == "probe", 3, "E4 same-cache surviving actor ready");
        var twinPath = twinEnemy.GetComponent<PNEnemyMove>().GoTo(new Vector3(4, 0, 0), .15f);
        yield return Until(() => twinPath.Status == PNEnemyNavigationStatus.Arrived, 10, "E4 surviving same-cache instance still navigates through its own wall opening");
        // Public membership transaction models an actor already admitted by a mode's transfer policy.
        // This does not add enemy transfer policy to any existing mode.
        var identity = twinEnemy.GetComponent<EveEntityIdentity>(); var staleContext = twinEnemy.GetComponent<PNEnemyReviewProbeBehavior>().Context;
        var transferCooldown = twinEnemy.Abilities.GetCooldown(PNEnemyGameplayContent.Recovery); transferCooldown.TryConsume(); double beforeTransfer = transferCooldown.CooldownRemaining;
        using (identity.HoldGameplay())
        {
            twinEnemy.CancelWorldExecutions(); twinEnemy.GetComponent<PNEnemyMove>().CancelWorldMotion();
            using (var membership = identity.PrepareWorldChange(b, "main")) { membership.Apply(); membership.Commit(); }
            twinEnemy.GetComponent<Rigidbody>().position = new Vector3(-4, .02f, 0); twinEnemy.transform.position = new Vector3(-4, .02f, 0);
        }
        yield return Until(() => twinEnemy.ActiveBehaviorId == "probe", 3, "E4 transferred enemy re-enters an admitted behavior in destination world");
        Check(!staleContext.IsCurrent && transferCooldown.CooldownRemaining <= beforeTransfer && transferCooldown.Current == 0, "E4 transfer retires old context while preserving entity cooldown");
        var transferPath = twinEnemy.GetComponent<PNEnemyMove>().GoTo(new Vector3(4, 0, 0), .15f);
        yield return Until(() => transferPath.Status == PNEnemyNavigationStatus.Arrived, 10, "E4 transferred enemy repaths using destination graph and physics");
        Reject(() => PNWorldNavigation.ForSession(session).Load(b, cacheA, "wrong"), "E4 duplicate/late navigation acquisition rejected");
        var broken = worlds.Create(EveId.Parse("project_n:enemy_review_broken"));
        yield return Until(() => broken.State == EveWorldState.Failed, 5, "E4 stale cache declaration fails world preparation");
        Check(PNWorldNavigation.ForSession(session).GraphCount == 2, "E4 failed cache declaration leaks no graph");
        yield return Until(() => worlds.Unload(broken), 6, "E4 failed preparation cleans owned scene/resources");
        var capacityWorlds = new List<EveWorldContext>();
        int availableWorlds = session.Worlds.MaximumResidents - session.Worlds.Snapshot().Count;
        for (int i = 0; i < availableWorlds; i++) capacityWorlds.Add(worlds.Create(EveId.Parse("project_n:enemy_review_a")));
        yield return Until(() => capacityWorlds.All(x => x.CanExecute), 15, "E4 fills the existing world directory capacity");
        Reject(() => worlds.Create(EveId.Parse("project_n:enemy_review_a")), "E4 world directory rejects overflow before navigation acquisition");
        Check(PNWorldNavigation.ForSession(session).GraphCount == session.Worlds.MaximumResidents && b.CanExecute && twin.CanExecute, "E4 capacity rejection preserves resident graphs/worlds");
        foreach (var capacityWorld in capacityWorlds)
            yield return Until(() => worlds.Unload(capacityWorld), 6, "E4 release capacity world");
        Check(PNWorldNavigation.ForSession(session).GraphCount == 2, "E4 capacity exercise returns to graph baseline");
        if (stage == "e4") yield break;
        var phaseEnemy = Enemy(b, new Vector3(-4, .02f, -4), true);
        yield return Until(() => phaseEnemy.ActiveBehaviorId == "probe", 3, "E5 concrete Sentinel phase owner ready");
        phaseEnemy.Switch("melee", PNEnemySwitchPolicy.CancelCurrent);
        phaseEnemy.GetComponent<PNStat>().SetCurrent(PNContentIds.Health, 40000);
        yield return Until(() => phaseEnemy.ActiveBehaviorId == "charge", 3, "E5 concrete HP phase policy selects the charge subcomponent");
        phaseEnemy.Switch("probe", PNEnemySwitchPolicy.CancelCurrent);
        var phaseProbe = phaseEnemy.GetComponent<PNEnemyReviewProbeBehavior>();
        Check(phaseProbe.Context.TryCast(PNEnemyGameplayContent.Charge, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline), "E5 disable fixture starts a motion");
        var disabledContext = phaseProbe.Context; phaseEnemy.enabled = false;
        Check(!phaseEnemy.IsExecutingEnemyAction && phaseEnemy.GetComponent<PNEnemyMove>().NavigationSuspensions == 0 && !disabledContext.IsCurrent, "E5 disabling owner retires motion, context and lease");
        phaseEnemy.enabled = true; yield return Until(() => phaseEnemy.ActiveBehaviorId == "probe", 3, "E5 owner re-admits behavior after enable");
        var retained = phaseProbe.Context.Own(new RetainedResource());
        injectingCleanupFailure = true;
        var failedSwitch = phaseEnemy.Switch("charge", PNEnemySwitchPolicy.CancelCurrent);
        injectingCleanupFailure = false;
        retained.AllowRelease = true; // Always permit final teardown, even if the assertion below fails.
        Check(failedSwitch.Status == PNEnemySwitchStatus.Failed && phaseEnemy.Failure != null && phaseEnemy.PendingCleanupScopes > 0 && expectedCleanupErrors == 1,
            "E5 injected cleanup failure is reported and retains owned resource for retry");
        phaseEnemy.GetComponent<EveEntityLoader>().Unload();
        Check(phaseEnemy.PendingCleanupScopes == 0 && retained.Releases == 1, "E5 retry drains retained cleanup exactly once");
        phaseEnemy.GetComponent<EveEntityIdentity>().ResetIdentity(); Destroy(phaseEnemy.gameObject);
        var deathEnemy = Enemy(b, new Vector3(-4, .02f, -4));
        yield return Until(() => deathEnemy.ActiveBehaviorId == "probe", 3, "E5 death fixture ready");
        Check(deathEnemy.GetComponent<PNEnemyReviewProbeBehavior>().Context.TryCast(PNEnemyGameplayContent.Charge, Vector3.right, PNEnemyActionExecution.AuxiliaryTimeline), "E5 death fixture starts registered motion");
        deathEnemy.GetComponent<EveTimeScale>().AddOwnedModifier(EveId.Parse("project_n:review_death_freeze"), EveTimeModifierKind.Scale, -1, this);
        var deathStat = deathEnemy.GetComponent<PNStat>(); deathStat.ReceiveDamage(deathStat.CreateDamage(200000, false, 0, EveId.Parse("base:physical")));
        Check(deathStat.IsDead && !deathEnemy.IsExecutingEnemyAction && deathEnemy.GetComponent<PNEnemyMove>().NavigationSuspensions == 0, "E5 death at time zero cancels execution and releases its lease immediately");
        deathEnemy.GetComponent<EveEntityLoader>().Unload(); deathEnemy.GetComponent<EveEntityIdentity>().ResetIdentity(); Destroy(deathEnemy.gameObject); yield return null;
        foreach (int population in new[] { 1, 8, 16 })
        {
            var crowd = new List<PNEnemySkill>();
            for (int i = 0; i < population; i++) crowd.Add(Enemy(b, new Vector3(-4 + (i % 4) * .65f, .02f, -4 + (i / 4) * .65f)));
            yield return Until(() => crowd.All(x => x.ActiveBehaviorId == "probe"), 4, "E5 crowd " + population + " admitted");
            foreach (var npc in crowd) npc.GetComponent<PNEnemyMove>().GoTo(new Vector3(4, 0, 4), .5f);
            yield return new WaitForSeconds(1);
            Check(crowd.All(x => x.Failure == null), "E5 crowd " + population + " has no owner faults (observation, no RVO/performance claim)");
            File.AppendAllText(Path.Combine(output, "crowd.txt"), "population=" + population + " statuses=" + string.Join(",", crowd.Select(x => x.GetComponent<PNEnemyMove>().NavigationRequest.Status)) + "\n");
            foreach (var npc in crowd) { npc.GetComponent<EveEntityLoader>().Unload(); npc.GetComponent<EveEntityIdentity>().ResetIdentity(); Destroy(npc.gameObject); }
        }
        var oldSession = session; var oldNavigation = PNWorldNavigation.ForSession(session);
        foreach (var actor in actors.Where(x => x != null))
        { actor.GetComponent<EveEntityLoader>()?.Unload(); actor.GetComponent<EveEntityIdentity>()?.ResetIdentity(); Destroy(actor); }
        actors.Clear(); reviewRuntime.EndSession();
        yield return Until(() => { EveRegistrySession.PumpCleanup(); return EveRegistrySession.State == EveSessionState.Stopped; }, 10, "E5 authoritative play stops and drains all world resources");
        Check(oldNavigation.GraphCount == 0 && oldNavigation.PendingRequests == 0 && global::AstarPath.active == null, "E5 no graph/request/singleton survives play stop");
        reviewRuntime.BeginSession(EveSessionRole.Client); session = EveRegistrySession.Context;
        scheduler = new EveEntityPreparationScheduler(session, PNGameBootstrap.Config.Preparation); worlds = new PNWorldRuntime(session, scheduler); GetComponent<PNWorldPhysicsLoop>().Bind(session);
        var replicaWorld = worlds.CreateReplica(EveId.Parse("project_n:enemy_review_a"), Guid.NewGuid(), 1, 1, false);
        yield return Until(() => replicaWorld.CanExecute, 8, "E5 client replica world loads its content without creating Astar");
        var clientEnemy = Enemy(replicaWorld, new Vector3(-4, .02f, 0)); yield return new WaitForSeconds(.2f);
        Check(clientEnemy.ActiveBehaviorId == null && clientEnemy.CastCount == 0 && global::AstarPath.active == null, "E5 client actor does not tick AI, execute damage or create navigation worker");
        Reject(() => clientEnemy.GetComponent<PNEnemyMove>().GoTo(Vector3.zero, 0), "E5 client cannot request authoritative navigation");
        clientEnemy.GetComponent<EveEntityLoader>().Unload(); clientEnemy.GetComponent<EveEntityIdentity>().ResetIdentity(); Destroy(clientEnemy.gameObject); actors.Clear(); reviewRuntime.EndSession();
        yield return Until(() => { EveRegistrySession.PumpCleanup(); return EveRegistrySession.State == EveSessionState.Stopped; }, 10, "E5 client fixture stops cleanly (authority gate only, no network claim)");
        reviewRuntime.BeginSession(EveSessionRole.Offline); session = EveRegistrySession.Context;
        scheduler = new EveEntityPreparationScheduler(session, PNGameBootstrap.Config.Preparation); worlds = new PNWorldRuntime(session, scheduler); GetComponent<PNWorldPhysicsLoop>().Bind(session);
        var restarted = worlds.Create(EveId.Parse("project_n:enemy_review_a"));
        yield return Until(() => restarted.CanExecute, 8, "E5 new offline play recreates navigation after prior stop");
        Check(session.SessionId != oldSession.SessionId && !ReferenceEquals(oldNavigation, PNWorldNavigation.ForSession(session)) && PNWorldNavigation.ForSession(session).GraphCount == 1, "E5 restarted play has a fresh owner and exactly one graph");
        Check(errors == 0, "E5 no feature/other managed errors through native integration scenario");
    }
    private sealed class RetainedResource : IDisposable
    {
        public bool AllowRelease; public int Releases;
        public void Dispose() { if (!AllowRelease) throw new InvalidOperationException("Intentional retained-resource release failure"); Releases++; }
    }
    private sealed class ReviewContent : EveIContentModule
    {
        private readonly PNNavigationCache a, b;
        public ReviewContent(PNNavigationCache a, PNNavigationCache b) { this.a = a; this.b = b; }
        public void Register(EveRegistryHub hub)
        {
            PNEnemyFollowupContent.Register(hub);
            var selectedDamage = EveId.Parse("enemy_review:damage_action");
            hub.Actions().Register(new PNSkillActionDefinition(selectedDamage, typeof(PNEnemyReviewDamageAction), null, PNSkillContextRequirements.Targets));
            hub.Actions().RequestOverride(EveId.Parse("base:damage_action"), selectedDamage, "enemy_review", "1.0.0", new[] { "project_n" });
            hub.Actions().SelectOverride(EveId.Parse("base:damage_action"), "enemy_review");
            hub.Actions().Register(new PNSkillActionDefinition(EveId.Parse("project_n:enemy_review_throw_action"), typeof(PNEnemyReviewThrowAction)));
            hub.Skills.Register(new EveSkillDefinition(EveId.Parse("project_n:enemy_review_start_failure"), Newtonsoft.Json.Linq.JObject.Parse(@"{
              'categoryIds':[],'affectedByAttackSpeed':false,
              'action':{'id':'project_n:enemy_review_throw_action','start':{},'perform':{},'exit':{},'cancel':{}}
            }")));
            hub.Skills.Register(new EveSkillDefinition(EveId.Parse("project_n:enemy_review_empty"), Newtonsoft.Json.Linq.JObject.Parse(@"{
              'categoryIds':[],'affectedByAttackSpeed':false,
              'action':{'id':'base:motion_action','start':{},'perform':{'distance':0.2,'duration':0.1,'ignoreEntities':false,'permissionId':'base:motion_voluntary'},'exit':{},'cancel':{}}
            }")));
            foreach (var upper in new[] { true, false })
            {
                var cache = upper ? a : b; var id = EveId.Parse(upper ? "project_n:enemy_review_a" : "project_n:enemy_review_b");
                hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(id, PNNavigationCache.Hash("review/" + cache.Fingerprint), typeof(ReviewProvider), () => new ReviewProvider(cache, upper)));
            }
            hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(EveId.Parse("project_n:enemy_review_broken"), PNNavigationCache.Hash("broken-review"), typeof(ReviewProvider), () => new ReviewProvider(a, true, true)));
        }
    }
    private sealed class ReviewProvider : IPNWorldProvider
    {
        private readonly PNNavigationCache cache; private readonly bool upper, broken;
        public ReviewProvider(PNNavigationCache cache, bool upper, bool broken = false) { this.cache = cache; this.upper = upper; this.broken = broken; }
        public IEnumerator Load(PNWorldBuildContext build)
        {
            var root = new GameObject("Review map"); SceneManager.MoveGameObjectToScene(root, build.PhysicsScene); build.Own(() => { if (root != null) Destroy(root); });
            Box("Floor", new Vector3(0, -.5f, 0), new Vector3(12, 1, 12), 13);
            if (!Environment.GetCommandLineArgs().Contains("--pn-followup-open"))
                Box("Wall", new Vector3(0, 1, upper ? -1.5f : 1.5f), new Vector3(1, 2, 9), 31);
            root.AddComponent<PNWorldNavigationSource>().Configure(cache, broken ? "stale-fingerprint" : cache.Fingerprint);
            build.AddSlot("main", build.PhysicsScene, new[] { root }); yield break;
            void Box(string name, Vector3 position, Vector3 scale, int layer)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name; box.transform.SetParent(root.transform); box.transform.position = position; box.transform.localScale = scale; box.layer = layer;
                box.GetComponent<Renderer>().enabled = upper;
                var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor", layer == 13 ? new Color(.17f, .21f, .28f) : new Color(.42f, .49f, .62f));
                box.GetComponent<Renderer>().sharedMaterial = material; build.Own(() => { if (material != null) Destroy(material); });
            }
        }
    }
}
#endif
