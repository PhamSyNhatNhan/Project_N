#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// One melee shape and hit history, shared by local sweeps and accepted owner reports.
internal sealed class PNSkillMeleeHitbox : IPNHitReportReceiver, IDisposable
{
    private readonly PNSkillActionContext context;
    private readonly PNStat caster;
    private readonly PNSkillDamageCapture damage;
    private readonly List<PNStat> targets = new List<PNStat>();
    private PNHitboxCast cast;
    private IDisposable reportLease;
    private Action<Vector3, PNHitOutcome> onContact;
    private Vector3 reportOrigin;
    private float reportRadius;
    private int extraLayers;
    private bool disposed;
    private readonly int hitIndex;
    private Predicate<PNStat> targetFilter;
    // V4b: a player whose own machine decides his hits is not swept here; his machine gets a "door" and a ticket (PNMeleeDoor, a ticket in the play's PNHurtClaimBook).
    private EveHitboxShape shape;
    private PNHurtClaimBook claimBook;
    private PNHitboxCast reachCast;
    private readonly List<PNStat> reach = new List<PNStat>();
    private bool doorCapable, doorOpened;
    // V4c: a repeating hitbox counts the hits on each target, so the n-th touch is hit n for the host's own sweep and for a claim alike.
    private bool repeating;
    private readonly Dictionary<EveEntityId, int> touches = new Dictionary<EveEntityId, int>();
    public double Scale => damage.Scale;
    // The size factor read once when this hitbox was built; a positive length never collapses to zero (same rule as PNHitCapture.Scale).
    public float ScaleLength(float length) => PNHitCapture.ScaleLength(length, damage.Scale);
    public PNDamage Damage { get; private set; }
    public double ReportTime { get; set; }
    public bool CanSample => cast != null;

    // `hitIndex` names this hitbox among the Action's hits (an Action with two sweeps gives them 0 and 1, so once-per-hit effects count each sweep).
    public PNSkillMeleeHitbox(PNSkillActionContext context, PNStat caster, PNSkillValues values, double? iframe, bool canCrit = true, int hitIndex = 0, IEnumerable<EveId> extraTags = null)
    {
        this.context = context;
        this.caster = caster;
        this.hitIndex = hitIndex;
        damage = context.PrepareMeleeDamage(values, iframe,
            () => context.Skill.GetComponent<PNSkillScaleState>()?.ReadScale(PNContentIds.MeleeScale) ?? 1, canCrit, extraTags);
    }

    // `targetFilter` adds a live rule on top of the caster's target layers (for example the world rule an enemy follows); null keeps the layers alone.
    // `repeat` (seconds, V4c) lets the same target be hit again after that interval (a body that keeps touching); null = once per target. A repeating hitbox names each hit of a target by its count (0, 1, 2 ...).
    public void Open(EveHitboxShape shape, Vector3 origin, float radius, int layers, Action<Vector3, PNHitOutcome> contact, Predicate<PNStat> targetFilter = null, double? repeat = null)
    {
        if (context.Mode == PNSkillMode.Observed) return;
        reportOrigin = origin;
        reportRadius = radius;
        extraLayers = layers;
        onContact = contact;
        this.targetFilter = targetFilter;
        this.shape = shape;
        repeating = repeat.HasValue;
        cast = new PNHitboxCast(shape, caster.gameObject.scene.GetPhysicsScene(), repeat);
        if (context.Mode == PNSkillMode.Authority)
        {
            Damage = damage.CreatePacket();
            reportLease = context.Skill.RegisterHitReceiver(this);
            // The play's claim book (none offline): a dictionary lookup, so a sweep with nobody to hand over costs nothing more.
            claimBook = PNHurtClaimBooks.Find(EveRegistrySession.Context);
            doorCapable = claimBook?.Policy != null && PNMeleeDoor.Supports(shape) && (context.Invocation?.CastKey ?? 0) > 0;
        }
    }

    private bool Allows(PNStat target)
        => !ReferenceEquals(target, caster) && PNLayers.Contains(caster.TargetLayers.Mask | extraLayers, target.Layer) && (targetFilter == null || targetFilter(target));

    public void Sample(EveHitboxPose pose, double time)
    {
        if (cast == null) return;
        if (doorCapable && !doorOpened) TryOpenDoor(pose, time);
        // Once the door is open the players whose machine decides are that machine's to hit; everyone else is swept as always.
        cast.Query(pose, time, targets, caster, filter: target => Allows(target) && !(doorOpened && Decided(target)));
        foreach (var target in targets)
        {
            if (disposed) break;
            if (!target.IsReady || target.IsDead) continue;
            cast.CommitHit(target, time);
            Contact(target, NextIndex(target));
        }
    }

    private bool Decided(PNStat target) => claimBook?.Policy?.ClaimsDecide(target) == true;

    // EN: The first sample of a sweep with a player of another machine who decides his own hits within reach (radius + the report slack): publish the door as one fact of the run and keep a ticket with this hitbox's
    //     prepared damage for ClaimGraceSeconds. When the fact would not fit (the run's bound) no door is opened and the host sweeps that player itself, as before.
    // VI: Lan quet dau co nguoi choi do may khac quyet trong tam: phat cua nhu mot fact cua run va giu ve voi sat thuong da chuan bi. Fact khong vua thi khong mo cua, host tu quet nhu cu.
    private void TryOpenDoor(EveHitboxPose pose, double time)
    {
        if (!context.CanPresent || !context.ShownToOthers || !PNMeleeDoor.TryFrom(shape, pose, hitIndex, out var door)) return;
        reachCast ??= new PNHitboxCast(EveHitboxShape.Sphere(shape.Radius + PNSkillRemoteInput.HitTolerance), caster.gameObject.scene.GetPhysicsScene());
        reachCast.Query(pose, time, reach, caster, filter: target => Allows(target) && Decided(target));
        if (reach.Count == 0) return;
        context.Present(PNMeleeDoor.Key, door.ToValues());
        claimBook.OpenTicket(PNClaimTicketKey.Melee(context.Invocation.CastKey, context.ActionOrdinal, hitIndex), pose.Position, shape.Radius, -1f, 0f, target => ApplyClaimed(target, hitIndex));
        doorOpened = true;
    }

