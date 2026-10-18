using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// Transition screens: pure contracts, the cycle state machine and the selection chain. A screen only draws; input stays with the
// network gate and gameplay is never driven from here.

public enum PNTransitionReason { Join, Transfer, Replacement, ActorLost, Custom }
public enum PNTransitionPhase { Preparing, Holding, Loading, CatchingUp, Revealing }
public enum PNTransitionCoverage { Opaque, Translucent }
public enum PNTransitionKind { None, UiToolkit, Prefab }

// Plain data handed to a screen; holds no Unity handles so it can be built in smoke tests and by mods.
public sealed class PNTransitionContext
{
    public const float HardMaximumSeconds = 60f;
    public PNTransitionReason Reason { get; }
    public string FromWorld { get; }
    public string ToWorld { get; }
    public bool Group { get; }
    public float MinimumSeconds { get; }
    public float MaximumSeconds { get; }
    public PNTransitionCoverage Coverage { get; }
    public PNTransitionContext(PNTransitionReason reason, string fromWorld, string toWorld, bool group, float minimumSeconds, float maximumSeconds, PNTransitionCoverage coverage)
    {
        if (!Enum.IsDefined(typeof(PNTransitionReason), reason) || !Enum.IsDefined(typeof(PNTransitionCoverage), coverage)) throw new ArgumentOutOfRangeException(nameof(reason));
        if (float.IsNaN(minimumSeconds) || float.IsNaN(maximumSeconds) || minimumSeconds < 0 || maximumSeconds <= 0 || minimumSeconds > maximumSeconds || maximumSeconds > HardMaximumSeconds)
            throw new ArgumentException("Transition display times must be finite, ordered and within the hard limit.");
        if (fromWorld?.Length > 128 || toWorld?.Length > 128) throw new ArgumentException("Bounded world labels required.");
        Reason = reason; FromWorld = fromWorld ?? ""; ToWorld = toWorld ?? ""; Group = group; MinimumSeconds = minimumSeconds; MaximumSeconds = maximumSeconds; Coverage = coverage;
    }
}

public readonly struct PNTransitionState
{
    public readonly PNTransitionPhase Phase;
    // 0..1 when the source can report progress, otherwise -1.
    public readonly float Fraction;
    public readonly float Elapsed;
    public PNTransitionState(PNTransitionPhase phase, float fraction, float elapsed) { Phase = phase; Fraction = fraction; Elapsed = elapsed; }
}

public interface IPNTransitionScreen : IDisposable
{
    void Show(PNTransitionContext context);
    void Tick(PNTransitionState state, float unscaledDeltaTime);
    // True once the screen fully covers the view; hiding never starts before this.
    bool ReadyToHide { get; }
    void Hide(string reason);
    // True once the fade-out finished and the screen can be disposed.
    bool Finished { get; }
}

