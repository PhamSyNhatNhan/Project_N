using System;

// EN: The player's choice for the ink marks of a puppet: baked once into the atlas (lightest) or computed live by the shader for the player and bosses, every character, or everything.
//     Stored in settings.json by id; the renderer reads it through PNVisualPaperSettings.Ink. Machine-local, not coop parity, not content.
// VI: Lựa chọn mực của con rối: nướng sẵn vào atlas (nhẹ nhất) hoặc shader tính trực tiếp cho người chơi và boss / mọi nhân vật / mọi thứ.
public enum PNInkMode
{
    Baked = 0,           // baked into the atlas at load (default)
    LiveKeyActors = 1,   // live ink for the player and bosses, baked for the rest
    LiveActors = 2,      // live ink for every character
    LiveAll = 3,         // live ink for every character and any other object that opts in (same as LiveActors until such objects exist)
}

public static class PNInkModes
{
    public const string BakedId = "baked", LiveKeyActorsId = "live_key_actors", LiveActorsId = "live_actors", LiveAllId = "live_all";
    public static string Id(PNInkMode mode)
    {
        switch (mode)
        {
            case PNInkMode.Baked: return BakedId; case PNInkMode.LiveKeyActors: return LiveKeyActorsId;
            case PNInkMode.LiveActors: return LiveActorsId; case PNInkMode.LiveAll: return LiveAllId;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
    // Strict: the exact lowercase id, nothing else.
    public static PNInkMode Parse(string id)
    {
        switch (id)
        {
            case BakedId: return PNInkMode.Baked; case LiveKeyActorsId: return PNInkMode.LiveKeyActors;
            case LiveActorsId: return PNInkMode.LiveActors; case LiveAllId: return PNInkMode.LiveAll;
            default: throw new FormatException("Invalid ink mode.");
        }
    }
}
