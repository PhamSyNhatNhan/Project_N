#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: Tells the sync service which objects a play can address (Docs/NetworkStatePlan.md N1). When a world becomes ready it finds the PNWorldObject components in the roots of every
//     content slot (the roots the provider handed over, never a scene-wide search), checks the keys, and registers each as a WorldObject target. When the world starts to unload
//     it forgets everything of that world, whoever still holds a writer or reader of it. The same glue runs on a client for the replica of the world.
// VI: Báo cho dịch vụ đồng bộ biết phiên chơi có thể nhắm tới những object nào: khi world sẵn sàng thì đăng ký PNWorldObject trong root của từng slot, khi world sắp dỡ thì quên hết.
public sealed class PNSyncWorldBinder : IDisposable
{
    private readonly PNWorldRuntime worlds;
    private readonly PNSyncService sync;
    private readonly Dictionary<EveWorldContext, List<IDisposable>> leases = new Dictionary<EveWorldContext, List<IDisposable>>();
    private readonly Dictionary<EveWorldContext, List<PNWorldObject>> bound = new Dictionary<EveWorldContext, List<PNWorldObject>>();
    private readonly Dictionary<PNWorldObject, IDisposable> late = new Dictionary<PNWorldObject, IDisposable>();
    private static readonly Dictionary<EveWorldContext, PNSyncWorldBinder> byWorld = new Dictionary<EveWorldContext, PNSyncWorldBinder>();
    private bool disposed;

    // The binder that serves a world once it is ready (null before that and after it unloaded).
    public static PNSyncWorldBinder Find(EveWorldContext world) => world != null && byWorld.TryGetValue(world, out var binder) ? binder : null;

    public PNSyncWorldBinder(PNWorldRuntime worlds, PNSyncService sync)
    {
        this.worlds = worlds ?? throw new ArgumentNullException(nameof(worlds)); this.sync = sync ?? throw new ArgumentNullException(nameof(sync));
        worlds.WorldReady += OnReady; worlds.WorldUnloading += OnUnloading;
    }

    public int BoundWorlds => leases.Count;

    private void OnReady(EveWorldContext world)
    {
        if (disposed || leases.ContainsKey(world)) return;
        var held = new List<IDisposable>(); var objects = new List<PNWorldObject>();
        leases.Add(world, held); bound.Add(world, objects); byWorld[world] = this;
        try
        {
            var build = worlds.Get(world);
            foreach (var slot in build.Slots)
            {
                var found = slot.Roots.Where(x => x != null).SelectMany(x => x.GetComponentsInChildren<PNWorldObject>(true)).ToArray();
                PNWorldObject.ValidateSet(found);
                foreach (var item in found)
                {
                    var target = PNSyncTarget.ForWorldObject(world.InstanceId, world.Incarnation, slot.Id, item.Key);
                    held.Add(sync.RegisterObject(target, item)); item.Bind(target); objects.Add(item);
                }
            }
            // Every object of the world is addressable before any of them is told, so a listener may look at its neighbours.
            foreach (var item in objects) foreach (var listener in item.GetComponents<IPNWorldObjectListener>()) listener.OnBound(sync, item.Target);
        }
        catch { Release(world); throw; }
    }

    private void OnUnloading(EveWorldContext world) { Release(world); }

    // A world object that came into being after the world was ready (a placed thing with an interactable, G9c): made addressable in the given slot and told, the way the ones found at ready were.
    // Returns false when the world is not bound yet (then the ready scan finds the object by itself).
    public bool AddLate(EveWorldContext world, string slotId, PNWorldObject item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (disposed || !bound.TryGetValue(world, out var objects)) return false;
        item.Validate();
        foreach (var other in objects) if (other != null && other.Key == item.Key && other != item) throw new InvalidOperationException("Duplicate world object key '" + item.Key + "'.");
        var target = PNSyncTarget.ForWorldObject(world.InstanceId, world.Incarnation, slotId, item.Key);
        late[item] = sync.RegisterObject(target, item); item.Bind(target); objects.Add(item);
        foreach (var listener in item.GetComponents<IPNWorldObjectListener>()) listener.OnBound(sync, target);
        return true;
    }

    // The object is going away: its listeners are told and it stops being addressable.
    public void RemoveLate(EveWorldContext world, PNWorldObject item)
    {
        if (item == null || !bound.TryGetValue(world, out var objects) || !objects.Remove(item)) return;
        foreach (var listener in item.GetComponents<IPNWorldObjectListener>()) { try { listener.OnUnbound(); } catch (Exception error) { UnityEngine.Debug.LogException(error); } }
        item.Unbind();
        if (late.TryGetValue(item, out var lease)) { late.Remove(item); lease.Dispose(); }
    }

    private void Release(EveWorldContext world)
    {
        if (byWorld.TryGetValue(world, out var current) && current == this) byWorld.Remove(world);
        if (leases.TryGetValue(world, out var held)) { foreach (var lease in held) lease.Dispose(); leases.Remove(world); }
        if (bound.TryGetValue(world, out var lateObjects)) foreach (var item in lateObjects) if (item != null && late.TryGetValue(item, out var lateLease)) { late.Remove(item); lateLease.Dispose(); }
        if (bound.TryGetValue(world, out var objects)) { foreach (var item in objects) if (item != null) { foreach (var listener in item.GetComponents<IPNWorldObjectListener>()) { try { listener.OnUnbound(); } catch (Exception error) { UnityEngine.Debug.LogException(error); } } item.Unbind(); } bound.Remove(world); }
        sync.RemoveWorld(world.InstanceId, world.Incarnation);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; worlds.WorldReady -= OnReady; worlds.WorldUnloading -= OnUnloading;
        foreach (var world in leases.Keys.ToArray()) Release(world);
    }
}
#endif
