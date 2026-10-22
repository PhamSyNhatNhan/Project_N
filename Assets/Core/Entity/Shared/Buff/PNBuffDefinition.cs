using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

public sealed class PNBuffTagDefinition : EveIIdentified
{
    public EveId Id { get; }
    public PNBuffTagDefinition(EveId id) { if (!id.IsValid) throw new ArgumentException("Buff tag ID required."); Id = id; }
}

public enum PNBuffInstanceScope { Target, Source }
public enum PNBuffEndReason { Expired, Dispelled, Bypass, ImmunityRemoved, LimitReduced, Replaced, Death, Unload, SessionEnded, RuntimeError, SourceUnavailable, StacksDepleted }
public enum PNBuffClock { Game, Entity, EntityWithoutMinimum }
public enum PNBuffStatus { Pending, Applied, Reapplied, Removed, Rejected, Failed, Triggered, StacksChanged }
public enum PNBuffSelection { Current, Future, CurrentAndFuture }
public enum PNBuffCountdownSlot { TopLeft, TopRight, BottomLeft, BottomRight }

// Opt-in presentation descriptor: the named Numbers slot shows remaining seconds, derived by the replica from
// replicated clock state instead of host publication every second. Immutable; v1 supports Ceiling/Hide only.
public sealed class PNBuffCountdown : IEquatable<PNBuffCountdown>
{
    public const string CeilingRounding = "Ceiling";
    public const string HideZero = "Hide";
    public PNBuffCountdownSlot Slot { get; }
    public string Rounding { get; }
    public string Zero { get; }
    public PNBuffCountdown(PNBuffCountdownSlot slot, string rounding = CeilingRounding, string zero = HideZero)
    {
        if (!Enum.IsDefined(typeof(PNBuffCountdownSlot), slot)) throw new ArgumentException("Unknown countdown slot.");
        if (rounding != CeilingRounding) throw new ArgumentException("Unsupported countdown rounding: " + rounding);
        if (zero != HideZero) throw new ArgumentException("Unsupported countdown zero behavior: " + zero);
        Slot = slot; Rounding = rounding; Zero = zero;
    }
    public static PNBuffCountdown BottomRight => new PNBuffCountdown(PNBuffCountdownSlot.BottomRight);
    internal PNBuffNumbers With(PNBuffNumbers numbers, long? value)
    {
        switch (Slot)
        {
            case PNBuffCountdownSlot.TopLeft: return new PNBuffNumbers(value, numbers.TopRight, numbers.BottomLeft, numbers.BottomRight);
            case PNBuffCountdownSlot.TopRight: return new PNBuffNumbers(numbers.TopLeft, value, numbers.BottomLeft, numbers.BottomRight);
            case PNBuffCountdownSlot.BottomLeft: return new PNBuffNumbers(numbers.TopLeft, numbers.TopRight, value, numbers.BottomRight);
            default: return new PNBuffNumbers(numbers.TopLeft, numbers.TopRight, numbers.BottomLeft, value);
        }
    }
    public bool Equals(PNBuffCountdown other) => other != null && Slot == other.Slot && Rounding == other.Rounding && Zero == other.Zero;
    public override bool Equals(object other) => Equals(other as PNBuffCountdown);
    public override int GetHashCode() => (int)Slot;
}

// Single pure countdown rule shared by host tracker and replica: Ceiling(Max(0, expiration - time)), hidden at zero,
// without expiration or beyond Int64. No sticky fault: an out-of-range value is hidden for this evaluation only.
internal static class PNBuffCountdownMath
{
    internal static long? Compute(double? expiration, double time, out bool overflow)
    {
        overflow = false;
        if (!expiration.HasValue) return null;
        double value = Math.Ceiling(Math.Max(0, expiration.Value - time));
        if (value >= 9223372036854775808d) { overflow = true; return null; }
        return value > 0 ? (long)value : (long?)null;
    }
}

