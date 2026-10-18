using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PNControlManager : MonoBehaviour, EveIEntityLoadStep
{
    private PNControlState state;
    private EveEntityLoadContext context;
    public PNControlState State => IsReady || context != null && context.AllowsModuleAccess ? state : throw new InvalidOperationException("Control state is not published.");
    public bool IsReady => state != null && !state.IsDisposed && context != null && context.CanUse;
    public bool IsInitialized => state != null && !state.IsDisposed;
    public int LoadOrder => -20;
    public IEnumerable<Type> Dependencies => Array.Empty<Type>();
    public void Load(EveEntityLoadContext context)
    {
        if (state != null) throw new InvalidOperationException("Control manager already loaded.");
        this.context = context;
        state = new PNControlState(context.Registry, () => context.CanMutate||context.IsValid&&context.AllowsModuleAccess); state.ObserverFailed += Report;
    }
    private void Report(Exception error) => Debug.LogException(error, this);
    public void Unload() { state?.Dispose(); state = null; context = null; }
    private void OnDestroy() => Unload();
}
