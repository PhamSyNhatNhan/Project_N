using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static EveSessionSmokeTests;

// EN: These checks consume a real DLL compiled separately by the PowerShell runner.
// VI: Các kiểm tra dùng DLL thật được runner PowerShell biên dịch riêng.
public static class EveCodeModTests
{
    private sealed class Unsupported : EveIModAssemblyLoader
    {
        public bool IsSupported => false;
        public Assembly Load(string path, byte[] image) => throw new Exception("Must not load on unsupported backend.");
    }
    public static void Run(string dllDirectory)
    {
        string root = Path.Combine(Application.persistentDataPath, "DllChecks");
        string mods = Path.Combine(root, "Mods");
        string folder = Path.Combine(mods, "smoke");
        Directory.CreateDirectory(Path.Combine(folder, "bin", "conflict"));
        File.Copy(Path.Combine(dllDirectory, "SmokeCodeMod.dll"), Path.Combine(folder, "bin", "SmokeCodeMod.dll"));
        File.Copy(Path.Combine(dllDirectory, "SmokeDependency.dll"), Path.Combine(folder, "bin", "SmokeDependency.dll"));
        File.Copy(Path.Combine(dllDirectory, "Conflict", "SmokeDependency.dll"), Path.Combine(folder, "bin", "conflict", "SmokeDependency.dll"));
        string manifestPath = Path.Combine(folder, "manifest.json");
        JObject Manifest() => JObject.Parse("{\"schemaVersion\":1,\"namespace\":\"smoke\",\"version\":\"1.0.0\",\"dependencies\":{\"project_n\":\"1.0.0\"},\"code\":{\"apiVersion\":\"6\",\"assembly\":\"bin/SmokeCodeMod.dll\",\"entryPoint\":\"SmokeCodeMod\",\"libraries\":[\"bin/SmokeDependency.dll\"]}}");
        void Write(JObject manifest) => File.WriteAllText(manifestPath, manifest.ToString());
        EveDirectoryContentSource Source() => new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), mods, new[] { "smoke" });
        Write(Manifest());
        var source = Source();
        Check(source.ReadPacks().Count == 2 && source.ReadPacks()[1].Code.EntryPoint == "SmokeCodeMod", "code metadata shares dependency-sorted packs with JSON");
        Reject(() => new EveCodeModLoader("project_n", new Unsupported()).Load(Source().ReadPacks()), "unsupported backend rejects before loading DLL");
        var invalid = Manifest(); invalid["code"]["apiVersion"] = "1"; Write(invalid);
        Reject(() => Source().ReadPacks(), "unsupported mod API rejected before load");
        invalid = Manifest(); invalid["code"]["assembly"] = "../SmokeCodeMod.dll"; Write(invalid);
        Reject(() => new EveCodeModLoader("project_n").Load(Source().ReadPacks()), "DLL traversal path rejected");
        invalid = Manifest(); invalid["code"]["libraries"] = new JArray("bin/SmokeDependency.dll", "bin/conflict/SmokeDependency.dll"); Write(invalid);
        Reject(() => new EveCodeModLoader("project_n").Load(Source().ReadPacks()), "conflicting shared DLL names rejected before execution");
        File.Copy(typeof(EveModApi).Assembly.Location, Path.Combine(folder, "bin", "HostCopy.dll"));
        invalid = Manifest(); invalid["code"]["libraries"] = new JArray("bin/SmokeDependency.dll", "bin/HostCopy.dll"); Write(invalid);
        Reject(() => new EveCodeModLoader("project_n").Load(Source().ReadPacks()), "bundled host API copy rejected");
        invalid = Manifest(); invalid["dependencies"]["project_n"] = "9.0.0"; Write(invalid);
        Reject(() => Source().ReadPacks(), "code pack game version dependency enforced");
        Write(Manifest());
        var loader = new EveCodeModLoader("project_n");
        var packs = Source().ReadPacks();
        var resources = loader.Load(packs);
        Check(resources.Fingerprints.Count == 2 && resources.Fingerprints.All(pair => pair.Value.Length == 64), "entry DLL and dependency both hashed");
        string trace = Path.Combine(root, "trace.txt");
        Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_TRACE", trace);
        var previousPath = Application.persistentDataPath;
        Application.persistentDataPath = root;
        try
        {
            var runtime = new EveGameRuntime();
            Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE", "register");
            Reject(runtime.Initialize, "DLL registration failure rejects bootstrap");
            Check(!EveRegistrySession.IsReady && EveRegistrySession.State == EveSessionState.Failed && File.ReadAllLines(trace).SequenceEqual(new[] { "register", "dispose" }), "failed registration cleans instantiated mod without start hook");
            File.WriteAllText(trace, "");
            Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE", "start");
            Reject(runtime.Initialize, "DLL start hook failure rejects unpublished session");
            Check(!EveRegistrySession.IsReady && File.ReadAllLines(trace).SequenceEqual(new[] { "register", "start:True:False", "end:False", "dispose" }), "partial start cleanup runs before any Ready publication");
            File.WriteAllText(trace, ""); Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE", null);
            runtime.Initialize();
            Check(EveRegistrySession.IsReady && File.ReadAllLines(trace).SequenceEqual(new[] { "register", "start:True:False" }), "real DLL hooks start after registry seal and before Ready");
            var hub = EveRegistrySession.Current;
            var set = new PNStatSet(hub); set.AddBase(new EveBaseStat(PNContentModule.AttackPower, 100));
            Check(set.Evaluate(EveId.Parse("base:attack_power")) == 107, "DLL override executes code in a separately loaded dependent library");
            Check(hub.Calculations().Get(EveId.Parse("base:scale")).Id == EveId.Parse("smoke:scale"), "DLL registers provider-owned factory through mod API");
            Check(EveRegistrySession.Context.Compatibility.ContentHashes.Keys.Count(key => key.StartsWith("code:dll:")) == 2, "multiplayer hashes actual mod DLL bytes");
            var hashes = resources.Fingerprints.ToDictionary(pair => pair.Key, pair => pair.Value);
            var compatibility = EveContentCompatibility.Create(hub, Source().ReadDocuments(), runtime.RulesVersion, hashes);
            hashes[hashes.Keys.First()] = new string('0', 64);
            Reject(() => compatibility.Validate(EveContentCompatibility.Create(hub, Source().ReadDocuments(), runtime.RulesVersion, hashes)), "multiplayer rejects modified DLL bytes even at same version");
            Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE", "dispose");
            Reject(runtime.EndSession, "failed mod cleanup blocks session end");
            Check(EveRegistrySession.State == EveSessionState.Stopping && !EveRegistrySession.IsReady && EveRegistrySession.PendingCleanupCount == 1, "pending mod cleanup retained in session lifecycle");
            Reject(runtime.Initialize, "restart blocked until failed mod disposal repaired");
            Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE", null);
            EveRegistrySession.RetryCleanup();
            Check(EveRegistrySession.State == EveSessionState.Stopped && File.ReadAllLines(trace).Count(line => line == "end:False") == 1, "retry finishes dispose without repeating successful end hook");
            File.WriteAllText(trace, ""); runtime.Initialize(); runtime.EndSession();
            Check(File.ReadAllLines(trace).SequenceEqual(new[] { "register", "start:True:False", "end:False", "dispose" }), "same DLL bytes reused with fresh per-session instances");
            File.WriteAllText(Path.Combine(mods, "enabled.json"), "{\"schemaVersion\":1,\"enabledMods\":[]}");
            Reject(runtime.Initialize, "changing loaded DLL set requires process restart");
            Check(!EveRegistrySession.IsReady, "changed DLL set cannot publish a partial session");
            invalid = Manifest(); invalid["version"] = "2.0.0"; Write(invalid);
            Reject(() => loader.Load(Source().ReadPacks()), "changing loaded mod version requires restart");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_MODE", null);
            Environment.SetEnvironmentVariable("PROJECT_N_MOD_TEST_TRACE", null);
            if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End();
            Application.persistentDataPath = previousPath;
        }
    }
}

