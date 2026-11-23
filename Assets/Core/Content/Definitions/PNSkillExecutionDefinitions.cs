using System;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Flags]
public enum PNSkillContextRequirements { None = 0, Targets = 1, Direction = 2, Position = 4 }

// EN: Registers a constructor, never an executing instance. Implementation participates in content hashes.
// VI: Đăng ký constructor, không đăng ký instance đang chạy. Implementation tham gia hash nội dung.
public abstract class PNSkillExecutionDefinition<T> : EveIIdentified where T : class
{
    [JsonIgnore] private readonly Type implementationType;
    public virtual EveId Id { get; }
    public virtual string Implementation => implementationType.FullName;
    [JsonIgnore] public Type ImplementationType => implementationType;

    protected PNSkillExecutionDefinition(EveId id, Type implementationType)
    {
        if (!id.IsValid) throw new ArgumentException("Execution ID is required.", nameof(id));
        if (implementationType == null || !typeof(T).IsAssignableFrom(implementationType) ||
            implementationType.IsAbstract || implementationType.ContainsGenericParameters ||
            implementationType.GetConstructor(Type.EmptyTypes) == null)
            throw new ArgumentException($"Implementation must be a concrete {typeof(T).Name} with a public parameterless constructor.", nameof(implementationType));
        Id = id;
        this.implementationType = implementationType;
    }

    public T Create() => (T)Activator.CreateInstance(implementationType);
}

// EN: Core registry entry for a reusable four-hook action.
// VI: Entry registry Core cho action dùng lại có bốn hook.
public class PNSkillActionDefinition : PNSkillExecutionDefinition<PNSkillAction>
{
    private readonly Action<PNSkillActionData, EveRegistryHub> validator;
    private readonly Action<JToken, PNContentReferenceContext> referenceValidator;
    public PNSkillContextRequirements RequiredContext { get; }
    // G8c: every Action is shown to the other machines. The framework tells them when the authority begins it and each of them plays the same Action in an Observed context
    // (no Ability, no motion, no hit: the services filter by mode). The Action itself has no code for it; a definition with nothing to draw says so once (`NoReplayOnObservers`).
    public bool ObserverReplay { get; private set; } = true;
    // The selected implementation consumes the driver-supplied progress units. This
    // describes timing only; safe visual seek is a separate, not-yet-enabled capability.
    public bool DriverProgress { get; private set; }
    [JsonIgnore] public Func<PNSkill,PNSkillData,UnityEngine.Vector3,IPNSkillActionPresentation> PresentationFactory { get; private set; }
    public EveId? PresentationScaleTag { get; private set; }
    private Func<PNSkill,PNSkillData,UnityEngine.Vector3,double,IPNSkillActionPresentation> scaledPresentation;
    public PNSkillActionDefinition WithPresentation(Func<PNSkill,PNSkillData,UnityEngine.Vector3,IPNSkillActionPresentation> factory)
    {
        PresentationFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        scaledPresentation = null;
        PresentationScaleTag = null;
        return this;
    }
    public PNSkillActionDefinition WithScaledPresentation(EveId tag, Func<PNSkill,PNSkillData,UnityEngine.Vector3,double,IPNSkillActionPresentation> factory)
    {
        if (!tag.IsValid) throw new ArgumentException("A registered presentation scale tag is required.", nameof(tag));
        scaledPresentation = factory ?? throw new ArgumentNullException(nameof(factory));
        PresentationFactory = (skill, data, direction) => factory(skill, data, direction, 1);
        PresentationScaleTag = tag;
        return this;
    }
    internal IPNSkillActionPresentation CreatePresentation(PNSkill skill, PNSkillData data, UnityEngine.Vector3 direction, double scale)
        => scaledPresentation != null ? scaledPresentation(skill, data, direction, scale) : PresentationFactory?.Invoke(skill, data, direction);
    public PNSkillActionDefinition WithDriverProgress() { DriverProgress = true; return this; }
    public PNSkillActionDefinition NoReplayOnObservers() { ObserverReplay = false; return this; }
    public PNSkillActionDefinition(EveId id, Type implementationType) : this(id, implementationType, null) { }
    public PNSkillActionDefinition(EveId id, Type implementationType, Action<PNSkillActionData, EveRegistryHub> validate) : this(id, implementationType, validate, PNSkillContextRequirements.None) { }
    public PNSkillActionDefinition(EveId id, Type implementationType, Action<PNSkillActionData, EveRegistryHub> validate, PNSkillContextRequirements requiredContext,
        Action<JToken, PNContentReferenceContext> validateReferences = null) : base(id, implementationType) { validator = validate; RequiredContext = requiredContext; referenceValidator = validateReferences; }
    public void ValidateReferences(JToken data, PNContentReferenceContext context) => referenceValidator?.Invoke(data, context);
    public void ValidatePayload(PNSkillActionData data, EveRegistryHub hub) { data.ValidateTags(hub); validator?.Invoke(data, hub); }
}

// EN: Core registry entry for work that actions can compose.
// VI: Entry registry Core cho thao tác mà action có thể ghép lại.
public class PNSkillOperationDefinition : PNSkillExecutionDefinition<PNSkillOperation>
{
    private readonly Action<JToken, EveRegistryHub> validator;
    private readonly Action<JToken, PNContentReferenceContext> referenceValidator;
    public PNSkillContextRequirements RequiredContext { get; }
    // True when the Operation reads its attribution/world only from the context (Source/World) and never needs the live
    // Skill, so it may run from a frozen context (e.g. a projectile OnHit plan) after the source skill/entity is gone.
    public bool SourceIndependent { get; }
    public PNSkillOperationDefinition(EveId id, Type implementationType) : this(id, implementationType, null) { }
    public PNSkillOperationDefinition(EveId id, Type implementationType, Action<JToken, EveRegistryHub> validate) : this(id, implementationType, validate, PNSkillContextRequirements.None) { }
    public PNSkillOperationDefinition(EveId id, Type implementationType, Action<JToken, EveRegistryHub> validate, PNSkillContextRequirements requiredContext,
        Action<JToken, PNContentReferenceContext> validateReferences = null) : this(id, implementationType, validate, requiredContext, validateReferences, false) { }
    public PNSkillOperationDefinition(EveId id, Type implementationType, Action<JToken, EveRegistryHub> validate, PNSkillContextRequirements requiredContext,
        Action<JToken, PNContentReferenceContext> validateReferences, bool sourceIndependent) : base(id, implementationType)
    { validator = validate; RequiredContext = requiredContext; referenceValidator = validateReferences; SourceIndependent = sourceIndependent; }
    public void ValidateReferences(JToken data, PNContentReferenceContext context) => referenceValidator?.Invoke(data, context);
    public void ValidatePayload(JToken data, EveRegistryHub hub) => validator?.Invoke(data, hub);
}
