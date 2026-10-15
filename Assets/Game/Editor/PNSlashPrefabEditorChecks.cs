#if UNITY_EDITOR
using System;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PNSlashPrefabEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Requires Edit Mode.");
        const string path = "Assets/Resources/PNAttacks/EveSlash.prefab";
        bool dirty = SceneManager.GetActiveScene().isDirty;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<PNSlashPrefab>();
        string fingerprint = PNSlashPrefabContent.Fingerprint(asset);
        var root = PrefabUtility.LoadPrefabContents(path); int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
        try
        {
            var definition = root.GetComponent<PNSlashPrefab>();
            var visual = root.GetComponentInChildren<PNArcSlashVisual>();
            var track = root.GetComponentInChildren<EveHitboxTrack>();
            definition.PreviewTime = .1f; definition.RefreshPreview();
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Check(mesh != null && mesh.vertexCount > 0 && mesh.hideFlags == HideFlags.HideAndDontSave, "Edit Mode visual preview missing or persistent.");
            float early = mesh.vertices[41].magnitude;
            definition.PreviewTime = .5f; definition.RefreshPreview();
            Check(mesh.vertices[41].magnitude > early, "Preview does not grow with timeline.");
            var frame = track.Data.Sample(.5f, new EveHitboxPose(Vector3.zero, Quaternion.identity));
            Check(Mathf.Abs(mesh.vertices[41].magnitude - frame.Shape.Radius) < .0001f, "Visual outer arc and queried radius disagree.");
            Check(Vector3.Distance(visual.transform.localPosition, frame.Pose.Position) < .0001f && track.PreviewTime == .5f, "Visual/gizmo scrub clocks disagree.");
            var copy = definition.Capture()[0];
            copy.Geometry.Radius *= 2f; copy.Validate();
            Check(Mathf.Abs(copy.Sample(.5f, new EveHitboxPose(Vector3.zero, Quaternion.identity)).Shape.Radius - 2f * frame.Shape.Radius) < .0001f,
                "Code override does not affect sampled geometry.");
            Check(track.Data.Geometry.Radius == .6f && fingerprint == PNSlashPrefabContent.Fingerprint(asset), "Preview/code edits modified asset.");
            track.PreviewTime = .8f;
            track.SendMessageUpwards("SetPreviewTime", track.PreviewTime, SendMessageOptions.DontRequireReceiver);
            Check(definition.PreviewTime == .8f && track.PreviewTime == .8f && visual.Progress == .8f,
                "Child hitbox scrub is overwritten by the root preview clock.");
            track.Data.Geometry.Radius = .7f;
            Check(fingerprint != PNSlashPrefabContent.Fingerprint(definition), "Geometry edits do not affect compatibility fingerprint.");
            track.Data.Geometry.Radius = .6f;
            track.LayerMask = (1 << 7) | (1 << 8);
            var maskedCopy = definition.Capture()[0]; maskedCopy.LayerMask = 0;
            Check(definition.Capture()[0].LayerMask == ((1 << 7) | (1 << 8)) && asset.Capture()[0].LayerMask == ~0,
                "Component layer mask is not captured independently for each cast.");
            Check(fingerprint != PNSlashPrefabContent.Fingerprint(definition), "Layer mask absent from gameplay fingerprint.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Check(SceneManager.GetActiveScene().isDirty == dirty, "Preview checks changed gameplay scene.");
        return "PASS " + count + " prefab preview checks: visible Edit Mode mesh, scrub/growth, geometry alignment, isolated overrides and preserved scene.";
    }
}
#endif
