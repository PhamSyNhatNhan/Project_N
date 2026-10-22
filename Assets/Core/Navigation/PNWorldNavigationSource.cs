#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

// Optional map declaration. No source => the world's existing load behavior is unchanged.
[DisallowMultipleComponent]
public sealed class PNWorldNavigationSource : MonoBehaviour
{
    [SerializeField] private PNNavigationCache cache;
    [SerializeField] private string expectedFingerprint;
    [SerializeField] private GameObject[] geometryRoots = Array.Empty<GameObject>();
    public PNNavigationCache Cache => cache;
    public string ExpectedFingerprint => expectedFingerprint;
    public GameObject[] GeometryRoots => (GameObject[])geometryRoots.Clone();
    public void SetGeometryRoots(params GameObject[] roots)
    { geometryRoots = roots == null ? throw new ArgumentNullException(nameof(roots)) : (GameObject[])roots.Clone(); }
    public void Configure(PNNavigationCache value, string expected)
    { cache = value; expectedFingerprint = expected; Validate(); }
    public void Validate()
    {
        if (cache == null) throw new InvalidOperationException("World requires a baked navigation cache.");
        cache.Validate();
        if (cache.Fingerprint != expectedFingerprint) throw new InvalidOperationException("Map navigation fingerprint is stale. Bake and update its content declaration.");
    }
}
#endif
