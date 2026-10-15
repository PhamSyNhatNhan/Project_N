#if UNITY_EDITOR
using System;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(PNSlashPrefab))]
public sealed class PNSlashPrefabEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Scrub Preview Time to view the same curves used by the attack. Select a Hitbox child to edit geometry in Scene. Green = track enabled; the skill's damage window is configured separately in JSON.", MessageType.Info);
        DrawDefaultInspector();
        try { ((PNSlashPrefab)target).RefreshPreview(); }
        catch (Exception error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
        if (GUILayout.Button("Refresh preview")) SceneView.RepaintAll();
    }
}

public static class PNSlashPrefabAuthoring
{
    public static string CreateDefault()
    {
        const string path = "Assets/Resources/PNAttacks/EveSlash.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return "Attack prefab already exists.";
        if (!AssetDatabase.IsValidFolder("Assets/Resources/PNAttacks")) AssetDatabase.CreateFolder("Assets/Resources", "PNAttacks");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("EveSlash");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            root.AddComponent<PNSlashPrefab>();
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/PNVfx/PNArcSlash.prefab"), scene);
            visual.name = "Visual"; visual.transform.SetParent(root.transform, false);
            var region = new GameObject("Hitbox"); region.transform.SetParent(root.transform, false);
            var track = region.AddComponent<EveHitboxTrack>();
            track.Data.Radial = new AnimationCurve(new Keyframe(0f, .2f), new Keyframe(.22f, 1f), new Keyframe(1f, 1.12f));
            track.Data.Angular = AnimationCurve.Linear(0f, .01f, .22f, 1f);
            track.Data.Angular.postWrapMode = WrapMode.ClampForever;
            root.GetComponent<PNSlashPrefab>().RefreshPreview();
            // Preview mesh is temporary; save the source prefab without embedding it.
            visual.GetComponent<MeshFilter>().sharedMesh = null;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return "Created " + path;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
    }
}
#endif
