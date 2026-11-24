#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: The "door" of a melee hit (Docs/SpawnedObjectSyncPlan.md 4.4d, V4b). The authority's hitbox service publishes it as one fact of the run (key `door`) when the sweep has a player whose own machine decides his
//     hits within reach. The machine of that player evaluates the shape against his own body and sends a Taken-by-cast hit report. The Action author writes nothing for it: the hitbox service does both ends.
//     Values (<= PNActionFact.MaxValues): [kind*100 + hit, originX, originY, originZ, yawDegrees, p1, p2, p3]. Sector: p = radius, thickness, angle. Cylinder: p = radius, height.
// VI: "Cua trung" cua mot nhat can chien. Dich vu hitbox cua authority phat no nhu mot fact cua run khi nhat quet co nguoi choi do MAY CUA ANH quyet trong tam. May do xet hinh voi than minh roi gui bao Taken theo cast.
public readonly struct PNMeleeDoor
{
    public const string Key = "door";
    // Contact (V4c): a sphere that follows the body of the caster while it dashes. Values: [200, startX, startY, startZ, interval, radius, windowStart, windowEnd] (the window is in seconds after the door's own age);
    // `Yaw` holds the repeat interval and A/B/C the radius and the window. At most MaxContactHits hits (interval 0: one hit per player).
    public const int Sector = 0, Cylinder = 1, Contact = 2;
    public const float MaxWindow = 60f;
    public readonly int Kind, Hit;
    public readonly Vector3 Origin;
    public readonly float Yaw, A, B, C;

    private PNMeleeDoor(int kind, int hit, Vector3 origin, float yaw, float a, float b, float c) { Kind = kind; Hit = hit; Origin = origin; Yaw = yaw; A = a; B = b; C = c; }

    // The hits one player can claim from a contact door: one for a door with no repeat, else one per interval over the window.
    public static int ContactHits(float windowStart, float windowEnd, float interval)
        => interval > 0f ? (int)Mathf.Floor((windowEnd - windowStart) / interval) + 1 : 1;
    public float Interval => Kind == Contact ? Yaw : 0f;
    public int Hits => Kind == Contact ? ContactHits(B, C, Yaw) : 1;

    public static bool TryContact(Vector3 start, float interval, float radius, float windowStart, float windowEnd, out PNMeleeDoor door)
    {
        door = default;
        if (float.IsNaN(interval) || float.IsNaN(radius) || float.IsNaN(windowStart) || float.IsNaN(windowEnd) || interval < 0f || radius <= 0f || radius > 64f
            || windowStart < 0f || windowEnd < windowStart || windowEnd > MaxWindow || interval > MaxWindow || ContactHits(windowStart, windowEnd, interval) > PNHurtClaimLimits.MaxHit + 1) return false;
        door = new PNMeleeDoor(Contact, 0, start, interval, radius, windowStart, windowEnd);
        return true;
    }

    // The shapes a door can carry; a hitbox of another shape keeps the host's own sweep (way A).
    public static bool Supports(EveHitboxShape shape) => shape != null && (shape.Kind == EveHitboxKind.Sector && shape.InnerRadius == 0f || shape.Kind == EveHitboxKind.Cylinder);

    public static bool TryFrom(EveHitboxShape shape, EveHitboxPose pose, int hit, out PNMeleeDoor door)
    {
        door = default;
        if (!Supports(shape) || hit < 0 || hit > PNHurtClaimLimits.MaxHit) return false;
        var forward = pose.Rotation * Vector3.right; forward.y = 0f;
        float yaw = forward.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(forward.z, forward.x) * Mathf.Rad2Deg;
        door = shape.Kind == EveHitboxKind.Sector
            ? new PNMeleeDoor(Sector, hit, pose.Position, yaw, shape.Radius, shape.Height, shape.Angle)
            : new PNMeleeDoor(Cylinder, hit, pose.Position, yaw, shape.Radius, shape.Height, 0f);
        return true;
    }

    public double[] ToValues() => new double[] { Kind * 100 + Hit, Origin.x, Origin.y, Origin.z, Yaw, A, B, C };
    public bool IsContact => Kind == Contact;
    public float Radius => A;
    public float WindowStart => B;
    public float WindowEnd => C;

    // Reads the fact's numbers; false for anything that is not a door the shape factories accept (a malformed fact is dropped, never trusted).
    public static bool TryRead(double[] v, out PNMeleeDoor door)
    {
        door = default;
        if (v == null || v.Length != 8) return false;
        foreach (var x in v) if (double.IsNaN(x) || double.IsInfinity(x)) return false;
        int packed = (int)v[0]; int kind = packed / 100, hit = packed % 100;
        if (packed != v[0] || packed < 0 || kind > Contact || hit > PNHurtClaimLimits.MaxHit) return false;
        if (kind == Contact) return hit == 0 && TryContact(new Vector3((float)v[1], (float)v[2], (float)v[3]), (float)v[4], (float)v[5], (float)v[6], (float)v[7], out door);
        if (v[5] <= 0 || v[5] > 64 || v[6] <= 0 || v[6] > 64) return false;
        if (kind == Sector && (v[7] <= 0 || v[7] > 360)) return false;
        door = new PNMeleeDoor(kind, hit, new Vector3((float)v[1], (float)v[2], (float)v[3]), (float)v[4], (float)v[5], (float)v[6], (float)v[7]);
        return true;
    }

    public EveHitboxShape BuildShape() => Kind == Contact ? EveHitboxShape.Sphere(A) : Kind == Sector ? EveHitboxShape.Sector(A, B, C) : EveHitboxShape.Cylinder(A, B);
    public EveHitboxPose Pose()
    {
        float rad = Yaw * Mathf.Deg2Rad;
        return EveHitboxPose.Planar(Origin, new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)));
    }
    // The planar reach of the shape: what the authority checks a claim against (plus the slack).
    public float Reach => A;
}

