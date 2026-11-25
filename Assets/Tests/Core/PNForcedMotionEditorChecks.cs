#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/BodyMotionPlan.md M4, Edit Mode, probe only (B3): the forced layer, the crowd_control factor through the Operation, the owner view's state, the host's safety net and the shared service rules.
// What needs real machines (a client's player pushed, 0 / 120 ms / loss) is the Player run; here the layer is driven by Tick with a fixed step.
public static class PNForcedMotionEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Forced motion check " + n + ": " + what); n++; }
        bool Near(double a, double b, double e = .02) => Math.Abs(a - b) <= e;
        bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var scene = EditorSceneManager.NewPreviewScene();
        long next = 996100;
        EveClientRoleProbeSkill Spawn(string name, Vector3 at)
        {
            var obj = new GameObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, scene); obj.transform.position = at;
            obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(next++), EveId.Parse("project_n:eve"), 5); obj.layer = PNLayers.Player;
            obj.AddComponent<EveCharacterStat>(); obj.AddComponent<PNAbilityManager>();
            var body = obj.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = obj.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            obj.AddComponent<PNPlayerMove>(); var skill = obj.AddComponent<EveClientRoleProbeSkill>(); skill.Prepare(); obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true);
            Physics.SyncTransforms(); return skill;
        }
        void SetRole(PNMove move, PNMoveNetworkRole role) => typeof(PNMove).GetProperty("NetworkRole").GetSetMethod(true).Invoke(move, new object[] { role });
        void Run(PNForcedMotion layer, float seconds, float step = .05f) { for (float t = 0; t < seconds - 1e-4f; t += step) layer.Tick(step); }
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:forced_motion"));
            var areaRoot = new GameObject("forced area manager"); SceneManager.MoveGameObjectToScene(areaRoot, scene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);

            // ---- the layer on a local body ----
            var skill = Spawn("forced local", new Vector3(0, 1, 0)); var go = skill.gameObject; var move = go.GetComponent<PNMove>(); var body = go.GetComponent<Rigidbody>();
            var controls = go.GetComponent<PNControlManager>(); var layer = PNForcedMotion.Of(move);
            Check(ReferenceEquals(PNForcedMotion.Of(move), layer), "the layer is one component per body");
            Check(Throws<ArgumentOutOfRangeException>(() => layer.Issue(Vector3.right, 0f, 0)) && Throws<ArgumentOutOfRangeException>(() => layer.Issue(Vector3.right * 100, 1f, 0)) &&
                Throws<ArgumentOutOfRangeException>(() => layer.Issue(Vector3.right, 1f, -1)) && Throws<ArgumentOutOfRangeException>(() => layer.Issue(new Vector3(float.NaN, 0, 0), 1f, 0)), "a command out of range is rejected");
            uint id = layer.Issue(Vector3.right * 3f, 1f, 0f);
            Check(id == 1 && layer.IsActive && !controls.State.IsAllowed(PNControlIds.VoluntaryMotion) && controls.State.IsAllowed(PNControlIds.ForcedMotion), "a push is a command, VoluntaryMotion is locked and ForcedMotion stays allowed");
            Check(Throws<InvalidOperationException>(() => move.RegisterMotion(PNControlIds.VoluntaryMotion, () => { })), "no voluntary motion can start while it is pushed");
            Run(layer, .5f); float half = body.position.x;
            Check(half > 1.5f && half < 3f, "a fast start: more than half the way at half the time (" + half + ")");
            Run(layer, .5f);
            Check(Near(body.position.x, 3f) && Near(body.position.y, 1f) && !layer.IsActive && controls.State.IsAllowed(PNControlIds.VoluntaryMotion), "the push ends at the point, height unchanged, the lock is returned (" + body.position + ")");
            var after = move.RegisterMotion(PNControlIds.VoluntaryMotion, () => { }); after.Dispose();
            Check(true, "a voluntary motion can start again");

            // parallel commands add (B10)
            body.position = Vector3.zero; Physics.SyncTransforms();
            layer.Issue(Vector3.right * 2f, 1f, 0f); layer.Issue(Vector3.right * 1f, .5f, 0f);
            Check(layer.ActiveCount == 2, "two commands run together");
            Run(layer, 1.05f);
            Check(Near(body.position.x, 3f) && !layer.IsActive, "two pulls the same way add (2 + 1)");
            body.position = Vector3.zero; Physics.SyncTransforms();
            layer.Issue(Vector3.right * 2f, 1f, 0f); layer.Issue(Vector3.left * 2f, 1f, 0f); Run(layer, 1.05f);
            Check(Near(body.position.x, 0f), "two opposite pulls cancel");
            // the same two black holes: the second does not replace the first
            body.position = Vector3.zero; Physics.SyncTransforms();
            layer.Issue(Vector3.right * 1f, 1f, 0f); Run(layer, .3f); layer.Issue(Vector3.forward * 1f, 1f, 0f); Run(layer, 1.05f);
            Check(Near(body.position.x, 1f) && Near(body.position.z, 1f), "a new command does not replace the running one (1, 1)");

            // lift
            body.position = new Vector3(0, 1, 0); Physics.SyncTransforms();
            layer.Issue(Vector3.right * 2f, 1f, 2f); float peak = 0;
            for (int i = 0; i < 20; i++) { layer.Tick(.05f); peak = Mathf.Max(peak, body.position.y - 1f); }
            Check(Near(peak, 2f, .08), "a lift rises by the height and comes back (" + peak + ")");
            Check(Near(body.position.y, 1f) && Near(body.position.x, 2f), "the lift ends at the displacement on the ground (" + body.position + ")");

            // wall (B11: only walls stop it)
            body.position = Vector3.zero; Physics.SyncTransforms();
            var wall = new GameObject("wall"); SceneManager.MoveGameObjectToScene(wall, scene); wall.layer = 0; var box = wall.AddComponent<BoxCollider>(); box.size = new Vector3(.2f, 4, 4); wall.transform.position = new Vector3(1.5f, 1, 0); Physics.SyncTransforms();
            layer.Issue(Vector3.right * 4f, 1f, 0f); Run(layer, 1.05f);
            Check(body.position.x < 1.5f && body.position.x > .8f, "a wall stops the push (" + body.position.x + ")");
            UnityEngine.Object.DestroyImmediate(wall); Physics.SyncTransforms();

            // another body does not stop it
            var other = Spawn("forced in the way", new Vector3(1.2f, 1, 0)); body.position = new Vector3(0, 1, 0); Physics.SyncTransforms();
            layer.Issue(Vector3.right * 3f, 1f, 0f); Run(layer, 1.05f);
            Check(Near(body.position.x, 3f), "bodies of other actors do not stop a forced motion (they never collide)");

            // a voluntary motion that runs is cancelled through its own callback (B4)
            bool cancelled = false; var running = move.RegisterMotion(PNControlIds.VoluntaryMotion, () => cancelled = true);
            layer.Issue(Vector3.left * 1f, .5f, 0f);
            Check(cancelled && !running.IsValid, "a voluntary motion that runs is told to cancel when the body is pushed");
            Run(layer, .6f);

            // late start by age: the part that already passed is applied at once
            body.position = Vector3.zero; Physics.SyncTransforms();
            typeof(PNForcedMotion).GetMethod("Begin", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(layer, new object[] { 77u, Vector3.right * 4f, 1f, 0f, .5f });
            layer.Tick(.0001f);
            Check(Near(body.position.x, 4f * 0.75f, .05), "a command started at age 0.5 s of 1 s begins at the point it would have reached (3 of 4 m: " + body.position.x + ")");
            Run(layer, .6f); Check(Near(body.position.x, 4f), "and ends at the same point");

            // cancel
            layer.Issue(Vector3.right, 1f, 0f); layer.CancelAll(); Check(!layer.IsActive && controls.State.IsAllowed(PNControlIds.VoluntaryMotion) && layer.Dropped >= 1, "CancelAll stops it and returns the lock");

            // ---- roles ----
            Check(!Throws<Exception>(() => { }), "(self check)");
            var remote = Spawn("forced remote owner", new Vector3(10, 1, 0)); var rmove = remote.GetComponent<PNMove>(); var rbody = remote.GetComponent<Rigidbody>(); var rlayer = PNForcedMotion.Of(rmove);
            SetRole(rmove, PNMoveNetworkRole.RemoteOwnerHost);
            uint seenId = 0; Vector3 seenDisplacement = default; rlayer.Issued += (i, d, dur, h) => { seenId = i; seenDisplacement = d; };
            Check(PNForcedMotion.CanDecide(rmove), "the host decides for a client's player");
            uint rid = rlayer.Issue(Vector3.right * 2f, .5f, 0f);
            Check(rid == 1 && seenId == 1 && seenDisplacement == Vector3.right * 2f, "the command is announced to the owner view");
            Run(rlayer, .6f);
            Check(Near(rbody.position.x, 10f) && !rlayer.IsActive, "the host runs only the clock for a client's player: it does not write the pose");
            bool relocated = false; rlayer.Relocated += p => relocated = true;
            rlayer.TickSafety(.5f); Check(!relocated, "the safety net waits for the owner's pose");
            rlayer.TickSafety(.5f);
            Check(relocated, "the owner never moved: the body is put at the end point (Teleport correction)");
            Check(Near(rbody.position.x, 12f, .1), "at the end point the host computed (" + rbody.position.x + ")");
            // the owner moved: no correction
            relocated = false; rbody.position = new Vector3(10, 1, 0); Physics.SyncTransforms(); rlayer.Issue(Vector3.right * 2f, .5f, 0f); Run(rlayer, .6f); rbody.position = new Vector3(12, 1, 0); Physics.SyncTransforms();
            rlayer.TickSafety(1f); Check(!relocated, "an owner that moved is not corrected");
            Check(Throws<Exception>(() => rlayer.Receive(5, Vector3.right, 1f, 0f, 0f)) == false && rlayer.Receive(5, Vector3.right, 1f, 0f, 0f) == false, "a host body does not take a command meant for the owner's machine");
            SetRole(rmove, PNMoveNetworkRole.Observer); Check(!PNForcedMotion.CanDecide(rmove) && rlayer.Issue(Vector3.right, 1f, 0f) == 0, "an observer never decides");
            SetRole(rmove, PNMoveNetworkRole.Local);

            // ---- the Operation: crowd_control decides the factor (B6/B7/B9) ----
            var attackerSkill = Spawn("forced attacker", new Vector3(-20, 1, 0)); var attackerStat = attackerSkill.GetComponent<PNStat>();
            var victimSkill = Spawn("forced victim", new Vector3(-18, 1, 0)); var victim = victimSkill.GetComponent<PNStat>(); var vbody = victimSkill.GetComponent<Rigidbody>(); var vlayer = PNForcedMotion.Of(victimSkill.GetComponent<PNMove>());
            var opId = PNGameplayContentModule.ForcedMotionOperation;
            var entry = PNGameplayContentModule.EveAttackEntry;
            PNForcedMotionOperation Operate(string payload, PNSkillMode mode = PNSkillMode.Authority)
            {
                var ctx = new PNSkillActionContext(attackerSkill, entry, new PNSkillData(null, new PNSkillActionData(PNGameplayContentModule.DamageAction)), hub, new[] { victim }) { Mode = mode };
                var op = (PNForcedMotionOperation)hub.Operations().Get(opId).Create(); op.Execute(ctx, JObject.Parse(payload)); return op;
            }
            void Reset() { vlayer.CancelAll(); vbody.position = new Vector3(-18, 1, 0); vbody.transform.position = vbody.position; Physics.SyncTransforms(); }
            var key = EveId.Parse("test:forced_cc");
            var push = "{'kind':'push','distance':2,'duration':1}";
            var op1 = Operate(push); Run(vlayer, 1.05f);
            Check(op1.Results.Count == 1 && Near(op1.Results[0].Factor, 1d) && Near(vbody.position.x, -16f), "push: factor 1 moves 2 m away from the attacker (" + vbody.position.x + ")");
            Reset(); op1 = Operate("{'kind':'pull','distance':5,'duration':1,'stopDistance':.6}"); Run(vlayer, 1.05f);
            Check(Near(vbody.position.x, -19.4f, .05), "pull: towards the attacker, stopping short of it (" + vbody.position.x + ")");
            Reset(); victim.SetSource(key, new[] { new EveStatModifier(PNContentIds.CrowdControl, -1, 0, PNContentIds.Mitigation) });
            op1 = Operate("{'kind':'push','distance':4,'duration':1,'crowdControl':1.1}"); Run(vlayer, 1.2f);
            Check(Near(op1.Results[0].Factor, .1, 1e-6) && Near(vbody.position.x, -18f + .4f, .03), "dash-like Mitigation -1 on 1.1: factor 0.1 scales distance (0.4 m: " + vbody.position.x + ")");
            Reset(); victim.SetSource(key, new[] { new EveStatModifier(PNContentIds.CrowdControl, -1, 1, PNContentIds.Mitigation) });
            op1 = Operate(push);
            Check(op1.Results.Count == 1 && op1.Results[0].Factor == 0d && op1.Results[0].CommandId == 0 && !vlayer.IsActive, "factor 0: the target is not moved at all");
            Reset(); victim.SetSource(key, new[] { new EveStatModifier(PNContentIds.CrowdControl, 1, 1, PNContentIds.Mitigation) });
            op1 = Operate(push); Run(vlayer, 2.1f);
            Check(Near(op1.Results[0].Factor, 2d) && Near(vbody.position.x, -18f + 4f, .05), "factor 2 doubles the distance (and the duration: 2 s)");
            victim.RemoveSource(key); Reset();
            attackerStat.SetSource(key, new[] { new EveStatModifier(PNContentIds.CrowdControl, .5, 1, PNContentIds.Scale) });
            op1 = Operate(push); Run(vlayer, 1.6f); attackerStat.RemoveSource(key);
            Check(Near(op1.Results[0].Factor, 1.5) && Near(vbody.position.x, -18f + 3f, .06), "the attacker Scale +50% makes it 1.5");
            Reset();
            var guess = Operate(push, PNSkillMode.Predicted);
            Check(guess.Results.Count == 0 && !vlayer.IsActive, "a guess or a replay moves nobody");
            Check(Throws<FormatException>(() => PNForcedMotionSettings.Parse(JObject.Parse("{'kind':'spin','distance':1,'duration':1}"))) && Throws<FormatException>(() => PNForcedMotionSettings.Parse(JObject.Parse("{'kind':'push','duration':1}"))) &&
                Throws<FormatException>(() => PNForcedMotionSettings.Parse(JObject.Parse("{'kind':'push','distance':1,'duration':0}"))) && Throws<FormatException>(() => PNForcedMotionSettings.Parse(JObject.Parse("{'kind':'push','distance':1,'duration':1,'extra':1}"))) &&
                Throws<FormatException>(() => PNForcedMotionSettings.Parse(JObject.Parse("{'kind':'push','distance':1,'duration':1,'crowdControl':-1}"))), "the payload is validated (kind, required fields, ranges, unknown fields)");

            // ---- the owner view's state ----
            var epoch = Guid.NewGuid();
            var good = new JObject { ["v"] = 1, ["e"] = epoch.ToString("N"), ["c"] = new JArray { new JArray(3, 1.5, 0, -2, 1.0, 0.5, 12.25) } };
            var parsed = PNForcedMotionView.Parse(good, out var parsedEpoch);
            Check(parsedEpoch == epoch && parsed.Count == 1 && parsed[0].Id == 3 && parsed[0].Displacement == new Vector3(1.5f, 0, -2) && Near(parsed[0].Duration, 1) && Near(parsed[0].Height, .5) && Near(parsed[0].Stamp, 12.25), "a command round-trips through the state");
            Check(Throws<FormatException>(() => PNForcedMotionView.Parse(new JObject { ["v"] = 2, ["e"] = epoch.ToString("N"), ["c"] = new JArray() }, out _)) &&
                Throws<FormatException>(() => PNForcedMotionView.Parse(new JObject { ["v"] = 1, ["e"] = "x", ["c"] = new JArray() }, out _)) &&
                Throws<FormatException>(() => PNForcedMotionView.Parse(new JObject { ["v"] = 1, ["e"] = epoch.ToString("N"), ["c"] = new JArray { new JArray(0, 1, 0, 0, 1, 0, 1) } }, out _)) &&
                Throws<FormatException>(() => PNForcedMotionView.Parse(new JObject { ["v"] = 1, ["e"] = epoch.ToString("N"), ["c"] = new JArray { new JArray(1, 1, 0, 0, 0, 0, 1) } }, out _)) &&
                Throws<FormatException>(() => PNForcedMotionView.Parse(new JObject { ["v"] = 1, ["e"] = epoch.ToString("N"), ["c"] = new JArray { new JArray(1, 1000, 0, 0, 1, 0, 1) } }, out _)) &&
                Throws<FormatException>(() => PNForcedMotionView.Parse(new JObject { ["v"] = 1, ["e"] = epoch.ToString("N"), ["c"] = new JArray(Enumerable.Range(0, 17).Select(i => (object)new JArray(1, 1, 0, 0, 1, 0, 1)).Select(o => (JToken)o).ToArray()) }, out _)), "a bad state is refused (version, epoch, id, duration, distance, count)");
            Check(hub.SyncStates().TryGet(PNForcedMotionView.State, out var def) && def.OwnerOnly && def.Targets == PNSyncTargets.Entity, "the state is an owner-only Entity state");
            Check(hub.Operations().TryGet(opId, out _), "the Operation is registered");
        }
        finally
        {
            try
            {
                foreach (var r in scene.GetRootGameObjects()) if (r != null) foreach (var l in r.GetComponentsInChildren<EveEntityLoader>(true)) { try { l.Unload(); } catch (Exception) { } }
                foreach (var r in scene.GetRootGameObjects()) if (r != null) r.SetActive(false);
            }
            finally { try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(scene); } }
        }
        return "PASS " + n + " forced motion checks";
    }
}
#endif
