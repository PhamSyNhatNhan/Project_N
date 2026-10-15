using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

public readonly struct PNAbilityCost
{
    public PNAbilityHandle Handle { get; }
    public int Amount { get; }
    public PNAbilityCost(PNAbilityHandle handle, int amount = 1)
    { if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount)); Handle = handle; Amount = amount; }
}

// EN: Entity-local passive state. Never discovers actions, HP, input, objects or effects.
// VI: Trạng thái thụ động riêng entity; không tìm action, HP, input, object hay hiệu ứng.
public sealed class PNAbilityCollection : IDisposable
{
    private readonly Guid ownerId = Guid.NewGuid();
    private readonly Dictionary<EveId, PNAbilityDefinition> definitions = new Dictionary<EveId, PNAbilityDefinition>();
    private readonly Dictionary<PNAbilityHandle, PNAbility> instances = new Dictionary<PNAbilityHandle, PNAbility>();
    private readonly Queue<PNAbilityChange> notices = new Queue<PNAbilityChange>();
    private long nextInstance;
    private bool disposed, dispatching, advancing;
    private int batching;
    private readonly Func<bool> canMutate;
    public event Action<PNAbilityChange> Changed;
    public event Action<Exception> ObserverFailed;
    public bool IsDisposed => disposed;
    public IReadOnlyList<PNAbility> Instances => instances.Values.OrderBy(a => a.Handle.Instance).ToList().AsReadOnly();
    public IReadOnlyList<PNAbilityDefinition> Definitions => definitions.Values.OrderBy(d => d.Id).ToList().AsReadOnly();

    public PNAbilityCollection(IEnumerable<PNAbilityDefinition> definitions = null, Func<bool> canMutate = null)
    {
        var inputs = (definitions ?? Array.Empty<PNAbilityDefinition>()).ToArray();
        if (inputs.Any(d => d == null) || inputs.Select(d => d.Id).Distinct().Count() != inputs.Length)
            throw new ArgumentException("Ability definitions must be non-null with unique IDs.", nameof(definitions));
        foreach (var definition in inputs) Define(definition);
        this.canMutate = canMutate;
    }
    internal void RequireMutable()
    { if (disposed) throw new ObjectDisposedException(nameof(PNAbilityCollection)); if (canMutate != null && !canMutate()) throw new InvalidOperationException("Ability mutation requires published authority."); }
    public PNAbility Define(PNAbilityDefinition definition)
    {
        RequireMutable(); if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (definitions.ContainsKey(definition.Id)) throw new ArgumentException("Duplicate ability definition: " + definition.Id);
        PNAbility created = null;
        Batch(() => { definitions.Add(definition.Id, definition); created = CreateInstance(definition.Id); });
        return created;
    }
    public PNAbility CreateInstance(EveId id)
    {
        RequireMutable();
        if (!definitions.TryGetValue(id, out var definition)) throw new KeyNotFoundException("Unknown ability: " + id);
        if (definition.Kind != PNAbilityKind.State && Find(id).Count != 0)
            throw new InvalidOperationException("Counter/cooldown IDs have exactly one runtime instance per entity.");
        PNAbility created = null; Batch(() => created = Create(definition, false)); return created;
    }
    private PNAbility Create(PNAbilityDefinition definition, bool transient)
    {
        var handle = new PNAbilityHandle(ownerId, checked(++nextInstance));
        PNAbility ability;
        switch (definition.Kind)
        {
            case PNAbilityKind.Cooldown: ability = new PNAbilityCooldown(this, definition, handle); break;
            case PNAbilityKind.Counter: ability = new PNAbilityCounter(this, definition, handle); break;
            default: ability = new PNAbilityState(this, definition, handle, transient); break;
        }
        instances.Add(handle, ability);
        Notify(new PNAbilityChange(PNAbilityChangeReason.Created, null, ability.Snapshot()));
        return ability;
    }
    public IReadOnlyList<PNAbility> Find(EveId id)
    {
        RequireMutable();
        if (!definitions.ContainsKey(id)) throw new KeyNotFoundException("Unknown ability: " + id);
        return instances.Values.Where(a => a.Id == id).OrderBy(a => a.Handle.Instance).ToList().AsReadOnly();
    }
    public bool TryGet(PNAbilityHandle handle, out PNAbility ability)
    { ability = null; return !disposed && instances.TryGetValue(handle, out ability); }
    public PNAbility Get(PNAbilityHandle handle)
    {
        RequireMutable(); if (!TryGet(handle, out var ability)) throw new KeyNotFoundException("Stale or foreign ability handle.");
        return ability;
    }
    public PNAbilityCooldown GetCooldown(EveId id) => GetUnique<PNAbilityCooldown>(id);
    public PNAbilityCounter GetCounter(EveId id) => GetUnique<PNAbilityCounter>(id);
    private T GetUnique<T>(EveId id) where T : PNAbility
    {
        var matches = Find(id);
        if (matches.Count != 1 || !(matches[0] is T typed)) throw new InvalidOperationException("Ability ID does not identify exactly one " + typeof(T).Name);
        return typed;
    }
    public IReadOnlyList<PNAbilityState> GetStates(EveId id)
    {
        RequireMutable();
        if (!definitions.TryGetValue(id, out var definition) || definition.Kind != PNAbilityKind.State)
            throw new KeyNotFoundException("Unknown state ability: " + id);
        return Find(id).Cast<PNAbilityState>().ToList().AsReadOnly();
    }
    public PNAbilityState GrantState(EveId id, double? duration = null)
    {
        RequireMutable(); PNAbilityState result = null;
        Batch(() => result = GrantStateInternal(id, duration)); return result;
    }
    private PNAbilityState GrantStateInternal(EveId id, double? duration)
    {
        RequireMutable(); if (duration.HasValue) PNAbilityDefinition.RequireSeconds(duration.Value, false);
        if (!definitions.TryGetValue(id, out var definition) || definition.Kind != PNAbilityKind.State)
            throw new KeyNotFoundException("Unknown state ability: " + id);
        var states = GetStates(id);
        var active = states.Where(s => s.IsActive).ToArray();
        double? seconds = duration ?? definition.Duration;
        if (active.Length != 0)
        {
            if (definition.Reapply != PNAbilityReapply.Independent)
            {
                if (active.Length != 1) throw new InvalidOperationException("Multiple states require an explicit instance handle.");
                var existing = active[0];
                if (definition.Reapply == PNAbilityReapply.Refresh)
                {
                    if (!seconds.HasValue) throw new InvalidOperationException("A refresh requires a timed duration.");
                    existing.RefreshDuration(seconds.Value);
                }
                else if (definition.Reapply == PNAbilityReapply.Extend)
                {
                    if (!seconds.HasValue) throw new InvalidOperationException("An extension requires a timed duration.");
                    existing.ExtendDuration(seconds.Value);
                }
                return existing;
            }
            var additional = (PNAbilityState)Create(definition, true);
            additional.TryActivate(seconds); return additional;
        }
        var ready = states.FirstOrDefault(s => s.CanActivate);
        if (ready == null && states.Count != 0) throw new InvalidOperationException("State is still on cooldown.");
        if (ready == null) ready = (PNAbilityState)Create(definition, false);
        ready.TryActivate(seconds); return ready;
    }
    public bool Remove(PNAbilityHandle handle)
    {
        RequireMutable(); if (!instances.TryGetValue(handle, out var ability)) return false;
        var before = ability.Snapshot(); instances.Remove(handle); ability.Detach();
        Notify(new PNAbilityChange(PNAbilityChangeReason.Removed, before, null)); return true;
    }
    public void ResetAll()
    {
        RequireMutable(); Batch(() =>
        {
            foreach (var ability in Instances)
                if (ability is PNAbilityState state && state.Transient) Remove(ability.Handle); else ability.Reset();
        });
    }
    public void ClearInstances()
    { RequireMutable(); Batch(() => { foreach (var ability in Instances) Remove(ability.Handle); }); }

