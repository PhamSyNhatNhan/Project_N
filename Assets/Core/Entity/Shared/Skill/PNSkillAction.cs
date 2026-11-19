using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using Newtonsoft.Json.Linq;

// EN: Inputs for one invocation; concrete skills can extend targeting and evaluated values.
// VI: Đầu vào một lần thi triển; skill cụ thể có thể mở rộng mục tiêu và giá trị đã tính.
public class PNSkillActionContext
{
    public virtual PNSkill Skill { get; }
    public virtual EveId SkillId { get; }
    public virtual PNSkillData Data { get; }
    public virtual EveRegistryHub Registry { get; }
    public virtual IReadOnlyList<PNStat> Targets { get; }
    public virtual Vector3? Direction { get; }
    public virtual Vector3? Position { get; }
    // EN: Attribution for source-independent Operations. A skill context captures its live Skill's stat (new capture per read;
    //     throws when that stat is not ready or is dead, exactly like the former direct capture). A context without a skill
    //     returns null unless a subclass supplies a frozen source.
    // VI: Nguồn gán cho Operation không phụ thuộc skill sống. Ngữ cảnh có skill chụp stat của Skill mỗi lần đọc; không có skill thì null
    //     trừ khi lớp con cấp nguồn đã đóng băng.
    public virtual PNCombatSource Source => Skill != null ? PNCombatSource.Capture(Skill.GetComponent<PNStat>()) : null;
    // EN: World used to validate live targets; the skill entity's world by default (null for an entity outside any world).
    // VI: World dùng để kiểm mục tiêu sống; mặc định là world của entity skill.
    public virtual EveWorldContext World => Skill != null ? Skill.GetComponent<EveEntityIdentity>()?.World : null;
    public PNSkillInvocation Invocation { get; }
    public EveId EntrySkillId => Invocation?.SkillId ?? SkillId;
    public long InvocationId => Invocation?.Id ?? 0;
    public Guid InvocationGeneration => Invocation?.GenerationId ?? Guid.Empty;
    internal bool AllowContributions { get; }
    internal bool HasBegun => begun;
    public long ActionOrdinal { get; private set; }
    internal PNActionRunStore RunStore;
    internal PNActionRunKey RunKey;
    internal double RunProgress, RunStamp;
    internal double? PendingRunStart;
    internal double PresentationScale = 1;
    public void CommitPresentationScale(double scale)
    {
        PNTimeMath.Nonnegative(scale);
        if (completed || !begun) throw new InvalidOperationException("Presentation requires a running Action.");
        PresentationScale = scale;
        Skill?.BeginActionRun(this, commitPresentation: true);
    }
    // True when FreezeOnHit may be called now: a begun context of a live invocation that allows contributions.
    // The owner's CLIENT is running this action ahead of the authority (S3-S5): no lock, no damage; a hit is handed to `HitReporter` instead of being applied.
    // G1: the mode is the invocation's; a context with no invocation (the Eve moveset's) can be given one directly.
    private PNSkillMode? modeOverride;
    public PNSkillMode Mode { get => modeOverride ?? Invocation?.Mode ?? PNSkillMode.Authority; set => modeOverride = value; }
    private int step = -1;
    public bool CanFreezeOnHit => begun && AllowContributions && Invocation != null && !Invocation.IsEnded;
    internal PNSkillContribution[] ContributionSnapshot = Array.Empty<PNSkillContribution>();
    internal IReadOnlyList<PNSkillEffect> AuthoredEffects = Array.Empty<PNSkillEffect>();
    private bool begun, completed;
    private readonly IReadOnlyList<EveId> frozenCategories;
    private readonly Dictionary<PNSkillEffect, HashSet<int>> hitExecutions = new Dictionary<PNSkillEffect, HashSet<int>>();
    public IReadOnlyList<EveId> CategoryIds => frozenCategories ?? (Invocation == null ? Array.Empty<EveId>() : Invocation.State.Categories(Invocation, Invocation.State.Combined(ContributionSnapshot)));

