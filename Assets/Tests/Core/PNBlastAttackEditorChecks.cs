#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/StrikeBlastPlan.md B2/B3 on the real prefabs: the Starweaver triangle is a Polygon Blast dependent on its cast (a cancelled cast ends it, a finished cast does not),
// and the Starreaper rain is a chain of Owner-dependent Cylinder Blasts (the cast's cancel leaves them, the owner's loss ends them all). Edit Mode, a preview scene, nothing saved.
public static class PNBlastAttackEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Editor required");
        var scene = EditorSceneManager.NewPreviewScene(); bool started = false; int n = 0; var made = new List<GameObject>();
        void Check(bool ok, string what) { if (!ok) throw new Exception("Blast attack check " + n + ": " + what); n++; }
        try
        {
            var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
            EveRegistrySession.Begin(); started = true; EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:blast-attacks"));
            PNVisualPacks.Set("project_n", Application.streamingAssetsPath + "/BaseContent");
            var rootObject = new GameObject("Blast attack manager fixture"); SceneManager.MoveGameObjectToScene(rootObject, scene); made.Add(rootObject);
            var blasts = rootObject.AddComponent<PNBlastManager>(); blasts.Initialize(EveRegistrySession.Context);
            GameObject Spawn(string path, string id, int entity, Vector3 at)
            {
                var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = at;
                go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(entity), EveId.Parse(id), 0); go.GetComponent<EveEntityLoader>().Load(hub); go.SetActive(true); made.Add(go); return go;
            }
            PNStat Dummy(int id, Vector3 where)
            {
                var d = new GameObject("Blast attack dummy " + id); d.SetActive(false); SceneManager.MoveGameObjectToScene(d, scene); d.transform.position = where;
                d.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:eve"), 0);
                var s = d.AddComponent<PNPlayerStat>().AsPlayer(); d.AddComponent<Rigidbody>().isKinematic = true;
                var c = d.AddComponent<CapsuleCollider>(); c.radius = .2f; c.height = 1.4f; c.center = Vector3.up * .7f; d.layer = PNLayers.Player;
                d.AddComponent<EveEntityLoader>().Load(hub); d.SetActive(true); made.Add(d); return s;
            }
            float Health(PNStat s) => (float)s.GetCurrent(PNContentIds.Health);

            // ---- Starweaver: the triangle ----
            var weaver = Spawn("Assets/Game/Entity/Enemy/Starweaver/Prefab/Starweaver.prefab", "project_n:starweaver", 978400, new Vector3(-6, 0, 0));
            var tipTarget = Dummy(978401, new Vector3(0, 0, 1.0f)); var cornerTarget = Dummy(978402, new Vector3(1.0f, 0, 1.0f)); Physics.SyncTransforms();
            var skill = weaver.GetComponent<PNStarweaverSkill>(); Check(skill.IsReady && tipTarget.IsReady, "the Starweaver and its targets are ready");
            float tip0 = Health(tipTarget), corner0 = Health(cornerTarget);
            PNTimedBlast Running() => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimedBlast>(true)).FirstOrDefault(x => x.IsRunning);
            var action = skill.CreateAction(PNStarweaverCombatContent.Triangle, out var context, new[] { tipTarget }, Vector3.right, Vector3.zero);
            action.Start(context);
            var triangle = Running();
            Check(triangle is PNStarweaverTriangleBlast && triangle.Dependency == PNBlastDependency.Action && Math.Abs(triangle.Delay - 2f) < 1e-5 && Math.Abs(triangle.Radius - 1.2f) < 1e-5, "starting the cast creates the triangle blast (Action dependency, delay 2 s, radius 1.2)");
            blasts.Advance(1f); action.AdvanceAuxiliary(1f);
            Check(Running() == triangle && Health(tipTarget) == tip0, "half way through the windup the blast waits");
            action.Cancel(context); blasts.Advance(.1f);
            Check(Running() == null && Health(tipTarget) == tip0, "a cancelled cast ends its blast and nothing is hit");
            action.Dispose();

            action = skill.CreateAction(PNStarweaverCombatContent.Triangle, out context, new[] { tipTarget }, Vector3.right, Vector3.zero); action.Start(context);
            for (int i = 0; i < 21; i++) { blasts.Advance(.1f); action.AdvanceAuxiliary(.1f); }
            Check(Health(tipTarget) < tip0 && Health(cornerTarget) == corner0, "a finished windup detonates: the tip is hit once, the corner is not");
            float afterHit = Health(tipTarget);
            for (int i = 0; i < 15; i++) { blasts.Advance(.1f); if (action.AdvanceAuxiliary(.1f)) break; }
            Check(Health(tipTarget) == afterHit && Running() == null, "the blast ends with the cast and never hits twice");
            try { action.Exit(context); } catch (Exception) { }

            // ---- Starreaper: the rain ----
            var reaper = Spawn("Assets/Game/Entity/Enemy/Starreaper/Prefab/Starreaper.prefab", "project_n:starreaper", 978410, new Vector3(-8, 0, 3));
            var rainSkill = reaper.GetComponent<PNStarreaperSkill>(); var reaperStat = reaper.GetComponent<PNStat>(); Check(rainSkill.IsReady, "the Starreaper is ready");
            var rainTarget = Dummy(978411, new Vector3(0, 0, 3)); Physics.SyncTransforms();
            var rain = rainSkill.CreateAction(PNStarreaperCombatContent.Rain, out var rainContext, new[] { rainTarget }, Vector3.right, rainTarget.transform.position);
            rain.Start(rainContext);
            for (int i = 0; i < 25 && blasts.ActiveCount == 0; i++) rain.AdvanceAuxiliary(.1f);
            int count = blasts.ActiveCount; var all = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNStarreaperRainBlast>(true)).Where(x => x.IsRunning).OrderBy(x => x.Delay).ToArray();
            Check(count >= 5 && count <= 7 && all.Length == count, "the rain creates 5 to 7 landing blasts (" + count + ")");
            Check(all.All(x => x.Dependency == PNBlastDependency.Owner) && Math.Abs(all[0].Delay - 1f) < 1e-5 && Math.Abs(all[1].Delay - 1.15f) < 1e-5 && all.All(x => Math.Abs(x.Radius - .5f) < 1e-5), "each is Owner-dependent with delay 1 + 0.15 i and radius 0.5");
            Check(all.All(x => Vector3.Distance(new Vector3(x.Center.x, 0, x.Center.z), new Vector3(rainTarget.transform.position.x, 0, rainTarget.transform.position.z)) <= 2.01f), "every landing lies within the 2 m scatter around the seeked target");
            rain.Cancel(rainContext); blasts.Advance(.05f);
            Check(blasts.ActiveCount == count, "cancelling the cast afterwards leaves the rain");
            rain.Dispose();
            float hp0 = Health(rainTarget); rainTarget.transform.position = new Vector3(all[all.Length - 1].Center.x, 0, all[all.Length - 1].Center.z); Physics.SyncTransforms();
            blasts.Advance(1.0f + .15f * all.Length + .2f);
            Check(Health(rainTarget) < hp0, "a body under a landing is hit when it lands");

            var rain2 = rainSkill.CreateAction(PNStarreaperCombatContent.Rain, out var rainContext2, new[] { rainTarget }, Vector3.right, new Vector3(0, 0, 3));
            rain2.Start(rainContext2); for (int i = 0; i < 25 && blasts.ActiveCount == 0; i++) rain2.AdvanceAuxiliary(.1f);
            Check(blasts.ActiveCount >= 5, "a second rain is created");
            float hp1 = Health(rainTarget); reaper.SetActive(false); blasts.Advance(.1f);
            Check(blasts.ActiveCount == 0, "the owner's loss ends every blast of the rain");
            blasts.Advance(5f); Check(Health(rainTarget) == hp1, "arrows that had not landed never do");
            rain2.Dispose();
            return "PASS " + n + " blast attack checks (Starweaver triangle: Action dependency, cancel, once; Starreaper rain: chain, Owner dependency; real prefabs in a preview scene)";
        }
        finally
        {
            try { blasts_Shutdown(made); if (started) EveRegistrySession.End(); }
            finally { foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o); EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        }
    }
    private static void blasts_Shutdown(List<GameObject> made) { foreach (var o in made) { var m = o != null ? o.GetComponent<PNBlastManager>() : null; if (m != null) m.Shutdown(); } }
}
#endif
