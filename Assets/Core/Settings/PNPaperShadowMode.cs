using System;

// EN: The player's choice for the shadow one part of a puppet casts on the parts under it (Visual, Assets/Core/Visual/PNVisualREADME.md). Stored in settings.json by id;
//     the renderer reads it through PNVisualPaperSettings.Mode. Machine-local, not coop parity, not content.
// VI: Lựa chọn của người chơi cho bóng giữa các mảnh của con rối: tắt / người chơi và boss / mọi nhân vật / nướng sẵn.
public enum PNPaperShadowMode
{
    Off = 0,          // no overlap shadow anywhere
    KeyActors = 1,    // dynamic shadow for the player and bosses only
    Actors = 2,       // dynamic shadow for every actor that is in view (small enemies too)
    All = 3,          // every actor plus any other dynamic object that opts in (same as Actors until such objects exist; not offered in the Settings view)
    Baked = 4,        // the shadow baked once at the rest pose into the receiving parts (no per-frame work; it does not follow the pose)
}

public static class PNPaperShadowModes
{
    public const string OffId = "off", KeyActorsId = "key_actors", ActorsId = "actors", AllId = "all", BakedId = "baked";
    public static string Id(PNPaperShadowMode mode)
    {
        switch (mode)
        {
            case PNPaperShadowMode.Off: return OffId; case PNPaperShadowMode.KeyActors: return KeyActorsId; case PNPaperShadowMode.Actors: return ActorsId;
            case PNPaperShadowMode.All: return AllId; case PNPaperShadowMode.Baked: return BakedId;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
    // Strict: the exact lowercase id, nothing else.
    public static PNPaperShadowMode Parse(string id)
    {
        switch (id)
        {
            case OffId: return PNPaperShadowMode.Off; case KeyActorsId: return PNPaperShadowMode.KeyActors; case ActorsId: return PNPaperShadowMode.Actors;
            case AllId: return PNPaperShadowMode.All; case BakedId: return PNPaperShadowMode.Baked;
            default: throw new FormatException("Invalid paper shadow mode.");
        }
    }
}