// Screen parameters. Parsed strictly from the registered payload so a redesign is a new registration or a changed payload.
public sealed class PNTransitionPayload
{
    public float R { get; }
    public float G { get; }
    public float B { get; }
    public float A { get; }
    public string Text { get; }
    public float FadeIn { get; }
    public float FadeOut { get; }
    public float MinimumSeconds { get; }
    public float MaximumSeconds { get; }
    public PNTransitionCoverage Coverage { get; }
    public string Prefab { get; }
    public static readonly PNTransitionPayload Default = new PNTransitionPayload(.13f, .35f, .78f, 1f, "Loading", .25f, .25f, .4f, 20f, PNTransitionCoverage.Opaque, null);
    public PNTransitionPayload(float r, float g, float b, float a, string text, float fadeIn, float fadeOut, float minimumSeconds, float maximumSeconds, PNTransitionCoverage coverage, string prefab)
    {
        foreach (var value in new[] { r, g, b, a }) if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1) throw new ArgumentException("Transition colour components must be within 0..1.");
        foreach (var value in new[] { fadeIn, fadeOut }) if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 5) throw new ArgumentException("Transition fade must be within 0..5 seconds.");
        if (float.IsNaN(minimumSeconds) || float.IsNaN(maximumSeconds) || minimumSeconds < 0 || maximumSeconds <= 0 || minimumSeconds > maximumSeconds || maximumSeconds > PNTransitionContext.HardMaximumSeconds)
            throw new ArgumentException("Transition display times must be ordered and within the hard limit.");
        if (text != null && text.Length > 128) throw new ArgumentException("Transition text is limited to 128 characters.");
        if (prefab != null && (prefab.Length == 0 || prefab.Length > 256)) throw new ArgumentException("Transition prefab path is bounded.");
        if (!Enum.IsDefined(typeof(PNTransitionCoverage), coverage)) throw new ArgumentOutOfRangeException(nameof(coverage));
        R = r; G = g; B = b; A = a; Text = text ?? ""; FadeIn = fadeIn; FadeOut = fadeOut; MinimumSeconds = minimumSeconds; MaximumSeconds = maximumSeconds; Coverage = coverage; Prefab = prefab;
    }
    private static readonly HashSet<string> Known = new HashSet<string> { "color", "text", "fadeIn", "fadeOut", "minSeconds", "maxSeconds", "coverage", "prefab" };
    // Registered shape: {"color":[r,g,b,a] | "#RRGGBB[AA]", "text":"...", "fadeIn":s, "fadeOut":s, "minSeconds":s, "maxSeconds":s, "coverage":"Opaque|Translucent", "prefab":"Resources/path"}.
    // Every field is optional (defaults above); unknown fields are rejected so typos do not silently fall back.
    public static PNTransitionPayload Parse(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) return Default;
        if (!(token is JObject obj)) throw new FormatException("Transition payload must be an object.");
        foreach (var property in obj.Properties()) if (!Known.Contains(property.Name)) throw new FormatException("Unknown transition payload field: " + property.Name);
        float r = Default.R, g = Default.G, b = Default.B, a = Default.A;
        var color = obj["color"];
        if (color != null)
        {
            if (color.Type == JTokenType.String) ParseHex((string)color, out r, out g, out b, out a);
            else if (color is JArray array && (array.Count == 3 || array.Count == 4)) { r = Number(array[0]); g = Number(array[1]); b = Number(array[2]); a = array.Count == 4 ? Number(array[3]) : 1f; }
            else throw new FormatException("Transition colour must be [r,g,b(,a)] or #RRGGBB(AA).");
        }
        float Opt(string name, float fallback) => obj[name] == null ? fallback : Number(obj[name]);
        PNTransitionCoverage coverage = Default.Coverage;
        if (obj["coverage"] != null && !Enum.TryParse((string)obj["coverage"], false, out coverage)) throw new FormatException("Unknown transition coverage.");
        string text = obj["text"] == null ? Default.Text : obj["text"].Type == JTokenType.String ? (string)obj["text"] : throw new FormatException("Transition text must be a string.");
        string prefab = obj["prefab"] == null ? null : obj["prefab"].Type == JTokenType.String ? (string)obj["prefab"] : throw new FormatException("Transition prefab must be a string.");
        return new PNTransitionPayload(r, g, b, a, text, Opt("fadeIn", Default.FadeIn), Opt("fadeOut", Default.FadeOut), Opt("minSeconds", Default.MinimumSeconds), Opt("maxSeconds", Default.MaximumSeconds), coverage, prefab);
    }
    private static float Number(JToken token)
    {
        if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer) throw new FormatException("Transition numbers must be numeric.");
        return (float)token;
    }
    private static void ParseHex(string value, out float r, out float g, out float b, out float a)
    {
        if (value == null || value.Length < 7 || value[0] != '#' || value.Length != 7 && value.Length != 9) throw new FormatException("Colour must be #RRGGBB or #RRGGBBAA.");
        float Part(int index) => int.Parse(value.Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
        r = Part(1); g = Part(3); b = Part(5); a = value.Length == 9 ? Part(7) : 1f;
    }
}

// Immutable registry content: id, kind and a factory per screen instance. The payload is part of the definition.
public sealed class PNTransitionScreenDefinition : EveIIdentified
{
    public EveId Id { get; }
    public PNTransitionKind Kind { get; }
    public PNTransitionPayload Payload { get; }
    public string Implementation { get; }
    private readonly Func<PNTransitionPayload, IPNTransitionScreen> factory;
    public PNTransitionScreenDefinition(EveId id, PNTransitionKind kind, PNTransitionPayload payload, string implementation, Func<PNTransitionPayload, IPNTransitionScreen> factory)
    {
        if (!id.IsValid || id.ToString().Length > 128 || !Enum.IsDefined(typeof(PNTransitionKind), kind) || payload == null || string.IsNullOrWhiteSpace(implementation) || implementation.Length > 256) throw new ArgumentException("Invalid transition screen definition.");
        if (kind == PNTransitionKind.Prefab && string.IsNullOrEmpty(payload.Prefab)) throw new ArgumentException("A prefab transition screen needs a prefab path in its payload.");
        Id = id; Kind = kind; Payload = payload; Implementation = implementation; this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }
    // Returns null for the "no screen" kind; any other factory result is required.
    public IPNTransitionScreen Create()
    {
        var screen = factory(Payload);
        if (Kind != PNTransitionKind.None && screen == null) throw new InvalidOperationException("Transition screen factory returned no screen: " + Id);
        return screen;
    }
}

