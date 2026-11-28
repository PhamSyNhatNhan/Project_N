#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// EN: Puts a rig puppet on an entity whose character declares `body.visual` (Docs/VisualPlan.md P4): the prefab's own sprite stays authored but is switched off, a PNVisualActor
//     child draws the body instead, the capsule takes the build's size (feet stay where they were), and the movement/hurt/death of the
//     entity drive the puppet's states (position and replicated health, so a replica needs nothing extra on the wire). A character without `body.visual` is untouched. The factory calls Begin after configuring an entity and Clear when it returns to the pool.
// VI: Gắn con rối rig lên entity có `body.visual`: tắt sprite cũ, vẽ bằng PNVisualActor, capsule theo build, bóng theo build, chuyển động/bị đánh/chết của entity điều khiển trạng thái.
[DisallowMultipleComponent]
public sealed class PNVisualBody : MonoBehaviour, IPNGlowSurface
{
    // Glows asked for by looks (PNGlowLook): the puppet draws and animates them.
    public bool GlowReady => actor != null;
    public int AddGlow(PNVisualGlowDefinition glow, Func<double?> remaining)
    {
        if (actor == null || glow == null) return 0;
        Color Shade(PNVisualColor c) => new Color(c.R, c.G, c.B, c.A);
        return actor.AddGlow(new PNVisualGlow
        {
            Parts = glow.Parts, Key = glow.Key.HasValue ? Shade(glow.Key.Value) : (Color?)null, Tolerance = glow.Tolerance,
            Color = Shade(glow.Color), Core = Shade(glow.Core), Amount = glow.Amount, Spill = glow.Spill, Spread = glow.Spread, Priority = glow.Priority,
            Rise = glow.Rise, Release = glow.Release, PulseDepth = glow.PulseDepth, PulseRate = glow.PulseRate, Remaining = remaining, Warning = glow.WarningSeconds,
        });
    }
    public void RemoveGlow(int handle, bool now) { if (actor != null) actor.RemoveGlow(handle, now); }

    [SerializeField] private Transform visual;          // the authored sprite object, switched off while a puppet is drawn
    [SerializeField] private CapsuleCollider capsule;
    [SerializeField, Min(0f)] private float walkSpeed = .08f, runSpeed = 1.1f;   // m/s of the entity; below walkSpeed it stands
    // A position that does not change for a frame is not a stop: physics steps and replicated positions arrive less often than frames are drawn.
    public const float StillSeconds = .1f;
    private float stillTimer, moveSpeed; private PNPhysicsPose pose;

    private PNVisualActor actor; private PNStat stat; private Vector3 lastPosition; private bool hasLast, dead, savedCapsule, hasHealth; private double lastHealth;
    private float baseHeight, baseRadius; private Vector3 baseCenter; private string locomotion = "";
    // Strings and the Lorica break (all derived from replicated data: the buff presentation snapshot and the health, so a replica needs nothing on the wire).
    private PNBuffManager buffs; private bool broken, pullPending;
    private PNPlayerMove skillFacing;
    public const float StringLowerSeconds = .8f, EntranceLowerSeconds = .9f, BreakPollSeconds = .1f;
    // Checks only: when set, it replaces the buff presentation as the source of "the Lorica is broken".
    public Func<bool> BreakSource;
    public Func<PNBuffTimingView> BreakTimingSource;
    private float lowerSpan=StringLowerSeconds;private bool reconnecting;
    public bool LoricaBroken => broken;
    // An action (a cast) or the recovery plays its motion and holds the locomotion states for `seconds`; EndAction (or a Lorica break, or death) releases it.
    private float holdTimer;
    public const float RecoverSeconds = .9f;
    public bool Holding => holdTimer > 0f;
    public bool PlayAction(string state, float seconds)
    {
        if (actor == null || dead || broken || !actor.Play(state, true)) return false;
        holdTimer = Mathf.Max(.05f, seconds); locomotion = ""; return true;
    }
    public void EndAction() { holdTimer = 0f; locomotion = ""; drivenState = ""; actor?.ReleaseAction(); }
    // PV3: the motion of an Action run, placed by the run's AGE (the same on the machine that runs the Action and on one that only watches): `age` and `duration` are the run's own seconds
    // (its clock, attack speed and slow areas already inside), so the puppet neither double counts its own clock nor drifts from the host. `seeking` restarts the state and places it.
    // A Time clock motion takes age x speed scale (looping ones wrap), a Phase clock motion takes age / duration. Returns false when this body cannot show it (no puppet, dead, broken).
    private string drivenState = "";
    public bool PlayActionAt(string state, double age, double duration, bool seeking)
    {
        if (actor == null || dead || broken) return false;
        if (seeking || drivenState != state || !actor.IsDriven)
        {
            if (!actor.Play(state, true)) return false;
            drivenState = state;
        }
        actor.DriveAction(age, duration);
        holdTimer = Mathf.Max(holdTimer, .1f); locomotion = ""; return true;
    }
    // Turns the puppet toward the way an action is aimed, as the camera sees it. While the action holds the pose, movement does not turn it back (Feed). An aim straight toward or away
    // from the camera leaves the puppet as it faces.
    public void FaceAction(Vector3 direction)
    {
        if (actor == null || dead || broken) return;
        var camera = PNViewCamera.Current;
        float horizontal = Vector3.Dot(direction, camera != null ? camera.transform.right : Vector3.right);
        if (Mathf.Abs(horizontal) > .01f) actor.SetFacingLeft(horizontal < 0f);
    }
    public bool PullingUp => pullPending;

