using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class PNSkillCategoryDefinition : EveIIdentified
{
    public EveId Id { get; }
    public EveId ControlPermissionId { get; }
    public EveId? DamageTagId { get; }
    public PNSkillCategoryDefinition(EveId id, EveId controlPermissionId)
        : this(id, controlPermissionId, null) { }
    public PNSkillCategoryDefinition(EveId id, EveId controlPermissionId, EveId? damageTagId)
    {
        if (!id.IsValid || !controlPermissionId.IsValid || damageTagId.HasValue && !damageTagId.Value.IsValid)
            throw new ArgumentException("Category, permission and optional damage tag IDs must be valid.");
        Id = id; ControlPermissionId = controlPermissionId; DamageTagId = damageTagId;
    }
    public void Validate(EveRegistryHub hub)
    {
        hub.ControlPermissions().Get(ControlPermissionId);
        if (!DamageTagId.HasValue) return;
        var tag = DamageTagId.Value;
        if (!hub.Tags().TryGet(tag, out _)) throw new ArgumentException($"Category {Id} references unknown damage tag {tag}.");
        var identity = hub.Tags().ResolveIdentity(tag);
        foreach (var candidate in new[] { tag, identity })
            if (hub.Stats.TryGet(candidate, out _) || hub.DamageTypes().TryGet(candidate, out _))
                throw new ArgumentException($"Category {Id} damage tag {tag} conflicts with a stat or damage type ({candidate}).");
    }
    internal static IReadOnlyList<EveId> ResolveTags(EveRegistryHub hub, IEnumerable<EveId> categories,
        IEnumerable<EveId> authored, IEnumerable<EveId> added, IEnumerable<EveId> suppressed)
    {
        var mapped = categories.Select(id => hub.SkillCategories().GetEffective(id).DamageTagId)
            .Where(id => id.HasValue).Select(id => id.Value);
        var result = new HashSet<EveId>(authored.Concat(mapped).Concat(added).Select(hub.Tags().ResolveIdentity));
        result.ExceptWith(suppressed.Select(hub.Tags().ResolveIdentity));
        return Array.AsReadOnly(result.OrderBy(id => id).ToArray());
    }
}

public enum PNSkillReadMode { Snapshot, Live }
public sealed class PNSkillMetadata
{
    internal static readonly string[] StandardTriggers = { "OnActivated", "OnActionStart", "OnHit", "OnComplete", "OnCancel", "OnInvocationComplete", "OnInvocationCancel" };
    public IReadOnlyList<EveId> CategoryIds { get; }
    public bool AffectedByAttackSpeed { get; }
    public IReadOnlyList<PNSkillEffect> Effects { get; }
    public IReadOnlyList<string> TriggerIds { get; }
    private PNSkillMetadata(EveId[] categories, bool speed, PNSkillEffect[] effects, string[] triggers)
    { CategoryIds = Array.AsReadOnly(categories); AffectedByAttackSpeed = speed; Effects = Array.AsReadOnly(effects); TriggerIds = Array.AsReadOnly(triggers); }
    public static PNSkillMetadata Parse(object data, EveRegistryHub hub)
    {
        if (!(data is JObject root) || !(root["categoryIds"] is JArray) || root["affectedByAttackSpeed"]?.Type != JTokenType.Boolean)
            throw new FormatException("Activation definition requires categoryIds and affectedByAttackSpeed.");
        var categories = PNSkillEffect.Ids(root["categoryIds"]).Select(x => hub.SkillCategories().ResolveIdentity(x)).Distinct().OrderBy(x => x).ToArray();
        foreach (var id in categories) hub.SkillCategories().Get(id);
        var effects = root["effects"] == null ? Array.Empty<PNSkillEffect>() : PNBuffContentBatch.Array(root["effects"]).Select(x => PNSkillEffect.Parse(x, hub)).ToArray();
        var triggers = StandardTriggers.Concat(PNBuffContentBatch.Array(root["triggerIds"]).Select(x => x.Type == JTokenType.String && !string.IsNullOrWhiteSpace(x.Value<string>()) ? x.Value<string>() : throw new FormatException("Trigger string required."))).Distinct().ToArray();
        if (effects.Any(x => !triggers.Contains(x.Trigger))) throw new FormatException("Authored effect uses undeclared trigger.");
        return new PNSkillMetadata(categories, root["affectedByAttackSpeed"].Value<bool>(), effects, triggers);
    }
}