// Selection chain: transfer request > world policy > mode > game default. A candidate that is missing or fails to create falls to the next.
public static class PNTransitionSelector
{
    public static readonly EveId DefaultScreen = EveId.Parse("project_n:default_transition");
    public static readonly EveId NoScreen = EveId.Parse("base:none");
    public static IReadOnlyList<EveId> Chain(EveId request, EveId world, EveId mode)
    {
        var chain = new List<EveId>(4);
        foreach (var id in new[] { request, world, mode, DefaultScreen }) if (id.IsValid && !chain.Contains(id)) chain.Add(id);
        return chain;
    }
}

public enum PNTransitionAction { None, StartHide, Close }
public enum PNTransitionStage { Idle, Showing, Hiding }

// One cycle at a time: overlapping signals merge, never open a second screen. Time is supplied by the caller (real time).
public sealed class PNTransitionMachine
{
    public const float HideWatchdogSeconds = 5f;
    public PNTransitionStage Stage { get; private set; }
    public PNTransitionPhase Phase { get; private set; }
    public float Fraction { get; private set; } = -1;
    public int Cycles { get; private set; }
    public bool ForcedHide { get; private set; }
    public PNTransitionContext Context { get; private set; }
    private double started, hideStarted;
    private bool queued;
    private PNTransitionContext queuedContext;

    // Returns true when this signal opened a new cycle (the caller creates the screen), false when it merged into a running one.
    public bool Begin(PNTransitionContext context, double now)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (Stage == PNTransitionStage.Idle)
        { Stage = PNTransitionStage.Showing; Phase = PNTransitionPhase.Preparing; Fraction = -1; ForcedHide = false; Context = context; started = now; Cycles++; return true; }
        if (Stage == PNTransitionStage.Hiding) { queued = true; queuedContext = context; return false; }
        // Merge: the cycle keeps its screen and timing; a later signal only replaces the reported phase through SetPhase.
        return false;
    }
    public void SetPhase(PNTransitionPhase phase, float fraction = -1) { if (Stage == PNTransitionStage.Showing) { Phase = phase; Fraction = fraction; } }
    public float Elapsed(double now) => Stage == PNTransitionStage.Idle ? 0 : (float)Math.Max(0, now - started);

    // conditionsMet: view bound to the target world, actor ready, input gate open, camera cut and one frame rendered.
    public PNTransitionAction Update(double now, bool conditionsMet, bool screenReady, bool screenFinished, out PNTransitionContext reopened)
    {
        reopened = null;
        switch (Stage)
        {
            case PNTransitionStage.Showing:
            {
                double elapsed = now - started;
                bool expired = elapsed >= Context.MaximumSeconds;
                bool minimumShown = elapsed >= Context.MinimumSeconds;
                if (expired || conditionsMet && minimumShown && screenReady)
                { Stage = PNTransitionStage.Hiding; ForcedHide = expired && !conditionsMet; hideStarted = now; Phase = PNTransitionPhase.Revealing; return PNTransitionAction.StartHide; }
                return PNTransitionAction.None;
            }
            case PNTransitionStage.Hiding:
                if (screenFinished || now - hideStarted >= HideWatchdogSeconds)
                {
                    Stage = PNTransitionStage.Idle; Fraction = -1;
                    if (queued) { queued = false; reopened = queuedContext; queuedContext = null; Begin(reopened, now); }
                    return PNTransitionAction.Close;
                }
                return PNTransitionAction.None;
            default: return PNTransitionAction.None;
        }
    }
    // Session end or controller teardown: forget the cycle without any further callbacks.
    public void Reset() { Stage = PNTransitionStage.Idle; queued = false; queuedContext = null; Context = null; Fraction = -1; }
}

// Neutral signal seam so Game does not reference Coop: network or any other source reports that the local participant is changing
// world. The controller decides when the cycle ends from local state.
public interface IPNTransitionSignalSink
{
    void Begin(PNTransitionReason reason, string fromWorld, string toWorld, bool group);
    void Progress(PNTransitionPhase phase, float fraction = -1);
}
public interface IPNTransitionSignalSource
{
    void Attach(IPNTransitionSignalSink sink);
    void Detach();
}

// Process-wide list of signal sources (network layer registers one); the controller of each play session attaches and detaches them.
public static class PNTransitionSources
{
    private static readonly List<IPNTransitionSignalSource> sources = new List<IPNTransitionSignalSource>();
    public static IReadOnlyList<IPNTransitionSignalSource> Snapshot() => sources.ToArray();
    public static void Register(IPNTransitionSignalSource source) { if (source == null) throw new ArgumentNullException(nameof(source)); if (!sources.Contains(source)) sources.Add(source); }
    public static void Unregister(IPNTransitionSignalSource source) { sources.Remove(source); }
#if UNITY_5_3_OR_NEWER
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => sources.Clear();
#endif
}