    public PNSkillActionContext(PNSkill skill, EveId skillId, PNSkillData data, EveRegistryHub registry,
        IEnumerable<PNStat> targets = null, Vector3? direction = null)
        : this(skill, skillId, data, registry, targets, direction, null, true, null) { }
    public PNSkillActionContext(PNSkill skill, EveId skillId, PNSkillData data, EveRegistryHub registry,
        IEnumerable<PNStat> targets, Vector3? direction, PNSkillInvocation invocation, bool allowContributions = true, Vector3? position = null, IEnumerable<EveId> categories = null)
        : this(skill, skillId, data, registry, targets, direction, invocation, allowContributions, position, categories, false) { }
    // EN: Context with no live skill (Skill == null, no invocation, no contributions), e.g. a projectile carrying a frozen
    //     source. Such a context only serves Operations registered SourceIndependent and must override Source/World.
    // VI: Ngữ cảnh không có skill sống (Skill == null, không invocation); dùng cho đạn mang nguồn đã đóng băng.
    //     Chỉ phục vụ Operation SourceIndependent và nên ghi đè Source/World.
    protected PNSkillActionContext(EveId skillId, PNSkillData data, EveRegistryHub registry,
        IEnumerable<PNStat> targets, Vector3? direction, Vector3? position, IEnumerable<EveId> categories)
        : this(null, skillId, data, registry, targets, direction, null, false, position, categories, true) { }
    private PNSkillActionContext(PNSkill skill, EveId skillId, PNSkillData data, EveRegistryHub registry,
        IEnumerable<PNStat> targets, Vector3? direction, PNSkillInvocation invocation, bool allowContributions, Vector3? position, IEnumerable<EveId> categories, bool detached)
    {
        if (!detached) Skill = skill ?? throw new ArgumentNullException(nameof(skill));
        if (!skillId.IsValid) throw new ArgumentException("Skill ID is required.", nameof(skillId));
        SkillId = skillId;
        Data = data;
        if (!detached && data == null && invocation == null && skill.CurrentInvocation == null) throw new ArgumentNullException(nameof(data));
        if (registry == null || !registry.IsSealed) throw new ArgumentException("Sealed registry required.", nameof(registry));
        Registry = registry;
        Invocation = detached ? null : invocation ?? skill.CurrentInvocation; AllowContributions = allowContributions; Position = position;
        frozenCategories = categories == null ? null : Array.AsReadOnly(categories.ToArray());
        if (position.HasValue && (float.IsNaN(position.Value.x) || float.IsInfinity(position.Value.x) || float.IsNaN(position.Value.y) || float.IsInfinity(position.Value.y) || float.IsNaN(position.Value.z) || float.IsInfinity(position.Value.z))) throw new ArgumentException("Effect position must be finite.");
        if (Invocation != null && allowContributions) Invocation.Contexts.Add(this);
        Data?.Action.ValidateTags(registry);
        var selected = new List<PNStat>(targets ?? Array.Empty<PNStat>());
        if (selected.Exists(target => target == null)) throw new ArgumentException("Targets cannot contain null.", nameof(targets));
        Targets = selected.AsReadOnly();
        if (direction.HasValue)
        {
            var value = direction.Value;
            if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                float.IsNaN(value.z) || float.IsInfinity(value.z))
                throw new ArgumentException("Skill direction must be finite.", nameof(direction));
            double length = Math.Sqrt((double)value.x * value.x + (double)value.y * value.y + (double)value.z * value.z);
            if (length == 0d) throw new ArgumentException("Skill direction cannot be zero.", nameof(direction));
            Direction = new Vector3((float)(value.x / length), (float)(value.y / length), (float)(value.z / length));
        }
    }
    // G8b: where the authority begins an Action whose definition asks for observers (`ReplayOnObservers`), the other players' machines are told (the `skill_cast` block) and replay it; the same on its end.
    private bool announced;
    private void Announce()
    {
        if (Mode != PNSkillMode.Authority || Invocation == null || Skill == null || Data == null || announced) return;
        try { if (!Registry.Actions().Get(Data.Action.Id).ObserverReplay) return; } catch (Exception) { return; }
        announced = true; Skill.AnnounceObservedAction(SkillId, Direction);
    }
    private void AnnounceEnd() { if (announced) { announced = false; Skill.AnnounceObservedActionEnd(SkillId); } }
    public void BeginAction()
    {
        if (begun || completed) return; begun = true;
        ActionOrdinal = Invocation?.NextActionOrdinal() ?? 0;
        if (Invocation != null && !Invocation.Contexts.Contains(this)) Invocation.Contexts.Add(this);
        Skill?.BeginActionRun(this);
        if (Invocation != null) ContributionSnapshot = Invocation.State.Snapshot();
        if (Invocation != null && SkillId != Invocation.SkillId && Registry.Skills.GetEffective(SkillId).Data is JObject root && root["effects"] != null)
            AuthoredEffects = PNSkillMetadata.Parse(root, Registry).Effects;
        Fire("OnActionStart"); Announce();
        if (Invocation?.IsEnded == true) throw new OperationCanceledException("Invocation cancelled during Action start.");
    }
    public void CompleteAction() { if (completed) return; completed = true; try { Fire("OnComplete"); } finally { Skill?.EndActionRun(this, false); AnnounceEnd(); } }
    public void CancelEffects() { if (completed) return; completed = true; try { Fire("OnCancel"); } finally { Skill?.EndActionRun(this, true); AnnounceEnd(); } }
    public void Fire(string trigger, PNStat hit = null, Vector3? point = null)
    { if (Invocation != null) Invocation.State.Fire(this, trigger, hit, point); }
    public IReadOnlyList<EveId> EffectiveTags()
    {
        if (Invocation != null && AllowContributions) return Invocation.State.Tags(this, Invocation.State.Combined(ContributionSnapshot));
        return PNSkillCategoryDefinition.ResolveTags(Registry, CategoryIds, Data?.Action.PerformTags ?? Array.Empty<EveId>(),
            Array.Empty<EveId>(), Array.Empty<EveId>());
    }
    public PNDamage ReclassifyDamage(PNDamage original, double amount, PNCombatSourceSnapshot capture)
        => ReclassifyDamage(original, amount, capture, EffectiveTags());
    private PNDamage ReclassifyDamage(PNDamage original, double amount, PNCombatSourceSnapshot capture, IReadOnlyList<EveId> effective)
    {
        var tags = effective.Select(Registry.Tags().ResolveIdentity).Distinct().OrderBy(x => x).ToArray();
        if (tags.SequenceEqual(original.Tags)) return original;
        var packet = capture.CreateDamage(amount, false, original.IFrameDuration, tags.ToArray());
        return new PNDamage(packet.SessionId, packet.SourceId, packet.Amount * (original.IsCritical ? original.CritDamage / 100 : 1), packet.Tags, packet.IFrameDuration, original.CanCrit, original.IsCritical, original.CritRate, original.CritDamage).WithPenetration(packet.Penetration);
    }
    public PNDamageResult ReceiveHit(PNStat target, Vector3? point, PNDamage original, double amount, PNCombatSourceSnapshot capture, int hitIndex = 0)
        => ReceiveHit(target, point, original, amount, capture, hitIndex, null);
    // `hitKey` names the hit across machines (PNActionImpact.Key); the combat event carries it so a guess's early phase and another player's copy can match it.
    private PNDamageResult ReceiveHit(PNStat target, Vector3? point, PNDamage original, double amount, PNCombatSourceSnapshot capture, int hitIndex, string hitKey)
    {
        var values = Invocation != null && AllowContributions ? Invocation.State.Combined(ContributionSnapshot) : Array.Empty<PNSkillContribution>();
        var tags = Invocation != null && AllowContributions ? Invocation.State.Tags(this, values) : EffectiveTags();
        var packet = ReclassifyDamage(original, amount, capture, tags);
        if (hitKey != null) { var at = point ?? target.transform.position; packet = packet.WithHit(hitKey, at.x, at.y, at.z); }
        var result = target.ReceiveDamage(packet);
        if (Invocation != null) Invocation.State.Fire(this, "OnHit", target, point, values, hitIndex);
        return result;
    }
    public PNSkillDamageCapture PrepareDamage(PNSkillValues values, bool canCrit, double? iframeDuration)
    {
        if (Mode != PNSkillMode.Authority) return new PNSkillDamageCapture(1);

        var caster = Skill.GetComponent<PNStat>();
        var amount = values.Components.Sum(component => component.Evaluate(caster));
        var packet = caster.CreateDamage(amount, canCrit, iframeDuration, Data.Action.PerformTags.ToArray());
        return new PNSkillDamageCapture(1, amount, caster.CaptureCombatSource(), packet);
    }

    public PNSkillDamageCapture PrepareMeleeDamage(PNSkillValues values, double? iframeDuration, Func<double> readOwnerScale, bool canCrit = true, IEnumerable<EveId> extraTags = null)
    {
        PNSkillDamageCapture damage;
        if (Mode == PNSkillMode.Authority)
        {
            var caster = Skill.GetComponent<PNStat>();
            var amount = values.Components.Sum(component => component.Evaluate(caster));
            // The Action's registered tags, plus any tags the hit's own payload names (a registered Operation's `tags`).
            var capture = PNHitCapture.Melee(caster.CaptureCombatSource(), extraTags == null ? Data.Action.PerformTags : Data.Action.PerformTags.Concat(extraTags));
            damage = new PNSkillDamageCapture(capture.ScaleFactor, amount, capture.Source,
                prepared: capture.Prepare(amount, canCrit, iframeDuration));
        }
        else
        {
            var scale = Mode == PNSkillMode.Predicted ? readOwnerScale() : PresentationScale;
            damage = new PNSkillDamageCapture(scale);
        }

        CommitPresentationScale(damage.Scale);
        return damage;
    }

    public PNHitOutcome Hit(PNStat target, Vector3? point, PNDamage packet, PNSkillDamageCapture damage, int hitIndex = 0)
        => Hit(target, point, packet, damage.Amount, damage.Source, hitIndex);

    // The one door for a hit made by a cast (G2). Authority: damage is dealt (`ReceiveHit`) and the skill hears `OnHitConfirmed`. Predicted: the hit goes to `HitReporter` (if any) and nothing is dealt.
    // Observed: nothing happens. Actions call this and never branch on the mode themselves.
    public PNHitOutcome Hit(PNStat target, Vector3? point, PNDamage original, double amount, PNCombatSourceSnapshot capture, int hitIndex = 0)
    {
        switch (Mode)
        {
            case PNSkillMode.Authority:
                long castKey = Invocation?.CastKey ?? 0; string hitKey = castKey > 0 && target != null ? PNActionImpact.Key(castKey, ActionOrdinal, hitIndex, target.EntityId.Value) : null;
                var result = ReceiveHit(target, point, original, amount, capture, hitIndex, hitKey);
                Skill?.OnHitConfirmed(this, target, result);
                if (target != null && Skill != null && Skill.HasActionImpactListeners) Skill.RaiseActionImpact(PNActionImpactOrigin.Real, castKey, ActionOrdinal, hitIndex, target.EntityId.Value, target, point ?? target.transform.position);
                return PNHitOutcome.Hit(result);
            case PNSkillMode.Predicted:
                // G6c: the framework reports the hit under the cast's key; a context with no keyed cast (or no wire) reports nothing.
                var sink = Skill?.ReportSink; long key = Invocation?.CastKey ?? 0;
                if (sink == null || key <= 0 || target == null) return PNHitOutcome.Skip(false);
                if (step < 0) step = Invocation.NextStep();
                // The early phase and the guess's own impact come before the report (the report may be answered at once on a loopback).
                var at = point ?? target.transform.position;
                PNCombatEvents.RaiseEarly(target.EntityId.Value, PNActionImpact.Key(key, ActionOrdinal, hitIndex, target.EntityId.Value), true, at.x, at.y, at.z);
                if (Skill.HasActionImpactListeners) Skill.RaiseActionImpact(PNActionImpactOrigin.Guess, key, ActionOrdinal, hitIndex, target.EntityId.Value, target, at);
                sink.ReportHit(key, target, step);
                return PNHitOutcome.Skip(true);
            default:
                return PNHitOutcome.Skip(false);
        }
    }
    // The door for a Buff an Action gives (to others or itself). Authority applies it; Predicted and Observed skip it (the Buff reaches them through its own replication). The Action never checks the mode.
    public PNBuffOutcome ApplyBuff(PNBuffManager manager, PNBuffApplyRequest request)
    {
        if (manager == null) throw new ArgumentNullException(nameof(manager));
        return Mode == PNSkillMode.Authority ? PNBuffOutcome.Done(manager.Buffs.Apply(request)) : PNBuffOutcome.Skip();
    }

    // The door for a heal an Action gives. Authority heals; every other mode skips it.
    public PNHealOutcome Heal(PNStat target, PNHeal heal)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        return Mode == PNSkillMode.Authority ? PNHealOutcome.Done(target.ReceiveHeal(heal)) : PNHealOutcome.Skip();
    }

    internal bool AdmitHitEffect(PNSkillEffect effect, int index)
    {
        if (!effect.OncePerHit) return true;
        if (!hitExecutions.TryGetValue(effect, out var hits)) hitExecutions.Add(effect, hits = new HashSet<int>());
        return hits.Add(index);
    }
}

// EN: One invocation owns its action state; the skill explicitly drives all four hooks.
// VI: Một lần thi triển giữ trạng thái action riêng; skill gọi tường minh cả bốn hook.
public abstract class PNSkillAction : IDisposable
{
    public virtual bool HasAuxiliaryTimeline => false;
    public virtual bool AdvanceAuxiliary(float delta) => true;
    public abstract void Start(PNSkillActionContext context);
    public abstract void Perform(PNSkillActionContext context);
    public abstract void Exit(PNSkillActionContext context);
    public abstract void Cancel(PNSkillActionContext context);

    // EN: Concrete actions own, cancel and dispose their operations, including partial starts.
    // VI: Action cụ thể sở hữu, hủy và dọn operation, kể cả khi Start lỗi giữa chừng.
    protected virtual PNSkillOperation CreateOperation(PNSkillActionContext context, EveId id)
        => context.Registry.Operations().Get(id).Create();

    public virtual void Dispose() { }
}
