#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json;

// Content-owned immutable selection. The factory creates a fresh policy for each play.
public sealed class PNModeDefinition : EveIIdentified
{
    public EveId Id { get; }
    public string Implementation => ImplementationType.FullName;
    [JsonIgnore] public Type ImplementationType { get; }
    private readonly Func<PNNetworkProfile, IPNSessionPolicy> factory;
    // Menu presentation only (null = not listed, like the Hall); it never takes part in selection or compatibility.
    [JsonIgnore] public PNModeListing Listing { get; }
    public PNModeDefinition(EveId id, Type implementationType, Func<PNNetworkProfile, IPNSessionPolicy> factory, PNModeListing listing)
        : this(id, implementationType, factory) { Listing = listing; }
    public PNModeDefinition(EveId id, Type implementationType, Func<PNNetworkProfile, IPNSessionPolicy> factory)
    {
        if (!id.IsValid || implementationType == null || implementationType.IsAbstract || implementationType.ContainsGenericParameters || !typeof(IPNSessionPolicy).IsAssignableFrom(implementationType))
            throw new ArgumentException("Invalid mode definition.");
        Id = id; ImplementationType = implementationType; this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }
    public IPNSessionPolicy Create(PNNetworkProfile profile)
    {
        profile.Validate();
        var result = factory(profile.Snapshot());
        if (result == null || !ImplementationType.IsInstanceOfType(result) || string.IsNullOrWhiteSpace(result.ModeId) || result.ModeId.Length>128 || result.MaximumClients < 1 ||
            result.MaximumClients > profile.MaximumClients || double.IsNaN(result.GraceSeconds) || result.GraceSeconds <= 0 ||
            result.GraceSeconds > profile.GraceSeconds) throw new InvalidOperationException("Invalid selected mode policy.");
        return result;
    }
    public static EveId Selection(string mode)
    {
        if(string.IsNullOrWhiteSpace(mode)||mode.Length>128)throw new ArgumentException("Mode ID is missing or oversized.");
        return EveId.Parse(mode.Contains(":") ? mode : "base:" + mode);
    }
    public static IPNSessionPolicy Select(EveRegistryHub registry, string mode, PNNetworkProfile profile)
        => registry.Get<PNModeDefinition>().Get(Selection(mode)).Create(profile);
}
#endif
