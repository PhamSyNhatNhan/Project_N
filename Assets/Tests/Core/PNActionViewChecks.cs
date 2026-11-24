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

// Docs/ObservedViewPlan.md PV1, Edit Mode: the view contract in Core. The catch-up policy (gradual up to 1.5x, seek beyond 0.15 s, one-shot window 0.1 s), the declaration rule, and the engine driving
// a view in the three roles: Authority and Predicted run the Action and its view from the execution frame, an Observed machine runs only the view from the stream progress. The view is a recording probe.
public static class PNActionViewChecks
{
    public static readonly EveId ProbeAction = EveId.Parse("test:view_probe");
    public static readonly EveId ProbeSkill = EveId.Parse("test:view_probe_skill");

    public static readonly EveId LookAction = EveId.Parse("test:look_probe");
    public static readonly EveId LookSkill = EveId.Parse("test:look_probe_skill");
    public static readonly EveId ProbeLookId = EveId.Parse("test:probe_look");
    private sealed class ProbeLook : PNLook
    {
        public static readonly List<ProbeLook> All = new List<ProbeLook>();
        public readonly List<string> Log = new List<string>(); public double Age; public bool Seek; public PNActionViewEnd? Ended; public bool Tail;
        public static bool Block;   // the factory answers null while true (what a look needs does not exist yet)
        public ProbeLook(PNLookBuild build) { All.Add(this); Log.Add(build.Data == null ? "entity" : "run"); }
        public override bool HasTail => Tail;
        public override void Advance(double age, double rate, bool seeking) { Age = age; Seek = seeking; Log.Add("advance"); }
        public override void End(PNActionViewEnd kind) { Ended = kind; Log.Add("end:" + kind); }
    }
    private sealed class ProbeModule : EveIContentModule
    {
        public void Register(EveRegistryHub hub)
        {
            hub.Actions().Register(new PNSkillActionDefinition(ProbeAction, typeof(ProbeActionImpl)).WithView(build => new ProbeView(build)));
            hub.Actions().Register(new PNSkillActionDefinition(LookAction, typeof(ProbeActionImpl)).NoView());
            hub.GetOrCreate<PNLookDefinition>().Register(new PNLookDefinition(ProbeLookId, build => ProbeLook.Block ? null : new ProbeLook(build)));
            hub.GetOrCreate<EveSkillDefinition>().Register(new EveSkillDefinition(LookSkill, JObject.Parse(@"{
              'categoryIds':[],'affectedByAttackSpeed':false,
              'action': {'id':'test:look_probe','start':{},'perform':{'look':'test:probe_look'},'exit':{},'cancel':{}}
            }")));
            hub.GetOrCreate<EveSkillDefinition>().Register(new EveSkillDefinition(ProbeSkill, JObject.Parse(@"{
              'categoryIds':[],'affectedByAttackSpeed':false,
              'action': {'id':'test:view_probe','start':{},'perform':{},'exit':{},'cancel':{}}
            }")));
        }
    }
    private sealed class ProbeActionImpl : PNSkillAction
    {
        public override void Start(PNSkillActionContext context) { }
        public override void Perform(PNSkillActionContext context) { }
        public override void Exit(PNSkillActionContext context) { }
        public override void Cancel(PNSkillActionContext context) { }
    }
    private sealed class ProbeView : IPNActionView
    {
        public static readonly List<ProbeView> All = new List<ProbeView>();
        public readonly List<string> Log = new List<string>();
        public double Age, Rate; public bool LastSeek, Leads = true, Hidden; public PNActionViewEnd? Ended; public double Scale;
        public bool Tail; public double[] LastFact;
        public ProbeView(PNActionViewBuild build) { Scale = build.Scale; All.Add(this); }
        public bool HasTail => Tail;
        public bool Lead { set { Leads = value; } }
        public void Advance(double age, double rate, bool seeking) { Age = age; Rate = rate; LastSeek = seeking; Hidden = false; Log.Add("advance"); }
        public void Fact(string key, double[] values, double age) { Log.Add("fact:" + key); LastFact = values; }
        public void Result(in PNActionViewResult result) => Log.Add("result:" + result.Kind);
        public void Hide() { Hidden = true; Log.Add("hide"); }
        public void End(PNActionViewEnd kind) { Ended = kind; Log.Add("end:" + kind); }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Action view check " + n + ": " + what); n++; }
        bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;

        // ---- the policy (pure) ----
        bool seek;
        Check(Near(PNActionViewPolicy.Follow(0, 1, .016, 1, false, out seek), 1) && seek, "the first placement takes the target and is a seek");
        Check(Near(PNActionViewPolicy.Follow(1, 1.016, .016, 1, true, out seek), 1.016) && !seek, "a normal frame (lag = one step) is on target, no seek");
        Check(Near(PNActionViewPolicy.Follow(1, 1.1, .016, 1, true, out seek), 1.024) && !seek, "a lag within 0.15 s is caught up gradually at 1.5x (age +0.024)");
        Check(Near(PNActionViewPolicy.Follow(1, 1.3, .016, 1, true, out seek), 1.3) && seek, "a lag beyond 0.15 s is a seek: the age is set");
        Check(Near(PNActionViewPolicy.Follow(1, .95, .016, 1, true, out seek), 1) && !seek, "a view slightly ahead holds its age");
        Check(Near(PNActionViewPolicy.Follow(1, .5, .016, 1, true, out seek), .5) && seek, "a view far ahead seeks back");
        Check(Near(PNActionViewPolicy.Follow(1, 1.06, .016, 3, true, out seek), 1.06) && !seek, "a run at rate 3 still follows a lag of its own step (0.048 x 1.5 = 0.072)");
        Check(Near(PNActionViewPolicy.Follow(1, 1.3, 0, 1, true, out seek), 1.3) && seek, "no clock: a far target is a seek");
        Check(Near(PNActionViewPolicy.Follow(1, 1.05, 0, 1, true, out seek), 1.05) && !seek, "no clock: a near target is taken at once, no seek");
        Check(PNActionViewPolicy.DropsFact(1, 1.25, true) && !PNActionViewPolicy.DropsFact(1.2, 1.25, true) && !PNActionViewPolicy.DropsFact(1, 1.25, false),
            "a one-shot fact older than 0.1 s is dropped only on a seek");

        // ---- the declaration rule ----
        var probe = new PNSkillActionDefinition(EveId.Parse("test:declare"), typeof(ProbeActionImpl));
        Check(!probe.ViewDeclared && !probe.HasView, "an Action starts with neither declaration");
        PNActionViewPolicy.ValidateDeclaration(probe, false);
        bool threw = false; try { PNActionViewPolicy.ValidateDeclaration(probe, true); } catch (FormatException) { threw = true; }
        Check(threw, "once required, an Action with neither WithView nor NoView is rejected (mods included)");
        var none = new PNSkillActionDefinition(EveId.Parse("test:none"), typeof(ProbeActionImpl)).NoView();
        Check(none.ViewDeclared && !none.HasView && !none.PublishesRun(new PNSkillData(new JObject(), new PNSkillActionData(EveId.Parse("test:none"), perform: new JObject()))), "NoView declares there is nothing to draw and the run is not published");
        PNActionViewPolicy.ValidateDeclaration(none, true);
        var both = new PNSkillActionDefinition(EveId.Parse("test:both"), typeof(ProbeActionImpl)).WithView(build => new ProbeView(build));
        threw = false; try { both.NoView(); } catch (InvalidOperationException) { threw = true; }
        Check(threw && both.ViewDeclared && both.HasView, "WithView and NoView cannot both be declared");
        threw = false; try { new PNSkillActionDefinition(EveId.Parse("test:both2"), typeof(ProbeActionImpl)).NoView().WithView(build => new ProbeView(build)); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "the other order is refused too");
        Check(PNActionViewPolicy.DeclarationRequired, "since PV8 the declaration is required");

        // ---- looks: the registry entry ----
        threw = false; try { new PNLookDefinition(EveId.Parse("test:a.b"), b => null); } catch (ArgumentException) { threw = true; }
        Check(threw, "a look path must be a valid presentation key");
        threw = false; try { new PNLookDefinition(EveId.Parse("test:" + new string('a', 25)), b => null); } catch (ArgumentException) { threw = true; }
        Check(threw, "a look path longer than the presentation key bound is refused");

        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new ProbeModule() }, docs);
        const long owner = 5, eveId = 995080;
        GameObject Root(Scene preview, string name, long id, long ownerId)
        {
            var obj = new GameObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, preview);
            obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:eve"), ownerId); return obj;
        }
        void Cleanup(Scene preview)
        {
            try
            {
                foreach (var r in preview.GetRootGameObjects()) if (r != null) foreach (var l in r.GetComponentsInChildren<EveEntityLoader>(true)) { try { l.Unload(); } catch (Exception) { } }
                foreach (var r in preview.GetRootGameObjects()) if (r != null) r.SetActive(false);
            }
            finally { try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(preview); } }
        }
        EveClientRoleProbeSkill Spawn(Scene preview, string name, long ownerId)
        {
            var actor = Root(preview, name, eveId, ownerId); actor.layer = PNLayers.Player; actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveClientRoleProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            Physics.SyncTransforms(); return skill;
        }
        var dataId = hub.Skills.ResolveIdentity(ProbeSkill).ToString();
        EveClientRoleProbeSkill.ExtraBuiltIn = new List<EveId> { ProbeSkill, LookSkill };

