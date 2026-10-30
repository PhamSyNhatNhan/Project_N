#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Hidden preview/render resources can survive their static references across assembly reloads.
// Retire only the exact transient batch owned by PNVisualStringSystem, never authored assets.
[InitializeOnLoad]
public static class PNVisualStringEditorLifetime
{
    static PNVisualStringEditorLifetime()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Release;
        EditorApplication.quitting += Release;
        EditorApplication.playModeStateChanged += Changed;
        EditorApplication.delayCall += CleanOrphans;
    }
    private static void Changed(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode) Release();
        else if (state == PlayModeStateChange.EnteredEditMode) CleanOrphans();
    }
    private static void Release() { PNVisualStringSystem.Release(); CleanOrphans(); }
    public static void CleanOrphans()
    {
        var current = PNVisualStringSystem.Renderer;
        foreach (var renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
        {
            if (renderer == null || renderer == current || EditorUtility.IsPersistent(renderer)) continue;
            var root = renderer.gameObject;
            if (root.name != "PN Visual Strings" || root.hideFlags != HideFlags.HideAndDontSave) continue;
            var mesh = root.GetComponent<MeshFilter>()?.sharedMesh;
            var material = renderer.sharedMaterial;
            if (mesh == null || mesh.name != "PN Visual Strings" || material == null ||
                material.name != "PN Visual Strings" || material.shader == null || material.shader.name != "PN/VisualStrings" ||
                EditorUtility.IsPersistent(mesh) || EditorUtility.IsPersistent(material)) continue;
            renderer.enabled = false;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(material);
        }
    }
}
#endif
