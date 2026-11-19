#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Native check of the Starforger slam hit (PNHitCapture + a PNHitboxCast cylinder at the landing): real colliders in a preview scene, Edit Mode, no Play Mode.
public static class PNHitCaptureEditorChecks
{
    public static string RunSlam()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Editor required");
        var scene = EditorSceneManager.NewPreviewScene(); bool started = false; int n = 0; GameObject go = null, a = null, b = null;
        void Check(bool ok, string what) { if (!ok) throw new Exception("Slam capture check " + n + ": " + what); n++; }
        try
        {
            var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
            EveRegistrySession.Begin(); started = true; EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:slam-capture"));
            PNVisualPacks.Set("project_n", Application.streamingAssetsPath + "/BaseContent");
            go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starforger/Prefab/Starforger.prefab")); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
            go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(978310), EveId.Parse("project_n:starforger"), 0); go.GetComponent<EveEntityLoader>().Load(hub); go.SetActive(true);
            PNStat Dummy(string name, int id, Vector3 where, bool withBuffs = false)
            {
                var d = new GameObject(name); d.SetActive(false); SceneManager.MoveGameObjectToScene(d, scene); d.transform.position = where;
                d.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:eve"), 0);
                var s = d.AddComponent<PNPlayerStat>().AsPlayer(); d.AddComponent<Rigidbody>().isKinematic = true;
                var c = d.AddComponent<CapsuleCollider>(); c.radius = .2f; c.height = 1.4f; c.center = Vector3.up * .7f; d.layer = PNLayers.Player;
                if (withBuffs) d.AddComponent<PNBuffManager>();
                d.AddComponent<EveEntityLoader>().Load(hub); d.SetActive(true); return s;
            }
            var landing = new Vector3(4f, 0, 0);
            var near = Dummy("Slam dummy near", 978311, landing + new Vector3(.5f, 0, 0)); a = near.gameObject;     // body edge 0.3 m from the landing: inside 0.7 m
            var far = Dummy("Slam dummy far", 978312, landing + new Vector3(1.2f, 0, 0)); b = far.gameObject;       // body edge 1.0 m: outside 0.7 m, inside 1.4 m
            var skill = go.GetComponent<PNStarforgerSkill>(); var caster = go.GetComponent<PNStat>();
            Check(near.IsReady && far.IsReady && skill.IsReady, "the dummies and the Starforger are ready");
            float Health(PNStat s) => (float)s.GetCurrent(PNContentIds.Health);
            void Slam(string label, PNSkillMode mode = PNSkillMode.Authority)
            {
                using (var action = skill.CreateAction(PNStarforgerCombatContent.Slam, out var context, new[] { near }, Vector3.left, landing))
                {
                    context.Mode = mode;
                    var data = context.Data.Action.Perform["hit"];
                    context.BeginAction();
                    try
                    {
                        // The same build the Action does at its telegraph: one melee hitbox (capture + scale), fired by the registered Operation at the landing.
                        using (var box = new PNSkillMeleeHitbox(context, caster, PNSkillValues.Parse(data["value"]), 0d, data["canCrit"].Value<bool>(), 0, PNSkillEffect.Ids(data["tags"])))
                        {
                            float radius = box.ScaleLength(data["radius"].Value<float>()), height = box.ScaleLength(data["height"].Value<float>());
                            var op = (PNStarforgerSlamHitOperation)context.Registry.Operations().Get(PNStarforgerCombatContent.SlamHit).Create();
                            op.Bind(box, radius, height); Physics.SyncTransforms(); op.Execute(context, data); op.Dispose();
                            if (mode == PNSkillMode.Authority) Check(radius == PNHitCapture.ScaleLength(.7f, box.Scale) && box.Scale >= 1d, label + ": radius is the captured scale of the payload");
                        }
                    }
                    finally { context.CancelEffects(); }
                }
            }
            float n0 = Health(near), f0 = Health(far);
            Slam("scale 1"); Check(Health(near) < n0, "a body 0.3 m from the landing is hit"); Check(Health(far) == f0, "a body 1.0 m from the landing is outside the 0.7 m cylinder");
            caster.SetSource(EveId.Parse("test:melee_scale"), new[] { new EveStatModifier(PNContentIds.MeleeScale, 1d, 1) });
            // Snapshot export: copies of the captured modifiers, filterable, unaffected by later changes to the source.
            var exportSnapshot = caster.CaptureCombatSource(); var exported = exportSnapshot.ExportModifiers();
            Check(exported.Any(m => m.Id == PNContentIds.MeleeScale && m.Value == 1d && m.Group == 1), "ExportModifiers carries the captured melee_scale modifier");
            Check(exportSnapshot.ExportModifiers(m => m.Id != PNContentIds.MeleeScale).All(m => m.Id != PNContentIds.MeleeScale), "ExportModifiers filter drops what it rejects");
            Check(!ReferenceEquals(exported[0], exportSnapshot.ExportModifiers()[0]), "each export is a fresh copy");
            float n1 = Health(near); Slam("scale 2"); Check(Health(far) < f0 && Health(near) < n1, "melee_scale +100% doubles the slam radius: the 1.0 m body is now hit");
            caster.SetSource(EveId.Parse("test:melee_scale"), Array.Empty<EveStatModifier>());
            Check(exportSnapshot.ExportModifiers().Any(m => m.Id == PNContentIds.MeleeScale), "the snapshot keeps the modifier after the source dropped it");
            // The same slam in the other two modes deals nothing and throws nothing (the hitbox service skips it).
            float n2 = Health(near), f2 = Health(far);
            Slam("observed", PNSkillMode.Observed); Check(Health(near) == n2 && Health(far) == f2, "an Observed slam deals no damage");
            Slam("predicted", PNSkillMode.Predicted); Check(Health(near) == n2 && Health(far) == f2, "a Predicted slam with no report sink deals no damage");