        // ---- Authority and Predicted: the Action runs and so does its view, from the execution frame ----
        var hostScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:pv1_authority"));
            var areaRoot = new GameObject("PV1 area manager"); SceneManager.MoveGameObjectToScene(areaRoot, hostScene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var skill = Spawn(hostScene, "PV1 authority Eve", owner);
            var data = skill.GetData(ProbeSkill);
            foreach (var mode in new[] { PNSkillMode.Authority, PNSkillMode.Predicted })
            {
                ProbeView.All.Clear(); int started = skill.ActionViewsStarted;
                var context = new PNSkillActionContext(skill, hub.Skills.ResolveIdentity(ProbeSkill), data, hub, null, Vector3.right) { Mode = mode };
                context.BeginAction();
                Check(ProbeView.All.Count == 0 && skill.ActionViewsRunning == 1, mode + ": the view is built when the Action first moves, not at start (scale is committed by then)");
                using (var frame = skill.BeginActionExecutionFrame(.02f)) frame.Advance(context, .02f, 1, null);
                Check(ProbeView.All.Count == 1 && skill.ActionViewsStarted == started + 1, mode + ": the view is built by the engine on the first step");
                var view = ProbeView.All[0];
                Check(Near(view.Age, .02) && view.LastSeek, mode + ": first step places the view at the Action's age");
                for (int i = 0; i < 4; i++) using (var frame = skill.BeginActionExecutionFrame(.02f)) frame.Advance(context, .02f, 1, null);
                Check(Near(view.Age, .1) && !view.LastSeek && skill.ActionViewSeeks == 0, mode + ": later steps advance with the Action, no seek (age " + view.Age + ")");
                context.Present("burst", 1d, 2d);
                Check(view.Log.Contains("fact:burst"), mode + ": context.Present reaches the view at once");
                using (var frame = skill.BeginActionExecutionFrame(.02f)) frame.Hold(context);
                Check(Near(view.Age, .1) && Near(view.Rate, 0), mode + ": a held frame keeps the age, rate 0");
                context.CompleteAction();
                Check(view.Ended == PNActionViewEnd.Completed && skill.ActionViewsRunning == 0, mode + ": completion ends the view as Completed");
                // cancelled halfway
                ProbeView.All.Clear();
                var second = new PNSkillActionContext(skill, hub.Skills.ResolveIdentity(ProbeSkill), data, hub, null, Vector3.right) { Mode = mode };
                second.BeginAction();
                using (var frame = skill.BeginActionExecutionFrame(.02f)) frame.Advance(second, .02f, 1, null);
                second.CancelEffects();
                Check(ProbeView.All.Count == 1 && ProbeView.All[0].Ended == PNActionViewEnd.Cancelled && skill.ActionViewsRunning == 0, mode + ": cancel ends the view as Cancelled, nothing left running");
                // an instant Action still shows once
                ProbeView.All.Clear();
                var instant = new PNSkillActionContext(skill, hub.Skills.ResolveIdentity(ProbeSkill), data, hub, null, Vector3.right) { Mode = mode };
                instant.BeginAction(); instant.CompleteAction();
                Check(ProbeView.All.Count == 1 && ProbeView.All[0].Log.SequenceEqual(new[] { "advance", "end:Completed" }), mode + ": an instant Action shows its picture once and ends");
            }
            // PV2: the per-run bound and the key rule hold for `Present` on every machine alike
            {
                ProbeView.All.Clear(); int over = skill.ActionFactsOverBound;
                var many = new PNSkillActionContext(skill, hub.Skills.ResolveIdentity(ProbeSkill), data, hub, null, Vector3.right);
                many.BeginAction(); using (var frame = skill.BeginActionExecutionFrame(.02f)) frame.Advance(many, .02f, 1, null);
                for (int i = 0; i < 9; i++) many.Present("f" + i);
                Check(ProbeView.All[0].Log.Count(x => x.StartsWith("fact:")) == 8 && skill.ActionFactsOverBound == over + 1, "the ninth fact of a run is dropped and counted");
                bool badKey = false; try { many.Present("Bad Key"); } catch (ArgumentException) { badKey = true; }
                bool badNumber = false; try { many.Present("ok", double.NaN); } catch (ArgumentException) { badNumber = true; }
                Check(badKey && badNumber, "a fact key or number out of bounds is an author error");
                many.CompleteAction();
            }
            // an Observed-mode context (the legacy replay) never gets a view from here
            ProbeView.All.Clear();
            var replay = new PNSkillActionContext(skill, hub.Skills.ResolveIdentity(ProbeSkill), data, hub, null, Vector3.right) { Mode = PNSkillMode.Observed };
            replay.BeginAction(); using (var frame = skill.BeginActionExecutionFrame(.02f)) frame.Advance(replay, .02f, 1, null); replay.CompleteAction();
            Check(ProbeView.All.Count == 0, "an Observed-mode context is not given a view by the running-machine path");
            // teardown ends what still runs
            ProbeView.All.Clear();
            var leftover = new PNSkillActionContext(skill, hub.Skills.ResolveIdentity(ProbeSkill), data, hub, null, Vector3.right);
            leftover.BeginAction(); using (var frame = skill.BeginActionExecutionFrame(.02f)) frame.Advance(leftover, .02f, 1, null);
            skill.EndObservedActions();
            Check(ProbeView.All.Count == 1 && ProbeView.All[0].Ended == PNActionViewEnd.Cancelled && skill.ActionViewsRunning == 0, "teardown ends a view that still runs");
            Check(skill.ActionViewFailures == 0, "no view code failed");

            // ---- PV4: the one the Action helped travels as a `recipient` fact, found again by id ----
            var self = skill.GetComponent<PNStat>();
            ProbeView.All.Clear();
            var helpContext = new PNSkillActionContext(skill, hub.Skills.ResolveIdentity(ProbeSkill), data, hub, null, Vector3.right) { Mode = PNSkillMode.Authority };
            helpContext.BeginAction();
            using (var frame = skill.BeginActionExecutionFrame(.02f)) frame.Advance(helpContext, .02f, 1, null);
            helpContext.PresentRecipient(self);
            var helpView = ProbeView.All[0];
            Check(helpView.Log.Contains("fact:recipient") && helpView.LastFact != null && helpView.LastFact.Length == 1 && Near(helpView.LastFact[0], self.EntityId.Value), "PresentRecipient reaches the view as a recipient fact carrying the entity id");
            Check(skill.TryFindEntity((long)helpView.LastFact[0], out var found) && found == self && !skill.TryFindEntity(987654321L, out _), "the view finds the entity by that id, and an unknown id is not found");
            helpContext.CancelEffects();

            // ---- PV3: a look attached to a run (skill data names it), built by the engine's generic view ----
            var lookData = skill.GetData(LookSkill);
            ProbeLook.All.Clear();
            var lookContext = new PNSkillActionContext(skill, hub.Skills.ResolveIdentity(LookSkill), lookData, hub, null, Vector3.right) { Mode = PNSkillMode.Authority };
            lookContext.BeginAction();
            using (var frame = skill.BeginActionExecutionFrame(.02f)) frame.Advance(lookContext, .02f, 1, null);
            Check(ProbeLook.All.Count == 1 && ProbeLook.All[0].Log[0] == "run" && Near(ProbeLook.All[0].Age, .02), "the skill data's look is built for the run and placed at its age");
            using (var frame = skill.BeginActionExecutionFrame(.08f)) frame.Advance(lookContext, .08f, 1, null);
            Check(Near(ProbeLook.All[0].Age, .1) && !ProbeLook.All[0].Seek, "the look follows the run's age");
            lookContext.CancelEffects();
            Check(ProbeLook.All[0].Ended == PNActionViewEnd.Cancelled && skill.ActionViewsRunning == 0, "cancelling the run ends its look as Cancelled");
            threw = false; try { PNLookData.Validate(new PNSkillActionData(LookAction, perform: JObject.Parse("{'look':'test:missing_look'}")), new PNSkillActionDefinition(EveId.Parse("test:v1"), typeof(ProbeActionImpl)), hub); } catch (Exception) { threw = true; }
            Check(threw, "a look the registry does not know fails validation");
            // PV7: NoView is "no view of its own"; the data may still name a look (a dash trail), and then the run is published for that look.
            var noViewLook = new PNSkillActionDefinition(EveId.Parse("test:v2"), typeof(ProbeActionImpl)).NoView();
            threw = false; try { PNLookData.Validate(new PNSkillActionData(LookAction, perform: JObject.Parse("{'look':'test:probe_look'}")), noViewLook, hub); } catch (Exception) { threw = true; }
            Check(!threw, "an Action that declares NoView may name a registered look");
            var noViewLookData = new PNSkillData(new JObject(), new PNSkillActionData(LookAction, perform: JObject.Parse("{'look':'test:probe_look'}")));
            Check(noViewLook.PublishesRun(noViewLookData) && noViewLook.HasViewFor(noViewLookData), "a NoView Action whose data names a look publishes its run and has a view for it");

            // ---- PV3: a look sustained on the entity, published through the picture ----
            ProbeLook.All.Clear();
            Check(skill.SetLook(ProbeLookId, true) && skill.HasLook(ProbeLookId) && ProbeLook.All.Count == 1 && ProbeLook.All[0].Log[0] == "entity", "SetLook draws the look locally");
            Check(!skill.SetLook(ProbeLookId, true) && ProbeLook.All.Count == 1, "turning on a look that is on changes nothing");
            Check(skill.Presentation.Get(ProbeLookId.Path) == ProbeLookId.Namespace, "the authority publishes the look in the entity's picture");
            Check(skill.SetLook(ProbeLookId, false) && !skill.HasLook(ProbeLookId) && ProbeLook.All[0].Ended == PNActionViewEnd.Completed, "turning it off ends it as Completed");
            Check(skill.Presentation.Get(ProbeLookId.Path).Length == 0, "and clears it from the picture");
            skill.SetLook(EveId.Parse("test:unknown_look"), true);
            Check(skill.EntityLookCount == 0, "a look this machine does not know is not drawn");
            skill.SetLook(ProbeLookId, true);
            Check(skill.EntityLookCount == 1 && skill.LookFailures == 0, "no look code failed");
        }
        finally { Cleanup(hostScene); }
        Check(ProbeLook.All[ProbeLook.All.Count - 1].Ended == PNActionViewEnd.Cancelled, "teardown ends a look that is still on");

