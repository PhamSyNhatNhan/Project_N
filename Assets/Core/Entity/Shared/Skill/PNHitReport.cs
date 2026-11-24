using System;
using System.Collections.Generic;
using EveEngine;

// EN: ONE kind of hit report (Docs/SpawnedObjectSyncPlan.md 4.4d, V4a). A machine that decides a hit tells the authority, which checks and applies it. There are two directions and two ways to name what hit:
//     - Role: Dealt = the reporter's own cast hit these entities (the player's skill or projectile guess); Taken = an object of the authority hit the reporter's own player.
//     - Subject: by cast (the cast key and a step: an Action's hit step, or ReportStepBase + the index of a projectile in the volley) or by object (the kind and sync id of a projectile / blast / area, plus a detail:
//       an area's "inside" flag). Today Dealt is named by cast and Taken by object; the shape allows both for later melee claims (V4b).
//     The wire (`project_n:hit_report`), the sender (`IPNHitReportSink`) and the receivers (`IPNHitReportReceiver`) are the same for all of them.
// VI: MOT loai bao trung (4.4d, V4a). May quyet cu trung bao cho authority, authority kiem roi ap. Hai chieu: Dealt (cast cua chinh nguoi bao trung ai do) va Taken (vat cua authority trung nguoi choi cua may bao);
//     hai cach goi ten: theo cast (cast key + step) hoac theo vat (loai + id dong bo + chi tiet, vd. "dang trong vung"). Mot lenh, mot sink gui, mot giao dien nhan.
public enum PNHitReportRole : byte
{
    // The reporter's machine ran the cast and saw it hit the named entities.
    Dealt = 0,
    // An object of the authority hit the reporter's own player, on the reporter's machine.
    Taken = 1,
}

public readonly struct PNHitReport
{
    public PNHitReportRole Role { get; }
    // Subject by cast: the cast key (> 0) and the step inside it.
    public long CastKey { get; }
    public int Step { get; }
    // Subject by object: the kind and sync id (> 0), and a detail (an area: 1 = inside, 0 = outside; a projectile: the number of the hit).
    public PNObjectKind Kind { get; }
    public long ObjectId { get; }
    public int Detail { get; }
    public bool ByCast => CastKey > 0;

    private PNHitReport(PNHitReportRole role, long castKey, int step, PNObjectKind kind, long objectId, int detail)
    { Role = role; CastKey = castKey; Step = step; Kind = kind; ObjectId = objectId; Detail = detail; }

    // The reporter's cast `castKey` hit something at `step`.
    public static PNHitReport DealtByCast(long castKey, int step) => new PNHitReport(PNHitReportRole.Dealt, castKey, step, 0, 0L, 0);
    // The object `objectId` of `kind` hit the reporter's player; `detail` is the area's "inside" flag or the projectile's hit number.
    public static PNHitReport TakenFromObject(PNObjectKind kind, long objectId, int detail = 0) => new PNHitReport(PNHitReportRole.Taken, 0L, 0, kind, objectId, detail);
    // V4b: the cast `castKey` of an authority Action hit the reporter's own player at the Action step `step` (its ordinal in the cast), in hit `hit` of that Action. Melee has no synced object to name, so it is named by cast.
    public static PNHitReport TakenByCast(long castKey, int step, int hit) => new PNHitReport(PNHitReportRole.Taken, castKey, step, 0, 0L, hit);
    // Builds a report from its wire parts; `IsValid` says whether the parts make one.
    public static PNHitReport FromWire(PNHitReportRole role, long castKey, int step, int kind, long objectId, int detail)
        => new PNHitReport(role, castKey, step, (PNObjectKind)kind, objectId, detail);

    // Whether the parts make a report: Dealt names a cast, Taken names an object of a known kind with a detail the claims allow.
    public bool IsValid(int maxStep)
    {
        switch (Role)
        {
            case PNHitReportRole.Dealt: return CastKey > 0 && Step >= 0 && Step <= maxStep;
            case PNHitReportRole.Taken:
                if (Detail < 0 || Detail > PNHurtClaimLimits.MaxHit) return false;
                // Taken is named by an object (kind + id) or, for a melee hit of an Action, by cast (key + step, the hit in Detail): never both.
                return CastKey > 0 ? ObjectId == 0 && Step >= 0 && Step <= maxStep : ObjectId > 0 && Enum.IsDefined(typeof(PNObjectKind), Kind);
            default: return false;
        }
    }
}

// Whoever can take a hit report on the authority: a running melee hitbox, and the projectile, blast and area managers. Each answers only for the reports that name something it owns (false otherwise).
public interface IPNHitReportReceiver
{
    // True when the report was applied. `victim` is the entity that was hit; `tolerance` is the slack (metres) the report gets over the object's own reach, for the time it took to arrive.
    bool AcceptHitReport(in PNHitReport report, PNStat victim, float tolerance);
}

// Where a machine's own player sends hit reports: the component that owns the connection of that player. Only the owner's machine answers `Claimant`.
public interface IPNHitReportSink
{
    // The player of this machine when this component is the one that owns him, else null: the only entity a copy decides (Taken), and whose casts report (Dealt).
    PNStat Claimant { get; }
    // Sends the report. `victims` names who was hit for a Dealt report (the reporter's cast hit them); a Taken report names only the reporter's own player, so it passes null. False when it could not go out.
    bool SendHitReport(in PNHitReport report, IReadOnlyList<EveEntityId> victims);
}
