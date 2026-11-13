using System;

// The look of the gameplay camera of a world, as data (Docs/CameraSinglePathPlan.md). One director-owned rig is shaped by the view of the viewed world; a world's own scene camera is only one source of it.
// Layers: game default -> mode -> world -> player; a later layer overrides an earlier one field by field and the result must stay inside the hard limits (a bad layer is refused, never partly applied).
// Pure (no Unity types) so the smoke suite checks it. Angles are degrees, distances metres; screen positions are the Cinemachine composer's (0 = the middle, +-0.5 = the edge of the screen).
public sealed class PNCameraView
{
    public PNCameraProjection Projection { get; }
    public float FieldOfView { get; }
    public float OrthographicSize { get; }
    public float Pitch { get; }
    public float Yaw { get; }
    public float Distance { get; }
    public float ScreenX { get; }
    public float ScreenY { get; }
    public float DampingX { get; }
    public float DampingY { get; }
    public float DampingZ { get; }
    public float LookaheadTime { get; }
    public float DeadZoneWidth { get; }
    public float DeadZoneHeight { get; }
    public float SoftZoneWidth { get; }
    public float SoftZoneHeight { get; }
    // The background colour of the world (clear colour); black by default (user decision 2026-11-13, effects come later).
    public float BackgroundR { get; }
    public float BackgroundG { get; }
    public float BackgroundB { get; }

    // What the shipped rigs (Hall, Lobby, ErMode) were: 45 degrees, 5.5 m, FOV 38, damping 1, soft zone .2 x .35, lookahead .05.
    public static readonly PNCameraView Default = new PNCameraView(PNCameraProjection.Perspective, 38f, 5f, 45f, 0f, 5.5f, 0f, 0f, 1f, 1f, 1f, .05f, 0f, 0f, .2f, .35f, 0f, 0f, 0f);

    public PNCameraView(PNCameraProjection projection, float fieldOfView, float orthographicSize, float pitch, float yaw, float distance, float screenX, float screenY, float dampingX, float dampingY, float dampingZ,
        float lookaheadTime, float deadZoneWidth, float deadZoneHeight, float softZoneWidth, float softZoneHeight, float backgroundR, float backgroundG, float backgroundB)
    {
        if (projection != PNCameraProjection.Perspective && projection != PNCameraProjection.Orthographic) throw new ArgumentOutOfRangeException(nameof(projection));
        Finite(fieldOfView, 1f, 170f, nameof(fieldOfView)); Finite(orthographicSize, .1f, 1000f, nameof(orthographicSize));
        Finite(pitch, -89f, 89f, nameof(pitch)); Finite(yaw, -360f, 360f, nameof(yaw)); Finite(distance, .1f, 1000f, nameof(distance));
        Finite(screenX, -.5f, .5f, nameof(screenX)); Finite(screenY, -.5f, .5f, nameof(screenY));
        Finite(dampingX, 0f, 20f, nameof(dampingX)); Finite(dampingY, 0f, 20f, nameof(dampingY)); Finite(dampingZ, 0f, 20f, nameof(dampingZ));
        Finite(lookaheadTime, 0f, 1f, nameof(lookaheadTime));
        Finite(deadZoneWidth, 0f, 2f, nameof(deadZoneWidth)); Finite(deadZoneHeight, 0f, 2f, nameof(deadZoneHeight));
        Finite(softZoneWidth, 0f, 2f, nameof(softZoneWidth)); Finite(softZoneHeight, 0f, 2f, nameof(softZoneHeight));
        Finite(backgroundR, 0f, 1f, nameof(backgroundR)); Finite(backgroundG, 0f, 1f, nameof(backgroundG)); Finite(backgroundB, 0f, 1f, nameof(backgroundB));
        Projection = projection; FieldOfView = fieldOfView; OrthographicSize = orthographicSize; Pitch = pitch; Yaw = yaw; Distance = distance; ScreenX = screenX; ScreenY = screenY;
        DampingX = dampingX; DampingY = dampingY; DampingZ = dampingZ; LookaheadTime = lookaheadTime; DeadZoneWidth = deadZoneWidth; DeadZoneHeight = deadZoneHeight;
        SoftZoneWidth = softZoneWidth; SoftZoneHeight = softZoneHeight; BackgroundR = backgroundR; BackgroundG = backgroundG; BackgroundB = backgroundB;
    }

