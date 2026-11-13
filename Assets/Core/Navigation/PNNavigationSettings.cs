#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

// The tuning of how an enemy follows a navigation route (profile, tolerances, repath and stuck handling). Kept when the baked Recast cache was removed (G14): the grid navigation uses it too.
[Serializable]
public sealed class PNNavigationSettings
{
    public string ProfileId = "ground-small";
    public float EndpointTolerance = .5f, RepathInterval = .2f, GoalThreshold = .35f;
    public float CornerTolerance = .12f, ArrivalEpsilon = .04f, CollisionSkin = .03f;
    public float StuckWindow = 1f, MinimumProgress = .05f;
    public int StuckRetries = 2;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProfileId)) throw new ArgumentException("Navigation profile required.");
        Positive(EndpointTolerance, nameof(EndpointTolerance)); Positive(RepathInterval, nameof(RepathInterval));
        Positive(GoalThreshold, nameof(GoalThreshold)); Positive(CornerTolerance, nameof(CornerTolerance));
        Positive(ArrivalEpsilon, nameof(ArrivalEpsilon)); Positive(CollisionSkin, nameof(CollisionSkin));
        Positive(StuckWindow, nameof(StuckWindow)); Positive(MinimumProgress, nameof(MinimumProgress));
        if (StuckRetries < 0 || StuckRetries > 100) throw new ArgumentOutOfRangeException(nameof(StuckRetries));
    }
    public static void Positive(float value, string name)
    { if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0) throw new ArgumentOutOfRangeException(name); }
    public static void Position(Vector3 value)
    { if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z)) throw new ArgumentException("Finite navigation goal required."); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal PNNavigationSettings Copy() => (PNNavigationSettings)MemberwiseClone();
}
#endif
