#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/SkillNetworkFrameworkPlan.md G1, Edit Mode: a cast knows where it runs (Authority / Predicted / Observed). The authority's casts say Authority; a machine without authority can open a Predicted or
// Observed invocation (never committed: no activation serial) and the action context reads the mode from it; the old `IsPrediction` flag is a view of the mode. Nothing else changes (golden trace).
public static class PNSkillModeChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Skill mode check " + n + ": " + what); n++; }
        bool Throws(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } catch (ArgumentException) { return true; } }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        const long owner = 5, eveId = 995070;

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
        EveClientRoleProbeSkill Spawn(Scene preview, string name)
        {
            var actor = Root(preview, name, eveId, owner); actor.layer = PNLayers.Player; actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveClientRoleProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            Physics.SyncTransforms(); return skill;
        }
        var entry = PNGameplayContentModule.EveAttackEntry;
        var damageId = PNGameplayContentModule.DamageAction;
        var payload = JObject.Parse(@"{""value"":{""amount"":10},""canCrit"":false,""iframeDuration"":0.75,""tags"":[""project_n:true""]}");
        void DamageAction(EveClientRoleProbeSkill skill, PNSkillMode mode, Action<PNDamageAction> check)
        {
            var self = skill.GetComponent<PNStat>();
            var data = new PNSkillData(null, new PNSkillActionData(damageId, perform: payload));
            var damage = new PNSkillActionContext(skill, entry, data, hub, new[] { self, self }) { Mode = mode };
            using var action = (PNDamageAction)hub.Actions().Get(damageId).Create();
            action.Start(damage);
            action.Perform(damage);
            check(action);
            action.Exit(damage);
        }

        // ---- the authority (offline): real casts say Authority ----
        var hostScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:g1_authority"));
            var areaRoot = new GameObject("G1 area manager"); SceneManager.MoveGameObjectToScene(areaRoot, hostScene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var skill = Spawn(hostScene, "G1 authority Eve");
            Check(skill.TryAttack(Vector3.right) && skill.CurrentInvocation != null, "the authority's attack opens an invocation");
            Check(skill.CurrentInvocation.Mode == PNSkillMode.Authority, "a real cast is an Authority invocation");
            Check(Throws(() => skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Predicted)), "a second cast cannot open while one runs");
            skill.TryInterrupt(EveId.Parse("test:g1_end")); Check(skill.CurrentInvocation == null, "the interrupted cast leaves no invocation");
            Check(Throws(() => skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Predicted)), "the authority refuses a Predicted invocation");
            Check(Throws(() => skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Observed)), "the authority refuses an Observed invocation");
            Check(Throws(() => skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Authority)), "Authority invocations only begin through the activation path");
            var context = new PNSkillActionContext(skill, hub.Skills.ResolveIdentity(EveId.Parse("base:eve_moveset")), skill.GetData(EveId.Parse("base:eve_moveset")), hub, null, Vector3.right);
            Check(context.Mode == PNSkillMode.Authority && !context.IsPrediction, "a context with no invocation says Authority");
            context.IsPrediction = true; Check(context.Mode == PNSkillMode.Predicted && context.IsPrediction, "the old flag sets the mode Predicted");
            context.IsPrediction = false; Check(context.Mode == PNSkillMode.Authority && !context.IsPrediction, "clearing the flag restores Authority");
            context.Mode = PNSkillMode.Observed; Check(context.Mode == PNSkillMode.Observed && !context.IsPrediction, "a context can be given any mode directly");
            // G2 Control: the lock Operation takes the lease only in Authority mode.
            var lockId = EveId.Parse("base:lock_movement");
            foreach (var mode in new[] { PNSkillMode.Predicted, PNSkillMode.Observed })
            {
                context.Mode = mode; var op = hub.Operations().Get(lockId).Create(); op.Execute(context, null);
                Check(skill.Controls.IsAllowed(PNControlIds.Move), mode + " mode takes no Control lease"); op.Dispose();
            }
            context.Mode = PNSkillMode.Authority; var real = hub.Operations().Get(lockId).Create(); real.Execute(context, null);
            Check(!skill.Controls.IsAllowed(PNControlIds.Move), "Authority mode takes the lease"); real.Dispose();
            Check(skill.Controls.IsAllowed(PNControlIds.Move), "disposing the Operation releases it");
            // G2 hit: Authority deals damage and the skill hears OnHitConfirmed; Predicted hands the hit to the sink; Observed does nothing; reading the result of a skipped hit is an error.
            var hitSink = new HitSink(); skill.ReportSink = hitSink; var self = skill.GetComponent<PNStat>();
            context.Mode = PNSkillMode.Predicted; var predictedHit = context.Hit(self, null, null, 0, null, 3);
            Check(!predictedHit.Applied && predictedHit.Status == PNHitStatus.Skipped && !predictedHit.Reported && hitSink.Hits == 0, "a predicted hit of a context with no keyed cast deals nothing and reports nothing");
            Check(Throws(() => { var r = predictedHit.Result; }), "reading the result of a skipped hit is an error");
            context.Mode = PNSkillMode.Observed; var observedHit = context.Hit(null, null, null, 0, null);
            Check(!observedHit.Applied && !observedHit.Reported && hitSink.Hits == 0, "an observed hit does nothing and reports nothing");
            // G2 Operations: Heal and the Buff Operations skip quietly in a guess or a replay (no payload is even read).
            foreach (var opId in new[] { "project_n:apply_buff", "project_n:remove_buff", "project_n:bypass_remove_buff", "project_n:trigger_buff", "project_n:change_buff_stacks", PNGameplayContentModule.HealOperation.ToString() })
                foreach (var mode in new[] { PNSkillMode.Predicted, PNSkillMode.Observed })
                {
                    context.Mode = mode; var op = hub.Operations().Get(EveId.Parse(opId)).Create(); bool threw = false;
                    try { op.Execute(context, null); } catch (Exception) { threw = true; } finally { op.Dispose(); }
                    Check(!threw, opId + " is skipped quietly in " + mode + " mode");
                    if (op is PNApplyBuffOperation applied) Check(applied.Skipped && applied.Results.Count == 0, "the Buff grant says it was skipped");
                }
            double hostHealth = self.GetCurrent(PNContentIds.Health);
            DamageAction(skill, PNSkillMode.Predicted, action =>
                Check(action.Damage == null && action.Results.Count == 0 && hitSink.Hits == 0 &&
                    self.GetCurrent(PNContentIds.Health) == hostHealth && skill.ConfirmedHits == 0,
                    "explicit damage without a cast key skips without confirmation"));
            DamageAction(skill, PNSkillMode.Authority, action =>
                Check(action.Results.Count == 1 && skill.ConfirmedHits == 1 && hitSink.Hits == 0 &&
                    self.GetCurrent(PNContentIds.Health) == hostHealth - 10 &&
                    !action.Damage.CanCrit && action.Damage.IFrameDuration == .75,
                    "authority confirms duplicate targets once with authored crit and iframe policy"));
            skill.ReportSink = null; context.Mode = PNSkillMode.Predicted; Check(!context.Hit(self, null, null, 0, null).Reported, "a predicted hit with no sink is skipped quietly");
        }
        finally { Cleanup(hostScene); }

        // ---- a client: Predicted and Observed invocations ----
        var clientScene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:g1_client"));
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { owner });
            var areaRoot = new GameObject("G1 client area manager"); SceneManager.MoveGameObjectToScene(areaRoot, clientScene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var skill = Spawn(clientScene, "G1 client Eve");
            using (var sync = new PNSyncService(hub, false, owner, EveRegistrySession.Context))
            {
                var scale = skill.gameObject.AddComponent<PNSkillScaleState>();
                var actor = skill.GetComponent<EveEntityLoader>();
                scale.OnBound(sync, PNSyncTarget.ForEntity(actor.LoadContext.Identity.Id, actor.Generation));
                try
                {
                    Check(scale.ReadScale(PNContentIds.MeleeScale) == 1, "entity factors default to neutral before the first image");
                    var ids = hub.Tags().Snapshot().Values.Where(tag => tag.SkillReplicaFactor)
                        .Select(tag => tag.Id).OrderBy(id => id.ToString(), StringComparer.Ordinal);
                    var row = new JObject { ["w"] = Guid.Empty.ToString("N"), ["n"] = 0, ["b"] = 0,
                        ["f"] = new JArray(ids.Select(id => id == PNContentIds.MeleeScale ? 2d : 1d)) };
                    scale.Apply(row, 1);
                    Check(scale.ReadScale(PNContentIds.MeleeScale) == 2 && scale.ReadScale(PNContentIds.MeleeDamage) == 1,
                        "a bounded image applies registered scale columns only");
                    var bad = (JObject)row.DeepClone();
                    bad["f"][0] = -1;
                    bool rejected = false;
                    try { scale.Apply(bad, 2); } catch (FormatException) { rejected = true; }
                    Check(rejected && scale.ReadScale(PNContentIds.MeleeScale) == 2, "invalid factors leave the last valid image intact");
                    var foreign = (JObject)row.DeepClone();
                    foreign["w"] = Guid.NewGuid().ToString("N");
                    foreign["f"] = new JArray(ids.Select(_ => 3d));
                    scale.Apply(foreign, 3);
                    Check(scale.ReadScale(PNContentIds.MeleeScale) == 2, "a later image from a different world cannot replace the current scale");
                }
                finally { scale.OnUnbound(); }
                Check(scale.ReadScale(PNContentIds.MeleeScale) == 1, "unbinding clears the replica scale");
            }
            long serial = skill.ActivationSerial;
            Check(skill.CurrentInvocation == null, "a client starts with no invocation");
            var predicted = skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Predicted);
            Check(predicted.Mode == PNSkillMode.Predicted && ReferenceEquals(skill.CurrentInvocation, predicted) && !predicted.IsEnded, "a client can open a Predicted invocation");
            Check(skill.ActivationSerial == serial && !predicted.Committed(), "it is never committed: no activation serial");
            // G2 Ability: the Predicted invocation owns the scope; inside it a state is granted through the instance the replica holds and rolled back; outside any scope the write is refused as before.
            Check(predicted.PredictionScope != null && predicted.PredictionScope.IsRecording, "a Predicted invocation opens an Ability scope");
            Check(!skill.Empowerment.IsActive, "the empowerment starts off");
            Check(skill.Abilities.GrantState(EveCharacterSkill.EmpowermentId) != null && skill.Empowerment.IsActive, "a guess grants a state through the instance the replica holds");
            predicted.PredictionScope.Rollback();
            Check(!skill.Empowerment.IsActive, "rolling the scope back takes the grant back");
            var pctx = new PNSkillActionContext(skill, predicted.SkillId, null, hub, null, Vector3.right, predicted);
            Check(pctx.Mode == PNSkillMode.Predicted && pctx.IsPrediction, "the context reads Predicted from its invocation");
            // G6c: a hit of a guessed cast is reported by the framework under the cast's key; each context has its own step; and the cast has random numbers every machine draws the same
            var keySink = new HitSink(); skill.ReportSink = keySink; var own = skill.GetComponent<PNStat>(); var pctx2 = new PNSkillActionContext(skill, predicted.SkillId, null, hub, null, Vector3.right, predicted);
            Check(predicted.CastKey > 0 && pctx.Hit(own, null, null, 0, null).Reported && keySink.Hits == 1 && keySink.LastKey == predicted.CastKey && keySink.LastStep == 0, "a guessed hit is reported under the cast's key, step 0");
            Check(pctx.Hit(own, null, null, 0, null, 5).Reported && keySink.LastStep == 0 && pctx2.Hit(own, null, null, 0, null).Reported && keySink.LastStep == 1 && keySink.Hits == 3, "one context keeps its step, the next context of the cast gets the next one");
            Check(!pctx.Hit(null, null, null, 0, null).Reported && keySink.Hits == 3, "a hit with no target reports nothing");
            double replicaHealth = own.GetCurrent(PNContentIds.Health);
            DamageAction(skill, PNSkillMode.Predicted, action =>
                Check(action.Damage == null && action.Results.Count == 0 && keySink.Hits == 4 &&
                    keySink.LastKey == predicted.CastKey && keySink.LastStep == 2 &&
                    own.GetCurrent(PNContentIds.Health) == replicaHealth && skill.ConfirmedHits == 0,
                    "explicit predicted damage reports one deduplicated target with its cast key and step"));
            skill.ReportSink = null;
            DamageAction(skill, PNSkillMode.Predicted, action =>
                Check(action.Damage == null && action.Results.Count == 0 && keySink.Hits == 4 &&
                    own.GetCurrent(PNContentIds.Health) == replicaHealth && skill.ConfirmedHits == 0,
                    "explicit predicted damage without a sink skips quietly"));
            skill.ReportSink = keySink;
            var again = new PNSkillRandom(PNSkillRandom.Seed(predicted.CastKey, own.EntityId.Value)); ulong a = predicted.Random.NextUInt64();
            Check(a == again.NextUInt64() && ReferenceEquals(predicted.Random, predicted.Random), "cast.Random is seeded by the cast's key and entity");
            var x = new PNSkillRandom(42); var y = new PNSkillRandom(42); var z = new PNSkillRandom(43); bool same = true, differs = false, inRange = true;
            for (int i = 0; i < 64; i++) { ulong vx = x.NextUInt64(); same &= vx == y.NextUInt64(); differs |= vx != z.NextUInt64(); double d = new PNSkillRandom((ulong)i).NextDouble(); inRange &= d >= 0 && d < 1; }
            Check(same && differs && inRange, "the generator is deterministic, depends on the seed and stays in [0, 1)");
            var dice = new PNSkillRandom(7); int lo = 0, hi = 0, hits = 0; for (int i = 0; i < 4000; i++) { int r = dice.Range(3, 6); if (r == 3) lo++; if (r == 5) hi++; if (r < 3 || r > 5) lo = -99999; if (dice.Chance(.25)) hits++; }
            Check(lo > 1000 && hi > 1000 && hits > 800 && hits < 1200 && !dice.Chance(0) && dice.Chance(1), "Range is half open and Chance follows its probability (" + lo + "/" + hi + "/" + hits + ")");
            skill.ReportSink = null;
            pctx.IsPrediction = false; Check(pctx.Mode == PNSkillMode.Authority && !pctx.IsPrediction, "clearing the flag overrides the invocation's mode as before");
            Check(Throws(() => skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Observed)), "only one invocation runs at a time");
            Check(Throws(() => skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Authority)), "a client never opens an Authority invocation");
            skill.EndNonAuthorityInvocation();
            Check(skill.CurrentInvocation == null && predicted.IsEnded && !predicted.PredictionScope.IsRecording, "ending it ends the invocation and its Ability scope");
            Check(Throws(() => skill.Abilities.GrantState(EveCharacterSkill.EmpowermentId)), "outside a scope a replica still refuses the write");
            skill.EndNonAuthorityInvocation(); Check(skill.CurrentInvocation == null, "ending twice is harmless");
            // G6c: the key of a cast is the input being dispatched, shifted; a cast with no input of its own takes the previous key + 1
            long before = skill.LastCastKey; skill.PendingCastKey = before / 256 + 5;
            var k1 = skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Predicted); long key1 = k1.CastKey; skill.EndNonAuthorityInvocation(); skill.PendingCastKey = 0;
            Check(key1 == PNSkillCastKey.FromInput(before / 256 + 5) && PNSkillCastKey.InputOf(key1) == before / 256 + 5, "a cast begun while an input is dispatched takes that input's key (" + key1 + ")");
            var k2 = skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Predicted); long key2 = k2.CastKey; skill.EndNonAuthorityInvocation();
            Check(key2 == key1 + 1 && skill.LastCastKey == key2, "a cast with no input of its own takes the previous key + 1");
            skill.PendingCastKey = PNSkillCastKey.InputOf(key1); var k3 = skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Predicted); long key3 = k3.CastKey; skill.EndNonAuthorityInvocation(); skill.PendingCastKey = 0;
            Check(key3 == key2 + 1, "keys only grow, even when an input's key is reused");

            var observed = skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Observed);
            var octx = new PNSkillActionContext(skill, observed.SkillId, null, hub, null, Vector3.right, observed);
            Check(observed.Mode == PNSkillMode.Observed && octx.Mode == PNSkillMode.Observed && !octx.IsPrediction, "an Observed invocation and its context say Observed (not a prediction)");
            skill.ReportSink = keySink;
            DamageAction(skill, PNSkillMode.Observed, action =>
                Check(action.Damage == null && action.Results.Count == 0 && keySink.Hits == 4 &&
                    own.GetCurrent(PNContentIds.Health) == replicaHealth && skill.ConfirmedHits == 0,
                    "observed explicit damage leaves HP, report sink and confirmation untouched"));
            Check(!skill.CanActivateSkill(entry), "a client still cannot activate a real cast (unchanged)");
            skill.EndNonAuthorityInvocation(); Check(observed.IsEnded && skill.CurrentInvocation == null, "the Observed invocation ends clean");
            Check(skill.ActivationSerial == serial, "no activation was counted on the client");
        }
        finally { Cleanup(clientScene); }
        return "PASS " + n + " skill mode checks (authority says Authority, client opens Predicted / Observed, never committed, context reads the mode, old flag is a view of it)";
    }

    private sealed class HitSink : IPNSkillReportSink
    {
        public int Hits; public long LastKey; public int LastStep;
        public void ReportHit(long castKey, PNStat target, int step) { Hits++; LastKey = castKey; LastStep = step; }
        public void ReportDecision(long castKey, string point, JToken value) { }
    }
    private static bool Committed(this PNSkillInvocation invocation) => (bool)typeof(PNSkillInvocation).GetField("Committed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(invocation);
}
#endif