// A registered immutable profile. Payload schemas and contextual inputs belong to its factory.
public sealed class PNBuffDefinition : EveIIdentified
{
    private readonly JToken payload;
    private readonly Func<JToken, PNBuffBehavior> factory;
    private readonly Action<JToken, EveRegistryHub> validate;
    private readonly Action<JToken> validateInput;
    public EveId Id { get; }
    public IReadOnlyList<EveId> Tags { get; }
    public EveId ImplementationId { get; }
    public EveId PresenterId { get; }
    public bool MultipleInstances { get; }
    public int MaximumInstances { get; }
    public PNBuffInstanceScope Scope { get; }
    public bool CanDispel { get; }
    public string IconPath { get; }
    public string Glow { get; }
    public bool ShowInUI { get; }
    // Opt-in replica-derived countdown. Never inherited by Derive; stock/mod definitions enable it explicitly.
    public PNBuffCountdown Countdown { get; private set; }
    public JToken Payload => payload?.DeepClone();
    private readonly Action<JToken, PNContentReferenceContext> validateReferences;
    public PNBuffDefinition(EveId id, EveId implementationId, Func<JToken, PNBuffBehavior> factory, JToken payload,
        IEnumerable<EveId> tags = null, bool multipleInstances = false, int maximumInstances = 1,
        PNBuffInstanceScope scope = PNBuffInstanceScope.Target, bool canDispel = true,
        Action<JToken, EveRegistryHub> validate = null, Action<JToken> validateInput = null,
        string iconPath = "", string glow = "", bool showInUI = true,
        Action<JToken, PNContentReferenceContext> validateReferences = null, EveId presenterId = default)
    {
        if (!id.IsValid || id.Namespace == "base") throw new ArgumentException("Original Buff ID required.");
        if (maximumInstances < 1 || !multipleInstances && maximumInstances != 1) throw new ArgumentOutOfRangeException(nameof(maximumInstances));
        if (!Enum.IsDefined(typeof(PNBuffInstanceScope), scope)) throw new ArgumentException("Unknown instance scope.");
        if (!implementationId.IsValid) throw new ArgumentException("Explicit Buff implementation ID required.");
        if (!PNBuffGlow.IsValid(glow)) throw new ArgumentException("Buff glow must be empty, a preset name or hex colour: " + glow, nameof(glow));
        Id = id; ImplementationId = implementationId; PresenterId = presenterId; this.validateReferences = validateReferences;
        this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
        this.payload = payload?.DeepClone(); this.validate = validate; this.validateInput = validateInput;
        var ids = (tags ?? Array.Empty<EveId>()).Distinct().OrderBy(x => x).ToArray();
        if (ids.Any(x => !x.IsValid)) throw new ArgumentException("Invalid Buff tag.");
        Tags = Array.AsReadOnly(ids); MultipleInstances = multipleInstances; MaximumInstances = maximumInstances;
        Scope = scope; CanDispel = canDispel; IconPath = iconPath ?? ""; Glow = glow ?? ""; ShowInUI = showInUI;
    }
    public void Validate(EveRegistryHub hub) { foreach (var tag in Tags) hub.BuffTags().Get(tag); ValidatePayload(hub); ValidateReferences(new PNContentReferenceContext(hub)); }
    public void ValidateReferences(PNContentReferenceContext context)
    { if (PresenterId.IsValid) context.Require<PNBuffPresenterDefinition>(PresenterId, "presenterId"); validateReferences?.Invoke(Payload, context); }
    internal void ValidatePayload(EveRegistryHub hub) => validate?.Invoke(Payload, hub);
    public void ValidateInput(JToken input)
    {
        if (validateInput != null) validateInput(input?.DeepClone());
        else if (input != null && input.Type != JTokenType.Null) throw new ArgumentException("This Buff accepts no contextual input.");
    }
    internal PNBuffBehavior Create() => factory(Payload) ?? throw new InvalidOperationException("Buff factory returned null.");
    public PNBuffDefinition Derive(EveId id, JToken data, IEnumerable<EveId> tags, bool multiple, int maximum,
        PNBuffInstanceScope scope, bool canDispel, string iconPath = "", string glow = "", bool showInUI = true)
        => new PNBuffDefinition(id, ImplementationId, factory, data, tags, multiple, maximum, scope, canDispel, validate, validateInput, iconPath, glow, showInUI, validateReferences, PresenterId);
    // Returns a new immutable definition carrying the countdown descriptor. Copies every component; does not
    // register anything or run the factory/validators. Existing constructor/Derive signatures are unchanged.
    public PNBuffDefinition WithCountdown(PNBuffCountdown descriptor)
    {
        var copy = new PNBuffDefinition(Id, ImplementationId, factory, payload, Tags, MultipleInstances, MaximumInstances, Scope, CanDispel,
            validate, validateInput, IconPath, Glow, ShowInUI, validateReferences, PresenterId);
        copy.Countdown = descriptor;
        return copy;
    }
}