    // EN: The contact door (V4c): a moving sphere (the caster's body while it dashes) over `[windowStart, windowEnd]` seconds after now, hitting a player again every `interval` seconds (0: once). `center`/`reach` bound where
    //     the body can be (the path of the dash plus the sphere), which is what a claim is checked against. Same door fact as a swing, one ticket per hit the window allows. Returns true when the door was opened: the players
    //     whose own machine decides are then no longer swept here. When there is nobody to hand over, no policy, no fact room or too many hits the host sweeps everyone, as before.
    // VI: Cua cham than: hinh cau di theo than quai khi luot. Moi ve ung voi mot nhat ma cua so cho phep; ve song den het cua so cong thoi gian an han.
    public bool OpenContactDoor(Vector3 start, Vector3 center, float pathReach, float interval, float windowStart, float windowEnd)
    {
        if (disposed || doorOpened || context.Mode != PNSkillMode.Authority || claimBook?.Policy == null || cast == null || (context.Invocation?.CastKey ?? 0) <= 0 || !context.CanPresent || !context.ShownToOthers) return false;
        if (!PNMeleeDoor.TryContact(start, interval, shape.Radius, windowStart, windowEnd, out var door)) return false;
        reachCast ??= new PNHitboxCast(EveHitboxShape.Sphere(pathReach + PNSkillRemoteInput.HitTolerance), caster.gameObject.scene.GetPhysicsScene());
        reachCast.Query(EveHitboxPose.Planar(center, Vector3.right), 0d, reach, caster, filter: target => Allows(target) && Decided(target));
        bool anyone = reach.Count != 0; reach.Clear();
        if (!anyone) return false;
        context.Present(PNMeleeDoor.Key, door.ToValues());
        long castKey = context.Invocation.CastKey; var ordinal = context.ActionOrdinal;
        for (int n = 0; n < door.Hits; n++)
        {
            int index = n;
            claimBook.OpenTicket(PNClaimTicketKey.Melee(castKey, ordinal, index), center, pathReach, -1f, 0f, target => ApplyClaimed(target, index), null, windowEnd);
        }
        doorOpened = true;
        return true;
    }

    // The ticket's way in: while the Action runs the hit is an ordinary one (OnHit effects, OnHitConfirmed); after it ended only the prepared damage is applied (HitClaimed).
    private bool ApplyClaimed(PNStat target, int index)
    {
        // While the Action runs its live rule (the world rule an enemy follows) applies as for any hit; once it ended that rule belongs to the Action that is gone, so only the caster's own target layers are kept.
        // The caster may be gone by now (killed and despawned, unloaded with its world): a destroyed or unloaded caster drops the claim (Unity null check first, then readiness).
        if (caster == null || !caster.IsReady || Damage == null || target == null || !target.IsReady || target.IsDead || target.gameObject.scene != caster.gameObject.scene) return false;
        if (!disposed ? !Allows(target) : ReferenceEquals(target, caster) || !PNLayers.Contains(caster.TargetLayers.Mask | extraLayers, target.Layer)) return false;
        if (!disposed) { Contact(target, index); return true; }
        return context.HitClaimed(target, target.transform.position, Damage, index).Applied;
    }

    public bool AcceptHitReport(in PNHitReport report, PNStat target, float tolerance)
    {
        // A melee hitbox takes only the Dealt reports of its own cast that name an Action step (projectile steps start at PNProjectileManager.ReportStepBase and belong to the projectile manager).
        if (report.Role != PNHitReportRole.Dealt || report.Step >= PNProjectileManager.ReportStepBase) return false;
        if (cast == null || target == null || !target.IsReady || target.IsDead ||
            !caster.IsReady || caster.IsDead || !Allows(target) || target.gameObject.scene != caster.gameObject.scene)
            return false;
        var offset = target.transform.position - reportOrigin;
        offset.y = 0;
        if (offset.magnitude > reportRadius + tolerance || !cast.TryCommitReported(target, ReportTime))
            return false;
        Contact(target, NextIndex(target));
        return true;
    }

    // The hit's name for this target: the Action's hit index for a hitbox that hits once; the target's own count for one that repeats (capped at the claim bound).
    private int NextIndex(PNStat target)
    {
        if (!repeating) return hitIndex;
        touches.TryGetValue(target.EntityId, out int count);
        touches[target.EntityId] = count + 1;
        return Math.Min(count, PNHurtClaimLimits.MaxHit);
    }

    private void Contact(PNStat target, int index)
    {
        var point = target.transform.position;
        var notify = onContact;
        var outcome = context.Hit(target, point, Damage, damage, index);
        notify?.Invoke(point, outcome);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        reportLease?.Dispose();
        cast?.Dispose();
        reachCast?.Dispose(); reachCast = null; claimBook = null; touches.Clear();
        reportLease = null;
        cast = null;
        onContact = null; targetFilter = null;
    }
}
#endif