// EN: The machine of the player being hit: takes the doors of the runs it watches and answers with a Taken-by-cast report when ITS copy of the player's body is inside the shape. Called by the Observed view driver
//     (PNSkill) at the age the door was published, so the check lands where the view draws the swing. One report per (run, hit).
// VI: May cua nguoi bi danh: nhan cua cua cac run dang xem, tra loi bang bao Taken theo cast khi than nguoi choi (ban sao cua may nay) nam trong hinh. Moi (run, nhat) mot bao.
public static class PNMeleeDoorClaims
{
    private static readonly HashSet<(long, long, int)> sent = new HashSet<(long, long, int)>();
    private static readonly List<PNStat> found = new List<PNStat>();
    private static readonly PNHitboxTargets targets = new PNHitboxTargets();

    // Returns true when a report was sent (evidence for the checks).
    internal static bool Offer(PNSkill skill, PNActionRunKey run, double[] values)
    {
        if (skill == null || !skill.IsReady || !EveRegistrySession.IsReady || !PNMeleeDoor.TryRead(values, out var door) || door.IsContact) return false;
        var self = skill.GetComponent<PNStat>(); if (self == null || !self.IsReady) return false;
        if (!Claimant(out var book, out var sink, out var claimant)) return false;
        if (sent.Contains((run.Cast, run.Ordinal, door.Hit))) return false;
        bool inside;
        try
        {
            using (var query = new EveHitboxQuery(door.BuildShape(), claimant.gameObject.scene.GetPhysicsScene()))
            {
                targets.Query(query, door.Pose(), found, self, ~0, QueryTriggerInteraction.Ignore, x => ReferenceEquals(x, claimant));
                inside = found.Count != 0;
            }
        }
        catch (ArgumentException) { return false; }
        finally { found.Clear(); }
        return inside && Send(skill, run, door.Hit, book, sink, claimant);
    }

    // The sink of this machine's own player comes from the play's claim book (none offline: nobody claims).
    private static bool Claimant(out PNHurtClaimBook book, out IPNHitReportSink sink, out PNStat claimant)
    {
        sink = null; claimant = null;
        book = EveRegistrySession.IsReady ? PNHurtClaimBooks.Find(EveRegistrySession.Context) : null;
        if (book == null) return false;
        sink = book.SinkFor(out claimant);
        return sink != null && claimant != null && claimant.IsReady && !claimant.IsDead;
    }