public abstract class PNBuffBehavior
{
    // Opt-in: replicated fields change only through callbacks/context/writer publication.
    // Custom time-dependent getters retain scanning unless they explicitly opt in.
    public virtual bool EventDrivenReplication => false;
    public PNBuffPresentationWriter Presentation { get; internal set; }
    public virtual void OnApply(PNBuffContext context, JToken input) { }
    public virtual bool OnReapply(PNBuffContext context, PNBuffApplyRequest request) => false;
    public virtual bool OnFull(PNBuffContext context, PNBuffApplyRequest request) => OnReapply(context, request);
    public virtual void OnScheduled(PNBuffContext context, string key) { }
    public virtual void OnSuspend(PNBuffContext context) { }
    public virtual void OnResume(PNBuffContext context) { }
    public virtual void OnEnding(PNBuffContext context, PNBuffEndReason reason, PNBuffSnapshot finalState) { }
    public virtual void OnCleanup(PNBuffContext context) { }
    public virtual void OnTargetDeath(PNBuffContext context) => context.End(PNBuffEndReason.Death);
    public virtual bool Supports(Type contract) => false;
    public virtual void ValidateContributions(PNBuffContext context, Type contract, IReadOnlyList<PNBuffContribution> proposed) { }
    public virtual void OnContributionsChanged(PNBuffContext context) { }
    // Return false for default reset. No instance-ending effects during a transition.
    public virtual bool TryMigrateFrom(PNBuffBehavior previous, PNBuffContext context) => false;
    public virtual void OnBehaviorStarted(PNBuffContext context, bool migrated) { }
    // Readable behavior state; presentation decides whether and where to show it.
    public virtual double? StackCount => null;
    public virtual bool CanTrigger => false;
    public virtual void Trigger(PNBuffContext context) { }
    public virtual bool CanChangeStacks => false;
    public virtual void ChangeStacks(PNBuffContext context, int delta) { }
}

public sealed class PNBuffApplyRequest
{
    private readonly JToken input;
    public EveId BuffId { get; }
    public PNBuffSource Source { get; }
    public JToken Input => input?.DeepClone();
    public PNBuffApplyRequest(EveId id, PNBuffSource source, JToken input = null)
    { if (!id.IsValid) throw new ArgumentException("Buff ID required."); BuffId = id; Source = source ?? throw new ArgumentNullException(nameof(source)); this.input = input?.DeepClone(); }
}

public sealed class PNBuffReceipt
{
    public PNBuffStatus Status { get; internal set; } = PNBuffStatus.Pending;
    public PNBuffHandle Handle { get; internal set; }
    public string Message { get; internal set; }
    public Exception Error { get; internal set; }
}

public readonly struct PNBuffHandle : IEquatable<PNBuffHandle>
{
    internal readonly PNBuffCollection Collection;
    public long InstanceId { get; }
    internal PNBuffHandle(PNBuffCollection collection, long id) { Collection = collection; InstanceId = id; }
    public bool IsAlive => Collection != null && Collection.Contains(this);
    public bool Equals(PNBuffHandle other) => ReferenceEquals(Collection, other.Collection) && InstanceId == other.InstanceId;
    public override bool Equals(object obj) => obj is PNBuffHandle other && Equals(other);
    public override int GetHashCode() => (Collection?.GetHashCode() ?? 0) ^ InstanceId.GetHashCode();
}

public sealed class PNBuffSnapshot
{
    public PNBuffHandle Handle { get; }
    public EveId Id { get; }
    public EveId ProfileId { get; }
    public EveEntityId SourceId { get; }
    public IReadOnlyList<EveId> Tags { get; }
    public double? Stacks { get; }
    public double? Remaining { get; }
    public bool Suspended { get; }
    public bool CanDispel { get; }
    public bool ShowInUI { get; }
    public string IconPath { get; }
    public string Glow { get; }
    internal PNBuffSnapshot(PNBuffInstance instance)
    {
        Handle = instance.Handle; Id = instance.Id; ProfileId = instance.Profile.Id; SourceId = instance.Source.EntityId;
        Tags = Array.AsReadOnly(instance.Tags.OrderBy(x => x).ToArray()); Stacks = instance.Behavior.StackCount;
        Remaining = instance.Expiration.HasValue ? Math.Max(0, instance.Expiration.Value - instance.Time) : (double?)null;
        Suspended = instance.Suspended; CanDispel = instance.Profile.CanDispel; ShowInUI = instance.Profile.ShowInUI;
        IconPath = instance.Profile.IconPath; Glow = instance.Profile.Glow;
    }
}
