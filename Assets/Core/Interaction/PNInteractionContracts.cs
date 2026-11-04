using System;
using EveEngine;

// EN: What a handler of an interactable sees and answers (Docs/InteractablePlan.md I2). CanUse is pure and runs on every machine to decide whether an entry is dimmed (a machine that
//     lacks the data answers Usable: the authority checks again); Interact runs only on the authority, after the generic checks (the object exists and is available, the player is in range
//     and alive, the permission, the uses left). The handler keeps no state of its own: what changes (a room started, a gate taken, money spent) belongs to the owner it calls.
// VI: Handler của vật tương tác thấy và trả lời gì. CanUse thuần, chạy mọi máy để làm mờ mục; Interact chỉ chạy ở authority sau các kiểm tra chung.
public readonly struct PNInteractionAvailability
{
    public bool Usable { get; }
    public string ReasonKey { get; }
    private PNInteractionAvailability(bool usable, string reasonKey) { Usable = usable; ReasonKey = reasonKey ?? ""; }
    public static PNInteractionAvailability Yes => new PNInteractionAvailability(true, "");
    public static PNInteractionAvailability Denied(string reasonKey = "") => new PNInteractionAvailability(false, reasonKey);
}

public sealed class PNInteractionContext
{
    public EveSessionContext Session { get; }
    public EveWorldContext World { get; }
    public EveEntityLoader Actor { get; }
    public long OwnerId { get; }
    public PNInteractableDefinition Definition { get; }
    public PNSyncTarget Target { get; }
    // The component the glue registered for the target (a PNWorldObject in the game).
    public object Owner { get; }
    public bool IsAuthority { get; }
    public PNInteractionContext(EveSessionContext session, EveWorldContext world, EveEntityLoader actor, long ownerId, PNInteractableDefinition definition, PNSyncTarget target, object owner, bool isAuthority)
    { Session = session; World = world; Actor = actor; OwnerId = ownerId; Definition = definition ?? throw new ArgumentNullException(nameof(definition)); Target = target; Owner = owner; IsAuthority = isAuthority; }
}

public interface IPNInteractionHandler
{
    PNInteractionAvailability CanUse(PNInteractionContext context);
    void Interact(PNInteractionContext context);
}
