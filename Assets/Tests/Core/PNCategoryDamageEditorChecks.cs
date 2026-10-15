#if UNITY_EDITOR
using System.IO;
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Batch entry point uses disposable native fixtures and does not save authored scenes.
public static class PNCategoryDamageEditorChecks
{
    private const string Review = ".utmp/CategoryTagReview";
    private const string Request = Review + "/RunNative.request";
    [InitializeOnLoadMethod]
    private static void ConsumeExplicitRequest()
    {
        if (!File.Exists(Request)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                File.WriteAllText(Review + "/NativeBlocked.txt", "Idle Edit Mode required; no editor state was changed.");
                return;
            }
            File.Delete(Request);
            try { RunBatch(); }
            catch (Exception error) { File.WriteAllText(Review + "/NativeError.txt", error.ToString()); }
        };
    }
    public static void RunBatch()
    {
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        float originalTime = Time.timeScale;
        var results = new[] { PNSkillEffectsEditorChecks.Run(), PNDamageFlowEditorChecks.RunFlatScale(), PNSlashCombatEditorChecks.Run() };
        if (SceneManager.GetActiveScene() != original || original.isDirty != dirty || Time.timeScale != originalTime)
            throw new Exception("Native checks changed authored scene or global time.");
        Directory.CreateDirectory(Review);
        File.WriteAllLines(Review + "/NativeResults.txt", results);
        if (File.Exists(Review + "/NativeError.txt")) File.Delete(Review + "/NativeError.txt");
        var counts = new object[] { 0, 0, 0 };
        typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries")?.GetMethod("GetCountsByType", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, counts);
        File.WriteAllLines(Review + "/FinalAudit.txt", new[] {
            $"scene={original.name}; dirty={original.isDirty}; playing={EditorApplication.isPlaying}; compiling={EditorApplication.isCompiling}; updating={EditorApplication.isUpdating}; timeScale={Time.timeScale}; session={EveEngine.EveRegistrySession.State}",
            $"console errors={counts[0]}; warnings={counts[1]}; logs={counts[2]} (not cleared)",
            "editor windows=" + string.Join(",", Resources.FindObjectsOfTypeAll<EditorWindow>().Select(x => x.GetType().FullName).OrderBy(x => x)),
            "Authored scene and global time preserved; Game/Simulator selection and Play Maximized settings were not modified."
        });
        if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(0);
    }
}
#endif
