using System;
using System.Collections.Generic;

// EN: Four-hook slash action; the owning skill drives phases and waits for visual completion.
// VI: Action chém bốn hook; skill sở hữu điều khiển giai đoạn và chờ VFX hoàn tất.
public class PNSlashAction : PNSkillAction
{
    public override bool HasAuxiliaryTimeline => true;
    public override bool AdvanceAuxiliary(float delta) { Advance(delta); return IsComplete; }
    private PNSkillOperation movementLock;
    private PNSlashVfxOperation slash;
    private PNSlashHitOperation hits;
    private PNSkillActionContext invocation;
    private bool started, performed, ended;
    public virtual bool IsComplete => performed && slash.IsComplete;
    public virtual PNDamage Damage => hits?.Damage;
    public PNArcSlashVisual Visual => slash?.Visual;
    public virtual IReadOnlyList<PNDamageResult> Results => hits?.Results ?? Array.AsReadOnly(Array.Empty<PNDamageResult>());

    public override void Start(PNSkillActionContext context)
    {
        if (started || ended || invocation != null) throw new InvalidOperationException("Slash action cannot start twice.");
        if (context == null || !context.Direction.HasValue ||
            (context.Direction.Value.x == 0f && context.Direction.Value.z == 0f))
            throw new ArgumentException("Slash requires a skill-provided planar direction.", nameof(context));
        // EN: Validate before acquiring permissions; failed visual creation is still cleaned by Cancel.
        // VI: Kiểm tra trước khi khóa; nếu tạo VFX lỗi thì Cancel vẫn dọn được.
        PNSlashHitSettings.Parse(context.Data.Action.Perform);
        invocation = context;
        movementLock = CreateOperation(context, EveEngine.EveId.Parse("base:lock_movement"));
        movementLock.Execute(context, context.Data.Action.Start);
        started = true;
        context.BeginAction();
    }
    public override void Perform(PNSkillActionContext context)
    {
        if (!started || performed || ended || !ReferenceEquals(invocation, context)) throw new InvalidOperationException("Invalid slash Perform transition.");
        performed = true; // A partial failure cannot replay this invocation.
        slash = (PNSlashVfxOperation)CreateOperation(context, EveEngine.EveId.Parse("base:slash_vfx"));
        slash.Execute(context, context.Data.Action.Perform);
        hits = (PNSlashHitOperation)CreateOperation(context, EveEngine.EveId.Parse("base:slash_hit"));
#if UNITY_5_3_OR_NEWER
        if (context is EveSlashContext eve) eve.Configure(slash.Visual);
        ConfigureAttack(slash.Visual);
        slash.Visual.Render(0f);
        hits.BindVisual(slash.Visual);
#endif
        hits.Execute(context, context.Data.Action.Perform);
    }
#if UNITY_5_3_OR_NEWER
    // Mods may change this invocation's copied geometry/curves before the first scan.
    protected virtual void ConfigureAttack(PNArcSlashVisual visual) { }
#endif
    public virtual void Advance(float deltaTime)
    {
        if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (performed && !ended)
        {
            try { hits.Advance(deltaTime); if (!ended) slash.Advance(deltaTime); }
            catch { Dispose(); throw; }
        }
    }
    public override void Exit(PNSkillActionContext context) { context.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext context) { context.CancelEffects(); Dispose(); }
    public override void Dispose()
    {
        if (ended) return;
        ended = true;
        try { hits?.Dispose(); }
        finally { try { slash?.Dispose(); } finally { movementLock?.Dispose(); } }
    }
}
