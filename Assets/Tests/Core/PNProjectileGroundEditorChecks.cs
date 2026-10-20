#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The Ground layer (13): floor colliders of the new-system scenes live on it, projectiles opt in through their environment mask, and the
// player's ground probe still sees it.
public static class PNProjectileGroundEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int count = 0; var evidence = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); count++; }
        int ground = LayerMask.NameToLayer("Ground");
        Check(ground == 13 && LayerMask.LayerToName(13) == "Ground", "the Ground layer is layer 13");
        // Scenes: the floor collider object is on Ground in all three new-system scenes.
        foreach (var path in new[] { "Assets/Game/Scenes/Shared/PNHall/PNHall.unity", "Assets/Game/Scenes/Roguelite/PNLobby/PNLobby.unity", "Assets/Game/Scenes/Test/ErMode/ErMode.unity" })
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var floors = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => t.name == "Eve 3D Ground").Select(t => t.gameObject).ToArray();
            Check(floors.Length == 1 && floors[0].layer == ground && floors[0].GetComponent<Collider>() != null, "the floor collider of " + Path.GetFileNameWithoutExtension(path) + " is on the Ground layer");
            Check(!scene.isDirty, "opening " + Path.GetFileNameWithoutExtension(path) + " leaves it clean");
        }
        // Eve's ground probe sees both the old Default floors and the Ground layer.
        var eve = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Character/Eve/Prefab/Eve.prefab");
        int mask = new SerializedObject(eve.GetComponent<PNPlayerMove>()).FindProperty("groundLayers").intValue;
        Check((mask & 1) != 0 && (mask & (1 << ground)) != 0, "Eve's ground probe mask contains Default and Ground");

        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNProjectileHitNativeModule() }, docs);
        var preview = EditorSceneManager.NewPreviewScene(); var fixtures = new List<GameObject>(); PNProjectileManager manager = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:projectile_ground"));
            var session = EveRegistrySession.Context; var source = new PNBuffSource(session.SessionId, new EveEntityId(994001));
            GameObject Fixture(string name) { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, preview); fixtures.Add(obj); return obj; }
            manager = Fixture("Projectile manager").AddComponent<PNProjectileManager>(); manager.Initialize(session, new PNProjectileProfile { MaxActive = 10 });
            var floor = Fixture("ground floor"); floor.layer = ground; floor.transform.position = new Vector3(0, -.5f, 0); floor.AddComponent<BoxCollider>().size = new Vector3(40, 1, 40);
            var plain = Fixture("default floor"); plain.layer = 0; plain.transform.position = new Vector3(0, -.5f, 100); plain.AddComponent<BoxCollider>().size = new Vector3(40, 1, 40);
            Physics.SyncTransforms();
            PNProbeProjectile.Reset();
            PNProjectileReceipt Fire(string id, Vector3 position) => manager.Create(new PNProjectileLaunch(EveId.Parse(id), null, source, 1 << 7), new PNProjectileSpawn(preview, position, Vector3.right));
            var falling = Fire("test:ground_fall", new Vector3(0, 2, 0)); var passing = Fire("test:ground_pass", new Vector3(0, 2, 0));
            var overDefault = Fire("test:ground_fall", new Vector3(0, 2, 100));
            for (int i = 0; i < 25; i++) manager.Advance(.05f);
            Check(!falling.Handle.IsAlive, "a projectile whose environment mask holds Ground lands on the Ground floor and ends");
            Check(passing.Handle.IsAlive && manager.Get(passing.Handle).Position.y < -.5f, "a projectile with the default mask falls through the Ground floor");
            Check(overDefault.Handle.IsAlive && manager.Get(overDefault.Handle).Position.y < -.5f, "a Ground-mask projectile ignores a floor on the Default layer");
            passing.Handle.End(); overDefault.Handle.End();
        }
        finally
        {
            try { manager?.Shutdown(); } catch (Exception error) { Debug.LogException(error); }
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { foreach (var obj in fixtures) if (obj != null) UnityEngine.Object.DestroyImmediate(obj); EditorSceneManager.ClosePreviewScene(preview); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }
        string summary = "PASS " + count + " native Ground layer checks";
        Directory.CreateDirectory(".utmp/ProjectileReview"); File.WriteAllLines(".utmp/ProjectileReview/NativeGroundResults.txt", evidence.Concat(new[] { summary })); return summary;
    }
}
#endif
