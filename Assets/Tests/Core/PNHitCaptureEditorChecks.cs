#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
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
            PNStat Dummy(string name, int id, Vector3 where)
            {
                var d = new GameObject(name); d.SetActive(false); SceneManager.MoveGameObjectToScene(d, scene); d.transform.position = where;
                d.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:eve"), 0);
                var s = d.AddComponent<PNPlayerStat>().AsPlayer(); d.AddComponent<Rigidbody>().isKinematic = true;
                var c = d.AddComponent<CapsuleCollider>(); c.radius = .2f; c.height = 1.4f; c.center = Vector3.up * .7f; d.layer = PNLayers.Player;
                d.AddComponent<EveEntityLoader>().Load(hub); d.SetActive(true); return s;
            }
            var landing = new Vector3(4f, 0, 0);
            var near = Dummy("Slam dummy near", 978311, landing + new Vector3(.5f, 0, 0)); a = near.gameObject;     // body edge 0.3 m from the landing: inside 0.7 m
            var far = Dummy("Slam dummy far", 978312, landing + new Vector3(1.2f, 0, 0)); b = far.gameObject;       // body edge 1.0 m: outside 0.7 m, inside 1.4 m
            var skill = go.GetComponent<PNStarforgerSkill>(); var caster = go.GetComponent<PNStat>();
            Check(near.IsReady && far.IsReady && skill.IsReady, "the dummies and the Starforger are ready");
            float Health(PNStat s) => (float)s.GetCurrent(PNContentIds.Health);
            void Slam(string label)
            {
                using (var action = skill.CreateAction(PNStarforgerCombatContent.Slam, out var context, new[] { near }, Vector3.left, landing))
                {
                    var data = context.Data.Action.Perform["hit"];
                    var capture = PNStarforgerSlamHitOperation.Prepare(context, data, out var prepared, out double amount, out float radius, out float height);
                    var op = (PNStarforgerSlamHitOperation)context.Registry.Operations().Get(PNStarforgerCombatContent.SlamHit).Create();
                    op.Bind(capture, prepared, amount, radius, height); Physics.SyncTransforms(); op.Execute(context, data); op.Dispose();
                    Check(radius == capture.Scale(.7f) && capture.Tags.Contains(PNContentIds.MeleeDamage), label + ": radius is the captured scale of the payload and melee_damage joins the tags");
                }
            }
            float n0 = Health(near), f0 = Health(far);
            Slam("scale 1"); Check(Health(near) < n0, "a body 0.3 m from the landing is hit"); Check(Health(far) == f0, "a body 1.0 m from the landing is outside the 0.7 m cylinder");
            caster.SetSource(EveId.Parse("test:melee_scale"), new[] { new EveStatModifier(PNContentIds.MeleeScale, 1d, 1) });
            float n1 = Health(near); Slam("scale 2"); Check(Health(far) < f0 && Health(near) < n1, "melee_scale +100% doubles the slam radius: the 1.0 m body is now hit");
            caster.SetSource(EveId.Parse("test:melee_scale"), Array.Empty<EveStatModifier>());
            return "PASS " + n + " slam capture checks (cylinder hit, melee_scale radius; real colliders in a preview scene)";
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { foreach (var o in new[] { go, a, b }) if (o != null) UnityEngine.Object.DestroyImmediate(o); EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        }
    }
}
#endif