// Immutable effects; execution implementations and payload validation stay registered.
public sealed class PNSkillEffect
{
    private readonly JToken data;
    public string Trigger { get; }
    public string Kind { get; }
    public EveId Id { get; }
    public JToken Data => data?.DeepClone();
    public string Target { get; }
    public string Position { get; }
    public string Direction { get; }
    public string Missing { get; }
    public string Ownership { get; }
    public bool AffectedByAttackSpeed { get; }
    public static IEnumerable<EveId> Ids(JToken token) => PNBuffContentBatch.Array(token).Select(x => x.Type == JTokenType.String ? EveId.Parse(x.Value<string>()) : throw new FormatException("ID string required."));
    public bool OncePerHit { get; }
    private static string Choice(JObject obj, string field, string fallback, params string[] allowed)
    { string value = obj[field] == null ? fallback : PNBuffContentBatch.String(obj, field); if (!allowed.Contains(value)) throw new FormatException("Invalid effect " + field); return value; }
    private PNSkillEffect(JObject obj, EveRegistryHub hub)
    {
        PNBuffContentBatch.Fields(obj, "trigger", "kind", "id", "data", "target", "position", "direction", "missing", "ownership", "affectedByAttackSpeed", "hitScope");
        Trigger = PNBuffContentBatch.String(obj, "trigger"); if (string.IsNullOrWhiteSpace(Trigger)) throw new FormatException("Effect trigger required.");
        Kind = Choice(obj, "kind", "Operation", "Operation", "Action"); Id = EveId.Parse(PNBuffContentBatch.String(obj, "id"));
        data = obj["data"]?.DeepClone();
        Target = Choice(obj, "target", "None", "None", "Self", "InvocationTargets", "HitTarget");
        Position = Choice(obj, "position", "None", "None", "Self", "Target", "Hit");
        Direction = Choice(obj, "direction", "None", "None", "Invocation", "ToTarget");
        Missing = Choice(obj, "missing", "Skip", "Skip", "Error");
        OncePerHit = Choice(obj, "hitScope", "PerTarget", "PerTarget", "PerHit") == "PerHit";
        if ((Target == "HitTarget" || Position == "Hit") && Trigger != "OnHit") throw new FormatException("Hit selector requires OnHit.");
        void Context(PNSkillContextRequirements required)
        {
            if ((required & PNSkillContextRequirements.Targets) != 0 && Target == "None" ||
                (required & PNSkillContextRequirements.Direction) != 0 && Direction == "None" ||
                (required & PNSkillContextRequirements.Position) != 0 && Position == "None") throw new FormatException("Effect selectors do not supply required context.");
        }
        if (Kind == "Operation") { var operation = hub.Operations().Get(Id); Context(operation.RequiredContext); operation.ValidatePayload(Data, hub); }
        else
        {
            Ownership = Choice(obj, "ownership", "", "Parent", "Independent");
            AffectedByAttackSpeed = PNBuffContentBatch.Bool(obj, "affectedByAttackSpeed");
            var action = hub.Actions().Get(Id); var envelope = PNBuffContentBatch.Object(Data);
            Context(action.RequiredContext);
            PNBuffContentBatch.Fields(envelope, "start", "perform", "exit", "cancel");
            action.ValidatePayload(ActionData(), hub);
        }
    }
    public static PNSkillEffect Parse(JToken token, EveRegistryHub hub) => new PNSkillEffect(PNBuffContentBatch.Object(token), hub);
    public PNSkillActionData ActionData()
    { var obj = PNBuffContentBatch.Object(Data); return new PNSkillActionData(Id, obj["start"], obj["perform"], obj["exit"], obj["cancel"]); }
}