        // ---- Observed: only the view, from the stream progress ----
        var clientScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:pv1_observer"));
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { owner });
            var areaRoot = new GameObject("PV1 observer area manager"); SceneManager.MoveGameObjectToScene(areaRoot, clientScene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var skill = Spawn(clientScene, "PV1 observed Eve", owner + 1);
            skill.SetObservedActionPresentation(true);
            string actionId = ProbeAction.ToString();
            Check(skill.CanPresentObservedAction(dataId, actionId), "an Action with a view is drawn by that view on an observer");
            ProbeView.All.Clear();
            var run = new PNActionRunKey(30, 1);
            const double dt = .016;
            double progress = 0;
            skill.PresentObservedAction(run, dataId, Vector3.right, progress, true, 1, dt);
            Check(ProbeView.All.Count == 1 && ProbeView.All[0].LastSeek && skill.ActionViewsRunning == 1, "the first sight builds and places the view (a seek)");
            var view = ProbeView.All[0];
            for (int i = 0; i < 10; i++) { progress += dt; skill.PresentObservedAction(run, dataId, Vector3.right, progress, true, 1, dt); }
            Check(Near(view.Age, progress) && !view.LastSeek && Near(view.Rate, 1) && skill.ActionViewSeeks == 0, "steady frames follow the stream with rate 1, no seek (rate " + view.Rate + ")");
            // a stall of 0.1 s: caught up gradually
            progress += .1;
            skill.PresentObservedAction(run, dataId, Vector3.right, progress, true, 1, dt);
            Check(!view.LastSeek && view.Age < progress - .05 && Near(view.Rate, 1.5) && skill.ActionViewSeeks == 0, "a 0.1 s lag is caught up gradually at 1.5x (age behind by " + (progress - view.Age) + ", rate " + view.Rate + ")");
            for (int i = 0; i < 20 && view.Age < progress - 1e-9; i++) { progress += dt; skill.PresentObservedAction(run, dataId, Vector3.right, progress, true, 1, dt); }
            Check(skill.ActionViewSeeks == 0 && Math.Abs(progress - view.Age) < .03, "the gradual catch-up closes the gap without a seek (gap " + (progress - view.Age) + ")");
            Check(skill.ActionViewMaxLag >= .099 && skill.ActionViewMaxLag <= .13, "the counter keeps the biggest lag (" + skill.ActionViewMaxLag + ")");
            // a facts arrives, then a big jump: a seek that drops an old one-shot and keeps a fresh one
            skill.PresentObservedFact(run, "old", new[] { 1d }, progress + .02);
            skill.PresentObservedFact(run, "fresh", new[] { 1d }, progress + .27);
            progress += .3;
            skill.PresentObservedAction(run, dataId, Vector3.right, progress, true, 1, dt);
            Check(view.LastSeek && Near(view.Age, progress) && skill.ActionViewSeeks == 1, "a 0.3 s lag is a seek: the age is set");
            Check(!view.Log.Contains("fact:old") && view.Log.Contains("fact:fresh") && skill.ActionViewFactsDropped == 1, "after a seek a one-shot older than 0.1 s is dropped, one inside the window is still shown");
            // a fact for a future age waits until the view reaches it
            skill.PresentObservedFact(run, "later", new[] { 1d }, progress + dt * 2);
            progress += dt; skill.PresentObservedAction(run, dataId, Vector3.right, progress, true, 1, dt);
            Check(!view.Log.Contains("fact:later"), "a fact from the future is held");
            progress += dt; skill.PresentObservedAction(run, dataId, Vector3.right, progress, true, 1, dt);
            Check(view.Log.Contains("fact:later"), "and delivered when the age reaches it");
            // PV2: facts from the stream are taken once each, however many frames list them
            var streamFacts = new List<PNActionFact> { new PNActionFact(progress, "wire", new[] { 1d }) };
            progress += dt; skill.PresentObservedRun(run, dataId, Vector3.right, null, progress, true, 1, dt, 1, streamFacts);
            progress += dt; skill.PresentObservedRun(run, dataId, Vector3.right, null, progress, true, 1, dt, 1, streamFacts);
            Check(view.Log.Count(x => x == "fact:wire") == 1, "a stream fact listed in two frames reaches the view once");
            // V4b: a melee door in the stream is for the hitbox service of the machine being hit, never for the picture (a malformed one is dropped there)
            var doorFacts = new List<PNActionFact> { new PNActionFact(progress, PNMeleeDoor.Key, new double[] { 0, 0, 0, 0, 0, 1.4, .6, 70 }) };
            progress += dt; skill.PresentObservedRun(run, dataId, Vector3.right, null, progress, true, 1, dt, 1, doorFacts);
            progress += dt; skill.PresentObservedRun(run, dataId, Vector3.right, null, progress, true, 1, dt, 1, doorFacts);
            Check(!view.Log.Contains("fact:" + PNMeleeDoor.Key), "a door fact never reaches the view of the run");
            skill.PresentObservedResult(run, new PNActionViewResult("hit", Vector3.zero, 1, progress));
            Check(view.Log.Contains("result:hit"), "a gameplay result of the run reaches its view");
            // a second overlapping run takes the lead
            var other = new PNActionRunKey(30, 2);
            skill.PresentObservedAction(run, dataId, Vector3.right, progress, false, 1, dt);
            skill.PresentObservedAction(other, dataId, Vector3.forward, 0, true, 1, dt);
            Check(!view.Leads && ProbeView.All.Count == 2 && ProbeView.All[1].Leads, "the lead moves to the newest run");
            skill.ReconcileObservedActionPresentation(true, _ => null);
            // coverage lost: hidden, then shown again as a placement
            skill.ReconcileObservedActionPresentation(false, _ => null);
            Check(view.Hidden && ProbeView.All[1].Hidden, "when the machine cannot read the run the views are hidden");
            skill.PresentObservedAction(run, dataId, Vector3.right, progress + dt, false, 1, dt);
            Check(!view.Hidden && view.LastSeek, "showing a hidden view again is a placement (seek)");
            // the stream no longer lists a run: it ends with the authority's verdict
            skill.PresentObservedAction(run, dataId, Vector3.right, progress + dt, false, 1, dt);
            skill.ReconcileObservedActionPresentation(true, key => key.Equals(run) ? PNActionRunEnd.Completed : PNActionRunEnd.Cancelled);
            Check(ProbeView.All[1].Ended == PNActionViewEnd.Cancelled && view.Ended == null, "a run that left the stream ends: the unlisted one by the authority's verdict, a listed one keeps running");
            skill.ReconcileObservedActionPresentation(true, key => PNActionRunEnd.Completed);
            Check(view.Ended == PNActionViewEnd.Completed && skill.ActionViewsRunning == 0, "the last unlisted run ends as Completed and nothing is left running");
            Check(skill.ActionViewFailures == 0 && skill.ActionViewTicks > 0, "no view code failed, and the view time was measured");
            // teardown
            skill.PresentObservedAction(new PNActionRunKey(31, 1), dataId, Vector3.right, 0, true, 1, dt);
            var last = ProbeView.All[ProbeView.All.Count - 1];
            skill.EndObservedActions();
            Check(last.Ended == PNActionViewEnd.Cancelled && skill.ActionViewsRunning == 0, "teardown ends a watched view too");

            // ---- PV4 on an observer: a hit of the run (the cast key and the Action's ordinal name it) is handed to its view once ----
            ProbeView.All.Clear();
            var hitRun = new PNActionRunKey(50, 2);
            skill.PresentObservedAction(hitRun, dataId, Vector3.right, 0, true, 1, dt);
            var hitView = ProbeView.All[0];
            var raise = typeof(PNSkill).GetMethod("RaiseActionImpact", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            int resultsBefore = skill.ActionViewResults;
            raise.Invoke(skill, new object[] { PNActionImpactOrigin.Copy, 50L, 2L, 0, 7L, null, new Vector3(1, 0, 2) });
            Check(hitView.Log.Contains("result:hit") && skill.ActionViewResults == resultsBefore + 1, "a hit of the run reaches its view through the engine");
            raise.Invoke(skill, new object[] { PNActionImpactOrigin.Copy, 50L, 3L, 0, 7L, null, Vector3.zero });
            raise.Invoke(skill, new object[] { PNActionImpactOrigin.Copy, 51L, 2L, 0, 7L, null, Vector3.zero });
            Check(hitView.Log.Count(x => x == "result:hit") == 1 && skill.ActionViewResults == resultsBefore + 1, "a hit of another Action or another cast is not given to it");
            skill.ReconcileObservedActionPresentation(true, _ => null); skill.ReconcileObservedActionPresentation(true, key => PNActionRunEnd.Completed);
            raise.Invoke(skill, new object[] { PNActionImpactOrigin.Copy, 50L, 2L, 0, 7L, null, Vector3.zero });
            Check(hitView.Log.Count(x => x == "result:hit") == 1, "a hit that comes after the run ended draws nothing");

            // ---- PV3 on an observer: the picture draws the look; a run whose data names a look is drawn without a hand-written view ----
            ProbeLook.All.Clear();
            var lookDataId = hub.Skills.ResolveIdentity(LookSkill).ToString();
            Check(skill.CanPresentObservedAction(lookDataId, LookAction.ToString()), "an Action whose data names a look is drawn on an observer");
            var lookRun = new PNActionRunKey(40, 1);
            skill.PresentObservedAction(lookRun, lookDataId, Vector3.right, .05, true, 1, dt);
            Check(ProbeLook.All.Count == 1 && ProbeLook.All[0].Log[0] == "run", "the observer builds the run's look");
            skill.ReconcileObservedActionPresentation(true, _ => null);
            skill.ReconcileObservedActionPresentation(true, key => PNActionRunEnd.Completed);
            Check(ProbeLook.All[0].Ended == PNActionViewEnd.Completed, "the run's end ends its look");
            ProbeLook.All.Clear();
            var picture = new Dictionary<string, string> { { ProbeLookId.Path, ProbeLookId.Namespace } };
            skill.Presentation.Apply(picture);
            Check(skill.HasLook(ProbeLookId) && ProbeLook.All.Count == 1, "the observer draws a look the picture names");
            skill.Presentation.Apply(new Dictionary<string, string>());
            Check(!skill.HasLook(ProbeLookId) && ProbeLook.All[0].Ended == PNActionViewEnd.Completed, "and ends it when the picture clears");
            skill.Presentation.Apply(picture);
            int before = ProbeLook.All.Count; skill.ReplayLooks();
            Check(ProbeLook.All.Count == before, "replaying the picture does not draw a look twice");

            // a picture that arrives before the look can be drawn: the engine waits and retries by itself, nobody replays
            skill.Presentation.Apply(new Dictionary<string, string>());
            ProbeLook.All.Clear(); ProbeLook.Block = true;
            skill.Presentation.Apply(picture);
            var retry = typeof(PNSkill).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            retry.Invoke(skill, null);
            Check(!skill.HasLook(ProbeLookId) && skill.PendingLookCount == 1 && ProbeLook.All.Count == 0, "a look that cannot be drawn yet is kept waiting, not dropped");
            ProbeLook.Block = false; retry.Invoke(skill, null);
            Check(skill.HasLook(ProbeLookId) && skill.PendingLookCount == 0 && ProbeLook.All.Count == 1, "it is drawn by the engine as soon as it can be, with no call from the character");
            skill.Presentation.Apply(new Dictionary<string, string>());
            ProbeLook.Block = true; skill.Presentation.Apply(picture);
            skill.Presentation.Apply(new Dictionary<string, string>());
            ProbeLook.Block = false; retry.Invoke(skill, null);
            Check(!skill.HasLook(ProbeLookId) && skill.PendingLookCount == 0, "a picture that clears a waiting look removes it for good");
        }
        finally { Cleanup(clientScene); }
        EveClientRoleProbeSkill.ExtraBuiltIn = null;
        return "Action view checks: " + n + " checks passed.";
    }
}
#endif
