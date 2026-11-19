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

// Docs/SpawnedObjectSyncPlan.md 4.6b, Edit Mode: a guess (Predicted cast on a machine without authority) runs the skill's Action effects and fires the activation triggers without committing; a refused guess
// cancels what it started (the Independent effects too) and fires nothing more (not even its cancel triggers). The effect used is the registered motion Action, observed through a real Rigidbody.
public class PNO46bProbe : PNSkill
{
    public static readonly EveId Activated = EveId.Parse("test:o46b_activated"), Refused = EveId.Parse("test:o46b_refused"), Complete = EveId.Parse("test:o46b_complete");
    public override System.Collections.Generic.IEnumerable<EveId> BuiltInSkillIds => new[] { Activated, Refused, Complete };
    public void Commit() => CommitActivation(null, Vector3.right);
    public void Tick(float delta) => AdvanceAuxiliaryEffects(delta);
}
public sealed class PNO46bModule : EveIContentModule
{
    private static string Motion(string trigger, string ownership, double distance) => "{'trigger':'" + trigger + "','kind':'Action','id':'base:motion_action','data':{'perform':{'distance':" + distance + ",'duration':1,'ignoreEntities':true,'permissionId':'base:motion_voluntary'}},'direction':'Invocation','ownership':'" + ownership + "','affectedByAttackSpeed':false}";
    private static void Skill(EveRegistryHub hub, EveId id, params string[] effects)
        => hub.Skills.Register(new EveSkillDefinition(id, JObject.Parse("{'categoryIds':['base:skill'],'affectedByAttackSpeed':false,'effects':[" + string.Join(",", effects) + "]}")));
    public void Register(EveRegistryHub hub)
    {
        Skill(hub, PNO46bProbe.Activated, Motion("OnActivated", "Parent", 1));
        Skill(hub, PNO46bProbe.Refused, Motion("OnActivated", "Independent", 1), Motion("OnInvocationCancel", "Independent", 3));
        Skill(hub, PNO46bProbe.Complete, Motion("OnInvocationComplete", "Independent", 1));
    }
}
public static class PNSkillPredictedActionEffectsChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Predicted Action effects check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNO46bModule() }, docs);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:o46b_client"));
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { 5L });
            var actor = new GameObject("O46b client"); actor.SetActive(false); SceneManager.MoveGameObjectToScene(actor, scene);
            actor.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(995072), EveId.Parse("project_n:eve"), 5);
            actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>(); var skill = actor.AddComponent<PNO46bProbe>(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false;
            Check(!EveRegistrySession.Context.HasAuthority, "the session has no authority");
            

            // A guess fires OnActivated (no commit) and runs the Action effect there.
            {
                long serial = skill.ActivationSerial; var guess = skill.BeginNonAuthorityInvocation(PNO46bProbe.Activated, PNSkillMode.Predicted);
                skill.Commit(); skill.Tick(.25f);
                Check(skill.ActivationSerial == serial, "the guess committed no activation (no serial)");
                Check(body.position.x > .2f, "the Action effect at OnActivated ran in the guess and moved the body (" + body.position.x + ")");
                skill.EndNonAuthorityInvocation(true, "test"); skill.Tick(.25f);
                Check(skill.Controls.IsAllowed(PNControlIds.Move), "ending the guess returns the motion lease");
            }
            body.position = Vector3.zero; body.linearVelocity = Vector3.zero;

            // A refused guess cancels its Independent effect and starts nothing from its cancel triggers.
            {
                var guess = skill.BeginNonAuthorityInvocation(PNO46bProbe.Refused, PNSkillMode.Predicted);
                skill.Commit(); skill.Tick(.25f); float before = body.position.x;
                Check(before > .2f, "the Independent effect runs in the guess");
                skill.RollbackPredictedCast(guess); skill.Tick(.5f);
                Check(Mathf.Approximately(body.position.x, before), "a refused guess cancels its Independent effect and starts none from its cancel triggers (" + before + " -> " + body.position.x + ")");
                Check(skill.CurrentInvocation == null && skill.Controls.IsAllowed(PNControlIds.Move), "the refused guess left no cast and no lease");
            }
            body.position = Vector3.zero; body.linearVelocity = Vector3.zero;

            // A guess that ends on its own fires OnInvocationComplete (no commit).
            {
                long serial = skill.ActivationSerial; skill.BeginNonAuthorityInvocation(PNO46bProbe.Complete, PNSkillMode.Predicted);
                skill.Commit(); skill.EndNonAuthorityInvocation(); skill.Tick(.5f);
                Check(body.position.x > .3f && skill.ActivationSerial == serial, "OnInvocationComplete fires for a guess that ends on its own, still without a commit (" + body.position.x + ")");
            }
            // An Observed replay runs no effect.
            skill.Tick(1f); skill.Tick(1f);
            body.position = Vector3.zero; body.linearVelocity = Vector3.zero;
            {
                skill.BeginNonAuthorityInvocation(PNO46bProbe.Activated, PNSkillMode.Observed); skill.Commit(); skill.Tick(.25f);
                Check(Mathf.Approximately(body.position.x, 0) && skill.ActivationSerial == 0, "an Observed replay fires no activation and runs no effect");
                skill.EndNonAuthorityInvocation();
            }
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
        return "PASS " + n + " predicted Action effect checks";
    }
}
#endif
