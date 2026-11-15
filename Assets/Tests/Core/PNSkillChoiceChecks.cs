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

// Docs/SkillNetworkFrameworkPlan.md G4, Edit Mode: `invocation.Choose` in the three execution modes. Predicted runs `compute` and tells the sink what it chose; Authority takes a proposal only if the framework finds it
// well formed (and, for an entity, existing / in the scene / within reach) and the skill's `validate` accepts it, otherwise `compute`; Observed is `compute` and nothing else. Values are bool / int / string / PNStat.
public static class PNSkillChoiceChecks
{
    private sealed class Sink : IPNSkillReportSink
    {
        public readonly List<(string Point, JToken Value)> Decisions = new List<(string, JToken)>();
        public readonly List<long> Keys = new List<long>();
        public void ReportHit(long castKey, PNStat target, int step) { }
        public void ReportDecision(long castKey, string point, JToken value) { Decisions.Add((point, value)); Keys.Add(castKey); }
    }
    private sealed class Source : IPNSkillDecisionSource
    {
        public readonly List<(string Point, JToken Value)> Waiting = new List<(string, JToken)>();
        public float ReachSlack { get; set; }
        public int Taken;
        public readonly List<long> Asked = new List<long>();
        // `OnlyKey` 0: any cast takes it; otherwise only the cast with that key does (the wire layer's rule)
        public long OnlyKey;
        public bool TryTake(long castKey, string point, out JToken value)
        {
            Asked.Add(castKey);
            for (int i = 0; i < Waiting.Count; i++) if (Waiting[i].Point == point && (OnlyKey == 0 || OnlyKey == castKey)) { value = Waiting[i].Value; Waiting.RemoveAt(i); Taken++; return true; }
            value = null; return false;
        }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Skill choice check " + n + ": " + what); n++; }
        bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        const long owner = 5, eveId = 995080;
        var entry = PNGameplayContentModule.EveAttackEntry;

