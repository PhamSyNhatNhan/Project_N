# EveEasy tools

`Tool` contains reusable utilities named `EveEasy…`, in namespace `EveEngine`.

## EveEasyPooling

VI: Wrapper GameObject cho `UnityEngine.Pool.ObjectPool<GameObject>`. Unity giữ danh sách rảnh; EveEasyPooling tạo từ prefab, gọi hook/event và theo dõi ownership để dọn toàn bộ instance. Không có generic custom hoặc handle/token. Caller (nhân vật/boss) giữ pool qua nhiều lượt và gọi Dispose khi bị hủy. Pool scene-local, không tự liên kết registry/entity/session hoặc sống qua chuyển scene.

EN: Caller-owned, main-thread GameObject pooling. Unity supplies the idle list and retention limit. The wrapper tracks owned/in-use objects for membership, duplicate-release protection and full teardown. Keep it across casts; dispose before scene/owner teardown.

```csharp
private EveEasyPooling bulletPool;

void Initialize(GameObject prefab)
{
    bulletPool = new EveEasyPooling(prefab);
}

void Fire()
{
    // EN: Reserved immediately, still inactive.
    // VI: Đã giữ chỗ ngay, object vẫn inactive.
    GameObject bullet = bulletPool.Get();
    try
    {
        bullet.transform.position = transform.position;
        // EN: Set up shot-specific data here before activating.
        // VI: Truyền dữ liệu riêng của viên đạn tại đây trước khi bật.
        bullet.SetActive(true);
    }
    catch { bulletPool.Release(bullet); throw; }
}

void ClearBulletsForNextPhase() => bulletPool.ReleaseAll();
void OnDestroy() => bulletPool?.Dispose();
```

## API

| API | Behavior |
| --- | --- |
| `new EveEasyPooling(prefab, defaultCapacity: 16, maxSize: int.MaxValue)` | Configure once. Capacity reserves list storage, does not prewarm instances. |
| `Get()` | Return inactive GameObject; remove from idle availability immediately. Create only if needed. |
| `Release(obj)` | Reset and disable, then return to Unity idle storage. False for null/foreign/already-released objects or disposed pool. |
| `ReleaseAll()` | Release all currently borrowed objects; attempt all even when reset fails. |
| `Clear()` | Destroy idle AND borrowed objects; pool remains reusable. |
| `Dispose()` | Destroy everything and storage root, clear event listeners, close permanently; repeated call safe. |
| `CountAll/CountActive/CountInactive` | Wrapper ownership counts; active means borrowed, independent of activeSelf. |

VI: Mặc định giữ toàn bộ peak đã tạo, phù hợp các đợt bullet hell lặp lại. `maxSize` chỉ giới hạn số instance rảnh giữ lại, không giới hạn đạn đang bay. Đặt nhỏ hơn peak khiến các đợt sau có thể tạo lại phần đã bị hủy. Get không tự bật object; caller đặt vị trí/rotation/scale và dữ liệu trước SetActive(true). Chỉ SetActive(false) không trả về pool.

EN: External Destroy is not the normal return path. Destroyed idle objects are discarded lazily on Get; destroyed borrowed objects are removed on Release or teardown. Counts may temporarily include externally destroyed objects. Never use stale raw references from an earlier use: timers, tasks and callbacks must be canceled/reset when releasing; no generation tokens are provided.

## Optional hooks

`EveIPoolable` is discovered once on root/child MonoBehaviours, including inactive children:

```csharp
public class MyBullet : MonoBehaviour, EveIPoolable
{
    private EveEasyPooling pool;
    private float timer;

    public void OnPoolCreated()
    {
        // EN: Initialize required references here; Unity Awake may not have run yet.
        // VI: Khởi tạo tham chiếu cần thiết tại đây; Awake của Unity có thể chưa chạy.
    }

    public void OnPoolGet(EveEasyPooling pool)
    {
        this.pool = pool;
        // EN: Caller supplies shot settings AFTER this hook.
        // VI: Caller truyền dữ liệu viên đạn SAU hook này.
        timer = 0f;
    }

    public void OnPoolRelease()
    {
        pool = null;
        timer = 0f;
        // EN: Cancel callbacks/subscriptions; clear targets and visual state.
        // VI: Dọn callback/listener, target và trạng thái hiển thị.
    }

    public void Finish() => pool?.Release(gameObject);
}
```

VI: Clone được tạo dưới root inactive để tránh OnEnable trước setup. OnPoolCreated/Get có thể chạy trước Awake. OnPoolRelease chạy sau disable, không có nghĩa gameplay thành công: dùng cho cả hết thời gian, va chạm hoặc hủy chiêu. Không tự Release trong OnDisable. Reset ParticleSystem/Animator/Rigidbody nếu cần; wrapper không quyết định gameplay.

Events `Created`, `Got`, `Released`, `Destroyed` receive the GameObject. Hooks run first. Release reaching maxSize may destroy the object, so observers must account for Unity null objects. Listener failures are logged independently; hooks propagate failures and evict bad objects. Hooks/events cannot mutate the same pool recursively. Components added after creation are not automatically rediscovered.

Clear/Dispose are teardown, not normal Release: no OnPoolRelease or Released event; use OnDisable/OnDestroy for destruction cleanup too. Ownership is removed before destruction callbacks. Legacy EasyPoolingList users are not migrated. Eve's slash VFX now uses a character-owned EveEasyPooling and returns on Exit/Cancel; mesh persists until pool teardown.

Validation: smoke wrapper tests under `Tests/EveEngine` (test-only Unity Pool substitute); opt-in native Unity checks in `Assets/Tests/EveEngine/Editor`, called with `EveEasyPoolingEditorChecks.Run()` outside Play Mode. Verified 3 x 800-object bursts retain/reuse 800 instances. No gameplay FPS benchmark claimed.
