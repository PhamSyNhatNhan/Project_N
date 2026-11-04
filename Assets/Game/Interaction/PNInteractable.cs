#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Something in a world a player stands near and activates (Docs/InteractablePlan.md I2). It sits on a PNWorldObject (its key and its place in the sync layer) and names a registered
//     interactable definition. Its state is one sync block, `project_n:interactable` = { available, uses, label }: the authority writes it (the room director opens and closes the object),
//     every machine reads it, so a client's scanner shows exactly what the host has opened. `uses` is the uses left (-1 = no limit); `label` is an optional localization key that
//     replaces the definition's label (a gate says which kind of room is behind it). It does nothing when activated: the command handler checks, the definition's handler acts.
// VI: Vật trong world mà người chơi đứng gần và kích hoạt. Nằm trên PNWorldObject, trạng thái là một khối đồng bộ do authority ghi, mọi máy đọc.
[DisallowMultipleComponent, RequireComponent(typeof(PNWorldObject))]
public sealed class PNInteractable : MonoBehaviour, IPNWorldObjectListener, IPNSyncStateSource, IPNSyncStateSink
{
    public static readonly EveId State = EveId.Parse("project_n:interactable"), Command = EveId.Parse("project_n:interact");
    public const int StateBytes = 512, CommandBytes = 16;
    [SerializeField] private string definition = "";
    [SerializeField] private Transform anchor;
    private PNSyncService sync;
    private PNSyncTarget target;
    private IDisposable sourceLease, sinkLease;

    public string DefinitionId => definition;
    public PNInteractableDefinition Definition { get; private set; }
    public bool IsBound => sync != null;
    public PNSyncTarget Target => target;
    public bool Available { get; private set; }
    public int UsesLeft { get; private set; } = -1;
    public string LabelKey { get; private set; } = "";
    public string Key => GetComponent<PNWorldObject>().Key;
    public Vector3 AnchorPosition => (anchor != null ? anchor : transform).position;
    // The text key to show: the authority's override, else the definition's.
    public string ShownLabel => !string.IsNullOrEmpty(LabelKey) ? LabelKey : Definition?.Settings.Label ?? "";
    public EveId StateId => State;

    public void Configure(string definitionId, Transform anchorTransform = null)
    {
        if (!EveId.TryParse(definitionId, out _)) throw new ArgumentException("An interactable definition id is required.", nameof(definitionId));
        definition = definitionId; anchor = anchorTransform;
    }

    public void OnBound(PNSyncService service, PNSyncTarget boundTarget)
    {
        sync = service; target = boundTarget;
        Definition = service.Registry.Instances().GetEffectiveKind<PNInteractableDefinition>(EveId.Parse(definition));
        Available = false; LabelKey = ""; UsesLeft = Definition.Settings.Uses == PNInteractionUses.Once ? 1 : -1;
        sinkLease = service.RegisterSink(target, this);
        if (service.IsAuthority) { sourceLease = service.RegisterSource(target, this); service.MarkDirty(target, State); }
    }

    public void OnUnbound()
    {
        sinkLease?.Dispose(); sourceLease?.Dispose(); sinkLease = sourceLease = null; sync = null; target = default;
    }

    // ---- the sync block ----
    public JToken Capture() => new JObject { ["available"] = Available, ["uses"] = UsesLeft, ["label"] = LabelKey ?? "" };

    public void Apply(JToken value, ulong revision)
    {
        var state = value as JObject ?? throw new FormatException("Interactable state object required.");
        if (state["available"]?.Type != JTokenType.Boolean || state["uses"]?.Type != JTokenType.Integer || state["label"]?.Type != JTokenType.String) throw new FormatException("Interactable state needs available, uses and label.");
        Available = state.Value<bool>("available"); UsesLeft = state.Value<int>("uses"); LabelKey = state.Value<string>("label");
    }

    // ---- the authority's side ----
    // Open or close the object; `labelKey` (optional) replaces the definition's label while it is open.
    public void SetAvailable(bool available, string labelKey = "")
    {
        RequireAuthority();
        labelKey = labelKey ?? "";
        if (Available == available && LabelKey == labelKey) return;
        Available = available; LabelKey = labelKey; sync.MarkDirty(target, State);
    }

    // A successful use: a once-only object spends its use and closes.
    public void Consume()
    {
        RequireAuthority();
        if (UsesLeft <= 0) return;
        UsesLeft--; if (UsesLeft == 0) Available = false;
        sync.MarkDirty(target, State);
    }

    private void RequireAuthority()
    {
        if (sync == null) throw new InvalidOperationException("The interactable is not bound to a world yet.");
        if (!sync.IsAuthority) throw new InvalidOperationException("Only the authority opens or closes an interactable.");
    }
}
#endif
