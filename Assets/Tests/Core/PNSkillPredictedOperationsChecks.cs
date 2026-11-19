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

// Docs/SpawnedObjectSyncPlan.md 4.6a, Edit Mode: a skill's effect trigger runs EVERY registered Operation in a Predicted cast (a guess on a machine without authority). The Operations know nothing about it: whatever only the
// authority may do goes through a door that reads the context's mode. This check lists the registry, runs each Operation in a Predicted invocation on a client session (with the real payload of the content when it has
// one) and requires: no exception, and no change of the authority-only state (health, entity count, the movement lease). An Operation whose payload the check cannot supply is reported by name in the result (its payload is
// refused before any door, which is allowed), so a new Operation never goes unlisted.
public static class PNSkillPredictedOperationsChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Predicted Operations check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        const long owner = 5, eveId = 995071;
        var entry = PNGameplayContentModule.EveAttackEntry;

        // The real payloads the content authors for an Operation id (first one found in any document).
        var payloads = new Dictionary<string, JToken>();
        void Collect(JToken token)
        {
            if (token is JObject obj)
            {
                if ((string)obj["kind"] == "Operation" && obj["id"] != null && obj["data"] != null) payloads.TryAdd(ShortId((string)obj["id"]), obj["data"]);
                foreach (var property in obj.Properties()) Collect(property.Value);
            }
            else if (token is JArray array) foreach (var item in array) Collect(item);
        }
        foreach (var file in Directory.GetFiles(Path.Combine(Application.streamingAssetsPath, "BaseContent"), "*.json", SearchOption.AllDirectories))
            try { Collect(JToken.Parse(File.ReadAllText(file))); } catch (Exception) { }

        var scene = EditorSceneManager.NewPreviewScene();
        var uncovered = new List<string>(); var covered = new List<string>();
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:o46a_client"));
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { owner });
            var areaRoot = new GameObject("O46a area manager"); SceneManager.MoveGameObjectToScene(areaRoot, scene); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            var projectileRoot = new GameObject("O46a projectile manager"); SceneManager.MoveGameObjectToScene(projectileRoot, scene); projectileRoot.AddComponent<PNProjectileManager>().Initialize(EveRegistrySession.Context);
            var actor = new GameObject("O46a client Eve"); actor.SetActive(false); SceneManager.MoveGameObjectToScene(actor, scene);
            actor.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(eveId), EveId.Parse("project_n:eve"), owner); actor.layer = PNLayers.Player;
            actor.AddComponent<EveCharacterStat>(); actor.AddComponent<PNAbilityManager>();
            var body = actor.AddComponent<Rigidbody>(); body.useGravity = false; var capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
            actor.AddComponent<PNPlayerMove>(); var skill = actor.AddComponent<EveClientRoleProbeSkill>(); skill.Prepare(); actor.AddComponent<EveEntityLoader>().Load(hub); actor.SetActive(true);
            Physics.SyncTransforms();
            var self = skill.GetComponent<PNStat>();

            var invocation = skill.BeginNonAuthorityInvocation(entry, PNSkillMode.Predicted);
            Check(invocation.Mode == PNSkillMode.Predicted && !EveRegistrySession.Context.HasAuthority, "a client opens a Predicted invocation on a session without authority");
            int Entities() => UnityEngine.Object.FindObjectsByType<EveEntityIdentity>(FindObjectsInactive.Include).Length;
            var ids = hub.Operations().Snapshot().Keys.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
            Check(ids.Length >= 9, "the registry lists the Operations (" + ids.Length + ")");
            foreach (var id in ids)
            {
                payloads.TryGetValue(ShortId(id.ToString()), out var payload);
                double health = self.GetCurrent(PNContentIds.Health); int entities = Entities();
                var context = new PNSkillActionContext(skill, invocation.SkillId, null, hub, null, Vector3.right, invocation, true, actor.transform.position);
                using (var operation = hub.Operations().Get(id).Create())
                {
                    bool refused = false;
                    try { operation.Execute(context, payload); }
                    catch (Exception error) when (payload == null && (error is FormatException || error is ArgumentException || error is InvalidCastException || error is NullReferenceException || error is Newtonsoft.Json.JsonException)) { refused = true; }
                    if (refused) uncovered.Add(id.ToString()); else covered.Add(id.ToString());
                }
                Check(self.GetCurrent(PNContentIds.Health) == health, id + " changed health in a Predicted cast");
                Check(Entities() == entities, id + " made an entity in a Predicted cast");
                Check(skill.Controls.IsAllowed(PNControlIds.Move), id + " took the movement lease in a Predicted cast");
            }
            Check(covered.Contains("project_n:create_projectile") || covered.Contains("base:create_projectile") || covered.Any(x => x.EndsWith(":create_projectile")), "create_projectile ran with its real payload");
            skill.EndNonAuthorityInvocation();
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
        return "PASS " + n + " checks; ran " + covered.Count + " Operations (" + string.Join(", ", covered) + "); payload not supplied for " + uncovered.Count + " (" + string.Join(", ", uncovered) + ")";
    }
    private static string ShortId(string id) { int colon = id.IndexOf(':'); return colon < 0 ? id : id.Substring(colon + 1); }
}
#endif
