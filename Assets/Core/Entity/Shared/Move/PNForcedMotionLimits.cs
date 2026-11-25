// EN: The ranges every forced motion command stays inside (Docs/BodyMotionPlan.md 3.3): content validation, the host's decision, the wire state and the layer all read the same numbers.
// VI: Cac gioi han chung cua lenh chuyen dong bi ep: noi dung, quyet dinh cua host, state mang va lop chuyen dong cung doc mot bo so.
public static class PNForcedMotionLimits
{
    public const int MaxCommands = 16;
    public const float MaxDuration = 10f, MaxDistance = 64f, MaxHeight = 16f;
}
