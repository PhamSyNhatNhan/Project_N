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

// EN: What every manager of spawned objects (projectile, blast, area) shares about hit reports: the authority's policy, the report sinks of this machine's players, and the evidence counters (V4a: one `IPNHitReportSink`, see PNHitReport).
//     One book per play (PNGameSession gives the same one to the three managers); a manager used alone makes its own.
// VI: Phan chung cua cac manager vat the (dan, blast, vung) ve lenh bao trung: chinh sach cua authority, cac sink cua nguoi choi may nay, bo dem bang chung. Moi phien mot book.
public sealed partial class PNHurtClaimBook
{
    public IPNHurtClaimPolicy Policy { get; set; }
    public int Accepted { get; private set; }
    public int Refused { get; private set; }
    public int Sent { get; private set; }
    // Why claims were refused: policy (not handed over), gone, again, rule (reach / the object's own rule).
    public readonly System.Collections.Generic.Dictionary<string, int> Refusals = new System.Collections.Generic.Dictionary<string, int>();
    // The hit-report sinks of this machine's players (one per player entity it shows; only the owner's answers `Claimant`). Every sender of a hit report (a projectile's guess, a blast, an area) finds its sink here.
    private readonly System.Collections.Generic.List<IPNHitReportSink> sinks = new System.Collections.Generic.List<IPNHitReportSink>();
    public void Register(IPNHitReportSink sink) { if (sink != null && !sinks.Contains(sink)) sinks.Add(sink); }
    public void Unregister(IPNHitReportSink sink) => sinks.Remove(sink);
    public IPNHitReportSink SinkFor(out PNStat claimant)
    {
        foreach (var sink in sinks) { claimant = sink.Claimant; if (claimant != null) return sink; }
        claimant = null; return null;
    }
    public void NoteSent() => Sent++;
    public void NoteAccepted() => Accepted++;
    public bool Refuse(string reason) { Refused++; Refusals[reason] = (Refusals.TryGetValue(reason, out var n) ? n : 0) + 1; return false; }
}

#if UNITY_5_3_OR_NEWER
// EN: What a claim ticket names: a blast object of the authority that detonated (V2), or one hit of a melee Action (V4b, cast key + Action ordinal + hit). One store and one rule for both (see PNHurtClaimBook tickets).
// VI: Ve noi ve cai gi: mot blast da no (V2) hoac mot nhat can chien cua Action (V4b). Mot kho va mot luat cho ca hai.
public readonly struct PNClaimTicketKey : System.IEquatable<PNClaimTicketKey>
{
    public readonly byte Kind; public readonly long A, B; public readonly int C;
    private PNClaimTicketKey(byte kind, long a, long b, int c) { Kind = kind; A = a; B = b; C = c; }
    // A blast may strike several times (IL2): each strike has its own ticket, named by the blast and the strike index.
    public static PNClaimTicketKey Blast(long objectId, int strike = 0) => new PNClaimTicketKey(1, objectId, 0L, strike);
    public static PNClaimTicketKey Melee(long castKey, long actionOrdinal, int hit) => new PNClaimTicketKey(2, castKey, actionOrdinal, hit);
    public bool Equals(PNClaimTicketKey other) => Kind == other.Kind && A == other.A && B == other.B && C == other.C;
    public override bool Equals(object other) => other is PNClaimTicketKey key && Equals(key);
    public override int GetHashCode() => unchecked(((Kind * 397 ^ A.GetHashCode()) * 397 ^ B.GetHashCode()) * 397 ^ C);
}

public sealed partial class PNHurtClaimBook
{
    // EN: The authority's hit already happened (a blast detonated, a melee sweep swung) while a player's own machine decides his hits: for PNHurtClaimLimits.ClaimGraceSeconds that machine may claim it, once per player.
    //     The ticket holds the hit's prepared damage (`Apply`), never the Action or the object, so it outlives them. Expired tickets are dropped every frame by the managers (PruneTickets) and on every open/accept.
    // VI: Ve: cu trung cua authority da xay ra; trong ClaimGraceSeconds may cua nguoi choi duoc bao mot lan. Ve giu sat thuong da chuan bi, khong giu Action hay vat, nen song qua chung.
    private sealed class Ticket { internal double Expires; internal System.Func<double> Clock; internal UnityEngine.Vector3 Center; internal float Radius, Height, Slack; internal System.Func<PNStat, bool> Apply; internal readonly System.Collections.Generic.HashSet<long> Claimed = new System.Collections.Generic.HashSet<long>(); }
    private readonly System.Collections.Generic.Dictionary<PNClaimTicketKey, Ticket> tickets = new System.Collections.Generic.Dictionary<PNClaimTicketKey, Ticket>();
    // The clock of tickets opened without one (melee); a manager passes its own (blast). Null = unscaled time.
    public System.Func<double> Clock { get; set; }
    public int TicketCount => tickets.Count;
    private static double Now(System.Func<double> clock) => clock != null ? clock() : UnityEngine.Time.unscaledTimeAsDouble;