public sealed class PNSkillContribution
{
    private readonly JToken definition;
    public PNSkillReadMode ReadMode { get; }
    public IReadOnlyList<EveId> SkillIds { get; }
    public IReadOnlyList<EveId> FilterCategories { get; }
    public bool MatchAll { get; }
    public IReadOnlyList<EveId> AddCategories { get; }
    public IReadOnlyList<EveId> SuppressCategories { get; }
    public IReadOnlyList<EveId> AddTags { get; }
    public IReadOnlyList<EveId> SuppressTags { get; }
    public IReadOnlyList<PNSkillEffect> Effects { get; }
    public PNSkillContribution(JToken token, EveRegistryHub hub)
    {
        definition = token?.DeepClone();
        var obj = PNBuffContentBatch.Object(token);
        PNBuffContentBatch.Fields(obj, "readMode", "skillIds", "categoryIds", "match", "addCategories", "suppressCategories", "addTags", "suppressTags", "effects");
        string mode = PNBuffContentBatch.String(obj, "readMode");
        ReadMode = mode == "Snapshot" ? PNSkillReadMode.Snapshot : mode == "Live" ? PNSkillReadMode.Live : throw new FormatException("Unknown contribution readMode.");
        IReadOnlyList<EveId> Read(string field, Func<EveId, EveId> resolve) => Array.AsReadOnly(PNSkillEffect.Ids(obj[field]).Select(resolve).Distinct().OrderBy(x => x).ToArray());
        EveId Category(EveId id) { hub.SkillCategories().Get(id); return hub.SkillCategories().ResolveIdentity(id); }
        EveId Tag(EveId id) { hub.Tags().Get(id); return hub.Tags().ResolveIdentity(id); }
        EveId Skill(EveId id) { hub.Skills.Get(id); return hub.Skills.ResolveIdentity(id); }
        SkillIds = Read("skillIds", Skill); FilterCategories = Read("categoryIds", Category);
        string match = obj["match"] == null ? "Any" : PNBuffContentBatch.String(obj, "match");
        if (match != "Any" && match != "All") throw new FormatException("Contribution match must be Any or All."); MatchAll = match == "All";
        AddCategories = Read("addCategories", Category); SuppressCategories = Read("suppressCategories", Category);
        AddTags = Read("addTags", Tag); SuppressTags = Read("suppressTags", Tag);
        // Modifier filters are exact skill IDs only, avoiding circular category-based classification.
        if (FilterCategories.Count > 0 && AddCategories.Count + SuppressCategories.Count > 0)
            throw new FormatException("Category modifiers cannot filter by categories.");
        Effects = Array.AsReadOnly(PNBuffContentBatch.Array(obj["effects"]).Select(x => PNSkillEffect.Parse(x, hub)).ToArray());
        if (Effects.Count + AddCategories.Count + SuppressCategories.Count + AddTags.Count + SuppressTags.Count == 0) throw new FormatException("Empty contribution.");
    }
    internal bool MatchesSkill(EveId id) => SkillIds.Count == 0 || SkillIds.Contains(id);
    internal PNSkillContribution Copy(EveRegistryHub hub) => new PNSkillContribution(definition, hub);
    internal bool Matches(EveId id, IReadOnlyList<EveId> categories) => MatchesSkill(id) && (FilterCategories.Count == 0 || (MatchAll ? FilterCategories.All(categories.Contains) : FilterCategories.Any(categories.Contains)));
    internal void Validate(EveRegistryHub hub)
    {
        foreach (var id in SkillIds) hub.Skills.Get(id);
        foreach (var id in FilterCategories.Concat(AddCategories).Concat(SuppressCategories)) hub.SkillCategories().Get(id);
        foreach (var id in AddTags.Concat(SuppressTags)) hub.Tags().Get(id);
        foreach (var effect in Effects)
            if (effect.Kind == "Operation") hub.Operations().Get(effect.Id).ValidatePayload(effect.Data, hub);
            else hub.Actions().Get(effect.Id).ValidatePayload(effect.ActionData(), hub);
        var standard = PNSkillMetadata.StandardTriggers;
        foreach (var effect in Effects.Where(x => !standard.Contains(x.Trigger)))
        {
            if (SkillIds.Count == 0) throw new ArgumentException("Custom trigger contribution requires exact Skill filter.");
            foreach (var id in SkillIds) if (!PNSkillMetadata.Parse(hub.Skills.GetEffective(id).Data, hub).TriggerIds.Contains(effect.Trigger)) throw new ArgumentException("Contribution trigger is not supported by selected Skill.");
        }
    }
}

public sealed class PNSkillContributionLease : IDisposable
{
    internal PNSkillEffectState Owner;
    internal PNSkillContribution Value;
    public long Id { get; }
    public string OwnerName { get; }
    public bool IsActive => Owner != null && !Owner.IsDisposed;
    internal PNSkillContributionLease(PNSkillEffectState owner, long id, string name, PNSkillContribution value) { Owner = owner; Id = id; OwnerName = name; Value = value; }
    public void Dispose() { var owner = Owner; Owner = null; owner?.Release(this); }
}

