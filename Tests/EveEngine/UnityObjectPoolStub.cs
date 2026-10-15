using System;
using System.Collections.Generic;

// EN: Test-only substitute. Real Unity ObjectPool is separately verified in the Editor.
// VI: Bản thay thế chỉ dành cho kiểm thử; ObjectPool Unity thật được kiểm tra riêng trong Editor.
namespace UnityEngine.Pool
{
    public sealed class ObjectPool<T> : IDisposable where T : class
    {
        private readonly Stack<T> idle;
        private readonly Func<T> create;
        private readonly Action<T> onGet, onRelease, onDestroy;
        private readonly int maxSize;
        public ObjectPool(Func<T> createFunc, Action<T> actionOnGet = null, Action<T> actionOnRelease = null,
            Action<T> actionOnDestroy = null, bool collectionCheck = true, int defaultCapacity = 10, int maxSize = 10000)
        {
            idle = new Stack<T>(defaultCapacity); create = createFunc;
            onGet = actionOnGet; onRelease = actionOnRelease; onDestroy = actionOnDestroy; this.maxSize = maxSize;
        }
        public T Get() { T item = idle.Count == 0 ? create() : idle.Pop(); onGet?.Invoke(item); return item; }
        public void Release(T item)
        {
            onRelease?.Invoke(item);
            if (idle.Count < maxSize) idle.Push(item); else onDestroy?.Invoke(item);
        }
        public void Clear() { while (idle.Count > 0) onDestroy?.Invoke(idle.Pop()); }
        public void Dispose() => Clear();
    }
}
