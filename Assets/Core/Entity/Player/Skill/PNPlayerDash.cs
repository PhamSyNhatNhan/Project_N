using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class PNPlayerDashSettings
{
    public EveId RecoveryId { get; private set; }
    public bool ApplyCooldownReduction { get; private set; }
    public PNMotionSettings Motion { get; private set; }
    public static PNPlayerDashSettings Parse(PNSkillData data)
    {
        if(!(data.Skill is JObject obj) || obj["recoveryId"]?.Type!=JTokenType.String || obj["applyCooldownReduction"]?.Type!=JTokenType.Boolean)
            throw new FormatException("Player Dash needs recoveryId and applyCooldownReduction.");
        return new PNPlayerDashSettings {RecoveryId=EveId.Parse(obj["recoveryId"].Value<string>()),
            ApplyCooldownReduction=obj["applyCooldownReduction"].Value<bool>(),Motion=PNMotionSettings.Parse(data.Action.Perform)};
    }
}
// EN: Shared player logic uses pre-registered Dash content; every entity owns its charges.
// VI: Logic player dùng dữ liệu Dash đăng ký sẵn; mỗi entity giữ lượng hồi riêng.
public partial class PNPlayerSkill
{
    protected virtual EveId PlayerDashSkillId => EveId.Parse("base:player_dash");
    public override IEnumerable<EveId> BuiltInSkillIds => new[]{PlayerDashSkillId};
    public override IEnumerable<Type> Dependencies => new[]{typeof(PNAbilityManager),typeof(PNStat),typeof(PNControlManager)};
    public PNPlayerDashSettings DashSettings { get; private set; }
    public PNAbilityCooldown DashRecovery { get; private set; }
    public bool IsDashing => playerDash!=null;
    // The Dash running is a guess on the owner's client (its invocation is Predicted).
    public bool IsPredictingDash=>playerDash!=null&&CurrentInvocation!=null&&CurrentInvocation.Mode==PNSkillMode.Predicted;
    private PNMotionAction playerDash;private PNCastTrack dashTrack;
    private PNSkillActionContext playerDashContext;
    private PNStat playerDashStats;
    private PNAbilityCollection playerDashAbilities;
    private EveTimeScale playerDashClock;
    private double playerDashCdr=double.NaN;
    private void LoadPlayerDash()
    {
        DashSettings=PNPlayerDashSettings.Parse(GetData(PlayerDashSkillId));
        playerDashAbilities=Abilities;DashRecovery=playerDashAbilities.GetCooldown(DashSettings.RecoveryId);
        playerDashStats=GetComponent<PNStat>();playerDashClock=GetComponent<EveTimeScale>();
        if(playerDashStats==null || !playerDashStats.IsInitialized)throw new InvalidOperationException("Player Dash needs initialized stats.");
        playerDashStats.Died+=OnPlayerDashDeath;playerDashAbilities.Changed+=OnPlayerDashAbilityChange;
        SyncPlayerDashCooldown();
    }
    protected void SyncPlayerDashCooldown()
    {
        if(DashSettings!=null && DashRecovery!=null && DashRecovery.IsAttached && playerDashStats!=null && (playerDashStats.IsReady || GetComponent<EveEntityLoader>()?.LoadContext?.AllowsModuleAccess == true) && DashSettings.ApplyCooldownReduction)
        {
            double cdr=playerDashStats.Evaluate(PNContentIds.CooldownReduction);
            if(cdr==playerDashCdr)return;ApplyCooldownReduction(DashRecovery);playerDashCdr=cdr;
        }
    }
    protected virtual bool CanStartPlayerDash()
    {
        var loader=GetComponent<EveEntityLoader>();
        return CanInput && isActiveAndEnabled && IsReady && !IsDashing && loader!=null && loader.IsReady &&
            playerDashStats!=null && playerDashStats.IsReady && !playerDashStats.IsDead;
    }
    protected virtual Vector3 PlayerDashDirection()
    {
        var move=GetComponent<PNMove>();var last=move!=null?move.LastMoveDirection:Vector2.right;
        return new Vector3(last.x,0,last.y);
    }
    public virtual bool TryDash()
    {
        if(!CanStartPlayerDash() || !CanActivateSkill(PNControlIds.Dash,PlayerDashSkillId) || !Controls.IsAllowed(PNControlIds.VoluntaryMotion) || DashRecovery==null || !DashRecovery.CanConsume())return false;
        PNMotionAction created=null;PNSkillActionContext context=null;
        // G6b: on the owner's client (no authority) the same Dash is a Predicted cast: the admission above opened it as one, the services read its mode, the charge is spent inside its scope.
        bool guess=!EveRegistrySession.Context.HasAuthority;
        try
        {
            var direction=PlayerDashDirection();direction.y=0;
            double magnitude=Math.Sqrt((double)direction.x*direction.x+(double)direction.z*direction.z);
            direction=magnitude>0?new Vector3((float)(direction.x/magnitude),0,(float)(direction.z/magnitude)):Vector3.right;
            SyncPlayerDashCooldown();
            // A guess only turns the body: the character's own start hook (Eve cancels her moveset and locks the sequence there) is the authority's.
            if(guess)GetComponent<PNPlayerMove>()?.FaceSkillDirection(new Vector2(direction.x,direction.z));else OnPlayerDashStarting(direction);
            BeginActivation(PlayerDashSkillId);
            created=CreateMotionAction(PlayerDashSkillId,direction,out context);
            if(guess)context.Mode=PNSkillMode.Predicted;
            var ownedMotion=created;
            created.BindMovementOwner(()=>{if(ReferenceEquals(playerDash,ownedMotion))CancelPlayerDash();});
            created.Start(context);created.Perform(context);
            playerDash=created;playerDashContext=context;dashTrack=CurrentInvocation?.Track(_=>CancelPlayerDash());
            if(!DashRecovery.TryConsume())throw new InvalidOperationException("Dash charges changed during activation.");
            CommitActivation(direction: direction);
            return true;
        }
        catch(Exception error)
        {
            if(IsDashing)CancelPlayerDash();else {created?.Dispose();OnPlayerDashEnded(true);}
            EndActivation(true); Debug.LogException(error,this);return false;
        }
    }
    protected virtual void OnPlayerDashStarting(Vector3 direction)
    {
        var move=GetComponent<PNPlayerMove>();
        if(move!=null)move.FaceSkillDirection(new Vector2(direction.x,direction.z));
        else GetComponent<PNMove>()?.TryFaceSkill(new Vector2(direction.x,direction.z));
    }
    protected virtual void OnPlayerDashEnded(bool cancelled) { }
    protected void AdvancePlayerDash(float delta)
    {
        if(float.IsNaN(delta)||float.IsInfinity(delta)||delta<0)throw new ArgumentOutOfRangeException(nameof(delta));
        if(DashSettings==null)return;
        SyncPlayerDashCooldown();
        if(!IsDashing)return;
        if(playerDashStats==null || !playerDashStats.IsReady || playerDashStats.IsDead){CancelPlayerDash();return;}
        try {playerDash.Advance(delta * ExecutionSpeed);if(playerDash.IsComplete)EndPlayerDash(false);}
        catch {CancelPlayerDash();throw;}
    }
    protected void CancelPlayerDash(){if(IsDashing)EndPlayerDash(true);}
    public override bool TryInterrupt(EveId reason)
    {
        if (!reason.IsValid) throw new ArgumentException("Interruption reason required.");
        if (!IsReady || !IsDashing) return false;
        CancelPlayerDash(); return true;
    }
    private void EndPlayerDash(bool cancelled)
    {
        var active=playerDash;var context=playerDashContext;bool guess=CurrentInvocation!=null&&CurrentInvocation.Mode==PNSkillMode.Predicted;playerDash=null;playerDashContext=null;dashTrack?.Release();dashTrack=null;
        try {if(cancelled)active.Cancel(context);else active.Exit(context);}
        finally {try {active.Dispose();} finally {try {if(!guess)OnPlayerDashEnded(cancelled);} finally {EndActivation(cancelled);}}}
    }
    private void OnPlayerDashDeath(PNDamageResult result)
    {CancelPlayerDash();if(DashRecovery!=null&&DashRecovery.IsAttached)DashRecovery.Reset();}
    private void OnPlayerDashAbilityChange(PNAbilityChange change)
    {if(change.Reason==PNAbilityChangeReason.Reset && change.After?.Id==DashSettings?.RecoveryId)playerDashCdr=double.NaN;}
    private void UnloadPlayerDash()
    {
        CancelPlayerDash();if(playerDashStats!=null)playerDashStats.Died-=OnPlayerDashDeath;
        if(playerDashAbilities!=null)playerDashAbilities.Changed-=OnPlayerDashAbilityChange;
        playerDashAbilities=null;playerDashStats=null;playerDashClock=null;playerDashCdr=double.NaN;DashRecovery=null;DashSettings=null;
    }
    protected override void Update()
    {
        if(!HasGameplayAuthority){if(IsPredictingDash)AdvancePlayerDash(playerDashClock!=null&&playerDashClock.isActiveAndEnabled?playerDashClock.DeltaTime:Time.deltaTime);return;}
        var delta = playerDashClock!=null&&playerDashClock.isActiveAndEnabled?playerDashClock.DeltaTime:Time.deltaTime;
        AdvanceAuxiliaryEffects(delta); AdvancePlayerDash(delta); AdvanceBindings(delta);
    }
}