public sealed class PNSkillInvocation : IDisposable
{
    internal PNSkillEffectState State;
    internal readonly PNSkillContribution[] ActivationSnapshot;
    internal readonly List<PNSkillActionContext> Contexts = new List<PNSkillActionContext>();
    internal bool Committed;
    internal PNSkillActionContext ActivationContext;
    public bool IsEnded { get; private set; }
    public long Id { get; }
    public EveId SkillId { get; }
    public Guid GenerationId => State.GenerationId;
    public IReadOnlyList<EveId> AdmissionCategories { get; }
    public PNSkillMetadata Metadata { get; }
    internal PNSkillInvocation(PNSkillEffectState state, long id, EveId skillId, PNSkillMetadata metadata)
    {
        State = state; Id = id; SkillId = skillId; Metadata = metadata; ActivationSnapshot = state.Snapshot();
        AdmissionCategories = state.Categories(this, state.Combined(ActivationSnapshot));
    }
    public void Dispose()
    {
        if (IsEnded) return; IsEnded = true;
        foreach (var context in Contexts.ToArray()) try { context.CancelEffects(); } catch (Exception error) { State.Report(error); }
        State.End(this);
    }
}

public sealed class PNSkillEffectState : IDisposable
{
    private readonly PNSkill skill;
    private readonly EveRegistryHub hub;
    private readonly List<PNSkillContributionLease> leases = new List<PNSkillContributionLease>();
    private readonly List<PNSkillInvocation> invocations = new List<PNSkillInvocation>();
    private readonly List<Auxiliary> auxiliaries = new List<Auxiliary>();
    private readonly PNStat source;
    private long nextLease, nextInvocation;
    private int triggerDepth;
    public bool IsDisposed { get; private set; }
    public int ActiveContributionCount=>leases.Count;
    public int ActiveExecutionCount=>invocations.Count+auxiliaries.Count;
    public Guid GenerationId { get; } = Guid.NewGuid();
    public event Action<Exception> Failed;
    public event Action<PNSkillInvocation, string> Triggered;
    internal PNSkillEffectState(PNSkill skill, EveRegistryHub hub) { this.skill = skill; this.hub = hub; source = skill.GetComponent<PNStat>(); if (source != null) source.Died += SourceDied; }
    private void SourceDied(PNDamageResult result) => CancelExecutions();
    private void RequireAuthority()
    { if (IsDisposed || !skill.IsReady || !EveRegistrySession.IsReady || !EveRegistrySession.Context.HasAuthority || !ReferenceEquals(hub, EveRegistrySession.Current)) throw new InvalidOperationException("Skill effects require loaded authoritative session."); }
    public PNSkillContributionLease Contribute(string owner, PNSkillContribution contribution)
    {
        RequireAuthority(); if (string.IsNullOrWhiteSpace(owner) || contribution == null) throw new ArgumentException("Contribution owner/value required.");
        contribution.Validate(hub);
        var lease = new PNSkillContributionLease(this, checked(++nextLease), owner, contribution.Copy(hub)); leases.Add(lease); return lease;
    }
    internal void Release(PNSkillContributionLease lease) => leases.Remove(lease);
    internal PNSkillContribution[] Snapshot() => leases.Where(x => x.IsActive && x.Value.ReadMode == PNSkillReadMode.Snapshot).Select(x => x.Value).ToArray();
    internal PNSkillContribution[] Combined(PNSkillContribution[] snapshot) => snapshot.Concat(leases.Where(x => x.IsActive && x.Value.ReadMode == PNSkillReadMode.Live).Select(x => x.Value)).ToArray();
    private static IReadOnlyList<EveId> Resolve(IEnumerable<EveId> original, IEnumerable<EveId> add, IEnumerable<EveId> suppress)
    { var result = new HashSet<EveId>(original); result.UnionWith(add); result.ExceptWith(suppress); return Array.AsReadOnly(result.OrderBy(x => x).ToArray()); }
    internal IReadOnlyList<EveId> Categories(PNSkillInvocation invocation, PNSkillContribution[] values)
    {
        var selected = values.Where(x => x.MatchesSkill(invocation.SkillId)).ToArray();
        return Resolve(invocation.Metadata.CategoryIds, selected.SelectMany(x => x.AddCategories), selected.SelectMany(x => x.SuppressCategories));
    }
    internal IReadOnlyList<EveId> Tags(PNSkillActionContext context, PNSkillContribution[] values, IReadOnlyList<EveId> categories = null)
    {
        categories = categories ?? Categories(context.Invocation, values);
        var selected = values.Where(x => x.Matches(context.Invocation.SkillId, categories)).ToArray();
        return PNSkillCategoryDefinition.ResolveTags(hub, categories, context.Data?.Action.PerformTags ?? Array.Empty<EveId>(),
            selected.SelectMany(x => x.AddTags), selected.SelectMany(x => x.SuppressTags));
    }
    internal PNSkillInvocation Prepare(EveId id)
    {
        RequireAuthority(); var canonical = hub.Skills.ResolveIdentity(id);
        var invocation = new PNSkillInvocation(this, checked(++nextInvocation), canonical, PNSkillMetadata.Parse(hub.Skills.GetEffective(id).Data, hub));
        invocations.Add(invocation); return invocation;
    }
    internal void End(PNSkillInvocation invocation)
    { invocations.Remove(invocation); foreach (var auxiliary in auxiliaries.Where(x => x.Parent == invocation && !x.Independent).ToArray()) try { Retire(auxiliary, true); } catch (Exception error) { Report(error); } }
    internal void Report(Exception error)
    { foreach (Action<Exception> handler in Failed?.GetInvocationList() ?? Array.Empty<Delegate>()) try { handler(error); } catch { } Debug.LogException(error, skill); }
    // The single place that decides which categories, tags and effects a trigger runs; Fire and FreezeOnHit both use it so the
    // live path and the frozen path cannot drift apart. `frozen` replaces the contribution list (OnHit passes the values it fixed).
    private (IReadOnlyList<EveId> Categories, IReadOnlyList<EveId> Tags, PNSkillEffect[] Effects) Gather(PNSkillActionContext context, string trigger, PNSkillContribution[] frozen)
    {
        var invocation = context.Invocation;
        var values = frozen ?? Combined(trigger == "OnActivated" ? invocation.ActivationSnapshot : context.ContributionSnapshot);
        var categories = trigger == "OnActivated" ? invocation.AdmissionCategories : Categories(invocation, values);
        var tags = Tags(context, values, categories);
        var authored = trigger == "OnActivated" ? invocation.Metadata.Effects : invocation.Metadata.Effects.Concat(context.AuthoredEffects);
        var effects = authored.Concat(values.Where(x => x.Matches(invocation.SkillId, categories)).SelectMany(x => x.Effects)).Where(x => x.Trigger == trigger).ToArray();
        return (categories, tags, effects);
    }
    // EN: Fix, at launch time, everything an OnHit would run (authored + matching contributions, Live ones frozen now), so the
    //     result can run later without this skill, invocation or source entity. Call after BeginAction of a context that allows
    //     contributions. Only Operation effects whose definition is SourceIndependent are kept; the rest are listed in Skipped.
    // VI: Đóng băng lúc bắn mọi thứ OnHit sẽ chạy; kết quả chạy sau được mà không cần skill/invocation/nguồn.
    public PNSkillHitSnapshot FreezeOnHit(PNSkillActionContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        var invocation = context.Invocation;
        if (invocation == null || invocation.IsEnded || !ReferenceEquals(invocation.State, this) || !context.AllowContributions || !context.HasBegun)
            throw new InvalidOperationException("FreezeOnHit requires a begun context of a live invocation of this skill that allows contributions.");
        RequireAuthority();
        var plan = Gather(context, "OnHit", null);
        var kept = new List<PNSkillEffect>(); var skipped = new List<PNSkillEffect>();
        foreach (var effect in plan.Effects)
            if (effect.Kind == "Operation" && hub.Operations().Get(effect.Id).SourceIndependent) kept.Add(effect); else skipped.Add(effect);
        return new PNSkillHitSnapshot(hub, invocation.SkillId, plan.Categories, plan.Tags, kept, skipped);
    }
    internal void Fire(PNSkillActionContext context, string trigger, PNStat hit, Vector3? point, PNSkillContribution[] frozen = null, int hitIndex = 0)
    {
        if (IsDisposed || context.Invocation == null || context.Invocation.IsEnded || !context.AllowContributions) return;
        var loader = skill.GetComponent<EveEntityLoader>();
        if (!skill.IsReady || !EveRegistrySession.IsReady || !EveRegistrySession.Context.HasAuthority || !ReferenceEquals(hub, EveRegistrySession.Current) || loader == null || !loader.IsReady) return;
        RequireAuthority(); if (triggerDepth >= 8) { Report(new InvalidOperationException("Skill trigger depth exceeded.")); return; }
        var invocation = context.Invocation;
        if (!invocation.Metadata.TriggerIds.Contains(trigger)) { Report(new ArgumentException("Undeclared Skill trigger: " + trigger)); return; }
        var plan = Gather(context, trigger, frozen); var categories = plan.Categories; var tags = plan.Tags; var effects = plan.Effects;
        triggerDepth++;
        try
        {
            foreach (var effect in effects)
            { if (invocation.IsEnded || IsDisposed) break; if (trigger == "OnHit" && !context.AdmitHitEffect(effect, hitIndex)) continue; try { Execute(effect, context, hit, point, categories, tags); } catch (Exception error) { Report(error); } }
            foreach (Action<PNSkillInvocation, string> handler in Triggered?.GetInvocationList() ?? Array.Empty<Delegate>()) try { handler(invocation, trigger); } catch (Exception error) { Report(error); }
        }
        finally { triggerDepth--; }
    }
    private void Execute(PNSkillEffect effect, PNSkillActionContext parent, PNStat hit, Vector3? point, IReadOnlyList<EveId> categories, IReadOnlyList<EveId> tags)
    {
        var source = skill.GetComponent<PNStat>();
        if (source == null || !source.IsReady || source.IsDead) return;
        var targets = effect.Target == "Self" ? new[] { source } : effect.Target == "HitTarget" ? (hit == null ? Array.Empty<PNStat>() : new[] { hit }) : effect.Target == "InvocationTargets" ? parent.Targets.ToArray() : Array.Empty<PNStat>();
        if (effect.Target != "None" && targets.Length == 0) { if (effect.Missing == "Error") throw new InvalidOperationException("Missing effect target."); return; }
        // One Operation may receive all targets; position/direction resolve per selected target.
        var groups = (effect.Position == "Target" || effect.Direction == "ToTarget") && targets.Length > 1 ? targets.Select(x => new[] { x }).ToArray() : new[] { targets };
        foreach (var group in groups)
        {
            Vector3? position = effect.Position == "Self" ? source.transform.position : effect.Position == "Target" ? (group.Length == 0 ? (Vector3?)null : group[0].transform.position) : effect.Position == "Hit" ? point : null;
            Vector3? direction = effect.Direction == "Invocation" ? parent.Direction : effect.Direction == "ToTarget" && group.Length > 0 ? group[0].transform.position - source.transform.position : (Vector3?)null;
            if (effect.Position != "None" && !position.HasValue || effect.Direction != "None" && (!direction.HasValue || direction.Value.x == 0 && direction.Value.y == 0 && direction.Value.z == 0))
            { if (effect.Missing == "Error") throw new InvalidOperationException("Missing effect position/direction."); continue; }
            var data = effect.Kind == "Action" ? new PNSkillData(null, effect.ActionData()) : parent.Data;
            var execution = new PNSkillActionContext(skill, parent.SkillId, data, hub, group, direction, parent.Invocation, false, position, categories);
            if (effect.Kind == "Operation") { using (var operation = hub.Operations().Get(effect.Id).Create()) operation.Execute(execution, effect.Data); }
            else
            {
                var action = hub.Actions().Get(effect.Id).Create();
                var auxiliary = new Auxiliary { Action = action, Context = execution, Parent = parent.Invocation, Independent = effect.Ownership == "Independent", Speed = effect.AffectedByAttackSpeed };
                try
                {
                    if(action is PNMotionAction motion)
                        motion.BindMovementOwner(()=>{if(auxiliaries.Contains(auxiliary))Retire(auxiliary,true);});
                    if (auxiliary.Speed && source.Evaluate(PNContentIds.AttackSpeed) <= 0) { action.Dispose(); continue; }
                    action.Start(execution); action.Perform(execution);
                    if (!action.HasAuxiliaryTimeline) { action.Exit(execution); action.Dispose(); }
                    else auxiliaries.Add(auxiliary);
                }
                catch { try { action.Cancel(execution); } finally { action.Dispose(); } throw; }
            }
        }
    }
    private sealed class Auxiliary
    { public PNSkillAction Action; public PNSkillActionContext Context; public PNSkillInvocation Parent; public bool Independent, Speed; }
    private void Retire(Auxiliary value, bool cancel)
    { auxiliaries.Remove(value); try { if (cancel) value.Action.Cancel(value.Context); else value.Action.Exit(value.Context); } finally { value.Action.Dispose(); } }
    internal void Advance(float delta)
    {
        if (IsDisposed) return;
        var stat = skill.GetComponent<PNStat>();
        if (!skill.IsReady || stat == null || !stat.IsReady || stat.IsDead) { foreach (var value in auxiliaries.ToArray()) try { Retire(value, true); } catch (Exception error) { Report(error); } return; }
        foreach (var value in auxiliaries.ToArray())
            try { float speed = value.Speed ? (float)Math.Max(0, stat.Evaluate(PNContentIds.AttackSpeed) / 100) : 1; if (value.Action.AdvanceAuxiliary(delta * speed)) Retire(value, false); }
            catch (Exception error) { try { Retire(value, true); } finally { Report(error); } }
    }
    internal void CancelExecutions()
    {
        foreach (var invocation in invocations.ToArray()) try { invocation.Dispose(); } catch (Exception error) { Report(error); }
        foreach (var auxiliary in auxiliaries.ToArray()) try { Retire(auxiliary, true); } catch (Exception error) { Report(error); }
    }
    public void Dispose()
    {
        if (IsDisposed) return; IsDisposed = true;
        if (source != null) source.Died -= SourceDied;
        foreach (var invocation in invocations.ToArray()) try { invocation.Dispose(); } catch (Exception error) { Report(error); }
        foreach (var auxiliary in auxiliaries.ToArray()) try { Retire(auxiliary, true); } catch (Exception error) { Report(error); }
        foreach (var lease in leases) lease.Owner = null; leases.Clear(); Triggered = null; Failed = null;
    }
}

