using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Newtonsoft.Json;
using UnityEngine;

// EN: Minimal Unity API substitutes for testing content and lifecycle logic outside Play Mode.
// VI: API Unity tối thiểu để kiểm tra nội dung và vòng đời bên ngoài Play Mode.
namespace UnityEngine
{
    public class SerializeField : Attribute { }
    public class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    public class DisallowMultipleComponent : Attribute { }
    public class RequireComponent : Attribute { public RequireComponent(Type type) { } }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType kind) { }
    }
    public class Transform
    {
        public Vector3 position;
        public Transform parent;
        public void SetParent(Transform value, bool worldPositionStays) => parent = value;
        public void Rotate(float x, float y, float z) { }
    }
    public struct Vector2
    {
        public float x;
        public float y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 right => new Vector2(1f, 0f);
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector2 normalized => magnitude > 0f ? this * (1f / magnitude) : zero;
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 ClampMagnitude(Vector2 value, float max) => value.magnitude > max ? value.normalized * max : value;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float value) => new Vector2(a.x * value, a.y * value);
    }
    public static class Time { public static float fixedDeltaTime = 0.02f; public static float deltaTime = 0.02f; }
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 down => new Vector3(0f, -1f, 0f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float value) => new Vector3(a.x * value, a.y * value, a.z * value);
        public static Vector3 operator /(Vector3 a, float value) => a * (1f / value);
    }
    public class Rigidbody2D : MonoBehaviour
    {
        public Vector2 position;
        public Vector2 linearVelocity;
    }
    public enum RigidbodyConstraints { None = 0, FreezeRotation = 112, FreezeAll = 126 }
    public enum ForceMode { Acceleration }
    public class Rigidbody : MonoBehaviour
    {
        public Vector3 position;
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
        public Vector3 addedAcceleration;
        public bool useGravity = true;
        public bool isKinematic;
        public RigidbodyConstraints constraints;
        public void AddForce(Vector3 force, ForceMode mode) => addedAcceleration += force;
    }
    public struct LayerMask
    {
        public int value;
        public static implicit operator int(LayerMask mask) => mask.value;
        public static implicit operator LayerMask(int value) => new LayerMask { value = value };
    }
    public struct Bounds
    {
        public Vector3 center;
        public Vector3 min;
    }
    public class Collider : MonoBehaviour
    {
        public Bounds bounds;
        public Rigidbody attachedRigidbody;
    }
    public class SpriteRenderer : MonoBehaviour { public bool flipX; }
    public struct RaycastHit
    {
        public Collider collider;
        public Vector3 normal;
    }
    public enum QueryTriggerInteraction { Ignore }
    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180d);
        public static float Cos(float value) => (float)Math.Cos(value);
        public static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
    public static class Physics
    {
        public static Vector3 gravity = new Vector3(0f, -9.81f, 0f);
        public static Func<RaycastHit[]> GroundHits = () => Array.Empty<RaycastHit>();
        public static int SphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction,
            RaycastHit[] results, float distance, int layerMask, QueryTriggerInteraction query)
        {
            RaycastHit[] hits = GroundHits();
            int count = Math.Min(hits.Length, results.Length);
            Array.Copy(hits, results, count);
            return count;
        }
    }
    public class GameObject : Object
    {
        internal MonoBehaviour Owner;
        public T AddComponent<T>() where T : MonoBehaviour, new()
        {
            var component = new T();
            var all = (Owner?.Components ?? Array.Empty<MonoBehaviour>()).Concat(new[] { component }).ToArray();
            foreach (var entry in all) entry.Components = all;
            return component;
        }
        public string name;
        public readonly Transform transform = new Transform();
        public GameObject(string name = "") { this.name = name; }
        public bool activeInHierarchy;
        public void SetActive(bool value) => activeInHierarchy = value;
        public T[] GetComponentsInChildren<T>(bool inactive) => Array.Empty<T>();
    }
    public class Object
    {
        public static void Destroy(object value) => MonoBehaviour.Destroy(value);
        public static void DestroyImmediate(object value) => MonoBehaviour.Destroy(value);
        public static GameObject Instantiate(GameObject prefab, Transform parent, bool worldPositionStays)
        {
            var item = new GameObject(prefab.name);
            item.transform.parent = parent;
            return item;
        }
    }
    public class Component : Object { public readonly GameObject gameObject = new GameObject(); }
    public class Behaviour : Component { public bool enabled = true; public bool isActiveAndEnabled => enabled; }
    public static class Debug { public static void LogException(Exception error, object context) { } public static void LogWarning(object message) { } }
    public class MonoBehaviour : Behaviour
    {
        public MonoBehaviour() { gameObject.Owner = this; }
        public readonly Transform transform = new Transform();
        public MonoBehaviour[] Components = Array.Empty<MonoBehaviour>();
        public static readonly List<object> Destroyed = new List<object>();
        public static readonly List<object> Persistent = new List<object>();
        public static void Destroy(object value) => Destroyed.Add(value);
        public static void DontDestroyOnLoad(object value) => Persistent.Add(value);
        public T[] GetComponents<T>() where T : MonoBehaviour => Components.OfType<T>().ToArray();
        public T GetComponent<T>() where T : MonoBehaviour => Components.OfType<T>().FirstOrDefault();
        public T GetComponentInChildren<T>(bool inactive) where T : MonoBehaviour => GetComponent<T>();
    }
    public static class Application
    {
        public static string streamingAssetsPath;
        public static string persistentDataPath;
        public static bool isPlaying;
    }
}
namespace UnityEngine.InputSystem
{
    public enum InputActionType { Button }
    public interface IInputInteraction { void Process(ref InputInteractionContext context); void Reset(); }
    public sealed class InputControl
    {
        public bool Actuated;
        public bool IsActuated() => Actuated;
    }
    public struct InputInteractionContext
    {
        public bool Actuated, timerHasExpired;
        public string Trace;
        public float Timeout;
        public bool ControlIsActuated() => Actuated;
        public InputControl control => new InputControl { Actuated = Actuated };
        public void Started() => Trace += "start;";
        public void Canceled() => Trace += "cancel;";
        public void PerformedAndStayPerformed() => Trace += "perform;";
        public void SetTimeout(float duration) => Timeout = duration;
    }
    public static class InputSystem
    {
        public class Settings { public float defaultHoldTime = 0.4f; }
        public static Settings settings = new Settings();
        private static readonly Dictionary<string, Type> interactions = new Dictionary<string, Type>();
        public static Type TryGetInteraction(string name) => interactions.TryGetValue(name, out var type) ? type : null;
        public static void RegisterInteraction<T>(string name) => interactions[name] = typeof(T);
    }
    public struct InputBinding { public bool isPartOfComposite; public string overrideInteractions, interactions, action; }
    public sealed class InputAction : IDisposable
    {
        public struct CallbackContext { public object interaction; }
        public event Action<CallbackContext> started;
        public event Action<CallbackContext> performed;
        public event Action<CallbackContext> canceled;
        public bool enabled, disposed;
        private object activeInteraction;
        public List<InputBinding> bindings = new List<InputBinding>();
        public void ApplyBindingOverride(int index, InputBinding binding) => bindings[index] = binding;
        public void EmitStarted(object interaction)
        { if (enabled) { activeInteraction = interaction; started?.Invoke(new CallbackContext { interaction = interaction }); } }
        public void EmitPerformed(object interaction)
        { if (enabled) performed?.Invoke(new CallbackContext { interaction = interaction }); }
        public void EmitCanceled(object interaction)
        { if (enabled) { activeInteraction = null; canceled?.Invoke(new CallbackContext { interaction = interaction }); } }
        public InputAction(string name, InputActionType type) { }
        public InputAction AddBinding(string path) => this;
        public InputAction AddBinding(InputBinding binding) { bindings.Add(binding); return this; }
        public void Enable() { enabled = true; }
        public void Disable()
        {
            if (enabled && activeInteraction != null) EmitCanceled(activeInteraction);
            enabled = false;
        }
        public void Dispose() { Disable(); disposed = true; }
    }
}
namespace UnityEngine.InputSystem.Interactions
{
    public class TapInteraction { }
    public class PressInteraction { }
    public class HoldInteraction { }
    public class SlowTapInteraction { }
}
public sealed class PlayerControls : IDisposable
{
    public sealed class MoveAction
    {
        public UnityEngine.Vector2 Value;
        public T ReadValue<T>() => typeof(T) == typeof(UnityEngine.Vector2) ? (T)(object)Value : default;
    }
    public sealed class ControllerActions
    {
        public MoveAction Move = new MoveAction();
        public UnityEngine.InputSystem.InputAction Attack = new UnityEngine.InputSystem.InputAction("Attack", UnityEngine.InputSystem.InputActionType.Button);
        public UnityEngine.InputSystem.InputAction Skill = new UnityEngine.InputSystem.InputAction("Skill", UnityEngine.InputSystem.InputActionType.Button);
        public UnityEngine.InputSystem.InputAction Ulti = new UnityEngine.InputSystem.InputAction("Ulti", UnityEngine.InputSystem.InputActionType.Button);
        public UnityEngine.InputSystem.InputAction Dash = new UnityEngine.InputSystem.InputAction("Dash", UnityEngine.InputSystem.InputActionType.Button);
        public UnityEngine.InputSystem.InputAction Burst = new UnityEngine.InputSystem.InputAction("Burst", UnityEngine.InputSystem.InputActionType.Button);
        public ControllerActions()
        {
            foreach (var action in new[] { Attack, Skill, Ulti, Dash, Burst })
                action.AddBinding(new UnityEngine.InputSystem.InputBinding { interactions = "Tap,Hold" });
        }
    }
    public ControllerActions Controller = new ControllerActions();
    public void Enable() { }
    public void Disable() { }
    public void Dispose()
    {
        Controller.Attack.Dispose(); Controller.Skill.Dispose(); Controller.Ulti.Dispose();
        Controller.Dash.Dispose(); Controller.Burst.Dispose();
    }
}
// EN: Editor substitutes compile the shared stat Inspector together with runtime sources.
// VI: API Editor thay thế để biên dịch Inspector stat chung cùng mã runtime.
namespace UnityEditor
{
    public class CustomEditor : Attribute { public CustomEditor(Type type, bool inherited) { } }
    public enum MessageType { Info }
    public class Editor
    {
        public object target;
        public virtual void OnInspectorGUI() { }
        public virtual bool RequiresConstantRepaint() => false;
        public void DrawDefaultInspector() { }
    }
    public static class EditorStyles { public static object boldLabel; public static object helpBox; }
    public static class EditorGUILayout
    {
        public class VerticalScope : IDisposable { public VerticalScope(object style) { } public void Dispose() { } }
        public static void HelpBox(string text, MessageType type) { }
        public static void Space() { }
        public static void LabelField(string text, object style) { }
        public static void LabelField(string label, string text) { }
    }
}
// EN: Exposes Unity lifecycle hooks to the smoke harness.
// VI: Cho bộ kiểm tra gọi các hook vòng đời Unity.
public class EveTestLoader : EveEntityLoader
{
    public void Enable() => OnEnable();
    public void Disable() => OnDisable();
}
// EN: Checks that a later load step observes initialized stats.
// VI: Kiểm tra bước nạp sau nhận được stat đã khởi tạo.
public class EveTestStep : MonoBehaviour, EveIEntityLoadStep
{
    public EveStat Stat;
    public int Calls;
    public int LoadOrder => -10;
    public IEnumerable<Type> Dependencies => new[] { typeof(EveStat) };
    public void Unload() { }
    public void Load(EveEntityLoadContext context)
    {
        if (!Stat.IsInitialized || context.CanUse || !context.Registry.IsSealed) throw new Exception("Load order violated");
        Calls++;
    }
}
// EN: Regression checks for session ownership, late spawn and external JSON mods.
// VI: Kiểm tra hồi quy vòng đời phiên, spawn muộn và mod JSON bên ngoài.
public static class EveSessionSmokeTests
{
    private static string root;
    private static string fixture;
    private static int checks;
    internal static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
    }
    internal static void Reject(Action action, string name)
    {
        try { action(); } catch (Exception ex) when (ex is FormatException || ex is IOException || ex is InvalidOperationException || ex is ArgumentException || ex is AggregateException || ex is NotSupportedException || ex is KeyNotFoundException)
        { checks++; return; }
        throw new Exception("Expected rejection: " + name);
    }
    private static void Set(object target, string field, object value)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (info != null) { info.SetValue(target, value); return; }
        }
        throw new Exception(field);
    }
    internal static (EveTestLoader loader, EveCharacterStat stat, EveTestStep step, EveEntityIdentity identity) Entity()
    {
        var stat = new EveCharacterStat();
        var identity = new EveEntityIdentity();
        Set(stat, "characterId", "project_n:eve");
        var step = new EveTestStep { Stat = stat };
        var loader = new EveTestLoader();
        MonoBehaviour[] components = { loader, identity, step, stat };
        foreach (MonoBehaviour component in components) component.Components = components;
        return (loader, stat, step, identity);
    }
    private static void Manifest(string folder, string ns, Dictionary<string, string> dependencies = null, string version = "1.0.0")
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonConvert.SerializeObject(new
        { schemaVersion = 1, @namespace = ns, version, dependencies = dependencies ?? new Dictionary<string, string>() }));
    }
    private static void Pack(string folder, string ns, Dictionary<string, string> dependencies = null)
    {
        Manifest(folder, ns, dependencies);
        File.WriteAllText(Path.Combine(folder, "character.json"),
            "{\"schemaVersion\":1,\"characters\":[{\"id\":\"" + ns + ":hero\",\"stats\":[{\"id\":\"project_n:health\",\"base\":200}]}]}");
    }
    private static EveDirectoryContentSource Source(params string[] mods) => new EveDirectoryContentSource(
        Path.Combine(root, "Assets", "StreamingAssets", "BaseContent"), Path.Combine(fixture, "Mods"), mods);
    private static EveRegistryHub Build(params string[] mods) => new EveContentBootstrap().Build(
        new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, Source(mods).ReadDocuments());
    private static void Lifecycle()
    {
        var early = Entity(); early.loader.Enable();
        var disabled = Entity(); disabled.loader.Enable(); disabled.loader.Disable();
        Check(!early.loader.IsReady && !EveRegistrySession.IsReady, "wait before registry ready");
        int notifications = 0;
        using (EveEventManager.Global.Subscribe<EveRuntimeReady>(EveRuntimeEventIds.Ready, (context, notice) =>
        { Check(EveRegistrySession.IsReady, "publish registry before notification"); notifications++; }))
        {
            var runtime = new EveGameRuntime(); runtime.Initialize();
            Check(early.stat.GetCurrent(PNContentModule.Health) == 40 && early.step.Calls == 1 && early.stat.Set is PNStatSet,
                "JSON and Project_N stat policy load through the inherited stat step");
            Check(EveRegistrySession.Current.Get<EveStatDefinition>().Get(EveBuiltInContentModule.MoveSpeed).Kind == EveStatKind.Number &&
                  EveRegistrySession.Current.Get<EveStatDefinition>().Get(EveBuiltInContentModule.JumpSpeed).Kind == EveStatKind.Number &&
                  early.stat.Evaluate(EveBuiltInContentModule.MoveSpeed) == 3f &&
                  early.stat.Evaluate(EveBuiltInContentModule.JumpSpeed) == 3.2d,
                  "engine registers movement stat IDs and Eve JSON supplies starting values");
            Check(!disabled.loader.IsReady, "disabled listener removed");
            disabled.loader.Enable(); Check(disabled.loader.IsReady, "reenable after ready");
            var registry = EveRegistrySession.Current;
            var dummyDefinition = registry.Characters.Get(EveId.Parse("project_n:dummy"));
            var dummyStats = new PNStatSet(registry);
            dummyStats.LoadCharacter(dummyDefinition);
            Check(dummyStats.GetCurrent(PNContentModule.Health) == 200000000f &&
                  dummyStats.GetMax(PNContentModule.Health) == 200000000f,
                  "dummy JSON preserves legacy base and current health");
            Check(dummyStats.Evaluate(PNContentModule.AttackPower) == 0f &&
                  dummyStats.Evaluate(PNContentModule.Defense) == 0f && dummyDefinition.SkillIds.Count == 0,
                  "dummy JSON has zero attack/defense and no skills");
            var late = Entity(); late.loader.Enable();
            Check(late.loader.IsReady && notifications == 1, "late spawn uses registry without event replay");
            Check(early.identity.Id != late.identity.Id && !early.identity.Id.IsHostAssigned,
                "offline entities with the same character receive separate local IDs");
            var playerA = Entity(); playerA.identity.Assign(new EveEntityId(101)); playerA.loader.Enable();
            var playerB = Entity(); playerB.identity.Assign(new EveEntityId(102)); playerB.loader.Enable();
            Check(playerA.stat.CharacterId == playerB.stat.CharacterId && playerA.identity.Id != playerB.identity.Id,
                "same character definition, distinct host entity IDs");
            playerA.stat.SetCurrent(PNContentModule.Health, 25);
            playerB.stat.SetCurrent(PNContentModule.Health, 70);
            var snapshotA = playerA.stat.EndFloorAndCaptureSnapshot();
            Check(snapshotA.EntityId == playerA.identity.Id && snapshotA.CharacterId == playerB.stat.CharacterId,
                "snapshot stores instance and definition identities");
            Reject(() => playerB.stat.RestoreFloorSnapshot(snapshotA), "reject another player's snapshot for the same character");
            Check(playerB.stat.GetCurrent(PNContentModule.Health) == 70,
                "rejected snapshot leaves other player's stats untouched");
            playerA.loader.Unload();
            var transferredA = Entity(); transferredA.identity.Assign(snapshotA.EntityId); transferredA.loader.Enable();
            transferredA.stat.RestoreFloorSnapshot(snapshotA);
            Check(transferredA.stat.GetCurrent(PNContentModule.Health) == 25,
                "transferred entity restores its own snapshot with the same ID");
            early.loader.Load(registry); runtime.Initialize();
            Check(early.step.Calls == 1 && notifications == 1, "idempotent initialization");
            Check(MonoBehaviour.Persistent.Contains(runtime.gameObject), "runtime requests scene persistence");
            var nextSceneRuntime = new EveGameRuntime(); nextSceneRuntime.Initialize();
            Check(ReferenceEquals(registry, EveRegistrySession.Current) && MonoBehaviour.Destroyed.Contains(nextSceneRuntime), "later scene reuses session");
            nextSceneRuntime.EndSession(); Check(EveRegistrySession.IsReady, "duplicate cannot end session");
            early.loader.Disable(); late.loader.Disable(); disabled.loader.Disable();
            playerA.loader.Disable(); playerB.loader.Disable(); transferredA.loader.Disable();
            runtime.EndSession(); Check(!EveRegistrySession.IsReady, "explicit session end clears registry");
            var nextEntity = Entity(); nextEntity.loader.Enable(); Check(!nextEntity.loader.IsReady, "no stale data on next session");
            var nextRuntime = new EveGameRuntime(); nextRuntime.Initialize();
            Check(nextEntity.loader.IsReady && !ReferenceEquals(registry, EveRegistrySession.Current), "new session gets fresh registry");
            nextEntity.loader.Disable(); nextRuntime.EndSession();
        }
    }
    private static void Mods()
    {
        int baseCharacterCount = Build().Characters.Count;
        Check(EveDirectoryContentSource.ReadEnabledMods(Path.Combine(fixture, "NoMods", "enabled.json")).Length == 0,
            "missing Mods directory is empty");
        var deps = new Dictionary<string, string> { { "project_n", "1.0.0" } };
        string mods = Path.Combine(fixture, "Mods");
        Pack(Path.Combine(mods, "addon"), "addon", deps);
        var hub = Build("addon");
        Check(hub.IsSealed && hub.Get<EveCharacterDefinition>().Get(EveId.Parse("addon:hero")) != null, "external mod without Unity assets");
        File.WriteAllText(Path.Combine(mods, "addon", "extra.json"), "{\"schemaVersion\":1,\"characters\":[{\"id\":\"addon:extra\",\"stats\":[]}]}");
        Check(Build("addon").Get<EveCharacterDefinition>().Count == baseCharacterCount + 2, "new JSON automatically discovered");
        Check(Build().Get<EveCharacterDefinition>().Count == baseCharacterCount, "disabled mod excluded");
        Pack(Path.Combine(mods, "later"), "later", new Dictionary<string, string> { { "addon", "1.0.0" }, { "project_n", "1.0.0" } });
        Check(Source("later", "addon").ReadDocuments().Select(d => d.Name).SequenceEqual(Source("addon", "later").ReadDocuments().Select(d => d.Name)), "deterministic dependency order");
        Reject(() => Build("later"), "missing dependency");
        Manifest(Path.Combine(mods, "later"), "later", new Dictionary<string, string> { { "addon", "2.0.0" }, { "project_n", "1.0.0" } });
        Reject(() => Build("later", "addon"), "wrong dependency version");
        Manifest(Path.Combine(mods, "later"), "later", new Dictionary<string, string> { { "addon", "1.0.0" }, { "project_n", "1.0.0" } });
        Manifest(Path.Combine(mods, "addon"), "addon", new Dictionary<string, string> { { "later", "1.0.0" } });
        Reject(() => Build("later", "addon"), "dependency cycle");
        Manifest(Path.Combine(mods, "addon"), "addon", deps);
        File.WriteAllText(Path.Combine(mods, "addon", "extra.json"), "{\"schemaVersion\":1,\"characters\":[{\"id\":\"project_n:intruder\",\"stats\":[]}]}");
        Reject(() => Build("addon"), "foreign definition namespace");
        File.WriteAllText(Path.Combine(mods, "addon", "extra.json"), File.ReadAllText(Path.Combine(mods, "addon", "character.json")));
        Reject(() => Build("addon"), "duplicate definition");
        File.WriteAllText(Path.Combine(mods, "addon", "extra.json"), "{\"schemaVersion\":1,\"characters\":[{\"id\":\"addon:extra\",\"stats\":[{\"id\":\"addon:missing\",\"base\":1}]}]}");
        Reject(() => Build("addon"), "missing stat reference");
        Reject(() => Build("../escape"), "mod folder traversal");
        Reject(() => Build("absent"), "enabled mod missing on disk");
        string selection = Path.Combine(mods, "enabled.json");
        Directory.CreateDirectory(Path.Combine(mods, "unrelated_folder"));
        Check(EveDirectoryContentSource.ReadEnabledMods(selection).SequenceEqual(new[] { "addon", "later" }),
            "missing selection auto discovers manifest packs only");
        File.WriteAllText(Path.Combine(mods, "addon", "extra.json"), "{\"schemaVersion\":1}");
        var automatic = new EveGameRuntime(); automatic.Initialize();
        Check(EveRegistrySession.Current.Get<EveCharacterDefinition>().Count == baseCharacterCount + 2,
            "runtime loads dropped mod folders without selection file");
        automatic.EndSession();
        File.WriteAllText(selection, "{\"schemaVersion\":1,\"enabledMods\":[\"addon\"]}");
        Check(EveDirectoryContentSource.ReadEnabledMods(selection).SequenceEqual(new[] { "addon" }),
            "selection can restrict auto discovery");
        File.WriteAllText(Path.Combine(mods, "addon", "extra.json"), "{\"schemaVersion\":1,\"characters\":[{\"id\":\"addon:extra\",\"stats\":[{\"id\":\"addon:missing\",\"base\":1}]}]}");
        var failed = new EveGameRuntime();
        Reject(() => failed.Initialize(), "failed bootstrap");
        Check(!EveRegistrySession.IsReady, "failed bootstrap never publishes partial registry");
        File.WriteAllText(Path.Combine(mods, "addon", "extra.json"), "{\"schemaVersion\":1}");
        failed.Initialize(); Check(EveRegistrySession.IsReady, "retry succeeds after content repair");
        Check(EveRegistrySession.Current.Get<EveCharacterDefinition>().TryGet(EveId.Parse("addon:hero"), out _), "runtime reads external enabled.json");
        Check(!EveRegistrySession.Current.Get<EveCharacterDefinition>().TryGet(EveId.Parse("later:hero"), out _),
            "selection excludes unlisted mod");
        failed.EndSession();
        File.WriteAllText(selection, "{bad}");
        Reject(() => EveDirectoryContentSource.ReadEnabledMods(selection), "malformed external selection");
    }
    private static void ContentValidation()
    {
        Check(!EveId.TryParse("bad/name:path", out _) && !EveId.TryParse("bad:UPPER", out _) &&
            !EveId.TryParse(new string('a', EveId.MaxNamespaceLength + 1) + ":value", out _) &&
            default(EveId).ToString() == "<invalid>", "EveId namespace, length and default validation");
        Check(EveId.Parse("test:some/path").CompareTo(EveId.Parse("test:some/z")) < 0,
            "EveId path ordering");
        EveRegistryHub Load(string json, string[] dependencies = null) => new EveContentBootstrap().Build(
            new EveIContentModule[] { new PNContentModule() },
            new[] { new EveContentDocument("strict.json", json, "project_n", dependencies: dependencies) });
        Reject(() => Load("{\"schemaVersion\":1,\"characters\":[{\"id\":\"project_n:x\",\"stats\":[{\"statId\":\"project_n:health\",\"base\":100,\"initalCurrent\":20}]}]}"),
            "unknown JSON property");
        Reject(() => Load("{\"schemaVersion\":1,\"schemaVersion\":1}"), "duplicate JSON property");
        Reject(() => Load("{\"schemaVersion\":1,\"characters\":[{\"id\":\"project_n:x\",\"stats\":[{\"statId\":\"project_n:health\",\"base\":100,\"initialCurrent\":101}]}]}"),
            "initialCurrent over base");
        Reject(() => Load("{\"schemaVersion\":1,\"characters\":[{\"id\":\"project_n:x\",\"stats\":[{\"id\":\"project_n:health\",\"statId\":\"project_n:health\",\"base\":100}]}]}"),
            "ambiguous stat reference");
        Reject(() => new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule() },
            new[] { new EveContentDocument("foreign.json", "{\"schemaVersion\":1,\"characters\":[{\"id\":\"addon:x\",\"stats\":[{\"statId\":\"project_n:health\",\"base\":100}]}]}",
                "addon", dependencies: Array.Empty<string>()) }), "cross-pack reference without dependency");
    }
    public static void Main(string[] args)
    {
        root = args[0]; fixture = Path.Combine(Path.GetTempPath(), "EveTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        Application.streamingAssetsPath = Path.Combine(root, "Assets", "StreamingAssets");
        Application.persistentDataPath = fixture;
        if (args.Length > 1 && args[1] == "--dll-tests")
        {
            try { EveCodeModTests.Run(args[2]); Console.WriteLine("PASS: " + checks + " real DLL mod checks"); }
            finally { Directory.Delete(fixture, true); }
            return;
        }
        try { Lifecycle(); EveLifecycleTests.Run(); EveLifecyclePrototypeTests.Run(); EveDataSafetyTests.Run(); EveSkillTests.Run(); PNSkillTests.Run(); PNAbilityTests.Run(); PNCombatRegistryTests.Run(); PNEnemyTypeTests.Run(); PNControlTests.Run(); PNBuffTests.Run(); PNStatCalculationTests.Run(); PNFlatContributionTests.Run(); PNDamageCalculationTests.Run(); PNTagTests.Run(); PNDamageFlowTests.Run(); PNStatReplicationTests.Run(); MoveProtocolTests.Run(); OptionalReplicationTests.Run(); PNIFrameTests.Run(); PNCriticalDamageTests.Run(); PNSkillValueTests.Run(); PNPlayerSkillInputTests.Run(); PNFirstSlashTests.Run(); PNSharedDashTests.Run(); EveMovesetTests.Run(); EveEasyPoolingTests.Run(); EveRegistryBindingTests.Run(); Mods(); ContentValidation(); Console.WriteLine("PASS: " + checks + " checks (Buff, enemy types, Eve moveset, abilities, base bindings, overrides, skill values, damage flow, tags, damage, calculations, combat registries, pooling, lifecycle, movement, skills, rollback, dependencies, identity, coop contracts, content, mods, data safety)"); }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); Environment.ExitCode = 1; }
        finally { Directory.Delete(fixture, true); }
    }
}
