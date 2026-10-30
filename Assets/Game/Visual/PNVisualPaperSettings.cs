#if UNITY_5_3_OR_NEWER
// EN: The player-facing setting for the overlap shadow between the parts of a puppet (clone trial). Baked-in rest-pose shadow is a planned mode, not built yet.
// VI: Cài đặt bóng chồng lớp giữa các mảnh của con rối: tắt / chỉ actor chính / mọi actor / tất cả.
public enum PNPaperShadowMode
{
    Off = 0,          // no overlap shadow anywhere
    KeyActors = 1,    // dynamic shadow for the player and bosses only
    Actors = 2,       // dynamic shadow for every actor that is in view (small enemies too)
    All = 3,          // every actor plus any other dynamic object that opts in (same as Actors until such objects exist)
    Baked = 4,        // the shadow baked once at the rest pose into the receiving parts (no per-frame work; it does not follow the pose)
}

public static class PNVisualPaperSettings
{
    private static PNPaperShadowMode mode = PNPaperShadowMode.Actors;
    // Bumped on every change; each actor compares it in Tick and re-selects its material (no events, no allocation).
    public static int Version { get; private set; }
    public static PNPaperShadowMode Mode { get => mode; set { if (mode == value) return; mode = value; Version++; } }

    // Bake the image-fixed ink marks into the visual atlas when it is built (default on). Read when the atlas is built: change it, then PNVisualRuntime.Release() to rebuild.
    public static bool BakeInk = true;

    // View margin: an actor is prepared (pose evaluated, mesh and casters written) while its bounds are inside the view plus this fraction of the view on each side,
    // and is released only once it is ExitExtra further out (hysteresis, so an actor on the edge does not flicker).
    public const float ViewMargin = .12f, ExitExtra = .05f;

    public static bool Wants(PNPaperShadowMode value, bool keyActor)
    {
        switch (value) { case PNPaperShadowMode.Off: case PNPaperShadowMode.Baked: return false; case PNPaperShadowMode.KeyActors: return keyActor; default: return true; }   // Baked is not a dynamic shadow
    }
}
#endif