// EN: OnHit effects fixed at launch time (see PNSkillEffectState.FreezeOnHit). Immutable; it keeps no skill, invocation or
//     source entity, so it can run after they are gone. Each projectile executes it with its own PNSkillHitAdmission.
// VI: Effect OnHit đã đóng băng lúc bắn (xem FreezeOnHit). Bất biến; không giữ skill/invocation/nguồn nên chạy được sau khi chúng mất.
public sealed class PNSkillHitSnapshot
{
    [ThreadStatic] private static int depth;
    private readonly EveRegistryHub hub;
    public EveId EntrySkillId { get; }
    public IReadOnlyList<EveId> Categories { get; }
    public IReadOnlyList<EveId> Tags { get; }
    // OnHit Operation effects whose definition is SourceIndependent, in the order Fire would run them.
    public IReadOnlyList<PNSkillEffect> Effects { get; }
    // Effects of the OnHit set that cannot run detached (Action kind, or an Operation that needs the live skill).
    public IReadOnlyList<PNSkillEffect> Skipped { get; }
    internal PNSkillHitSnapshot(EveRegistryHub hub, EveId entrySkillId, IReadOnlyList<EveId> categories, IReadOnlyList<EveId> tags,
        IEnumerable<PNSkillEffect> effects, IEnumerable<PNSkillEffect> skipped)
    {
        this.hub = hub; EntrySkillId = entrySkillId; Categories = categories; Tags = tags;
        Effects = Array.AsReadOnly(effects.ToArray()); Skipped = Array.AsReadOnly(skipped.ToArray());
    }

