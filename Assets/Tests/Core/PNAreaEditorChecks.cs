#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

public class PNAreaSelectedProbe : PNTimeScaleArea { }
public class PNAreaFailureProbe : PNAreaInstance
{
    public static int Cleanups;
    protected override void OnStart(PNAreaCreateRequest request) { throw new InvalidOperationException("Intentional failed initialization"); }
    protected override void OnPerform(float delta) { }
    protected override void Cleanup() { Cleanups++; }
}
// Records the order of the hooks (Docs/InstanceLifecyclePlan.md 3.1); `Duration` is the Ending it asks for, `FailEnd` makes OnEnd throw.
public class PNAreaLifeProbe : PNAreaInstance
{
    public static readonly System.Collections.Generic.List<string> Events = new System.Collections.Generic.List<string>();
    public static float Duration = .5f, EndingSum; public static bool FailEnd; public static int Performs, Cleanups;
    public override float EndingDuration => Duration;
    protected override void OnStart(PNAreaCreateRequest request) { Events.Add("start"); }
    protected override void OnPerform(float delta) { Performs++; }
    protected override void OnEnd(PNAreaEndReason reason) { Events.Add("end:" + reason); if (FailEnd) throw new InvalidOperationException("Intentional failed OnEnd"); }
    protected override void OnEnding(float delta) { EndingSum += delta; }
    protected override void OnCancel(PNAreaEndReason reason) { Events.Add("cancel:" + reason); }
    protected override void Cleanup() { Events.Add("cleanup"); Cleanups++; }
}
// Traits for the area checks: each logs into the life probe's event list so the order against the area's own hooks is visible.
public class PNAreaMarkTrait : PNAreaTrait
{
    protected virtual string Label => "m1";
    public override void OnCreated(PNAreaTraitContext context) { PNAreaLifeProbe.Events.Add(Label + ":created"); }
    public override void OnEnded(PNAreaTraitContext context, PNAreaEndReason reason) { PNAreaLifeProbe.Events.Add(Label + ":ended:" + reason); }
}
public sealed class PNAreaMarkTwoTrait : PNAreaMarkTrait { protected override string Label => "m2"; }
public sealed class PNAreaNativeModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        hub.Instances().Register(new PNAreaDefinition(EveId.Parse("area_test:life"), typeof(PNAreaLifeProbe), new JObject(), (data, content) => { }, input => { }));
        hub.AreaTraits().Register(new PNAreaTraitDefinition(EveId.Parse("area_test:mark"), typeof(PNAreaMarkTrait), PNAreaTraitDefinition.NoPayload));
        hub.AreaTraits().Register(new PNAreaTraitDefinition(EveId.Parse("area_test:mark2"), typeof(PNAreaMarkTwoTrait), PNAreaTraitDefinition.NoPayload));
        PNAreaTraitUse Use(string id) => new PNAreaTraitUse(EveId.Parse(id));
        hub.Instances().Register(new PNAreaDefinition(EveId.Parse("area_test:traited"), typeof(PNAreaLifeProbe), new JObject(), (data, content) => { }, input => { }, null, new[] { Use("project_n:solizeitastronhringur"), Use("area_test:mark") }));
        hub.Instances().Register(new PNAreaDefinition(EveId.Parse("area_test:traited2"), typeof(PNAreaLifeProbe), new JObject(), (data, content) => { }, input => { }, null, new[] { Use("area_test:mark2"), Use("area_test:mark") }));
        hub.Instances().Register(new PNAreaDefinition(EveId.Parse("area_test:failtrait"), typeof(PNAreaFailureProbe), new JObject(), (data, content) => { }, input => { }, null, new[] { Use("project_n:solizeitastronhringur") }));
        var original = hub.Instances().GetKind<PNAreaDefinition>(EveId.Parse("project_n:timescale_area"));
        var registry = hub.Instances();
        registry.Register(new PNAreaDefinition(EveId.Parse("area_test:selected"), typeof(PNAreaSelectedProbe), original.Payload, PNTimeScaleArea.ValidateDefinition, input => { new PNTimeScaleAreaSettings(input); }));
        registry.RequestOverride(EveId.Parse("base:timescale_area"), EveId.Parse("area_test:selected"), "area_test", "1.0.0", new[] { "project_n" });
        registry.Register(new PNAreaDefinition(EveId.Parse("area_test:fail"), typeof(PNAreaFailureProbe), new JObject(), (data, content) => { }, input => { }));
        var timed = hub.Buffs().Get(EveId.Parse("project_n:timescale_reduction"));
        hub.Buffs().Register(timed.Derive(EveId.Parse("area_test:timed"), timed.Payload, timed.Tags, true, int.MaxValue, PNBuffInstanceScope.Target, true));
    }
}
public static class PNAreaEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var modules = new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNAreaNativeModule() };
        string json = "{'schemaVersion':1,'extensions':{'project_n':{'instances':[{'id':'area_test:json','factoryId':'base:timescale_area','data':{'buffId':'area_test:grant','pollPeriod':0.05,'fadeIn':0.1,'fadeOut':0.3}}],'buffs':[{'id':'area_test:grant','factoryId':'base:timescale_reduction_area','data':{'areaOwned':true},'tags':['base:timescale'],'multipleInstances':true,'maximumInstances':2147483647,'scope':'Target','canDispel':false,'showInUI':false}]}}}";
        var jsonHub = new EveContentBootstrap().Build(modules, docs.Concat(new[] { new EveContentDocument("area same batch", json, "area_test", dependencies: new[] { "project_n" }) }));
        var hub = new EveContentBootstrap().Build(modules, docs,
            new[] { new EveOverrideSelection("instances", EveId.Parse("base:timescale_area"), "area_test") });
        var scene = EditorSceneManager.NewPreviewScene();
        Directory.CreateDirectory(".utmp/AreaReview");
        var evidence = new List<string>(); int checks = 0;
        PNAreaManager areas = null; Material material = null; RenderTexture rt = null; Texture2D image = null; var previous = RenderTexture.active;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; evidence.Add("PASS " + name); }
        PNStat Entity(long id, Vector3 position)
        {
            var go = new GameObject("Area fixture " + id); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene); go.layer = PNLayers.Void; go.transform.position = position;
            go.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:dummy"), 0);
            go.AddComponent<CapsuleCollider>().height = 1; go.AddComponent<EveTimeScale>();
            var stat = go.AddComponent<PNEnemyStat>(); go.AddComponent<EveEntityLoader>().Load(hub); go.SetActive(true); go.GetComponent<PNBuffManager>().Advance(0); return stat;
        }
        JObject Input(double? duration = 2, bool dependent = false, double reduction = .5, double minimum = .2)
            => new JObject { ["radius"] = 2, ["reduction"] = reduction, ["minimumReduction"] = minimum, ["duration"] = duration.HasValue ? new JValue(duration.Value) : JValue.CreateNull(), ["dependent"] = dependent, ["targetLayers"] = new JArray("Void"), ["layerMode"] = "Include" };
        try
        {
            Check(jsonHub.Instances().Get(EveId.Parse("area_test:json")) != null, "Area JSON derivation with same-batch Buff reference");
            bool dependencyRejected = false;
            try { new EveContentBootstrap().Build(modules, docs.Concat(new[] { new EveContentDocument("area no dependency", json, "area_test", dependencies: Array.Empty<string>()) })); }
            catch (FormatException) { dependencyRejected = true; }
            Check(dependencyRejected, "Area extension enforces pack dependencies");
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:area"));
            var root = new GameObject("Area manager fixture"); SceneManager.MoveGameObjectToScene(root, scene); areas = root.AddComponent<PNAreaManager>(); areas.Initialize(EveRegistrySession.Context);
            var source = Entity(990001, new Vector3(20, 0, 0)); var target = Entity(990002, Vector3.zero);
            var buffs = target.GetComponent<PNBuffManager>().Buffs; var clock = target.GetComponent<EveTimeScale>(); var capture = PNCombatSource.Capture(source);
            Check(hub.Instances().ResolveIdentity(EveId.Parse("base:timescale_area")) == EveId.Parse("project_n:timescale_area"), "Area base alias");
            var bad = Input(); bad["radius"] = -1;
            Check(areas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), bad, capture, scene, Vector3.zero)).Status == PNAreaStatus.Failed && areas.PooledObjectCount == 0, "Bad input rejected before borrowing");
            Check(areas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), Input(null, false), capture, scene, Vector3.zero)).Status == PNAreaStatus.Failed, "Untimed independent rejected");
            clock.MinScale = .4f;
            JObject timed = JObject.Parse("{'reduction':1,'minimumReduction':0.2,'duration':0.2}");
            var first = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:timescale_reduction"), capture, timed));
            var second = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:timescale_reduction"), capture, timed));
            Check(first.Handle.IsAlive && second.Handle.IsAlive && !first.Handle.Equals(second.Handle) && clock.Scale == 0, "Independent same-source Buffs and minimum penetration");
            buffs.Remove(first.Handle); Check(Math.Abs(clock.Scale - .2f) < .00001, "Removing one modifier owner preserves the other");
            target.GetComponent<PNBuffManager>().Advance(.2); Check(!second.Handle.IsAlive && Math.Abs(clock.Scale - 1) < .00001, "Game duration expires at entity zero");
            clock.MinScale = 0;
            PNAreaReceipt Spawn(JObject data = null, PNStat owner = null, PNStat follow = null)
            {
                var receipt = areas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), data ?? Input(), capture, scene, Vector3.zero, owner, follow));
                Check(receipt.Status == PNAreaStatus.Created, "Area create success"); return receipt;
            }
            var a = Spawn(); Check(Math.Abs(clock.Scale - .5f) < .00001, "Immediate body admission");
            Check(areas.Get(a.Handle).ProfileId == EveId.Parse("area_test:selected") && scene.GetRootGameObjects().Any(x => x.GetComponent<PNAreaSelectedProbe>() != null), "Selected registry override creates selected pooled component");
            var b = Spawn(); Check(clock.Scale == 0 && buffs.Snapshot().Count == 2, "Overlapping independent area Buffs");
            var snapshot = buffs.Snapshot()[0]; Check(buffs.Remove(snapshot.Handle).Status == PNBuffStatus.Rejected, "Area Buff cannot ordinary dispel");
            buffs.Remove(snapshot.Handle, PNBuffEndReason.Bypass, true); areas.Advance(.051f);
            Check(buffs.Snapshot().Count == 2 && clock.Scale == 0, "Bypass removal regranted on polling");
            a.Handle.End(); Check(!a.Handle.IsAlive && Math.Abs(clock.Scale - .5f) < .00001, "Ending one region releases exact handle immediately");
            b.Handle.End(); areas.Advance(.31f); Check(clock.Scale == 1 && areas.BorrowedCount == 0 && areas.PooledObjectCount == 2, "Fade retirement returns to existing pool");
            var reused = Spawn(); Check(reused.Handle.InstanceId != a.Handle.InstanceId && !a.Handle.End() && areas.PooledObjectCount == 2, "Reuse with stale handle rejection");
            using (buffs.Immune(tags: new[] { EveId.Parse("base:timescale") }, purge: true))
            { areas.Advance(.051f); Check(buffs.Snapshot().Count == 0 && clock.Scale == 1, "Immunity leaves area membership waiting"); }
            areas.Advance(.051f); Check(buffs.Snapshot().Count == 1, "Immunity revoked then area regrants");
            target.gameObject.layer = PNLayers.Boundary; areas.Advance(.051f); Check(buffs.Snapshot().Count == 0, "Dynamic root layer removes Buff"); target.gameObject.layer = PNLayers.Void;
            target.transform.position = new Vector3(2.4f, 0, 0); Physics.SyncTransforms(); areas.Advance(.051f); Check(buffs.Snapshot().Count == 1, "Collider intersection admits center outside sphere");
            target.transform.position = new Vector3(4, 0, 0); Physics.SyncTransforms(); areas.Advance(.051f); Check(buffs.Snapshot().Count == 0, "Exit removes Buff");
            target.transform.position = Vector3.zero; Physics.SyncTransforms();
            reused.Handle.End(); areas.Advance(.31f);
            var excluded = Input(); excluded["layerMode"] = "Exclude"; excluded["targetLayers"] = new JArray("Boundary");
            var exclusion = Spawn(excluded); Check(clock.Scale == .5f, "Exclude mask admits all other root layers"); exclusion.Handle.End(); areas.Advance(.31f);
            var failed = areas.Create(new PNAreaCreateRequest(EveId.Parse("area_test:fail"), new JObject(), capture, scene, Vector3.zero));
            Check(failed.Status == PNAreaStatus.Failed && areas.BorrowedCount == 0 && PNAreaFailureProbe.Cleanups > 0, "Generic implementation initialization failure cleaned and returned");
            // ---- the lifecycle (IL1a): an END gets OnEnd then an Ending then one Cleanup; a CANCEL gets OnCancel and Cleanup at once; a repeat is dropped ----
            int basePooled = areas.PooledObjectCount;
            PNAreaReceipt Life() { PNAreaLifeProbe.Events.Clear(); PNAreaLifeProbe.EndingSum = 0; PNAreaLifeProbe.Duration = .5f; PNAreaLifeProbe.FailEnd = false; PNAreaLifeProbe.Cleanups = 0;
                var made = areas.Create(new PNAreaCreateRequest(EveId.Parse("area_test:life"), new JObject(), capture, scene, Vector3.zero)); Check(made.Status == PNAreaStatus.Created, "Lifecycle probe created"); return made; }
            string Events() => string.Join(",", PNAreaLifeProbe.Events);
            var life = Life(); areas.Advance(.1f);
            Check(life.Handle.IsAlive && areas.Get(life.Handle).IsRunning && PNAreaLifeProbe.Performs > 0, "A running area performs and is alive");
            Check(areas.End(life.Handle, PNAreaEndReason.Requested), "End is accepted");
            Check(!life.Handle.IsAlive && !areas.Get(life.Handle).IsRunning && areas.Get(life.Handle).EndReason == PNAreaEndReason.Requested && areas.BorrowedCount == 1 && Events() == "start,end:Requested", "An END reason leaves Running at once: not alive, reason kept, borrowed until the Ending ends");
            Check(!areas.End(life.Handle, PNAreaEndReason.Expired) && !life.Handle.End() && Events() == "start,end:Requested", "A second END during the Ending is dropped");
            areas.Advance(.3f); Check(Events() == "start,end:Requested" && Math.Abs(PNAreaLifeProbe.EndingSum - .3f) < 1e-4f && areas.BorrowedCount == 1, "The Ending runs OnEnding with the delta and keeps the area");
            areas.Advance(.3f); Check(Events() == "start,end:Requested,cleanup" && PNAreaLifeProbe.Cleanups == 1 && areas.BorrowedCount == 0, "The Ending ends with exactly one Cleanup and the area goes back to the pool");
            life = Life(); areas.End(life.Handle, PNAreaEndReason.Replaced);
            Check(areas.Get(life.Handle).EndReason == PNAreaEndReason.Replaced && Events() == "start,end:Replaced" && !life.Handle.IsAlive, "Replaced is an END reason: an Ending, not a cancel"); areas.Advance(.6f);
            Check(Events() == "start,end:Replaced,cleanup" && areas.BorrowedCount == 0, "A replaced area finishes its Ending");
            life = Life(); areas.End(life.Handle, PNAreaEndReason.Expired); Check(areas.End(life.Handle, PNAreaEndReason.RuntimeError), "A CANCEL during the Ending is accepted");
            Check(Events() == "start,end:Expired,cancel:RuntimeError,cleanup" && areas.BorrowedCount == 0 && PNAreaLifeProbe.Cleanups == 1 && areas.Get(life.Handle) == null, "A CANCEL during the Ending drops the rest of it: OnCancel, one Cleanup, returned");
            foreach (var reason in new[] { PNAreaEndReason.SceneUnloaded, PNAreaEndReason.SessionEnded, PNAreaEndReason.RuntimeError })
            {
                life = Life(); areas.End(life.Handle, reason);
                Check(Events() == "start,cancel:" + reason + ",cleanup" && areas.BorrowedCount == 0 && PNAreaLifeProbe.Cleanups == 1, "A CANCEL (" + reason + ") from Running skips OnEnd and the Ending");
            }
            life = Life(); PNAreaLifeProbe.Duration = 0; areas.End(life.Handle, PNAreaEndReason.Expired);
            Check(Events() == "start,end:Expired,cleanup" && areas.BorrowedCount == 0, "An Ending of zero seconds cleans up at once");
            life = Life(); PNAreaLifeProbe.FailEnd = true; areas.End(life.Handle, PNAreaEndReason.Requested);
            Check(Events() == "start,end:Requested,cancel:RuntimeError,cleanup" && areas.BorrowedCount == 0 && PNAreaLifeProbe.Cleanups == 1, "A fault in OnEnd becomes a RuntimeError cancel with one Cleanup");
            PNAreaLifeProbe.FailEnd = false;
            Check(Enum.IsDefined(typeof(PNAreaEndReason), (int)7) && (int)PNAreaEndReason.Replaced == 7, "Replaced is appended at 7 (the byte of the sync End record)");
            // ---- the traits (IL1b): hooks run for the real area only, in order; solizeitastronhringur lets one area live per world instance ----
            PNAreaReceipt Make(string id, PNCombatSource who = null) => areas.Create(new PNAreaCreateRequest(EveId.Parse(id), new JObject(), who ?? capture, scene, Vector3.zero));
            PNAreaEndReason? Reason(PNAreaReceipt receipt) => areas.Get(receipt.Handle)?.EndReason;
            var rival = PNCombatSource.Capture(target);
            PNAreaLifeProbe.Events.Clear(); PNAreaLifeProbe.Duration = .5f;
            var tA = Make("area_test:traited");
            Check(tA.Status == PNAreaStatus.Created && tA.Handle.IsAlive && Events() == "start,m1:created", "OnCreated runs for each trait in order after the area started");
            var tB = Make("area_test:traited", rival);
            Check(!tA.Handle.IsAlive && Reason(tA) == PNAreaEndReason.Replaced && tB.Handle.IsAlive && Events() == "start,m1:created,start,m1:created,end:Replaced,m1:ended:Replaced",
                "A new area with the trait ends the old one (another owner too) with Replaced: the new is made first, the old ends after, and OnEnded follows the area's own OnEnd");
            areas.Advance(.6f); Check(areas.BorrowedCount == 1 && tB.Handle.IsAlive, "The replaced area plays its Ending and goes back to the pool; the new one stays");
            PNAreaLifeProbe.Events.Clear(); areas.End(tB.Handle, PNAreaEndReason.Requested);
            var tC = Make("area_test:traited");
            Check(Reason(tB) == PNAreaEndReason.Requested && !Events().Contains("Replaced") && tC.Handle.IsAlive, "An area already in its Ending is not counted and is not ended again");
            areas.Advance(.6f);
            PNAreaLifeProbe.Events.Clear(); var tD = Make("area_test:traited"); var tE = Make("area_test:traited");
            Check(Reason(tC) == PNAreaEndReason.Replaced && Reason(tD) == PNAreaEndReason.Replaced && tE.Handle.IsAlive, "Two made in a row: the one handled later wins");
            areas.Advance(.6f);
            PNAreaLifeProbe.Events.Clear();
            var broken = Make("area_test:failtrait");
            Check(broken.Status == PNAreaStatus.Failed && tE.Handle.IsAlive && Events() == "", "A creation that fails calls no hook and leaves the old area alone");
            PNAreaLifeProbe.Events.Clear(); areas.End(tE.Handle, PNAreaEndReason.SceneUnloaded);
            Check(Events() == "cancel:SceneUnloaded,cleanup", "A Cancel does not call OnEnded");
            PNAreaLifeProbe.Events.Clear(); PNAreaLifeProbe.Duration = 0; var tF = Make("area_test:traited2"); areas.End(tF.Handle, PNAreaEndReason.Expired);
            Check(Events() == "start,m2:created,m1:created,end:Expired,m2:ended:Expired,m1:ended:Expired,cleanup", "Traits are called in declared order for created and ended");
            PNAreaLifeProbe.Duration = .5f; PNAreaLifeProbe.Events.Clear();
            var real = Make("area_test:traited"); PNAreaLifeProbe.Events.Clear();
            var guess = areas.CreateGuess(PNAreaCreateRequest.Replica(EveId.Parse("area_test:traited"), new JObject(), scene, Vector3.zero, null, null, 1d).WithCast(77, 0));
            Check(guess.Status == PNAreaStatus.Created && real.Handle.IsAlive && Events() == "start", "A guess runs no trait hook and ends nobody");
            areas.DiscardPredicted(77);
            Check(real.Handle.IsAlive && Events() == "start,end:Requested", "A discarded guess ends without OnEnded");
            areas.End(real.Handle, PNAreaEndReason.Requested); areas.Advance(.6f);
            Check(areas.BorrowedCount == 0, "Every trait fixture area went back to the pool");
            int lifePooled = areas.PooledObjectCount - basePooled;
            var timedGrant = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:timescale_reduction"), capture, timed));
            using (buffs.Contribute("test:timescale_override", new PNBuffOverrideContribution(EveId.Parse("area_test:timed"), 1), selection: PNBuffSelection.Current, exact: timedGrant.Handle))
                Check(timedGrant.Handle.IsAlive && Math.Abs(clock.Scale) < .00001, "Buff full override retains valid original Apply input and owns modifiers");
            Check(timedGrant.Handle.IsAlive && Math.Abs(clock.Scale) < .00001, "Revoking full override reacquires original timed modifiers"); buffs.Remove(timedGrant.Handle);
            var dependent = Spawn(Input(null, true), source); source.gameObject.SetActive(false); areas.Advance(0); source.gameObject.SetActive(true);
            Check(!dependent.Handle.IsAlive && clock.Scale == 1, "Owner disable ends permanently after reenable"); areas.Advance(.31f);
            var independent = Spawn(Input(1), source); source.gameObject.SetActive(false); areas.Advance(.1f);
            Check(independent.Handle.IsAlive, "Timed independent survives source disable"); source.gameObject.SetActive(true);
            areas.Advance(.91f); Check(!independent.Handle.IsAlive && clock.Scale == 1, "Independent expires on Game clock"); areas.Advance(.31f);
            var follow = Spawn(Input(2), null, source); areas.Advance(0);
            Check(areas.Get(follow.Handle).Position == source.transform.position, "Area follows explicit target");
            source.GetComponent<EveEntityLoader>().Unload(); source.GetComponent<EveEntityLoader>().Load(hub); source.transform.position += Vector3.right;
            areas.Advance(.051f); var frozen = areas.Get(follow.Handle).Position; source.transform.position += Vector3.right; areas.Advance(.051f);
            Check(areas.Get(follow.Handle).Position == frozen, "Independent follow freezes after owner load lifetime changed"); follow.Handle.End(); areas.Advance(.31f);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                var burst = Enumerable.Range(0, 32).Select(_ => Spawn(Input(1))).ToArray();
                Check(buffs.Snapshot().Count == 32 && clock.Scale == 0, "Burst independent admission cycle " + cycle);
                foreach (var region in burst) region.Handle.End(); areas.Advance(.31f);
                Check(buffs.Snapshot().Count == 0 && clock.Scale == 1 && areas.PooledObjectCount == 33 + lifePooled, "Pool retains repeated burst peak without accumulating grants cycle " + cycle);
            }
            var child = new GameObject("Trigger child"); child.transform.SetParent(target.transform, false); child.AddComponent<SphereCollider>().isTrigger = true;
            var own = Spawn(); Check(buffs.Snapshot().Count == 1, "Trigger child does not duplicate entity");
            areas.Advance(.2f);
            foreach (var region in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeScaleArea>(true))) region.gameObject.layer = PNLayers.Void;
            // High contrast opaque backdrop verifies world/depth mask in actual URP frames.
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor", new Color(.6f, .75f, .65f));
            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(backdrop, scene); backdrop.layer = PNLayers.Void; backdrop.transform.position = new Vector3(0, 0, 3); backdrop.transform.localScale = new Vector3(12, 8, .2f); backdrop.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(backdrop.GetComponent<Collider>());
            var cameraRoot = new GameObject("Area capture camera"); SceneManager.MoveGameObjectToScene(cameraRoot, scene); var camera = cameraRoot.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene; camera.cullingMask = 1 << PNLayers.Void; camera.orthographic = true; camera.orthographicSize = 3.2f; camera.transform.position = new Vector3(0, 0, -8); camera.clearFlags = CameraClearFlags.SolidColor;
            cameraRoot.AddComponent<UniversalAdditionalCameraData>(); rt = new RenderTexture(960, 640, 24); rt.Create(); camera.targetTexture = rt; image = new Texture2D(960, 640, TextureFormat.RGB24, false);
            Directory.CreateDirectory(".utmp/AreaReview");
            void Capture(string name)
            { camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); image.Apply(); File.WriteAllBytes(".utmp/AreaReview/" + name + ".png", image.EncodeToPNG()); }
            Check(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("PNVfx/TimeScaleArea")), "Area shader compiled");
            Capture("Sphere"); var center = image.GetPixel(480, 320); var outside = image.GetPixel(40, 320);
            Check(center.g < outside.g * .9f && center.b > center.g, "Actual sphere localized purple mask");
            var overlap = Spawn(); areas.Advance(.15f); foreach (var region in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeScaleArea>(true))) region.gameObject.layer = PNLayers.Void;
            Capture("Overlap"); var doubled = image.GetPixel(480, 320); Check(Math.Abs(doubled.g - center.g) < .03f, "Identical sphere overlap does not accumulate color");
            var secondRegion = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeScaleArea>(true)).First(x => x.Handle.InstanceId == overlap.Handle.InstanceId);
            secondRegion.transform.position = Vector3.right * 2; Capture("Intersecting");
            Check(Math.Abs(image.GetPixel(675, 320).r - center.r) < .015f, "Sphere union suppresses internal overlap boundary");
            secondRegion.transform.position = Vector3.zero; camera.orthographic = false; camera.transform.position = Vector3.zero; Capture("CameraInside");
            Check(image.GetPixel(480, 320).b > image.GetPixel(480, 320).g, "Camera inside sphere receives volume effect");
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -8);
            var foreground = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(foreground, scene); foreground.layer = PNLayers.Void; foreground.transform.position = new Vector3(0, 0, -4); foreground.transform.localScale = Vector3.one; foreground.GetComponent<Renderer>().sharedMaterial = material;
            Capture("ForegroundOcclusion"); Check(image.GetPixel(480, 320).g > center.g * 1.1f, "Opaque foreground in front of sphere remains unaffected"); UnityEngine.Object.DestroyImmediate(foreground);
            own.Handle.End(); overlap.Handle.End(); areas.Advance(.15f); Capture("Fade"); areas.Advance(.2f);
            Capture("Cleared"); Check(PNTimeScaleAreaRendering.RegisteredCount == 0 && clock.Scale == 1, "All pooled visual registrations retired");
            life = Life(); areas.End(life.Handle, PNAreaEndReason.Expired); areas.Shutdown();
            Check(Events() == "start,end:Expired,cancel:SessionEnded,cleanup" && PNAreaLifeProbe.Cleanups == 1, "A shutdown during the Ending cancels it and cleans up once");
            Check(original.isDirty == dirty, "Original scene dirty state preserved");
            File.WriteAllLines(".utmp/AreaReview/NativeResults.txt", evidence); return "PASS " + checks + " native area checks";
        }
        finally
        {
            areas?.Shutdown(); EveRegistrySession.End(); RenderTexture.active = previous;
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); } if (image != null) UnityEngine.Object.DestroyImmediate(image); if (material != null) UnityEngine.Object.DestroyImmediate(material);
            EditorSceneManager.ClosePreviewScene(scene);
            File.WriteAllLines(".utmp/AreaReview/NativeProgress.txt", evidence);
        }
    }
}
#endif
