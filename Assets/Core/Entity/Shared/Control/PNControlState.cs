using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

public sealed class PNControlPermissionDefinition : EveIIdentified
{
    public EveId Id { get; }
    public PNControlPermissionDefinition(EveId id)
    { if (!id.IsValid) throw new ArgumentException("Permission ID required."); Id = id; }
}

public static class PNControlIds
{
    public static readonly EveId Move = EveId.Parse("project_n:move");
    public static readonly EveId Jump = EveId.Parse("project_n:jump");
    public static readonly EveId FaceControl = EveId.Parse("project_n:face_control");
    public static readonly EveId FaceSkill = EveId.Parse("project_n:face_skill");
    public static readonly EveId SkillAll = EveId.Parse("project_n:skill_all");
    public static readonly EveId Attack = EveId.Parse("project_n:skill_attack");
    public static readonly EveId Skill = EveId.Parse("project_n:skill_skill");
    // Binary API compatibility only; no skill_cast registry identity/alias is retained.
    [Obsolete("Use Skill. The registry permission is skill_skill.")]
    public static readonly EveId Cast = Skill;
    public static readonly EveId Dash = EveId.Parse("project_n:skill_dash");
    public static readonly EveId Ultimate = EveId.Parse("project_n:skill_ultimate");
    public static readonly EveId Burst = EveId.Parse("project_n:skill_burst");
    public static readonly EveId VoluntaryMotion = EveId.Parse("project_n:motion_voluntary");
    public static readonly EveId ForcedMotion = EveId.Parse("project_n:motion_forced");
    public static IEnumerable<EveId> All => new[] { Move, Jump, FaceControl, FaceSkill, SkillAll, Attack, Skill, Dash, Ultimate, Burst, VoluntaryMotion, ForcedMotion };
}

public sealed class PNControlDecision
{
    public bool Allowed { get; }
    public IReadOnlyList<EveId> BlockedPermissions { get; }
    public IReadOnlyList<long> BlockingLeaseIds { get; }
    internal PNControlDecision(IEnumerable<EveId> permissions, IEnumerable<long> leases)
    {
        BlockedPermissions = Array.AsReadOnly(permissions.Distinct().OrderBy(x => x).ToArray());
        BlockingLeaseIds = Array.AsReadOnly(leases.Distinct().OrderBy(x => x).ToArray());
        Allowed = BlockedPermissions.Count == 0 && BlockingLeaseIds.Count == 0;
    }
}

public sealed class PNControlLease : IDisposable
{
    private PNControlState state;
    public long Id { get; }
    public string Owner { get; }
    public IReadOnlyList<EveId> Permissions { get; }
    public IReadOnlyList<EveId> SkillIds { get; }
    public bool IsActive => state != null && state.Contains(Id);
    internal PNControlLease(PNControlState state, long id, string owner, EveId[] permissions, EveId[] skills)
    { this.state = state; Id = id; Owner = owner; Permissions = Array.AsReadOnly(permissions); SkillIds = Array.AsReadOnly(skills); }
    public void Dispose() { var previous = state; state = null; previous?.Release(Id); }
}

public sealed class PNControlLeaseSnapshot
{
    public long Id { get; }
    public string Owner { get; }
    public IReadOnlyList<EveId> Permissions { get; }
    public IReadOnlyList<EveId> SkillIds { get; }
    internal PNControlLeaseSnapshot(PNControlLease lease)
    { Id = lease.Id; Owner = lease.Owner; Permissions = lease.Permissions; SkillIds = lease.SkillIds; }
}

