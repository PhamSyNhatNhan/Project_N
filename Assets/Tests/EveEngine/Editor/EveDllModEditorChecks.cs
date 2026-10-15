#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Opt-in isolated loader checks keep the player's DLL mod selection and scene untouched.
// VI: Kiểm tra loader tách riêng, chạy tường minh, giữ nguyên bộ DLL mod người chơi và scene.
public static class EveDllModEditorChecks
{
    private static EveCodeModLoader loader;
    public static void Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.IsReady || EveRegistrySession.State == EveSessionState.Stopping)
            throw new InvalidOperationException("DLL checks require idle Edit Mode without a live/pending session.");
        var scene = SceneManager.GetActiveScene();
        bool dirty = scene.isDirty;
        var source = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".utmp", "NativeModChecks", "Mods"), new[] { "native_probe" });
        if (loader == null) loader = new EveCodeModLoader("project_n", new EveMonoAssemblyLoader(), true);
        var mods = loader.Load(source.ReadPacks());
        string mode = Environment.GetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE");
        string trace = Environment.GetEnvironmentVariable("PROJECT_N_MOD_TEST_TRACE");
        Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE", null);
        Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_TRACE", null);
        try
        {
            EveRegistrySession.Begin();
            EveRegistrySession.AttachMods(mods);
            var documents = source.ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }.Concat(mods.ContentModules), documents);
            var compatibility = EveContentCompatibility.Create(hub, documents, "native:mod_probe", mods.Fingerprints);
            EveRegistrySession.Start(hub, EveSessionRole.Offline, compatibility);
            var stats = new PNStatSet(hub);
            stats.AddBase(new EveBaseStat(PNContentIds.AttackPower, 100f));
            if (stats.Evaluate(EveId.Parse("base:attack_power")) != 107f || mods.Fingerprints.Count != 2 ||
                hub.Calculations().Get(EveId.Parse("base:scale")).Id != EveId.Parse("native_probe:scale"))
                throw new Exception("Native DLL dependency/override verification failed.");
            EveRegistrySession.End();
            if (mods.PendingCleanupCount != 0 || scene.isDirty != dirty) throw new Exception("Native mod cleanup changed scene state.");
            Debug.Log("PASS native DLL mod loading, shared API identity, dependent-library execution, base override, fingerprints and session cleanup.");
        }
        catch (Exception error) { EveRegistrySession.Fail(error); throw; }
        finally
        {
            Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE", mode);
            Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_TRACE", trace);
        }
    }
}
#endif