    // EN: Run the frozen effects for one hit. `hitContext` supplies Source/World/Registry/Data (e.g. the projectile's detached
    //     context); `direction` is the projectile heading; `point` is the hit point (also the origin of the ToTarget direction).
    //     Supported selectors: target HitTarget|None, position Hit|Target|None, direction Invocation|ToTarget|None. Self and
    //     InvocationTargets have no meaning here and follow the effect's `missing` policy. A failing effect is reported and does
    //     not stop the others or undo damage already committed. Needs the same ready authoritative session the plan was frozen in.
    public PNSkillHitReport Execute(PNSkillActionContext hitContext, PNStat target, Vector3? point, Vector3? direction, int hitIndex, PNSkillHitAdmission admission)
    {
        if (hitContext == null) throw new ArgumentNullException(nameof(hitContext));
        if (admission == null) throw new ArgumentNullException(nameof(admission));
        var report = new PNSkillHitReport();
        if (!EveRegistrySession.IsReady || !EveRegistrySession.Context.HasAuthority || !ReferenceEquals(hub, EveRegistrySession.Current) || !ReferenceEquals(hitContext.Registry, hub))
        { report.Failures.Add(new InvalidOperationException("Frozen hit effects need the authoritative session they were frozen in.")); return report; }
        if (depth >= 8) { report.Failures.Add(new InvalidOperationException("Skill trigger depth exceeded.")); return report; }
        depth++;
        try
        {
            foreach (var effect in Effects)
            {
                if (effect.OncePerHit && !admission.Admit(effect, hitIndex)) { report.Skipped++; continue; }
                try { if (Run(effect, hitContext, target, point, direction)) report.Executed++; else report.Skipped++; }
                catch (Exception error) { report.Failures.Add(error); }
            }
        }
        finally { depth--; }
        return report;
    }
    private bool Run(PNSkillEffect effect, PNSkillActionContext hitContext, PNStat target, Vector3? point, Vector3? direction)
    {
        bool Missing(string reason) { if (effect.Missing == "Error") throw new InvalidOperationException(reason); return false; }
        PNStat[] targets;
        if (effect.Target == "HitTarget") { if (target == null) return Missing("Missing effect target."); targets = new[] { target }; }
        else if (effect.Target == "None") targets = Array.Empty<PNStat>();
        else return Missing("Effect target " + effect.Target + " is not available to a frozen hit.");
        Vector3? position;
        if (effect.Position == "Hit") position = point;
        else if (effect.Position == "Target") position = targets.Length == 0 ? (Vector3?)null : targets[0].transform.position;
        else if (effect.Position == "None") position = null;
        else return Missing("Effect position " + effect.Position + " is not available to a frozen hit.");
        Vector3? heading;
        if (effect.Direction == "Invocation") heading = direction;
        else if (effect.Direction == "ToTarget") heading = targets.Length == 0 || !point.HasValue ? (Vector3?)null : targets[0].transform.position - point.Value;
        else if (effect.Direction == "None") heading = null;
        else return Missing("Effect direction " + effect.Direction + " is not available to a frozen hit.");
        if (effect.Position != "None" && !position.HasValue || effect.Direction != "None" && (!heading.HasValue || heading.Value.x == 0 && heading.Value.y == 0 && heading.Value.z == 0))
            return Missing("Missing effect position/direction.");
        var execution = new PNHitExecutionContext(hitContext, EntrySkillId, Categories, targets, heading, position);
        using (var operation = hub.Operations().Get(effect.Id).Create()) operation.Execute(execution, effect.Data);
        return true;
    }
}

// Per-projectile state for `hitScope: PerHit` effects: each effect runs at most once per hit index.
public sealed class PNSkillHitAdmission
{
    private readonly HashSet<(PNSkillEffect, int)> admitted = new HashSet<(PNSkillEffect, int)>();
    internal bool Admit(PNSkillEffect effect, int hitIndex) => admitted.Add((effect, hitIndex));
}

public sealed class PNSkillHitReport
{
    public int Executed { get; internal set; }
    public int Skipped { get; internal set; }
    internal readonly List<Exception> Failures = new List<Exception>();
    public IReadOnlyList<Exception> Errors => Failures.AsReadOnly();
}

// Operation context for one frozen effect: no skill, attribution and world come from the projectile's own context.
internal sealed class PNHitExecutionContext : PNSkillActionContext
{
    private readonly PNSkillActionContext parent;
    internal PNHitExecutionContext(PNSkillActionContext parent, EveId skillId, IEnumerable<EveId> categories, IEnumerable<PNStat> targets, Vector3? direction, Vector3? position)
        : base(skillId, parent.Data, parent.Registry, targets, direction, position, categories) { this.parent = parent; }
    public override PNBuffSource Source => parent.Source;
    public override EveWorldContext World => parent.World;
}
