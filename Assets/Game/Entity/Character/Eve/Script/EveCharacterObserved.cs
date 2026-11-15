using System;
using EveEngine;
using UnityEngine;

// EN: Eve's cast as the OTHER players see it (Docs/SkillNetworkingAPlan.md S6). The authority raises a picture at the three places every cast draws something: a slash starts (`sweep`), a motion starts
//     (`motion`: the energy trail), Burst turns on / off (`burst`). An observer's copy plays the pictures and nothing more: the slash is the same `EveSweepAction` with `IsPrediction` and no reporter
//     (so it draws its arc and hits nothing), no Ability is written, no motion moves the body (its place comes from Move), no Control is taken. The owner's client ignores them: it drew the cast itself.
// VI: Cast của Eve như NGƯỜI KHÁC thấy. Authority báo ba loại hình: nhát chém, vệt năng lượng của motion, bật/tắt Burst. Máy quan sát chỉ phát hình: nhát chém là EveSweepAction ở chế độ IsPrediction
//     không báo hit (vẽ đường chém, không trúng gì), không ghi Ability, không motion (vị trí thân do Move), không Control. Máy chủ nhân bỏ qua vì đã tự vẽ.
public partial class EveCharacterSkill
{
    public const string SweepEvent = "sweep", MotionEvent = "motion", BurstEvent = "burst";
    private EveSweepAction observedSweep;
    private PNSkillActionContext observedContext;
    private float observedSweepRemaining, observedMotionRemaining;
    public bool IsObservingSweep => observedSweep != null;
    public bool IsObservingMotion => observedMotionRemaining > 0f;
    public int ObservedSweepsPlayed { get; private set; }

    public override void PlayCastEvent(PNSkillCastEvent value)
    {
        if (HasGameplayAuthority || predictingCast || ownedAbilities == null || movesetRegistry == null) return;
        switch (value.Kind)
        {
            case SweepEvent: PlayObservedSweep(value); break;
            case MotionEvent:
                if (value.Duration <= 0f) break;
                observedMotionRemaining = value.Duration;
#if UNITY_5_3_OR_NEWER
                EnsureEnergy().SetMotion(true);
#endif
                break;
            case BurstEvent:
                bool on = value.Detail == "1";
#if UNITY_5_3_OR_NEWER
                WeaponRig?.SetBurst(on); if (on) EnsureEnergy().Flash(); else energy?.SetBurst(false);
#endif
                break;
        }
    }

    private void PlayObservedSweep(PNSkillCastEvent value)
    {
        EndObservedSweep();
        var direction = new Vector3(value.X, 0f, value.Z);
        if (value.X * value.X + value.Z * value.Z < 1e-6f || value.Detail.Length == 0) return;
        EveId id; PNSkillData data;
        // A profile this machine does not know (a different mod set or version) is a picture it cannot draw: dropped, never an error.
        try { id = EveId.Parse("base:eve_" + value.Detail); data = GetData(id); } catch (Exception) { return; }
        EveSweepAction created = null;
        try
        {
            var settings = EveSweepSettings.Parse(data.Action.Perform);
            observedContext = new PNSkillActionContext(this, movesetRegistry.Skills.ResolveIdentity(id), data, movesetRegistry, null, direction) { IsPrediction = true };
            var action = movesetRegistry.Actions().Get(data.Action.Id).Create();
            created = action as EveSweepAction;
            if (created == null) { action.Dispose(); return; }
            created.Start(observedContext); created.Perform(observedContext);
            observedSweep = created; observedSweepRemaining = settings.Duration; ObservedSweepsPlayed++;
        }
        catch (Exception)
        {
            observedSweep = created; EndObservedSweep(); throw;
        }
    }

    private void AdvanceObserved(float delta)
    {
        if (delta <= 0f || float.IsNaN(delta) || float.IsInfinity(delta)) return;
        if (observedSweep != null)
        {
            try
            {
                observedSweep.Advance(delta); observedSweepRemaining -= delta;
                if (observedSweepRemaining <= 0f) EndObservedSweep();
            }
            catch (Exception error) { EndObservedSweep(); Debug.LogException(error, this); }
        }
        if (observedMotionRemaining > 0f)
        {
            observedMotionRemaining -= delta;
            if (observedMotionRemaining <= 0f) { observedMotionRemaining = 0f; EndObservedMotion(); }
        }
    }

    private void EndObservedMotion()
    {
        observedMotionRemaining = 0f;
#if UNITY_5_3_OR_NEWER
        if (energy != null && !IsBusy) energy.SetMotion(false);
#endif
    }

    private void EndObservedSweep()
    {
        var active = observedSweep; var context = observedContext;
        observedSweep = null; observedContext = null; observedSweepRemaining = 0f;
        if (active == null) return;
        try { active.Cancel(context); }
        finally { active.Dispose(); }
    }

    // Everything an observer drew stops with the cast, the entity or the session.
    private void EndObserved() { EndObservedSweep(); if (observedMotionRemaining > 0f) EndObservedMotion(); }
}