    // One report per (run, hit): the early phase and the copy's own impact come before the report (it may be answered at once on a loopback), the same order a guessed hit uses.
    private static bool Send(PNSkill skill, PNActionRunKey run, int hit, PNHurtClaimBook book, IPNHitReportSink sink, PNStat claimant)
    {
        if (sent.Count > 512) sent.Clear();
        if (!sent.Add((run.Cast, run.Ordinal, hit))) return false;
        var at = claimant.transform.position;
        PNCombatEvents.RaiseEarly(claimant.EntityId.Value, PNActionImpact.Key(run.Cast, run.Ordinal, hit, claimant.EntityId.Value), true, at.x, at.y, at.z);
        if (skill.HasActionImpactListeners) skill.RaiseActionImpact(PNActionImpactOrigin.Guess, run.Cast, run.Ordinal, hit, claimant.EntityId.Value, claimant, at);
        if (!sink.SendHitReport(PNHitReport.TakenByCast(run.Cast, (int)run.Ordinal, hit), null)) return false;
        SentTotal++;
        book.NoteSent();
        return true;
    }

    // EN: A contact door (V4c) does not decide once at its age: it is watched while the run's age is inside the window. The watch belongs to the observed run (the view item) and is dropped with it. Each frame the body the
    //     machine draws for the monster (its own copy, where the player sees it) is tested against this machine's player; a touch sends hit n, the next only after the interval.
    // VI: Cua chạm thân không xét một lần: no duoc canh trong cua so tuoi cua run. Moi khung xet than ban sao quai (cho nguoi choi nhin thay) voi nguoi choi cua may nay; cham thi gui nhat n, nhat sau chi sau khoang lap.
    internal sealed class ContactWatch
    {
        private readonly PNMeleeDoor door; private readonly double begin, end; private double next; private int hit; internal bool Done { get; private set; }
        internal int Hits => hit;
        internal ContactWatch(PNMeleeDoor door, double doorAge) { this.door = door; begin = doorAge + door.WindowStart; end = doorAge + door.WindowEnd; next = begin; }

        // Returns true when this frame sent a report.
        internal bool Tick(PNSkill skill, PNActionRunKey run, double age)
        {
            if (Done || age < begin) return false;
            if (age > end + 1e-9) { Done = true; return false; }
            if (age < next || hit >= door.Hits || skill == null || !skill.IsReady || !Claimant(out var book, out var sink, out var claimant)) return false;
            // The body is the copy of the monster on this machine, as the player sees it; the height matches the authority's sphere (a hand above the feet).
            var self = skill.GetComponent<PNStat>(); if (self == null || !self.IsReady) return false;
            bool inside;
            try
            {
                using (var query = new EveHitboxQuery(door.BuildShape(), claimant.gameObject.scene.GetPhysicsScene()))
                {
                    targets.Query(query, EveHitboxPose.Planar(skill.transform.position + Vector3.up * .7f, Vector3.right), found, self, ~0, QueryTriggerInteraction.Ignore, x => ReferenceEquals(x, claimant));
                    inside = found.Count != 0;
                }
            }
            catch (ArgumentException) { return false; }
            finally { found.Clear(); }
            if (!inside || !Send(skill, run, hit, book, sink, claimant)) return false;
            ContactSentTotal++;
            hit++; next = age + door.Interval;
            if (door.Interval <= 0f) Done = true;
            return true;
        }
    }

    // The watch of a contact door, or null when the values are not a contact door.
    internal static ContactWatch Watch(double[] values, double doorAge) => PNMeleeDoor.TryRead(values, out var door) && door.IsContact ? new ContactWatch(door, doorAge) : null;

    // For the checks: a machine that starts a new play forgets what it sent.
    // The melee claims this machine sent since it started (evidence for the Player checks; Reset does not clear it).
    public static int SentTotal;
    // Of those, the claims a contact door (a lunge) caused.
    public static int ContactSentTotal;
    internal static void Reset() => sent.Clear();
}
#endif