    // EN: Precheck all costs, commit all changes, then notify. No action or cancellation policy implied.
    // VI: Kiểm tra mọi chi phí, ghi toàn bộ rồi thông báo; không quyết định action hay chính sách hủy.
    public bool TryConsume(IEnumerable<PNAbilityCost> costs)
    {
        RequireMutable(); if (costs == null) throw new ArgumentNullException(nameof(costs));
        var inputs = costs.ToArray();
        if (inputs.Any(c => c.Amount <= 0) || inputs.Select(c => c.Handle).Distinct().Count() != inputs.Length)
            throw new ArgumentException("Costs must be positive and use distinct handles.", nameof(costs));
        var targets = new List<PNAbility>();
        foreach (var cost in inputs)
        {
            if (!TryGet(cost.Handle, out var ability) || !ability.CanConsume(cost.Amount)) return false;
            targets.Add(ability);
        }
        Batch(() => { for (int i = 0; i < inputs.Length; i++) targets[i].TryConsume(inputs[i].Amount); });
        return true;
    }
    public void Advance(double gameDeltaTime, double entityScale = 1, double rawScale = 1)
    {
        RequireMutable(); PNAbilityDefinition.RequireSeconds(gameDeltaTime, true);
        PNAbilityDefinition.RequireSeconds(entityScale, true);
        if (double.IsNaN(rawScale) || double.IsInfinity(rawScale)) throw new ArgumentOutOfRangeException(nameof(rawScale));
        if (advancing || dispatching) throw new InvalidOperationException("Ability clock cannot advance recursively or inside a change notification.");
        var work = Instances;
        var deltas = work.Select(a => gameDeltaTime * (a.Definition.Clock == PNAbilityClock.Game ? 1 :
            a.Definition.Clock == PNAbilityClock.Entity ? entityScale : Math.Max(0, rawScale))).ToArray();
        foreach (double delta in deltas) PNAbilityDefinition.RequireSeconds(delta, true);
        Batch(() =>
        {
            advancing = true;
            try
            {
                for (int i = 0; i < work.Count; i++) work[i].Advance(deltas[i]);
                foreach (var state in work.OfType<PNAbilityState>())
                    if (state.Transient && !state.IsActive && state.CooldownRemaining == 0) Remove(state.Handle);
            }
            finally { advancing = false; }
        });
    }
    private void Batch(Action action)
    {
        batching++;
        try { action(); }
        finally { batching--; Flush(); }
    }
    internal void Notify(PNAbilityChange change) { notices.Enqueue(change); Flush(); }
    private void Flush()
    {
        if (batching != 0 || dispatching) return;
        dispatching = true;
        try
        {
            while (notices.Count != 0)
            {
                var notice = notices.Dequeue(); var listeners = Changed;
                if (listeners == null) continue;
                foreach (Action<PNAbilityChange> listener in listeners.GetInvocationList())
                    try { listener(notice); }
                    catch (Exception error)
                    {
                        if (ObserverFailed == null) continue;
                        foreach (Action<Exception> diagnostic in ObserverFailed.GetInvocationList())
                            try { diagnostic(error); } catch { }
                    }
            }
        }
        finally { dispatching = false; }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var ability in instances.Values) ability.Detach();
        instances.Clear(); definitions.Clear(); notices.Clear(); Changed = null; ObserverFailed = null;
    }
}