    public PNVisualActor Actor => actor;
    public bool Active => actor != null;

    // Returns false (and changes nothing) when the character has no body.visual.
    public bool Begin(EveCharacterDefinition character, EveRegistryHub hub)
    {
        if (character == null) throw new ArgumentNullException(nameof(character)); if (hub == null) throw new ArgumentNullException(nameof(hub));
        if (actor != null) Clear();
        var body = PNBodySettings.Parse(character.Data);
        if (!body.Visual.HasValue) return false;
        var appearance = hub.Get<PNVisualAppearanceDefinition>().Get(body.Visual.Value);
        if (capsule == null) capsule = GetComponent<CapsuleCollider>();
        if (visual == null) { var found = transform.Find("Visual"); if (found != null) visual = found; }
        // feet in this object's space: the bottom of the authored capsule (the body keeps standing on the same ground)
        var feet = capsule != null ? capsule.center + Vector3.down * (capsule.height * .5f) : Vector3.zero;
        var child = new GameObject("PN Visual Actor"); child.transform.SetParent(transform, false);
        var created = child.AddComponent<PNVisualActor>();
        try { created.Bind(appearance, hub, feet, body.BuildOverride); }
        catch { DestroyObject(child); throw; }
        actor = created;
        actor.KeyActor = GetComponent<PNPlayerStat>() != null; actor.RefreshShadowMode();   // the KeyActors shadow setting: the player now, bosses when they get a visual
        if (visual != null) visual.gameObject.SetActive(false);
        if (!TryGetComponent(out pose)) pose = gameObject.AddComponent<PNPhysicsPose>();
        // The puppet's camera facing re-places it from its foot anchor after the camera moved, so the anchor is what carries the offset.
        var drawnFacing = child.GetComponent<PNCameraFacingTarget>();
        pose.Bind(offset => { if (child == null) return; drawnFacing.FootAnchor = feet + offset; child.transform.localPosition = feet + offset; });
        stillTimer = moveSpeed = 0f;
        if (capsule != null)
        {
            baseHeight = capsule.height; baseRadius = capsule.radius; baseCenter = capsule.center; savedCapsule = true;
            capsule.height = Mathf.Max(actor.CapsuleHeight, actor.CapsuleRadius * 2f); capsule.radius = actor.CapsuleRadius;
            capsule.center = new Vector3(baseCenter.x, feet.y + capsule.height * .5f, baseCenter.z);
        }
        stat = GetComponent<PNStat>();
        dead = false; hasLast = false; hasHealth = false; locomotion = "";
        actor.Play("idle");
        actor.Tick(0f); // Publish the selected idle pose before a newly bound puppet can be rendered.
        buffs = GetComponent<PNBuffManager>(); broken = false; pullPending = false;
        skillFacing = GetComponent<PNPlayerMove>(); if (skillFacing != null) skillFacing.SkillFaced += OnSkillFaced;     // a skill that turns the body turns the puppet
        if(stat is PNEnemyStat)PNLoricaPresentation.Ensure(gameObject);
        StartEntrance(appearance, hub);
        return true;
    }

    // The entrance a string style asks for: a puppet that comes into the world hangs slumped and the strings are lowered onto it ("slumped"), or only the strings are lowered ("lowered").
    // No entrance while the local loading cover is up (the world is not seen yet, and a late joiner meets the puppets already attached).
    private void StartEntrance(PNVisualAppearanceDefinition appearance, EveRegistryHub hub)
    {
        if (!actor.HasStrings) return;
        string entrance = actor.Strings.Entrance(hub, appearance);
        if (entrance == PNVisualStringStyleDefinition.EntranceNone) return;
        var cover = PNTransitionController.Current; if (cover != null && cover.IsShowing) return;
        if (entrance == PNVisualStringStyleDefinition.EntranceSlumped) { actor.Play("stun", true); actor.Tick(6.5f); }   // the stun motion holds its last pose: start there
        actor.LowerStrings(EntranceLowerSeconds); pullPending = true;
    }

    // Up from the slump: the `recover` motion when the rig has one (it starts from the pose being shown), the idle otherwise.
    private void Recover()
    {
        if (actor.Play("recover", true)) holdTimer = RecoverSeconds; else {actor.Play("idle", true);}
    }