// Entity-local state. Read-only snapshots retain owner names without exposing mutations.
public sealed class PNControlState : IDisposable
{
    private readonly EveRegistryHub registry;
    private readonly Func<bool> canMutate;
    private readonly Dictionary<EveId, bool> bases = new Dictionary<EveId, bool>();
    private readonly Dictionary<long, PNControlLease> leases = new Dictionary<long, PNControlLease>();
    private long nextId;
    public bool IsDisposed { get; private set; }
    public event Action Changed;
    public event Action<Exception> ObserverFailed;
    public PNControlState(EveRegistryHub registry, Func<bool> canMutate = null)
    {
        if (registry == null || !registry.IsSealed) throw new ArgumentException("Sealed registry required.");
        this.registry = registry; this.canMutate = canMutate ?? (() => true); registry.ControlPermissions();
    }
    private EveId Permission(EveId id)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(PNControlState));
        var definitions = registry.ControlPermissions(); var canonical = definitions.ResolveIdentity(id);
        definitions.Get(canonical); return canonical;
    }
    private EveId Skill(EveId id) { registry.Skills.Get(id); return registry.Skills.ResolveIdentity(id); }
    public bool GetBaseAllowed(EveId permission)
    { permission = Permission(permission); return !bases.TryGetValue(permission, out var value) || value; }
    public bool IsAllowed(EveId permission)
    { permission = Permission(permission); return GetBaseAllowed(permission) && !leases.Values.Any(x => x.Permissions.Contains(permission)); }
    public void SetBaseAllowed(EveId permission, bool allowed)
    {
        if (!canMutate()) throw new InvalidOperationException("Control mutation requires active session authority.");
        permission = Permission(permission); bool before = IsAllowed(permission); bases[permission] = allowed;
        if (before != IsAllowed(permission)) Notify();
    }
    public PNControlLease Acquire(string owner, IEnumerable<EveId> permissions = null, IEnumerable<EveId> skillIds = null)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(PNControlState));
        if (!canMutate()) throw new InvalidOperationException("Control mutation requires active session authority.");
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Invocation owner required.");
        var rights = (permissions ?? Array.Empty<EveId>()).Select(Permission).Distinct().ToArray();
        var skills = (skillIds ?? Array.Empty<EveId>()).Select(Skill).Distinct().ToArray();
        if (rights.Length + skills.Length == 0) throw new ArgumentException("Empty control lease.");
        bool changed = rights.Any(IsAllowed) || skills.Any(id => !leases.Values.Any(x => x.SkillIds.Contains(id)));
        var lease = new PNControlLease(this, checked(++nextId), owner, rights, skills); leases.Add(lease.Id, lease);
        if (changed) Notify(); return lease;
    }
    public PNControlDecision CheckSkill(EveId category, EveId skillId)
        => CheckSkill(new[] { category }, skillId);
    public PNControlDecision CheckSkill(IEnumerable<EveId> permissions, EveId skillId)
    {
        var rights = new[] { PNControlIds.SkillAll }.Concat(permissions).Select(Permission).Distinct().ToArray();
        skillId = Skill(skillId);
        return new PNControlDecision(rights.Where(id => !IsAllowed(id)),
            leases.Values.Where(x => x.Permissions.Any(rights.Contains) || x.SkillIds.Contains(skillId)).Select(x => x.Id));
    }
    public IReadOnlyList<PNControlLeaseSnapshot> Snapshot()
    { if (IsDisposed) throw new ObjectDisposedException(nameof(PNControlState)); return Array.AsReadOnly(leases.Values.OrderBy(x => x.Id).Select(x => new PNControlLeaseSnapshot(x)).ToArray()); }
    internal bool Contains(long id) => !IsDisposed && leases.ContainsKey(id);
    internal void Release(long id)
    {
        if (!leases.TryGetValue(id, out var lease)) return;
        leases.Remove(id);
        if (!IsDisposed && (lease.Permissions.Any(IsAllowed) || lease.SkillIds.Any(skill => !leases.Values.Any(x => x.SkillIds.Contains(skill))))) Notify();
    }
    private void Notify()
    {
        foreach (Action callback in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { callback(); } catch (Exception error) { try { ObserverFailed?.Invoke(error); } catch { } }
    }
    public void Dispose() { if (IsDisposed) return; IsDisposed = true; leases.Clear(); bases.Clear(); Changed = null; ObserverFailed = null; }
}
