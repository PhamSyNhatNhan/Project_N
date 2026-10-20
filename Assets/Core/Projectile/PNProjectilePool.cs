#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: Projectile object pool (one per definition and scene). Unlike EveEasyPooling it keeps idle objects in release order, so the
//     manager can destroy the oldest idle object across pools when the global cap is reached. Reuse takes the newest idle object.
//     Calls the same EveIPoolable hooks as EveEasyPooling. Main thread only; every borrow is owned by the manager.
// VI: Pool đạn (mỗi definition và scene một pool). Giữ object rảnh theo thứ tự trả để manager hủy được object rảnh lâu nhất khi
//     chạm trần; tái sử dụng lấy object rảnh mới nhất. Gọi cùng hook EveIPoolable như EveEasyPooling.
public sealed class PNProjectilePool : IDisposable
{
    private struct Idle { public GameObject Item; public long Stamp; }
    private readonly GameObject template;
    private readonly Dictionary<GameObject, EveIPoolable[]> owned = new Dictionary<GameObject, EveIPoolable[]>();
    private readonly HashSet<GameObject> active = new HashSet<GameObject>();
    private readonly LinkedList<Idle> idle = new LinkedList<Idle>();
    private GameObject storage;
    private bool busy;
    public PNProjectilePool(GameObject template) { this.template = template != null ? template : throw new ArgumentNullException(nameof(template)); }
    public int CountAll => owned.Count;
    public int CountActive => active.Count;
    public int CountIdle => idle.Count;
    public bool IsDisposed { get; private set; }
    // Release stamp of the oldest idle object (lower = older); long.MaxValue when none is idle.
    public long OldestIdleStamp => idle.First != null ? idle.First.Value.Stamp : long.MaxValue;
    public event Action<GameObject> Created;
    public event Action<GameObject> Destroyed;

    public bool HasIdle => idle.Count != 0;
    // Takes the newest idle object, or null when none is idle (the caller then checks the cap and calls Create).
    public GameObject TakeIdle()
    {
        Enter();
        try
        {
            while (idle.Last != null)
            {
                var item = idle.Last.Value.Item; idle.RemoveLast();
                if (item == null) { Forget(item); continue; }
                return Activate(item);
            }
            return null;
        }
        finally { busy = false; }
    }
    // Creates one new object and takes it. The caller has already made room under the global cap.
    public GameObject Create()
    {
        Enter();
        GameObject item = null;
        try { item = Build(); return Activate(item); }
        catch { if (!ReferenceEquals(item, null)) Forget(item); throw; }
        finally { busy = false; }
    }
    public bool Release(GameObject item, long stamp)
    {
        if (ReferenceEquals(item, null) || IsDisposed || !active.Contains(item)) return false;
        Enter();
        try
        {
            active.Remove(item);
            if (item == null) { Forget(item); return true; }
            try
            {
                item.SetActive(false);
                List<Exception> errors = null;
                foreach (var callback in owned[item])
                {
                    try { callback.OnPoolRelease(); }
                    catch (Exception error) { (errors ?? (errors = new List<Exception>())).Add(error); }
                }
                if (errors != null) throw new AggregateException("Projectile pool reset failed.", errors);
                if (item == null) { Forget(item); return true; }
                item.SetActive(false); item.transform.SetParent(storage.transform, false);
            }
            catch { Forget(item); throw; }
            idle.AddLast(new Idle { Item = item, Stamp = stamp });
            return true;
        }
        finally { busy = false; }
    }
    // Destroys the oldest idle object; false when none is idle.
    public bool DestroyOldestIdle()
    {
        Enter();
        try
        {
            if (idle.First == null) return false;
            var item = idle.First.Value.Item; idle.RemoveFirst(); Forget(item); return true;
        }
        finally { busy = false; }
    }
    public void Dispose()
    {
        if (IsDisposed) return;
        Enter(); IsDisposed = true;
        try
        {
            idle.Clear(); active.Clear();
            foreach (var item in new List<GameObject>(owned.Keys)) Forget(item);
        }
        finally { Destroy(storage); storage = null; Created = null; Destroyed = null; busy = false; }
    }
    private GameObject Build()
    {
        if (template == null) throw new InvalidOperationException("Projectile pool template has been destroyed.");
        if (storage == null) { storage = new GameObject("PNProjectilePool: " + template.name); storage.SetActive(false); }
        var item = UnityEngine.Object.Instantiate(template, storage.transform, false);
        try
        {
            item.SetActive(false);
            var callbacks = new List<EveIPoolable>();
            foreach (var component in item.GetComponentsInChildren<MonoBehaviour>(true)) if (component is EveIPoolable callback) callbacks.Add(callback);
            owned.Add(item, callbacks.ToArray());
            foreach (var callback in owned[item]) callback.OnPoolCreated();
            if (item == null) throw new InvalidOperationException("Projectile instance was destroyed during creation.");
            Notify(Created, item);
            return item;
        }
        catch { Forget(item); throw; }
    }
    private GameObject Activate(GameObject item)
    {
        active.Add(item);
        try
        {
            item.SetActive(false); item.transform.SetParent(null, false);
            foreach (var callback in owned[item]) callback.OnPoolGet(null);
            if (item == null) throw new InvalidOperationException("Projectile instance was destroyed during Get.");
            return item;
        }
        catch { Forget(item); throw; }
    }
    private void Forget(GameObject item)
    {
        if (ReferenceEquals(item, null)) return;
        active.Remove(item);
        bool tracked = owned.Remove(item);
        Destroy(item);
        if (tracked) Notify(Destroyed, item);
    }
    private static void Destroy(GameObject item)
    {
        if (item == null) return;
        item.SetActive(false);
        if (Application.isPlaying) UnityEngine.Object.Destroy(item); else UnityEngine.Object.DestroyImmediate(item);
    }
    private static void Notify(Action<GameObject> listeners, GameObject item)
    {
        if (listeners == null) return;
        foreach (Action<GameObject> listener in listeners.GetInvocationList())
        { try { listener(item); } catch (Exception error) { Debug.LogException(error); } }
    }
    private void Enter()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(PNProjectilePool));
        if (busy) throw new InvalidOperationException("Cannot mutate a projectile pool from its lifecycle hooks or events.");
        busy = true;
    }
}
#endif