    private static void Finite(float value, float min, float max, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max) throw new ArgumentOutOfRangeException(name);
    }

    public PNCameraView With(PNCameraViewLayer layer) => layer == null ? this : new PNCameraView(
        layer.Projection ?? Projection, layer.FieldOfView ?? FieldOfView, layer.OrthographicSize ?? OrthographicSize, layer.Pitch ?? Pitch, layer.Yaw ?? Yaw, layer.Distance ?? Distance,
        layer.ScreenX ?? ScreenX, layer.ScreenY ?? ScreenY, layer.DampingX ?? DampingX, layer.DampingY ?? DampingY, layer.DampingZ ?? DampingZ, layer.LookaheadTime ?? LookaheadTime,
        layer.DeadZoneWidth ?? DeadZoneWidth, layer.DeadZoneHeight ?? DeadZoneHeight, layer.SoftZoneWidth ?? SoftZoneWidth, layer.SoftZoneHeight ?? SoftZoneHeight,
        layer.BackgroundR ?? BackgroundR, layer.BackgroundG ?? BackgroundG, layer.BackgroundB ?? BackgroundB);

    // The same look (used to tell whether a re-resolve changed anything the rig must be told about).
    public bool SameAs(PNCameraView other) => other != null && Projection == other.Projection && FieldOfView == other.FieldOfView && OrthographicSize == other.OrthographicSize && Pitch == other.Pitch && Yaw == other.Yaw
        && Distance == other.Distance && ScreenX == other.ScreenX && ScreenY == other.ScreenY && DampingX == other.DampingX && DampingY == other.DampingY && DampingZ == other.DampingZ
        && LookaheadTime == other.LookaheadTime && DeadZoneWidth == other.DeadZoneWidth && DeadZoneHeight == other.DeadZoneHeight && SoftZoneWidth == other.SoftZoneWidth && SoftZoneHeight == other.SoftZoneHeight
        && BackgroundR == other.BackgroundR && BackgroundG == other.BackgroundG && BackgroundB == other.BackgroundB;
}

// Optional overrides of one source; unset fields keep the earlier layer.
public sealed class PNCameraViewLayer
{
    public PNCameraProjection? Projection;
    public float? FieldOfView, OrthographicSize, Pitch, Yaw, Distance, ScreenX, ScreenY, DampingX, DampingY, DampingZ, LookaheadTime, DeadZoneWidth, DeadZoneHeight, SoftZoneWidth, SoftZoneHeight, BackgroundR, BackgroundG, BackgroundB;
}

public enum PNCameraViewSource { Game = 0, Mode = 1, World = 2, Player = 3 }

// Applies Game -> Mode -> World -> Player; a layer that would leave the hard limits is refused and the previous layer stays.
public sealed class PNCameraViewStack
{
    private readonly PNCameraViewLayer[] layers = new PNCameraViewLayer[4];
    public PNCameraView Current { get; private set; } = PNCameraView.Default;
    public void Set(PNCameraViewSource source, PNCameraViewLayer layer)
    {
        int index = (int)source;
        if (index < 0 || index > 3) throw new ArgumentOutOfRangeException(nameof(source));
        var previous = layers[index]; layers[index] = layer;
        try { Current = Resolve(); }
        catch { layers[index] = previous; throw; }
    }
    public PNCameraViewLayer Get(PNCameraViewSource source) => layers[(int)source];
    public void Clear(PNCameraViewSource source) { layers[(int)source] = null; Current = Resolve(); }
    private PNCameraView Resolve()
    {
        var view = PNCameraView.Default;
        foreach (var layer in layers) view = view.With(layer);
        return view;
    }
}
