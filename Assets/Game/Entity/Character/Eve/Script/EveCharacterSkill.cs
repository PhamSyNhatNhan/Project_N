using System;
using EveEngine;
using UnityEngine;

// EN: Eve's extension point for character-specific skill data and behavior.
// VI: Điểm kế thừa cho dữ liệu và hành vi skill riêng của Eve.
[DisallowMultipleComponent]
#if UNITY_5_3_OR_NEWER
[DefaultExecutionOrder(100)]
#endif
public partial class EveCharacterSkill : PNPlayerSkill
{
    private EveSweepAction attack;
    private PNSkillActionContext attackContext;
    private EveTimeScale clock;
    [SerializeField] private Vector3 attackDirection = new Vector3(1f, 0f, 0f);
    public virtual bool IsAttacking => attack != null;
#if UNITY_5_3_OR_NEWER
    public EveSweepVisual AttackVisual => attack?.Visual;
#endif
    // EN: Fallback aim for owners without movement; explicit casts may supply any direction.
    // VI: Huong du phong khi khong co movement; lan chem tuong minh co the truyen huong rieng.
    public virtual Vector3 AttackDirection { get => attackDirection; set => attackDirection = value; }


    public override void Load(EveEntityLoadContext context)
    {
        base.Load(context);
        clock = GetComponent<EveTimeScale>();
        LoadMoveset(context);
    }

    public virtual bool TryAttack() => TryAttack(SelectDirection(false, out _));

    // EN: This prototype skill chooses movement facing; action/VFX only consume the supplied vector.
    // VI: Skill ban thu chon huong di chuyen; action/VFX chi dung vector duoc truyen xuong.
    protected virtual Vector3 ResolveAttackDirection()
    {
        var move = GetComponent<PNMove>();
        return move != null
            ? new Vector3(move.LastMoveDirection.x, 0f, move.LastMoveDirection.y)
            : AttackDirection;
    }

    public virtual bool TryAttack(Vector3 direction)
    {
        if (!CanBegin() || !CanActivateSkill(PNControlIds.Attack, PNGameplayContentModule.EveAttackEntry) ||
            (empowerment.IsActive && !Controls.IsAllowed(PNControlIds.VoluntaryMotion))) return false;
        try
        {
            BeginActivation(PNGameplayContentModule.EveAttackEntry);
            BeginNormalAttack(DecideAttack(direction));
            CommitActivation(castTarget == null ? null : new[] { castTarget }, direction);
            return true;
        }
        catch (Exception error)
        {
            CancelMoveset();
            Debug.LogException(error, this);
            return false;
        }
    }

    protected override void Update()
        => AdvanceAttack(clock != null && clock.isActiveAndEnabled ? clock.DeltaTime : Time.deltaTime);

    // EN: Eve's current slash is a normal attack; scale its shared VFX/hit timeline only.
    // VI: Đòn chém hiện tại là đánh thường; chỉ đổi nhịp timeline chung của VFX/hitbox.
    protected virtual void AdvanceAttack(float deltaTime)
        => AdvanceMoveset(deltaTime);

    private void FinishAttack(bool cancelled)
    {
        var active = attack;
        var context = attackContext;
        attack = null; attackContext = null;
        if (active == null) return;
        try { if (cancelled) active.Cancel(context); else active.Exit(context); }
        finally { active.Dispose(); }
    }
    protected override void OnDisable()
    {
        if(GetComponent<EveEntityIdentity>()?.IsChangingWorld==true)return;
        try { CancelMoveset(); }
        finally { base.OnDisable(); }
    }
    protected override void OnDestroy()
    {
        try { UnloadMoveset(); }
        finally { base.OnDestroy(); }
    }
    public override void Unload()
    {
        try { UnloadMoveset(); }
        finally { clock = null; base.Unload(); }
    }
}
