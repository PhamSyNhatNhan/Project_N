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
    public static void Run(string modsDirectory = null)
    {
        if (EditorApplication.isPlaying || EveRegistrySession.IsReady || EveRegistrySession.State == EveSessionState.Stopping)
            throw new InvalidOperationException("DLL checks require idle Edit Mode without a live/pending session.");
        var scene = SceneManager.GetActiveScene();
        bool dirty = scene.isDirty;
        var source = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
            modsDirectory ?? Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".utmp", "NativeModChecks", "Mods"), new[] { "native_probe" });
        if (loader == null) loader = new EveCodeModLoader("project_n", new EveMonoAssemblyLoader(), true);
        var mods = loader.Load(source.ReadPacks());
        var content = new EveContentContext(mods);
        string mode = Environment.GetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE");
        string trace = Environment.GetEnvironmentVariable("PROJECT_N_MOD_TEST_TRACE");
        Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE", null);
        Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_TRACE", null);
        try
        {
            var documents = source.ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }.Concat(mods.ContentModules), documents);
            var compatibility = EveContentCompatibility.Create(hub, documents, "native:mod_probe", mods.Fingerprints);
            content.Publish(hub, compatibility);
            EveRegistrySession.Begin(content);
            EveRegistrySession.Start(hub, EveSessionRole.Offline, compatibility);
            var skillEvidence=PNSkillBindingDllChecks.Verify(hub,"native_probe");
            System.IO.Directory.CreateDirectory(".utmp/SkillOverride");System.IO.File.WriteAllText(".utmp/SkillOverride/NativeDLL.txt",skillEvidence);
            var stats = new PNStatSet(hub);
            stats.AddBase(new EveBaseStat(PNContentIds.AttackPower, 100f));
            if (stats.Evaluate(EveId.Parse("base:attack_power")) != 107f || mods.Fingerprints.Count != 2 ||
                hub.Calculations().Get(EveId.Parse("base:scale")).Id != EveId.Parse("native_probe:scale"))
                throw new Exception("Native DLL dependency/override verification failed.");
            EveRegistrySession.End();
            EveRegistrySession.Begin(content);
            EveRegistrySession.Start(hub, EveSessionRole.Host, compatibility);
            if (!ReferenceEquals(EveRegistrySession.Current, hub)) throw new Exception("DLL content rebuilt between plays.");
            var second = new PNStatSet(hub); second.AddBase(new EveBaseStat(PNContentIds.AttackPower, 100f));
            if (second.Evaluate(EveId.Parse("base:attack_power")) != 107f) throw new Exception("DLL factory unavailable on second play.");
            EveRegistrySession.End();
            EveRegistrySession.Unload(content);
            if (mods.PendingCleanupCount != 0 || scene.isDirty != dirty) throw new Exception("Native mod cleanup changed scene state.");
            Debug.Log("PASS native DLL mod loading, shared API identity, dependent-library execution, base override, fingerprints and session cleanup.");
        }
        catch (Exception error) { EveRegistrySession.Fail(error); throw; }
        finally
        {
            if (EveRegistrySession.State != EveSessionState.Stopping) EveRegistrySession.Unload(content);
            Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE", mode);
            Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_TRACE", trace);
        }
    }
}
#endif