        GameObject Root(Scene preview, string name, long id, string character, Vector3 position, long ownerId)
        {
            var obj = new GameObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj, preview); obj.transform.position = position;
            obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(character), ownerId); return obj;
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
        EveCharacterSkill Spawn(Scene preview, string name)
        {
            var actor = Root(preview, name, eveId, "project_n:eve", Vector3.zero, owner); actor.layer = PNLayers.Player; actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveClientRoleProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            Physics.SyncTransforms(); return skill;
        }
        PNEnemyStat Enemy(Scene preview, string name, long id, Vector3 at)
        {
            var obj = Root(preview, name, id, "project_n:dummy", at, 0); var enemy = obj.AddComponent<PNEnemyStat>(); obj.layer = PNLayers.Enemy; obj.AddComponent<SphereCollider>().radius = .15f;
            obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); return enemy;
        }
        JObject Value(object v) => new JObject { ["v"] = JToken.FromObject(v) };

        // ---- a client: Predicted and Observed ----
        var clientScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:g4_client"));
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { owner });
            var skill = Spawn(clientScene, "G4 client Eve"); var sink = new Sink(); var source = new Source { Waiting = { ("never", Value(1)) } }; skill.ReportSink = sink; skill.DecisionSource = source;
            var predicted = skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Predicted);
            Check(predicted.Choose("count", () => 7) == 7 && predicted.Choose("flag", () => true) && predicted.Choose("name", () => "empowered_left") == "empowered_left", "Predicted returns what compute chose");
            Check(sink.Decisions.Count == 3 && sink.Decisions[0].Point == "count" && (int)sink.Decisions[0].Value["v"] == 7 && (int)sink.Decisions[1].Value["v"] == 1 && (string)sink.Decisions[2].Value["v"] == "empowered_left", "and tells the sink each choice (bool as 0/1, int, string)");
            Check(predicted.Choose<PNStat>("target", () => null) == null && sink.Decisions.Count == 3, "a null entity is not reported");
            Check(predicted.Choose("flag", () => false, proposed => proposed) == false && source.Taken == 0, "Predicted never asks for a proposal and never runs validate");
            Check(predicted.CastKey > 0 && sink.Keys.Count > 0 && sink.Keys.TrueForAll(k => k == predicted.CastKey), "G6c: every proposal travels under the cast's key");
            Check(Throws<ArgumentException>(() => predicted.Choose("Bad Point", () => 1)) && Throws<ArgumentException>(() => predicted.Choose("", () => 1)) && Throws<ArgumentException>(() => predicted.Choose(new string('a', PNSkillDecisions.MaxPointLength + 1), () => 1)), "a point name must be lowercase letters, digits and underscores");
            Check(Throws<NotSupportedException>(() => predicted.Choose("real", () => 1f)), "a float is not a decision value");
            Check(Throws<ArgumentNullException>(() => predicted.Choose<int>("count", null)), "compute is required");
            skill.EndNonAuthorityInvocation();
            var observed = skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Observed); sink.Decisions.Clear();
            Check(observed.Choose("count", () => 9) == 9 && sink.Decisions.Count == 0 && source.Taken == 0, "Observed is compute: nothing reported, nothing taken");
            skill.EndNonAuthorityInvocation();
            Check(sink.Decisions.Count == 0, "no report outside a cast");
        }
        finally { Cleanup(clientScene); }

        // ---- the authority ----
        var hostScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:g4_authority"));
            var areaRoot = new GameObject("G4 area manager"); SceneManager.MoveGameObjectToScene(areaRoot, hostScene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var skill = Spawn(hostScene, "G4 authority Eve");
            var near = Enemy(hostScene, "G4 near", 995081, new Vector3(.45f, .4f, 0)); var far = Enemy(hostScene, "G4 far", 995082, new Vector3(40f, .4f, 0));
            var edge = Enemy(hostScene, "G4 edge", 995083, new Vector3(3.5f, .4f, 0)); Physics.SyncTransforms();
            Check(skill.TryAttack(Vector3.right) && skill.CurrentInvocation != null && skill.CurrentInvocation.Mode == PNSkillMode.Authority, "a real cast is an Authority invocation");
            var cast = skill.CurrentInvocation;
            Check(cast.Choose("count", () => 7) == 7 && cast.Choose("flag", () => true), "with no source the authority is compute (own or offline player)");
            var source = new Source { ReachSlack = 1.5f }; skill.DecisionSource = source;
            Check(cast.Choose("count", () => 7) == 7 && source.Taken == 0, "a source with nothing waiting is compute");
            source.Waiting.Add(("count", Value(3)));
            Check(cast.Choose("count", () => 7) == 3 && source.Taken == 1 && source.Asked[source.Asked.Count - 1] == cast.CastKey, "a well formed proposal is taken (the source is asked under the cast's key)");
            source.OnlyKey = cast.CastKey + 1; source.Waiting.Add(("count", Value(3)));
            Check(cast.Choose("count", () => 7) == 7 && source.Taken == 1, "G6c: a proposal made for another cast is not taken");
            source.OnlyKey = 0; source.Waiting.Clear(); source.Waiting.Add(("count", Value(3)));
            Check(cast.Choose("count", () => 7) == 3 && source.Taken == 2, "and the cast's own is");
            source.Taken = 1;
            source.Waiting.Add(("count", Value(3)));
            Check(cast.Choose("count", () => 7, proposed => proposed > 5) == 7 && source.Taken == 2 && source.Waiting.Count == 0, "a proposal the skill's rule refuses is consumed and compute stands");
            source.Waiting.Add(("count", Value("three")));
            Check(cast.Choose("count", () => 7) == 7, "a proposal of the wrong type is ignored");
            source.Waiting.Add(("count", Value(long.MaxValue)));
            Check(cast.Choose("count", () => 7) == 7, "an integer out of range is ignored");
            source.Waiting.Add(("count", new JArray()));
            Check(cast.Choose("count", () => 7) == 7, "a proposal that is not an object is ignored");
            source.Waiting.Add(("flag", Value(0))); Check(!cast.Choose("flag", () => true), "a bool proposal");
            source.Waiting.Add(("name", Value("empowered_right"))); Check(cast.Choose("name", () => "x") == "empowered_right", "a string proposal");
            source.Waiting.Add(("other", Value(9))); Check(cast.Choose("count", () => 7) == 7 && source.Waiting.Count == 1, "a proposal waits for its own point");

            JObject Named(long id) => new JObject { ["t"] = id };
            source.Waiting.Add(("target", Named(near.EntityId.Value)));
            Check(ReferenceEquals(cast.Choose<PNStat>("target", () => far, reach: 5f), near), "an entity in reach is taken");
            source.Waiting.Add(("target", Named(far.EntityId.Value)));
            Check(ReferenceEquals(cast.Choose<PNStat>("target", () => near, reach: 5f), near), "an entity out of reach falls back to compute");
            source.Waiting.Add(("target", Named(edge.EntityId.Value)));
            Check(ReferenceEquals(cast.Choose<PNStat>("target", () => near, reach: 3f), edge), "an entity just beyond the reach is taken within the latency slack (3.5 m, reach 3, slack 1.5)");
            source.Waiting.Add(("target", Named(edge.EntityId.Value))); source.ReachSlack = 0;
            Check(ReferenceEquals(cast.Choose<PNStat>("target", () => near, reach: 3f), near), "without slack it is not");
            source.Waiting.Add(("target", Named(far.EntityId.Value)));
            Check(ReferenceEquals(cast.Choose<PNStat>("target", () => near), far), "with no reach given any ready entity is legal");
            source.Waiting.Add(("target", Named(far.EntityId.Value)));
            Check(ReferenceEquals(cast.Choose<PNStat>("target", () => near, stat => stat != far), near), "the skill's rule can still refuse a legal entity");
            foreach (var bad in new long[] { 0, 777000, eveId })
            {
                source.Waiting.Add(("target", Named(bad)));
                Check(ReferenceEquals(cast.Choose<PNStat>("target", () => near), near), "an entity id of " + bad + " (zero, unknown, the caster itself) falls back to compute");
            }
            source.Waiting.Add(("target", new JObject { ["t"] = "x" })); Check(ReferenceEquals(cast.Choose<PNStat>("target", () => near), near), "a non-numeric entity id falls back to compute");
            skill.TryInterrupt(EveId.Parse("test:g4_end"));
            Check(skill.CurrentInvocation == null, "the cast ended");
            Check(Throws<InvalidOperationException>(() => cast.Choose("count", () => 1)) || cast.Choose("count", () => 1) == 1, "asking an ended cast is compute or an error, never a crash");
            skill.DecisionSource = null;
        }
        finally { Cleanup(hostScene); }
        return "PASS " + n + " skill choice checks (Choose in Predicted / Authority / Observed, value types, validation, reach slack)";
    }
}
#endif