    private bool ReadBreak(out PNBuffTimingView timing)
    {
        timing=default;
        if(BreakTimingSource!=null){timing=BreakTimingSource();return true;}
        if(BreakSource!=null){timing=new PNBuffTimingView(BreakSource(),1,null,false,1,PNBuffClock.Entity);return true;}
        return buffs!=null && buffs.TryReadPresentationTiming(PNContentIds.LoricaBreak,out timing);
    }

    private void OnSkillFaced(Vector2 direction) => FaceAction(new Vector3(direction.x, 0f, direction.y));

    public void Clear()
    {
        if (skillFacing != null) skillFacing.SkillFaced -= OnSkillFaced;
        skillFacing = null;
        reconnecting=false;lowerSpan=StringLowerSeconds;stat = null;
        if (pose != null) pose.Unbind();
        stillTimer = moveSpeed = 0f;
        if (actor != null) { DestroyObject(actor.gameObject); actor = null; }
        if (visual != null) visual.gameObject.SetActive(true);
        if (savedCapsule && capsule != null) { capsule.height = baseHeight; capsule.radius = baseRadius; capsule.center = baseCenter; }
        savedCapsule = false; hasLast = false; hasHealth = false; dead = false; locomotion = ""; buffs = null; broken = pullPending = false; holdTimer = 0f;
    }

    private static void DestroyObject(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

    // The movement feed: how fast and which way the entity really moved this frame (position based, so it also works for a replica that is not simulated here).
    private void LateUpdate() => Feed(actor!=null?actor.ClockDelta():0);
    public void Feed(float dt)
    {
        if (actor == null) return;
        // Health is replicated, so the hurt and death of a replica (no damage events run there) are read from it, the same way on every machine.
        if (stat != null && stat.HasHealth)
        {
            double hp = stat.GetCurrent(PNContentIds.Health); bool nowDead = stat.IsDead;
            if (hasHealth && hp < lastHealth - 1e-6 && !nowDead && !broken) { actor.PulseStringEffects();actor.Play("hurt", true); }
            lastHealth = hp; hasHealth = true;
            if (nowDead && !dead) { dead = true; broken = pullPending = false; holdTimer = 0f; actor.CutStrings(); actor.Play("dead"); }          // death lets the stars let go
            else if (!nowDead && dead) { dead = false; locomotion = ""; actor.ShowStrings(); actor.Play("idle"); }
        }
        if (dead) return;
        // Absolute Buff-clock timing, not integer UI seconds or an extra post-break delay.
        if(ReadBreak(out var timing))
        {
            if(timing.Present && !broken)
            {
                broken=true;pullPending=false;holdTimer=0;reconnecting=false;
                lowerSpan=(float)Math.Min(StringLowerSeconds,timing.Remaining.HasValue?Math.Max(.001,timing.Remaining.Value):StringLowerSeconds);
                actor.CutStrings();actor.Play("stun",true);
            }
            if(timing.Present)
            {
                if(timing.Remaining.HasValue && timing.Remaining.Value<=lowerSpan)
                {actor.Strings?.SyncLowering(Mathf.Clamp01(1-(float)timing.Remaining.Value/lowerSpan));reconnecting=true;}
                else if(reconnecting){actor.HideStrings();reconnecting=false;}
            }
            else if(broken)
            {
                broken=false;reconnecting=false;pullPending=false;actor.ShowStrings();locomotion="";Recover();
            }
        }
        if (pullPending && actor.Strings != null && !actor.Strings.AnyLowering) { pullPending = false; locomotion = ""; Recover(); }       // the strings are on again: pull the puppet up
        if (holdTimer > 0f) { holdTimer -= dt; if (holdTimer <= 0f) locomotion = ""; }
        var position = transform.position + (pose != null ? pose.Offset : Vector3.zero);
        if (broken || pullPending || holdTimer > 0f) { lastPosition = position; hasLast = true; stillTimer = moveSpeed = 0f; return; }       // no walking animation while slumped or being pulled up
        if (hasLast && dt > 1e-5f)
        {
            var delta = position - lastPosition; delta.y = 0f; stillTimer += dt;
            // A step after a rest is timed by this frame alone, else by the time since the last step.
            if (delta.sqrMagnitude > 1e-10f) { moveSpeed = delta.magnitude / (moveSpeed > 0f ? stillTimer : dt); stillTimer = 0f; }
            else if (stillTimer >= StillSeconds) moveSpeed = 0f;
            if (Mathf.Abs(delta.x) > 1e-4f) actor.SetFacingLeft(delta.x < 0f);
            string next = moveSpeed < walkSpeed ? "idle" : moveSpeed < runSpeed ? "walk" : "run";
            if (next != locomotion) { locomotion = next; actor.Play(next); }
        }
        lastPosition = position; hasLast = true;
    }

    private void OnDestroy() => Clear();
}
#endif
