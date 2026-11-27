using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// EN: `project_n:gura_trident_action` (Docs/GuraCharacter.md 4.2, the far form of the Skill): a thrown trident that deals the damage of its own projectile. Payload:
//       windup (s) before the throw, recovery (s) after a miss, maxFlight (s) the Action waits for the trident, projectile = a `create_projectile` payload (its damage included), lockMovement (optional).
//     The Action creates the projectile through the registered Operation and watches it through the projectile manager. A hit ends the Action at once and leaves `Struck` (the entity hit: the real one on the
//     authority, the guess on a predicting machine) for the behavior, which goes on with the rush. A miss (wall, end of range or of time) ends it after the recovery with `Struck` null.
//     The trident's reward (skill data `skill.reward`) is paid by the authority when it hits. The Action has no network code.
// VI: Dinh ba nem (dang xa cua Skill): trung thi ket thuc ngay va de lai `Struck` cho behavior lao toi; truot thi hoi don roi ket thuc.
public sealed class PNGuraTridentAction : PNSkillAction
{
    public static void Validate(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform);
        PNBuffContentBatch.Fields(p, "windup", "recovery", "maxFlight", "projectile", "lockMovement");
        if (p["lockMovement"] != null && p["lockMovement"].Type != JTokenType.Boolean)
            throw new FormatException("Trident lockMovement must be a boolean.");
        foreach (var key in new[] { "windup", "recovery", "maxFlight" }) { double n = PNBuffContentBatch.Number(p, key); if (!(n >= 0d && n <= 10d) || key == "maxFlight" && n <= 0d) throw new FormatException("Invalid trident number: " + key); }
        PNCreateProjectileOperation.Validate(p["projectile"], hub);
    }

#if UNITY_5_3_OR_NEWER
    private PNSkillActionContext context; private JObject payload;
    private float age, windup, recovery, maxFlight, thrownAt, missedAt;
    private bool thrown, missed, ended;
    private PNSkillOperation launch, movementLock; private PNCreateProjectileOperation volley;
    public override bool HasAuxiliaryTimeline => true;
    // What the trident hit, kept after the Action is disposed.
    public PNStat Struck { get; private set; }

    public override void Start(PNSkillActionContext c)
    {
        if (context != null || ended) throw new InvalidOperationException("Fresh cast required");
        Validate(c.Data.Action, c.Registry);
        if (!c.Direction.HasValue) throw new InvalidOperationException("Trident direction required");
        payload = (JObject)c.Data.Action.Perform; windup = payload["windup"].Value<float>(); recovery = payload["recovery"].Value<float>(); maxFlight = payload["maxFlight"].Value<float>();
        context = c;
        try
        {
            c.BeginAction();
            if (payload["lockMovement"]?.Value<bool>() == true)
            {
                movementLock = CreateOperation(c, EveId.Parse("base:lock_movement"));
                movementLock.Execute(c, null);
            }
        }
        catch { Dispose(); throw; }
    }
    public override void Perform(PNSkillActionContext c) { }

    public override bool AdvanceAuxiliary(float dt)
    {
        if (ended || context == null) return true;
        age += dt;
        if (!thrown && age >= windup) Throw();
        if (!thrown) return false;
        if (!missed)
        {
            if (TakeHit()) return true;
            if (volley == null || Ended() || age - thrownAt >= maxFlight) { missed = true; missedAt = age; }
        }
        return missed && age >= missedAt + recovery;
    }

    private void Throw()
    {
        thrown = true; thrownAt = age;
        launch = CreateOperation(context, PNProjectileGameplayContent.CreateProjectile);
        launch.Execute(context, payload["projectile"]);
        volley = launch as PNCreateProjectileOperation;
        if (volley != null && volley.Results.Count == 0) volley = null;
    }
    private bool TakeHit()
    {
        if (volley == null) return false;
        var manager = PNProjectileManager.Resolve(EveRegistrySession.Context, context.Skill.gameObject.scene);
        foreach (var receipt in volley.Results)
        {
            if (receipt.Status != PNProjectileStatus.Created || !manager.TryGetTargetHit(receipt.Handle, out var target, out _)) continue;
            Struck = target;
            if (context.Mode == PNSkillMode.Authority && context.Skill is PNGuraSkill gura) gura.Pay(context.SkillId, true);
            return true;
        }
        return false;
    }
    private bool Ended()
    {
        foreach (var receipt in volley.Results)
            if (receipt.Status == PNProjectileStatus.Pending || receipt.Status == PNProjectileStatus.Created && receipt.Handle.IsAlive) return false;
        return true;
    }
    public override void Exit(PNSkillActionContext c) { try { c.CompleteAction(); } finally { Dispose(); } }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); } finally { Dispose(); } }
    public override void Dispose()
    {
        if (ended) return; ended = true;
        try { launch?.Dispose(); }
        finally
        {
            movementLock?.Dispose();
            movementLock = null; launch = null; volley = null; context = null;
        }
    }
#else
    public override void Start(PNSkillActionContext c) { Validate(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
#endif
}
