#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Opt-in pitch/foot-anchor checks in a disposable preview scene.
// VI: Kiem tra goc nghieng va diem chan trong preview scene dung tam.
public static class PNBillboardEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        bool dirty = scene.isDirty;
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var stat in root.GetComponentsInChildren<PNStat>(true))
            {
                if (!(stat is EveCharacterStat) && !(stat is DummyStat)) continue;
                var visual = stat.transform.Find("Visual");
                if (visual == null || visual.GetComponent<ErModeCharacterBillboard>() == null)
                    throw new Exception("Character Visual has no billboard.");
                var so = new SerializedObject(visual.GetComponent<ErModeCharacterBillboard>());
                if (so.FindProperty("viewCamera").objectReferenceValue == null)
                    throw new Exception("Character billboard has no camera reference.");
                count++;
            }
        // Eve is spawned at runtime by the player factory; only the dummy is authored in ErMode. Eve's billboard is checked on its prefab.
        if (count != 1) throw new Exception("Expected the authored dummy billboard in the scene.");
        var evePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Character/Eve/Prefab/Eve.prefab");
        if (evePrefab == null || evePrefab.transform.Find("Visual")?.GetComponent<ErModeCharacterBillboard>() == null)
            throw new Exception("Eve prefab Visual has no billboard.");
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Billboard probe root");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, preview);
            root.transform.position = new Vector3(2f, .4f, 1f);
            root.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            var collider = root.AddComponent<CapsuleCollider>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(1.2f, .8f, 1f);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.flipX = true;
            var billboard = visual.AddComponent<ErModeCharacterBillboard>();
            var cameraObject = new GameObject("Billboard probe camera");
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            var camera = cameraObject.AddComponent<Camera>();
            var anchor = new Vector3(0f, -.2f, 0f);
            var settings = new SerializedObject(billboard);
            settings.FindProperty("viewCamera").objectReferenceValue = camera;
            settings.FindProperty("footOffset").floatValue = .25f;
            settings.FindProperty("footAnchor").vector3Value = anchor;
            settings.ApplyModifiedProperties();
            var rootRotation = root.transform.rotation;
            var rootPosition = root.transform.position;
            foreach (float pitch in new[] { 0f, 45f, 60f, -20f })
            {
                camera.transform.rotation = Quaternion.Euler(pitch, 25f, 10f);
                if (!billboard.AlignToCamera() || Quaternion.Angle(visual.transform.rotation,
                    Quaternion.Euler(pitch, 0f, 0f)) > .01f)
                    throw new Exception("Pitch-only alignment failed.");
                var foot = visual.transform.TransformPoint(new Vector3(0f, -.25f, 0f));
                if (Vector3.Distance(foot, root.transform.TransformPoint(anchor)) > .0001f)
                    throw new Exception("Billboard foot anchor moved.");
                if (root.transform.position != rootPosition || root.transform.rotation != rootRotation ||
                    collider.center != Vector3.zero || !renderer.flipX)
                    throw new Exception("Billboard changed root physics or sprite flip.");
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        if (scene.isDirty != dirty) throw new Exception("Billboard checks changed scene dirty state.");
        return "PASS Eve/dummy billboards: pitch0/45/60/-20, ignored camera yaw/roll, fixed feet, preserved root/physics/flipX.";
    }
}
#endif
