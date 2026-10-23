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
    public virtual PNBuffSource Source => Skill != null ? PNBuffSource.Capture(Skill.GetComponent<PNStat>()) : null;
    // EN: World used to validate live targets; the skill entity's world by default (null for an entity outside any world).
    // VI: World dùng để kiểm mục tiêu sống; mặc định là world của entity skill.
    public virtual EveWorldContext World => Skill != null ? Skill.GetComponent<EveEntityIdentity>()?.World : null;
    public PNSkillInvocation Invocation { get; }
    public EveId EntrySkillId => Invocation?.SkillId ?? SkillId;
    public long InvocationId => Invocation?.Id ?? 0;
    public Guid InvocationGeneration => Invocation?.GenerationId ?? Guid.Empty;
    internal bool AllowContributions { get; }
    internal bool HasBegun => begun;
    // True when FreezeOnHit may be called now: a begun context of a live invocation that allows contributions.
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
    public void BeginAction()
    {
        if (begun) return; begun = true;
        if (Invocation != null) ContributionSnapshot = Invocation.State.Snapshot();
        if (Invocation != null && SkillId != Invocation.SkillId && Registry.Skills.GetEffective(SkillId).Data is JObject root && root["effects"] != null)
            AuthoredEffects = PNSkillMetadata.Parse(root, Registry).Effects;
        Fire("OnActionStart");
        if (Invocation?.IsEnded == true) throw new OperationCanceledException("Invocation cancelled during Action start.");
    }
    public void CompleteAction() { if (completed) return; completed = true; Fire("OnComplete"); }
    public void CancelEffects() { if (completed) return; completed = true; Fire("OnCancel"); }
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
    {
        var values = Invocation != null && AllowContributions ? Invocation.State.Combined(ContributionSnapshot) : Array.Empty<PNSkillContribution>();
        var tags = Invocation != null && AllowContributions ? Invocation.State.Tags(this, values) : EffectiveTags();
        var result = target.ReceiveDamage(ReclassifyDamage(original, amount, capture, tags));
        if (Invocation != null) Invocation.State.Fire(this, "OnHit", target, point, values, hitIndex);
        return result;
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
