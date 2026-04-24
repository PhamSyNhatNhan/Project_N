public class TimeScaleChangedData
{
    public float MovementScale;
    public float AnimationScale;
}

public class SlowData
{
    public string         Key;
    public float          Scale;
    public SlowAffectType AffectType;

    public static SlowData Create(string key, float scale, SlowAffectType type = SlowAffectType.Both)
        => new() { Key = key, Scale = scale, AffectType = type };

    public static SlowData Remove(string key, SlowAffectType type = SlowAffectType.Both)
        => new() { Key = key, AffectType = type };
}