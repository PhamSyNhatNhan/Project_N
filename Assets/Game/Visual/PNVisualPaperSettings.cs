#if UNITY_5_3_OR_NEWER
// EN: Run-time state of the Visual look. `Mode` is the player's overlap-shadow choice (PNPaperShadowMode, Assets/Core/Settings); PNSettingsService writes it from the saved
//     setting at start-up and on every change. The enum itself lives in Core because the settings file stores it.
// VI: Trạng thái chạy của diện mạo Visual; `Mode` do PNSettingsService đặt từ cài đặt đã lưu.
public static class PNVisualPaperSettings
{
    private static PNPaperShadowMode mode = PNPaperShadowMode.Actors;
    // Bumped on every change; each actor compares it in Tick and re-selects its material (no events, no allocation).
    public static int Version { get; private set; }
    public static PNPaperShadowMode Mode { get => mode; set { if (mode == value) return; mode = value; Version++; } }

    private static PNInkMode ink = PNInkMode.Baked;
    // The player's ink choice (PNInkMode, Assets/Core/Settings). Read when the atlas set is built (PNVisualRuntime.Rebuild); it also bumps Version.
    public static PNInkMode Ink { get => ink; set { if (ink == value) return; ink = value; Version++; } }
    // True when an actor of this kind draws its ink live (raw atlas, shader computes the marks).
    public static bool IsLive(bool keyActor) { switch (ink) { case PNInkMode.Baked: return false; case PNInkMode.LiveKeyActors: return keyActor; default: return true; } }

    // True when an actor of this kind uses the raw atlas (live ink): `BakeInk` false (the old switch) means every actor.
    public static bool UsesRaw(bool keyActor) => !BakeInk || IsLive(keyActor);

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
