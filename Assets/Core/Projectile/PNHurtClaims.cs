using EveEngine;

// EN: Who decides whether an enemy's attack hits a player (Docs/SpawnedObjectSyncPlan.md 4.4c, way B). A player on another machine than the authority is hit by what HIS machine sees: its copy of the
//     object tests his body and sends a claim; the authority checks it and applies the damage. The authority stops testing that player itself while the claim path is alive, and takes the decision back
//     (way A) once his machine has been silent. Nothing here names a kind of object: projectiles are first, blasts / areas / Actions follow.
// VI: Ai quyet don cua quai co trung nguoi choi khong. Nguoi choi o may khac authority bi trung theo cai MAY CUA ANH THAY: ban hinh o may do xet va cham va gui lenh bao, authority kiem roi ap sat thuong.
//     Authority thoi tu xet nguoi choi do khi duong bao con song, va lay lai quyet dinh (cach A) khi may do im lang.
public interface IPNHurtClaimPolicy
{
    // True while the machine of this player decides the hits on him: he is a player owned by another connection and that connection is alive. Called on the authority.
    bool ClaimsDecide(PNStat target);
}

public interface IPNHurtClaimSink
{
    // The player of this machine when this component is the one that owns him, else null: the only entity a copy decides.
    PNStat Claimant { get; }
    // Sends the claim "object `objectId` hit me as its hit number `hit`". False when it could not go out.
    bool SendClaim(PNObjectKind kind, long objectId, int hit);
}

public static class PNHurtClaimLimits
{
    // Seconds without any message from a player's machine after which the authority decides his hits again; the claims a machine may send for one object are bounded by the targets it can hit.
    public const double SilenceSeconds = 1d;
    public const int MaxHit = 63;
    // How long after a blast detonates on the authority a claim for it is still taken (the claim travels a trip behind the copy's own detonation).
    public const double ClaimGraceSeconds = 0.5d;
    // A claimed hit is named by who was hit, not by a count: both machines know the victim, while the authority's running hit count also includes the hits the claimer never sees. Index = ClaimKeyBase + low 20 bits of his entity id (the hit index of an ordinary hit stays far below).
    public const int ClaimKeyBase = 1 << 20;
    public static int ClaimKeyIndex(long entityId) => ClaimKeyBase + (int)(entityId & 0xFFFFF);
}

// EN: What every manager of spawned objects (projectile, blast, area) shares about hit claims: the authority's policy, the claim sinks of this machine's players, and the evidence counters.
//     One book per play (PNGameSession gives the same one to the three managers); a manager used alone makes its own.
// VI: Phan chung cua cac manager vat the (dan, blast, vung) ve lenh bao trung: chinh sach cua authority, cac sink cua nguoi choi may nay, bo dem bang chung. Moi phien mot book.
public sealed class PNHurtClaimBook
{
    public IPNHurtClaimPolicy Policy { get; set; }
    public int Accepted { get; private set; }
    public int Refused { get; private set; }
    public int Sent { get; private set; }
    // Why claims were refused: policy (not handed over), gone, again, rule (reach / the object's own rule).
    public readonly System.Collections.Generic.Dictionary<string, int> Refusals = new System.Collections.Generic.Dictionary<string, int>();
    private readonly System.Collections.Generic.List<IPNHurtClaimSink> sinks = new System.Collections.Generic.List<IPNHurtClaimSink>();
    public void Register(IPNHurtClaimSink sink) { if (sink != null && !sinks.Contains(sink)) sinks.Add(sink); }
    public void Unregister(IPNHurtClaimSink sink) => sinks.Remove(sink);
    public IPNHurtClaimSink SinkFor(out PNStat claimant)
    {
        foreach (var sink in sinks) { claimant = sink.Claimant; if (claimant != null) return sink; }
        claimant = null; return null;
    }
    public void NoteSent() => Sent++;
    public void NoteAccepted() => Accepted++;
    public bool Refuse(string reason) { Refused++; Refusals[reason] = (Refusals.TryGetValue(reason, out var n) ? n : 0) + 1; return false; }
}
