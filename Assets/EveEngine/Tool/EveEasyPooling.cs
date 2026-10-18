using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace EveEngine
{
    // EN: Caller-owned, main-thread GameObject pool. Unity owns the idle list.
    // VI: Pool GameObject do caller sở hữu, chạy trên main thread; Unity giữ danh sách rảnh.
    public sealed class EveEasyPooling : IDisposable
    {
        private readonly GameObject prefab;
        private readonly ObjectPool<GameObject> pool;
        private readonly Dictionary<GameObject, EveIPoolable[]> owned = new Dictionary<GameObject, EveIPoolable[]>();
        private readonly HashSet<GameObject> active = new HashSet<GameObject>();
        private GameObject storage;
        private bool busy;

        // EN: Retain the observed peak by default; maxSize limits idle retention, never live bullets.
        // VI: Mặc định giữ peak đã tạo; maxSize chỉ giới hạn object rảnh, không giới hạn đạn đang bay.
        public EveEasyPooling(GameObject prefab, int defaultCapacity = 16, int maxSize = int.MaxValue)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (defaultCapacity < 0) throw new ArgumentOutOfRangeException(nameof(defaultCapacity));
            if (maxSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
            this.prefab = prefab;
            MaxSize = maxSize;
            pool = new ObjectPool<GameObject>(CreateItem, actionOnDestroy: DestroyItem,
                collectionCheck: false, defaultCapacity: defaultCapacity, maxSize: maxSize);
        }
        public int MaxSize { get; }
        public int CountAll => owned.Count;
        public int CountActive => active.Count;
        public int CountInactive => owned.Count - active.Count;
        public bool IsDisposed { get; private set; }
        public event Action<GameObject> Created;
        public event Action<GameObject> Got;
        public event Action<GameObject> Released;
        public event Action<GameObject> Destroyed;

        public GameObject Get()
        {
            Enter();
            GameObject item = null;
            try
            {
                // EN: Unity cannot detect GameObjects destroyed externally while idle.
                // VI: Unity không tự phát hiện GameObject rảnh đã bị hủy từ bên ngoài.
                do { item = pool.Get(); if (item == null) DestroyItem(item); } while (item == null);
                active.Add(item);
                item.SetActive(false);
                item.transform.SetParent(null, false);
                foreach (var callback in owned[item]) callback.OnPoolGet(this);
                if (item == null) throw new InvalidOperationException("Pool instance was destroyed during Get.");
                Notify(Got, item);
                return item;
            }
            catch { if (!ReferenceEquals(item, null)) DestroyItem(item); throw; }
            finally { busy = false; }
        }
        public bool Release(GameObject item)
        {
            if (ReferenceEquals(item, null) || IsDisposed || !active.Contains(item)) return false;
            Enter();
            try { ReleaseItem(item); return true; }
            finally { busy = false; }
        }
        // A live world transfer changes resource ownership without a pool reset.
        // Unity's idle pool never owns borrowed references; wrapper counts use our
        // owned/active sets. The receiving caller must eventually destroy the item.
        public void ExtractActive(GameObject item)
        {
            Enter();
            try
            {
                if(item==null||!active.Contains(item)||!owned.ContainsKey(item))throw new InvalidOperationException("Only a live borrow can leave a pool.");
                active.Remove(item);owned.Remove(item);
            }
            finally { busy=false; }
        }
        public void ReleaseAll()
        {
            Enter();
            try
            {
                List<Exception> errors = null;
                foreach (var item in new List<GameObject>(active))
                {
                    try { ReleaseItem(item); }
                    catch (Exception error) { (errors ?? (errors = new List<Exception>())).Add(error); }
                }
                if (errors != null) throw new AggregateException("Some pool releases failed; all objects were processed.", errors);
            }
            finally { busy = false; }
        }
        // EN: Unlike Unity Clear, this destroys objects still in use too.
        // VI: Khác Clear của Unity, hàm này hủy cả object đang được sử dụng.
        public void Clear()
        {
            Enter();
            try { ClearItems(); }
            finally { busy = false; }
        }
        public void Dispose()
        {
            if (IsDisposed) return;
            Enter();
            IsDisposed = true;
            try { ClearItems(); }
            finally
            {
                DestroyObject(storage); storage = null;
                Created = null; Got = null; Released = null; Destroyed = null;
                busy = false;
            }
        }
        private GameObject CreateItem()
        {
            if (prefab == null) throw new InvalidOperationException("Pool prefab has been destroyed.");
            if (storage == null)
            {
                storage = new GameObject("EveEasyPooling: " + prefab.name);
                storage.SetActive(false);
            }
            GameObject item = UnityEngine.Object.Instantiate(prefab, storage.transform, false);
            try
            {
                item.SetActive(false);
                var callbacks = new List<EveIPoolable>();
                foreach (var component in item.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component is EveIPoolable callback) callbacks.Add(callback);
                owned.Add(item, callbacks.ToArray());
                foreach (var callback in owned[item]) callback.OnPoolCreated();
                if (item == null) throw new InvalidOperationException("Pool instance was destroyed during creation.");
                Notify(Created, item);
                return item;
            }
            catch { DestroyItem(item); throw; }
        }
        private void ReleaseItem(GameObject item)
        {
            // EN: Invalidate before OnDisable/reset to avoid a double release.
            // VI: Bỏ trạng thái đang dùng trước OnDisable/reset để tránh trả hai lần.
            active.Remove(item);
            if (item == null) { DestroyItem(item); return; }
            try
            {
                item.SetActive(false);
                List<Exception> errors = null;
                foreach (var callback in owned[item])
                {
                    try { callback.OnPoolRelease(); }
                    catch (Exception error) { (errors ?? (errors = new List<Exception>())).Add(error); }
                }
                if (errors != null) throw new AggregateException("Pool reset failed.", errors);
                if (item == null) { DestroyItem(item); return; }
                item.SetActive(false);
                item.transform.SetParent(storage.transform, false);
            }
            catch { DestroyItem(item); throw; }
            pool.Release(item);
            Notify(Released, item);
        }
        private void ClearItems()
        {
            active.Clear();
            // EN: Unity destroys idle items through DestroyItem.
            // VI: Unity hủy các object rảnh qua DestroyItem.
            pool.Clear();
            foreach (var item in new List<GameObject>(owned.Keys)) DestroyItem(item);
        }
        private void DestroyItem(GameObject item)
        {
            if (ReferenceEquals(item, null)) return;
            active.Remove(item);
            bool tracked = owned.Remove(item);
            DestroyObject(item);
            if (tracked) Notify(Destroyed, item);
        }
        private static void DestroyObject(GameObject item)
        {
            if (item == null) return;
            item.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(item);
            else UnityEngine.Object.DestroyImmediate(item);
        }
        private static void Notify(Action<GameObject> listeners, GameObject item)
        {
            if (listeners == null) return;
            foreach (Action<GameObject> listener in listeners.GetInvocationList())
            {
                try { listener(item); }
                catch (Exception error) { Debug.LogException(error, null); }
            }
        }
        private void Enter()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(EveEasyPooling));
            if (busy) throw new InvalidOperationException("Cannot mutate a pool from its lifecycle hooks or events.");
            busy = true;
        }
    }
}
