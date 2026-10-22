#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// One immutable, baked Recast profile. Runtime never rescans a world to repair a cache.
[CreateAssetMenu(menuName = "Project N/Navigation cache")]
public sealed class PNNavigationCache : ScriptableObject
{
    [SerializeField] private byte[] graphData;
    [SerializeField] private string mapHash, profileId, fingerprint, astarVersion;
    [SerializeField] private float radius, height;
    public string MapHash => mapHash;
    public string ProfileId => profileId;
    public string Fingerprint => fingerprint;
    public float Radius => radius;
    public float Height => height;
    public byte[] CopyGraphData() { Validate(); return (byte[])graphData.Clone(); }
    public void SetBakedData(byte[] data, string geometryHash, string profile, float agentRadius, float agentHeight)
    {
        if (data == null || data.Length == 0 || !HashValid(geometryHash) || string.IsNullOrWhiteSpace(profile))
            throw new ArgumentException("Baked data, geometry hash and agent profile required.");
        PNNavigationSettings.Positive(agentRadius, nameof(agentRadius));
        PNNavigationSettings.Positive(agentHeight, nameof(agentHeight));
        if (agentHeight < 2 * agentRadius) throw new ArgumentException("Agent height must contain its diameter.");
        graphData = (byte[])data.Clone(); mapHash = geometryHash; profileId = profile;
        radius = agentRadius; height = agentHeight; astarVersion = "5.3.3";
        fingerprint = ComputeFingerprint();
    }
    public void Validate()
    {
        if (graphData == null || graphData.Length == 0 || !HashValid(mapHash) || string.IsNullOrWhiteSpace(profileId) ||
            astarVersion != "5.3.3" || !HashValid(fingerprint)) throw new InvalidOperationException("Missing or incompatible navigation cache.");
        PNNavigationSettings.Positive(radius, nameof(radius)); PNNavigationSettings.Positive(height, nameof(height));
        if (height < radius * 2 || fingerprint != ComputeFingerprint()) throw new InvalidOperationException("Navigation cache fingerprint mismatch.");
    }
    private string ComputeFingerprint()
    {
        using var hash = SHA256.Create();
        var metadata = Encoding.UTF8.GetBytes(FormattableString.Invariant($"pn-nav:1/{astarVersion}/{mapHash}/{profileId}/{radius:R}/{height:R}/"));
        return Hex(hash.ComputeHash(metadata.Concat(graphData).ToArray()));
    }
    public static string Hash(string text)
    { using var hash = SHA256.Create(); return Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(text ?? throw new ArgumentNullException(nameof(text))))); }
    private static string Hex(byte[] data) => BitConverter.ToString(data).Replace("-", "").ToLowerInvariant();
    private static bool HashValid(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
}

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
