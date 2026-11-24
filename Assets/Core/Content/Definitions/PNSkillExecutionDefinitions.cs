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
    // Observed View (Docs/ObservedViewPlan.md): an Action says once how it is drawn. `WithView` names the view the engine builds and drives on every machine that shows the run
    // (the Authority and the Predicted machine run the Action and its view; the others run only the view). `NoView` says there is nothing to draw (the run is then not published).
    // Since PV8 every Action declares one of the two (`PNActionViewPolicy.DeclarationRequired`: a registration error otherwise, mods included).
    // The run of this skill data is published (the other machines are told) only when there is something to draw: a view, or a look the data names.
    // `NoView` says the Action has no view of its OWN; its data may still name a look (a dash trail), and then the run is published for that look.
    public bool PublishesRun(PNSkillData data) => ViewFactory != null || PNLookData.Declares(data);
    public bool HasView => ViewFactory != null;
    // The run of THIS skill data has a view: the definition's, or the generic one that follows the look the data names (`perform.look`).
    public bool HasViewFor(PNSkillData data) => ViewFactory != null || PNLookData.Declares(data);
    // False only while an Action still draws itself on the machines that run it (Eve's sweep until PV5): the engine then builds its view on the machines that only watch.
    public bool ViewOnRunningMachines { get; private set; } = true;
    public bool ViewDeclared => ViewFactory != null || noView;
    private bool noView;
    // The selected implementation consumes the driver-supplied progress units. This
    // describes timing only; safe visual seek is a separate, not-yet-enabled capability.
    public bool DriverProgress { get; private set; }
    [JsonIgnore] public Func<PNActionViewBuild,IPNActionView> ViewFactory { get; private set; }
    public EveId? PresentationScaleTag { get; private set; }
    // `scaleTag` (optional) names the registered tag whose factor scales the picture; the committed scale reaches the factory as `PNActionViewBuild.Scale`.
    public PNSkillActionDefinition WithView(Func<PNActionViewBuild,IPNActionView> factory, EveId? scaleTag = null, bool onRunningMachines = true)
    {
        if (noView) throw new InvalidOperationException("An Action declares WithView or NoView, not both.");
        if (scaleTag.HasValue && !scaleTag.Value.IsValid) throw new ArgumentException("A registered presentation scale tag is required.", nameof(scaleTag));
        ViewFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        PresentationScaleTag = scaleTag; ViewOnRunningMachines = onRunningMachines;
        return this;
    }
    public PNSkillActionDefinition NoView()
    {
        if (ViewFactory != null) throw new InvalidOperationException("An Action declares WithView or NoView, not both.");
        noView = true; return this;
    }
    internal IPNActionView CreateView(PNActionViewBuild build) => ViewFactory?.Invoke(build);
    public PNSkillActionDefinition WithDriverProgress() { DriverProgress = true; return this; }
    public PNSkillActionDefinition(EveId id, Type implementationType) : this(id, implementationType, null) { }
    public PNSkillActionDefinition(EveId id, Type implementationType, Action<PNSkillActionData, EveRegistryHub> validate) : this(id, implementationType, validate, PNSkillContextRequirements.None) { }
    public PNSkillActionDefinition(EveId id, Type implementationType, Action<PNSkillActionData, EveRegistryHub> validate, PNSkillContextRequirements requiredContext,
        Action<JToken, PNContentReferenceContext> validateReferences = null) : base(id, implementationType) { validator = validate; RequiredContext = requiredContext; referenceValidator = validateReferences; }
    public void ValidateReferences(JToken data, PNContentReferenceContext context) => referenceValidator?.Invoke(data, context);
    public void ValidatePayload(PNSkillActionData data, EveRegistryHub hub) { data.ValidateTags(hub); PNLookData.Validate(data, this, hub); validator?.Invoke(data, hub); }
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
