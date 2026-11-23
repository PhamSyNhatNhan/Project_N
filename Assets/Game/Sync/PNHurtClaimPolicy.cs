using System;
using EveEngine;

// The authority's answer to "does this player's own machine decide the hits on him" (Docs/SpawnedObjectSyncPlan.md 4.4c): true for a player owned by another connection while that connection is alive.
// `SilentSeconds` is the transport's answer (seconds since the owner's machine last sent anything, negative when the owner is not a remote connection: the host's own player, offline, unknown).
public sealed class PNHurtClaimPolicy : IPNHurtClaimPolicy
{
    public Func<long, double> SilentSeconds { get; set; }
    public bool ClaimsDecide(PNStat target)
    {
        var silent = SilentSeconds; if (silent == null || target == null) return false;
        var loader = target.GetComponent<EveEntityLoader>(); if (loader == null || loader.LoadContext == null) return false;
        long owner = loader.LoadContext.Identity.OwnerId; if (owner == 0) return false;
        double seconds = silent(owner);
        return seconds >= 0d && seconds < PNHurtClaimLimits.SilenceSeconds;
    }
}
