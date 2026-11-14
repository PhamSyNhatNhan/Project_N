using System;
using UnityEngine;
using EveEngine;

// EN: The owner's client runs its own Dash at once, before the authority says anything (Docs/SkillNetworkingAPlan.md S3, N1): the same motion action as the real Dash, on the body the client owns, and the
//     charge spent on the replica. It never commits an activation and never writes a shared Ability: the authority's cast is the real one. If the authority declines, `CancelPredictedDash` stops the motion
//     (the body stays where it is; Move's correction pulls it back only if the host's rule says so) and the caller rolls the charge back.
// VI: Client chủ nhân chạy Dash của mình ngay, trước khi authority trả lời: cùng action chuyển động như Dash thật trên thân client sở hữu, charge tiêu trên replica. Không commit activation, không ghi Ability chung.
public partial class PNPlayerSkill
{
    private bool predictedDash;
    public bool IsPredictingDash => predictedDash && IsDashing;

    public bool TryPredictDash(out PNAbilityPrediction prediction)
    {
        prediction = default;
        if (HasGameplayAuthority || !CanForwardInput || DashSettings == null || DashRecovery == null || !CanStartPlayerDash() || !DashRecovery.CanConsume()) return false;
        PNMotionAction created = null; PNSkillActionContext context = null;
        try
        {
            var direction = PlayerDashDirection(); direction.y = 0;
            double magnitude = Math.Sqrt((double)direction.x * direction.x + (double)direction.z * direction.z);
            direction = magnitude > 0 ? new Vector3((float)(direction.x / magnitude), 0, (float)(direction.z / magnitude)) : Vector3.right;
            // Only the facing: the character's own start hook (Eve locks its combo sequence there) writes state a guess must not touch.
            GetComponent<PNPlayerMove>()?.FaceSkillDirection(new Vector2(direction.x, direction.z));
            created = CreateMotionAction(PlayerDashSkillId, direction, out context);
            var owned = created;
            created.IsPrediction = true;
            created.BindMovementOwner(() => { if (ReferenceEquals(playerDash, owned)) CancelPlayerDash(); });
            created.Start(context); created.Perform(context);
            playerDash = created; playerDashContext = context; predictedDash = true;
            if (!DashRecovery.TryPredictConsume(out prediction)) throw new InvalidOperationException("Dash charge changed during prediction.");
            return true;
        }
        catch (Exception error)
        {
            if (IsDashing) CancelPlayerDash(); else created?.Dispose();
            prediction = default; predictedDash = false; Debug.LogException(error, this); return false;
        }
    }

    public void CancelPredictedDash() { if (predictedDash) CancelPlayerDash(); }

    private void AdvancePredictedDash(float delta)
    {
        if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!IsDashing) { predictedDash = false; return; }
        if (playerDashStats == null || !playerDashStats.IsReady || playerDashStats.IsDead) { CancelPlayerDash(); return; }
        try { playerDash.Advance(delta); if (playerDash.IsComplete) EndPlayerDash(false); }
        catch { CancelPlayerDash(); throw; }
    }
}
