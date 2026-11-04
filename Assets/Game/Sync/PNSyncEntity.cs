#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: A component on an entity that makes it addressable by the state-and-command layer (Docs/NetworkStatePlan.md N4): the entity's id + generation become a PNSyncTarget of kind Entity.
//     It is an entity load step: it registers the target when the entity commits (before it is published, so a listener may register its writers and readers first) and, when the entity is
//     unloaded or retired, tells the listeners and calls PNSyncService.RemoveEntity, which retires every generation up to this life: a pooled or rebuilt entity is a NEW generation, so state of an
//     old life never reaches the new one. Add it only to entities that have a block or command to sync (a boss, a summon), not to every entity.
// VI: Gắn vào entity để lớp đồng bộ nhắm tới được (id + generation). Là bước nạp: đăng ký khi entity commit, gỡ và retire khi entity dỡ.
public interface IPNSyncEntityListener
{
    void OnBound(PNSyncService sync, PNSyncTarget target);
    void OnUnbound();
}

[DisallowMultipleComponent]
public sealed class PNSyncEntity : MonoBehaviour, EveIEntityLoadStep, EveIEntityCommitStep
{
    private EveEntityLoadContext context;
    private PNSyncService sync;
    private IDisposable lease;
    public PNSyncTarget Target { get; private set; }
    public bool IsBound { get; private set; }

    public int LoadOrder => 90;
    public IEnumerable<Type> Dependencies => Type.EmptyTypes;
    public void Load(EveEntityLoadContext value)
    {
        if (context != null) throw new InvalidOperationException("Sync entity already loaded.");
        context = value ?? throw new ArgumentNullException(nameof(value));
    }
    public void CommitEntity(EveEntityLoadContext value) { Bind(); }
    public void EntityPublished(EveEntityLoadContext value) { }

    private void Bind()
    {
        if (IsBound || context == null) return;
        var runtime = PNGameBootstrap.Runtime; var game = runtime != null ? runtime.GetComponent<PNGameSession>() : null;
        if (game == null || game.Sync == null || !ReferenceEquals(game.Session, context.Session)) throw new InvalidOperationException("The entity's session has no sync service.");
        sync = game.Sync; Target = PNSyncTarget.ForEntity(context.Identity.Id, context.Generation);
        lease = sync.RegisterObject(Target, this); IsBound = true;
        try { foreach (var listener in GetComponents<IPNSyncEntityListener>()) listener.OnBound(sync, Target); }
        catch { Unbind(); throw; }
    }

    private void Unbind()
    {
        if (!IsBound) return;
        IsBound = false;
        foreach (var listener in GetComponents<IPNSyncEntityListener>()) { try { listener.OnUnbound(); } catch (Exception error) { Debug.LogException(error, this); } }
        lease?.Dispose(); lease = null;
        var target = Target; Target = default;
        try { sync?.RemoveEntity(target.Entity, target.Generation); } catch (ObjectDisposedException) { }
        sync = null;
    }

    public void Unload() { Unbind(); context = null; }
}
#endif
