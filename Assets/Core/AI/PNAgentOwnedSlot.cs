#if UNITY_5_3_OR_NEWER
using System;

// Own this once in a selection scope; replace a running helper without retaining every old helper.
public sealed class PNAgentOwnedSlot<T> : IDisposable where T : class, IDisposable
{
    private bool disposed;
    public T Value { get; private set; }
    public T Replace(Func<T> create)
    {
        if (disposed) throw new ObjectDisposedException(nameof(PNAgentOwnedSlot<T>));
        if (create == null) throw new ArgumentNullException(nameof(create));
        Value?.Dispose(); Value = null; Value = create(); return Value;
    }
    public void Dispose() { if (disposed) return; Value?.Dispose(); Value = null; disposed = true; }
}
#endif
