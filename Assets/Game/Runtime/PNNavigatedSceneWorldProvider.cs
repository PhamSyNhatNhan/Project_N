#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Linq;
using EveEngine;

// A content module can declare an authored scene + its exact navigation fingerprint together.
public sealed class PNNavigatedSceneWorldProvider : IPNWorldProvider
{
    private readonly string[] scenes;
    private readonly string fingerprint;
    public PNNavigatedSceneWorldProvider(string[] scenes, string fingerprint)
    { this.scenes = (string[])scenes.Clone(); this.fingerprint = fingerprint; }
    public static PNWorldDefinition Definition(EveId id, string[] scenes, string navigationFingerprint)
    {
        if (scenes == null || scenes.Length == 0 || scenes.Any(string.IsNullOrWhiteSpace) || string.IsNullOrWhiteSpace(navigationFingerprint)) throw new ArgumentException("Scene paths and navigation fingerprint required.");
        var selected = (string[])scenes.Clone();
        return new PNWorldDefinition(id, PNNavigationCache.Hash("pn-navigated-scene:1/" + string.Join("|", selected) + "/" + navigationFingerprint),
            typeof(PNNavigatedSceneWorldProvider), () => new PNNavigatedSceneWorldProvider(selected, navigationFingerprint));
    }
    public IEnumerator Load(PNWorldBuildContext context)
    {
        var job = new PNSceneWorldProvider(scenes).Load(context);
        try { while (job.MoveNext()) yield return job.Current; }
        finally { (job as IDisposable)?.Dispose(); }
        var sources = context.Slots.SelectMany(x => x.Roots).SelectMany(x => x.GetComponentsInChildren<PNWorldNavigationSource>(true)).ToArray();
        if (sources.Length != 1 || sources[0].ExpectedFingerprint != fingerprint) throw new InvalidOperationException("Selected world and scene navigation declarations differ.");
        sources[0].Validate();
    }
}
#endif