    // `height` < 0: no vertical check. `slack` is added to the report's tolerance (a blast gives 1 m). `apply` deals the prepared damage and says whether it did.
    // `extraSeconds` lengthens the grace for a hit that has not happened yet when its ticket is opened (a contact door: the hit comes while the dash runs).
    public void OpenTicket(PNClaimTicketKey key, UnityEngine.Vector3 center, float radius, float height, float slack, System.Func<PNStat, bool> apply, System.Func<double> clock = null, float extraSeconds = 0f)
    {
        if (apply == null || Policy == null) return;
        PruneTickets();
        clock = clock ?? Clock;
        tickets[key] = new Ticket { Expires = Now(clock) + PNHurtClaimLimits.ClaimGraceSeconds + System.Math.Max(0f, extraSeconds), Clock = clock, Center = center, Radius = radius, Height = height, Slack = slack, Apply = apply };
    }
    public void PruneTickets()
    {
        if (tickets.Count == 0) return;
        System.Collections.Generic.List<PNClaimTicketKey> gone = null;
        foreach (var pair in tickets) if (Now(pair.Value.Clock) > pair.Value.Expires) (gone ??= new System.Collections.Generic.List<PNClaimTicketKey>()).Add(pair.Key);
        if (gone != null) foreach (var key in gone) tickets.Remove(key);
    }
    public void ClearTickets() => tickets.Clear();
    // The one rule of a ticket claim: the policy hands the player over, the ticket is there and in time, he has not claimed it, he can be hit, he is within reach (radius + tolerance + slack). Refusals: policy / gone / again / rule.
    public bool AcceptTicket(PNClaimTicketKey key, PNStat target, float tolerance)
    {
        if (target == null) return false;
        if (Policy == null || !Policy.ClaimsDecide(target)) return Refuse("policy");
        PruneTickets();
        if (!tickets.TryGetValue(key, out var ticket)) return Refuse("gone");
        if (!ticket.Claimed.Add(target.EntityId.Value)) return Refuse("again");
        if (!target.IsReady || PNTargetCandidates.IsDead(target) || !PNWorldGameplay.CanExecute(target)) return Refuse("rule");
        var d = target.transform.position - ticket.Center; float slack = System.Math.Max(0f, tolerance) + ticket.Slack;
        if (System.Math.Sqrt(d.x * d.x + d.z * d.z) > ticket.Radius + slack || ticket.Height >= 0f && System.Math.Abs(d.y) > ticket.Height + slack + 1f) return Refuse("rule");
        if (!ticket.Apply(target)) return Refuse("rule");
        PNReportOvershoot.Note(key.Kind == 1 ? "taken/blast" : "taken/melee", (float)System.Math.Sqrt(d.x * d.x + d.z * d.z) - ticket.Radius, target.EntityId.Value);
        NoteAccepted(); return true;
    }
}

// EN: The claim book of a play, found by its session (PNGameSession sets it on a networked play; offline there is none, so nobody claims). The melee hitbox service and the hit-report receiver find it here
//     instead of looking for a manager.
// VI: Book cua mot phien, tim theo session. Dich vu hitbox can chien va ben nhan bao trung tim no o day thay vi tim manager.
public static class PNHurtClaimBooks
{
    private static readonly System.Collections.Generic.Dictionary<System.Guid, PNHurtClaimBook> books = new System.Collections.Generic.Dictionary<System.Guid, PNHurtClaimBook>();
    public static void Set(EveSessionContext session, PNHurtClaimBook book)
    {
        if (session == null) throw new System.ArgumentNullException(nameof(session));
        if (book == null) throw new System.ArgumentNullException(nameof(book));
        books[session.SessionId] = book;
    }
    public static void Remove(EveSessionContext session) { if (session != null) books.Remove(session.SessionId); }
    public static PNHurtClaimBook Find(EveSessionContext session) => session != null && books.TryGetValue(session.SessionId, out var book) ? book : null;
}
#endif
