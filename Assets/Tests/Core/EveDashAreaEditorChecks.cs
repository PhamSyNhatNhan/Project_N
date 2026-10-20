#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;

public static class EveDashAreaEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var scene = EditorSceneManager.NewPreviewScene(); var evidence = new List<string>(); PNAreaManager areas = null;
        RenderTexture rt = null; Texture2D image = null; Material backdropMaterial = null; var previous = RenderTexture.active;
        Directory.CreateDirectory(".utmp/EveDashAreaReview");
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); evidence.Add("PASS " + label); }
        GameObject Root(string name, long id, string character, Vector3 position, int layer)
        {
            var root = new GameObject(name); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, scene); root.layer = layer; root.transform.position = position;
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(character), 0); return root;
        }
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:eve_dash_area"));
            var managerRoot = new GameObject("Eve Dash area manager fixture"); SceneManager.MoveGameObjectToScene(managerRoot, scene); areas = managerRoot.AddComponent<PNAreaManager>(); areas.Initialize(EveRegistrySession.Context);
            var eve = Root("Dash Eve fixture", 991001, "project_n:eve", Vector3.zero, 6);
            eve.AddComponent<EveTimeScale>(); var stat = eve.AddComponent<EveCharacterStat>(); eve.AddComponent<PNAbilityManager>(); var body = eve.AddComponent<Rigidbody>(); body.useGravity = false; body.interpolation = RigidbodyInterpolation.Interpolate;
            var capsule = eve.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f; capsule.center = Vector3.up * .3f;
            var move = eve.AddComponent<PNPlayerMove>(); var skill = eve.AddComponent<EveBurstBuffProbe>(); skill.PrepareInput(); eve.AddComponent<EveEntityLoader>().Load(hub); eve.SetActive(true); eve.GetComponent<PNBuffManager>().Advance(0);
            PNStat Enemy(long id, Vector3 point, int layer)
            {
                var root = Root("Dash field receiver " + id, id, "project_n:dummy", point, layer); root.AddComponent<SphereCollider>().radius = .2f; root.AddComponent<EveTimeScale>(); var target = root.AddComponent<PNEnemyStat>(); root.AddComponent<EveEntityLoader>().Load(hub); root.SetActive(true); root.GetComponent<PNBuffManager>().Advance(0); return target;
            }
            var enemy = Enemy(991002, new Vector3(2, .3f, 0), 7); var ally = Enemy(991003, new Vector3(2, .3f, 1), 6);
            move.SetMoveDirection(Vector2.right); move.SetMoveDirection(Vector2.zero); Physics.SyncTransforms();
            Check(skill.Moveset.DashDistance == 1.5f && skill.DashSettings.Motion.Distance == 1.5f, "Eve selected registered Dash distance is 1.5");
            Check(PNPlayerDashSettings.Parse(PNSkillData.Parse(hub.Skills.GetEffective(EveId.Parse("base:player_dash")).Data)).Motion.Distance == 1.5, "Shared player Dash remains independently configured");
            Check(skill.TryDash(), "Dash starts"); skill.Tick(.08f); Check(Math.Abs(body.position.x - .75) < .02 && areas.ActiveCount == 0, "Half Dash real Rigidbody motion and no premature region");
            skill.Tick(.08f); Check(Math.Abs(body.position.x - 1.5) < .02 && !skill.IsDashing && skill.DashRecovery.Current == 2, "Dash completes 1.5 units with one consumed charge");
            Check(areas.ActiveCount == 1 && Vector3.Distance(areas.Snapshot()[0].Position, body.position) < .01, "OnComplete creates exactly one region at physics endpoint");
            Check(Math.Abs(enemy.GetComponent<EveTimeScale>().Scale - .1f) < .00001 && enemy.GetComponent<PNBuffManager>().Buffs.Snapshot().Count == 1, "Enemy receives .9 reduction and .9 minimum penetration");
            Check(ally.GetComponent<PNBuffManager>().Buffs.Snapshot().Count == 0 && eve.GetComponent<PNBuffManager>().Buffs.Snapshot().Count == 0, "Player layer and Eve excluded");
            var region = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeScaleArea>(true)).First(x => x.IsRunning);
            Check(region.Radius == 2.5f && body.interpolation == RigidbodyInterpolation.Interpolate, "Radius 2.5 and interpolation restored");
            var endpoint = body.position; body.position = Vector3.zero; eve.transform.position = Vector3.zero; Physics.SyncTransforms();
            Check(skill.TryDash(), "Second Dash starts"); skill.Tick(.2f); Check(areas.ActiveCount == 2 && enemy.GetComponent<EveTimeScale>().Scale == 0, "Repeated Dash creates independent overlapping regions");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(wall, scene); wall.transform.position = new Vector3(.9f, .3f, 0); wall.transform.localScale = new Vector3(.1f, 2, 3); wall.layer = 9;
            body.position = Vector3.zero; eve.transform.position = Vector3.zero; Physics.SyncTransforms(); skill.DashRecovery.ResetToFull();
            Check(skill.TryDash(), "Wall-clipped Dash starts"); skill.Tick(.2f);
            Check(body.position.x > .2f && body.position.x < 1 && areas.ActiveCount == 3 && areas.Snapshot().Any(x => Vector3.Distance(x.Position, body.position) < .01), "Wall clipping creates region at shortened actual endpoint");
            int count = areas.ActiveCount; Check(skill.TryDash(), "Interrupted Dash starts"); skill.Tick(.02f); skill.TryInterrupt(PNControlIds.Dash);
            Check(!skill.IsDashing && areas.ActiveCount == count, "Cancelled Dash creates no region"); UnityEngine.Object.DestroyImmediate(wall);
            areas.Advance(14.99f); Check(areas.ActiveCount == 3, "Region lifetime remains 15 Game seconds");
            // Native URP capture of real Dash-created sphere union.
            backdropMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")); backdropMaterial.SetColor("_BaseColor", new Color(.6f, .75f, .65f));
            var background = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(background, scene); background.transform.position = new Vector3(1, 0, 4); background.transform.localScale = new Vector3(15, 10, .2f); background.GetComponent<Renderer>().sharedMaterial = backdropMaterial; UnityEngine.Object.DestroyImmediate(background.GetComponent<Collider>());
            var camRoot = new GameObject("Dash field capture camera"); SceneManager.MoveGameObjectToScene(camRoot, scene); var camera = camRoot.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene; camera.orthographic = true; camera.orthographicSize = 3.4f; camera.transform.position = new Vector3(1, 0, -8); camRoot.AddComponent<UniversalAdditionalCameraData>();
            rt = new RenderTexture(960, 640, 24); rt.Create(); camera.targetTexture = rt; image = new Texture2D(960, 640, TextureFormat.RGB24, false);
            camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); image.Apply(); File.WriteAllBytes(".utmp/EveDashAreaReview/DashFields.png", image.EncodeToPNG());
            Check(image.GetPixel(480, 320).b > image.GetPixel(480, 320).g * 1.3f, "Actual URP Dash-created regions render purple union");
            eve.GetComponent<EveEntityLoader>().Unload(); areas.Advance(.005f); Check(areas.ActiveCount == 3, "Independent regions survive Eve unload");
            areas.Advance(.02f); Check(areas.ActiveCount == 0 && enemy.GetComponent<EveTimeScale>().Scale == 1, "Game expiry removes all owned enemy modifiers"); areas.Advance(.31f);
            Check(areas.BorrowedCount == 0 && PNTimeScaleAreaRendering.RegisteredCount == 0, "Region VFX and pool retirement clean");
        }
        finally
        {
            areas?.Shutdown(); EveRegistrySession.End(); RenderTexture.active = previous;
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); } if (image != null) UnityEngine.Object.DestroyImmediate(image); if (backdropMaterial != null) UnityEngine.Object.DestroyImmediate(backdropMaterial);
            EditorSceneManager.ClosePreviewScene(scene); File.WriteAllLines(".utmp/EveDashAreaReview/NativeResults.txt", evidence);
        }
        Check(original.isDirty == dirty, "Authored scene state preserved");
        File.WriteAllLines(".utmp/EveDashAreaReview/NativeResults.txt", evidence); return "PASS " + evidence.Count + " native Eve Dash area checks";
    }
}
#endif
