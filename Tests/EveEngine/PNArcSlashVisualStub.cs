// EN: Visual-only substitute; the actual mesh/shader are verified in Unity, not by smoke stubs.
// VI: Thay thế chỉ phần hình ảnh; mesh/shader thật kiểm chứng trong Unity, không qua stub.
public class PNArcSlashVisual : UnityEngine.MonoBehaviour
{
    public static bool Fail;
    public static int Spawned, Released, Created;
    public static UnityEngine.GameObject LastObject;
    public static UnityEngine.Vector3 LastDirection;
    private static readonly System.Collections.Generic.Dictionary<UnityEngine.GameObject, PNArcSlashVisual> cache
        = new System.Collections.Generic.Dictionary<UnityEngine.GameObject, PNArcSlashVisual>();
    private EveEngine.EveEasyPooling pool;
    private UnityEngine.GameObject pooledObject;
    public static EveEngine.EveEasyPooling CreatePool() => new EveEngine.EveEasyPooling(new UnityEngine.GameObject("slash prefab"));
    public static PNArcSlashVisual Spawn(EveEngine.EveEasyPooling pool, PNSkill owner, PNSlashVfxSettings settings,
        UnityEngine.Vector3 direction)
    {
        var obj = pool.Get();
        try
        {
            if (Fail) throw new System.InvalidOperationException("Visual setup failed.");
            if (!cache.TryGetValue(obj, out var visual))
            {
                visual = new PNArcSlashVisual(); cache.Add(obj, visual); Created++;
            }
            visual.pool = pool; visual.pooledObject = obj;
            obj.SetActive(true);
            LastObject = obj; LastDirection = direction; Spawned++;
            return visual;
        }
        catch { pool.Release(obj); throw; }
    }
    public void Render(float progress) { }
    public void Release()
    {
        var ownerPool = pool; pool = null;
        if (ownerPool != null && ownerPool.Release(pooledObject)) Released++;
    }
}
