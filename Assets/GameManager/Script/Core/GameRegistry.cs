using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GameRegistry — trung gian (locator) thuần, KHÔNG chứa logic domain.
///
/// Vai trò duy nhất:
///   - Giữ map name → registry component (kéo vào Inspector).
///   - Cho code khác lấy component qua Get&lt;T&gt;(name) — không cần ref,
///     không FindObjectOfType.
///   - Cho mod đăng ký component mới lúc runtime qua Register(name, component).
///
/// Logic load (JSON, prefab...) và API truy vấn (GetPrefab, GetConfig...)
/// do từng component con tự định nghĩa — GameRegistry không biết gì về chúng.
///
/// Cách dùng:
///   GameRegistry.Get&lt;EnemyRegistry&gt;("Enemy").GetPrefab("Slime");
///   GameRegistry.Get&lt;MapRegistry&gt;("Map").GetConfig("Calamity");
///
/// Mod thêm registry mới:
///   1. Viết MonoBehaviour implement IGameRegistryComponent.
///   2. Sau event RegistryReady
///      (EventManager.Gm.TriggerGenericEvent.Get("RegistryReady")):
///      tạo GameObject, AddComponent, rồi GameRegistry.Register("MyTable", comp).
///
/// Setup: gắn lên object con "Registry" của GameManager, các registry con
/// gắn cùng object hoặc object con, kéo vào danh sách entries kèm tên.
/// Thứ tự trong danh sách = thứ tự Initialize.
/// </summary>
public class GameRegistry : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [Serializable]
    public struct Entry
    {
        public string        name;
        public MonoBehaviour component;   // phải implement IGameRegistryComponent
    }

    [Header("Registry Components")]
    [Tooltip("Kéo các registry component vào kèm tên. Thứ tự = thứ tự Initialize.")]
    [SerializeField] private Entry[] entries;

    // ── Static map ────────────────────────────────────────────────
    private static readonly Dictionary<string, MonoBehaviour> _components
        = new Dictionary<string, MonoBehaviour>();

    /// <summary>True sau khi tất cả component trong danh sách Initialize xong.</summary>
    public static bool IsReady { get; private set; }

    // ── API ───────────────────────────────────────────────────────

    /// <summary>
    /// Lấy registry component theo tên. KHÔNG tự tạo — tên không tồn tại
    /// hoặc sai kiểu sẽ log lỗi đích danh và trả về null (fail-fast).
    /// </summary>
    public static T Get<T>(string name) where T : class
    {
        if (string.IsNullOrEmpty(name) || !_components.TryGetValue(name, out var component))
        {
            Debug.LogError($"[GameRegistry] Không tồn tại registry '{name}'. " +
                           $"Đã đăng ký: [{string.Join(", ", _components.Keys)}]");
            return null;
        }

        if (component is T typed) return typed;

        Debug.LogError($"[GameRegistry] Registry '{name}' là {component.GetType().Name}, " +
                       $"không phải {typeof(T).Name}.");
        return null;
    }

    public static bool Has(string name)
        => !string.IsNullOrEmpty(name) && _components.ContainsKey(name);

    public static IEnumerable<string> GetAllNames() => _components.Keys;

    /// <summary>
    /// Đăng ký registry component — mod gọi được lúc runtime
    /// (sau event RegistryReady). Tự gọi Initialize nếu GameRegistry đã ready.
    /// Trùng tên sẽ bị từ chối + log lỗi (tránh 2 mod đè nhau âm thầm).
    /// </summary>
    public static void Register(string name, MonoBehaviour component)
    {
        if (string.IsNullOrEmpty(name) || component == null)
        {
            Debug.LogError("[GameRegistry] Register bị bỏ qua — name hoặc component null.");
            return;
        }

        if (_components.ContainsKey(name))
        {
            Debug.LogError($"[GameRegistry] Registry '{name}' đã tồn tại " +
                           $"({_components[name].GetType().Name}) — Register bị từ chối. " +
                           $"Unregister trước nếu muốn thay thế.");
            return;
        }

        if (component is not IGameRegistryComponent registryComponent)
        {
            Debug.LogError($"[GameRegistry] '{component.GetType().Name}' không implement " +
                           $"IGameRegistryComponent — Register bị từ chối.");
            return;
        }

        _components[name] = component;

        // Đăng ký sau khi GameRegistry đã chạy xong → tự Initialize luôn
        if (IsReady) registryComponent.Initialize();
    }

    public static void Unregister(string name)
    {
        if (!string.IsNullOrEmpty(name)) _components.Remove(name);
    }

    // ── Lifecycle ─────────────────────────────────────────────────
    private static GameRegistry _activeInstance;

    private void Awake()
    {
        Debug.Log($"[GameRegistry] Awake — scene='{gameObject.scene.name}', entityId={GetEntityId()}, frame={Time.frameCount}");
        // Guard: GameManager là DontDestroyOnLoad → scene sau load lên
        // có thể chứa GameManager duplicate. Bản duplicate KHÔNG được phép
        // clear/ghi đè map của bản đang sống.
        if (_activeInstance != null && _activeInstance != this)
        {
            Debug.LogWarning($"[GameRegistry] Phát hiện GameRegistry duplicate trong scene " +
                             $"'{gameObject.scene.name}' — bỏ qua (bản gốc DDOL đang giữ data). " +
                             $"Nên xóa GameManager thừa khỏi scene này.");
            enabled = false;
            return;
        }
        _activeInstance = this;

        _components.Clear();
        IsReady = false;

        if (entries != null)
        {
            // Nạp map trước, Initialize sau — để component này có thể
            // Get component khác trong Initialize của mình.
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.name) || e.component == null)
                {
                    Debug.LogWarning("[GameRegistry] Entry thiếu name hoặc component — bỏ qua.");
                    continue;
                }

                if (e.component is not IGameRegistryComponent)
                {
                    Debug.LogError($"[GameRegistry] '{e.component.GetType().Name}' (entry '{e.name}') " +
                                   $"không implement IGameRegistryComponent — bỏ qua.");
                    continue;
                }

                if (_components.ContainsKey(e.name))
                {
                    Debug.LogError($"[GameRegistry] Trùng tên entry '{e.name}' trong Inspector — bỏ qua.");
                    continue;
                }

                _components[e.name] = e.component;
            }

            // Initialize theo đúng thứ tự danh sách Inspector
            foreach (var e in entries)
            {
                if (e.component is IGameRegistryComponent registryComponent
                    && _components.TryGetValue(e.name ?? "", out var registered)
                    && ReferenceEquals(registered, e.component))
                {
                    try
                    {
                        registryComponent.Initialize();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[GameRegistry] Initialize '{e.name}' lỗi: {ex.Message}");
                    }
                }
            }
        }

        IsReady = true;

        // Báo cho mod / hệ thống khác biết registry đã sẵn sàng
        EventManager.Gm.TriggerGenericEvent.Get("RegistryReady").Invoke(this, null);
    }

    private void OnDestroy()
    {
        if (_activeInstance == this)
        {
            _activeInstance = null;
            _components.Clear();
            IsReady = false;
        }
    }
}

/// <summary>
/// Interface cho mọi registry component con.
/// GameRegistry gọi Initialize() thay cho Awake — để kiểm soát thứ tự
/// và đảm bảo chạy sau khi map đã nạp đầy đủ.
/// LƯU Ý: component con KHÔNG nên dùng Awake để load data nữa.
/// </summary>
public interface IGameRegistryComponent
{
    void Initialize();
}