            // Buff and heal context services: the authority does them, the other modes skip them without throwing.
            var patient = Dummy("Heal dummy", 978313, new Vector3(20f, 0, 0), true); var patientObject = patient.gameObject; try
            {
                var buffs = patientObject.GetComponent<PNBuffManager>(); Check(buffs != null && buffs.IsReady, "the heal dummy has a ready Buff manager");
                patient.ReceiveDamage(caster.CreateDamage(10d, false, null, EveId.Parse("base:physical")));
                float hurt = Health(patient);
                using (var action = skill.CreateAction(PNStarforgerCombatContent.Slam, out var context, new[] { patient }, Vector3.left, landing))
                {
                    context.BeginAction();
                    try
                    {
                        PNBuffApplyRequest Guard() => new PNBuffApplyRequest(EveId.Parse("project_n:block_hits"), PNCombatSource.Capture(caster),
                            new JObject { ["duration"] = 5d, ["hits"] = 1 });
                        foreach (var other in new[] { PNSkillMode.Observed, PNSkillMode.Predicted })
                        {
                            context.Mode = other;
                            var skipped = context.ApplyBuff(buffs, Guard());
                            Check(!skipped.Applied && !skipped.Succeeded, other + ": a Buff is skipped");
                            Check(!context.Heal(patient, new PNHeal(patient.SessionId, caster.EntityId, 5d)).Applied && Health(patient) == hurt, other + ": a heal is skipped and heals nothing");
                        }
                        context.Mode = PNSkillMode.Authority;
                        var done = context.ApplyBuff(buffs, Guard());
                        // A preview-scene entity is not "executing", so its Buff manager answers Rejected (Target suspended); what is checked here is that only the authority reaches the manager and gets a receipt.
                        Check(done.Applied && done.Receipt != null, "the authority hands the Buff to the manager and gets its receipt (" + (done.Applied ? done.Receipt.Status + ", " + done.Receipt.Message : "skipped") + ")");
                        if (done.Succeeded) buffs.Buffs.Remove(done.Receipt.Handle, PNBuffEndReason.Dispelled, true);
                        Check(context.Heal(patient, new PNHeal(patient.SessionId, caster.EntityId, 5d)).Applied && Health(patient) > hurt, "the authority heals");
                    }
                    finally { context.CancelEffects(); }
                }
            }
            finally { if (patientObject != null) UnityEngine.Object.DestroyImmediate(patientObject); }
            return "PASS " + n + " slam capture checks (cylinder hit, melee_scale radius, three-mode skip, Buff/heal context services; real colliders in a preview scene)";
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { foreach (var o in new[] { go, a, b }) if (o != null) UnityEngine.Object.DestroyImmediate(o); EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        }
    }
}
#endif
