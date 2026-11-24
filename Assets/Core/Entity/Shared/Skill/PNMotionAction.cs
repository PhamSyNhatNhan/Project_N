using System;
using UnityEngine;
using Newtonsoft.Json.Linq;
using EveEngine;

// EN: Motion parameters are registered action data; skills only evaluate a shorter distance.
// VI: Tham số di chuyển nằm trong dữ liệu action đã đăng ký; skill chỉ giới hạn khoảng cách.
public interface IPNSkillMotionAction { void BindMovementOwner(Action cancel); }
public enum PNMotionOutcome { Running, Completed, Blocked, Cancelled, BudgetExceeded, InvalidLanding }
public interface IPNSkillActionOutcome { PNMotionOutcome Outcome { get; } }
public interface IPNSkillDeferredCleanup { bool CleanupPending { get; } }
public sealed class PNMotionSettings
{
    public float Distance { get; private set; }
    public float Duration { get; private set; }
    public bool IgnoreEntities { get; private set; }
    public EveId PermissionId { get; private set; }
    public static PNMotionSettings Parse(JToken token)
    {
        if(!(token is JObject obj))throw new FormatException("Motion payload is required.");
        foreach(var p in obj.Properties())if(p.Name!="distance"&&p.Name!="duration"&&p.Name!="ignoreEntities"&&p.Name!="tags"&&p.Name!="permissionId"&&p.Name!="look")throw new FormatException("Unknown motion setting: "+p.Name);
        float Number(string key)
        {
            if(obj[key]==null || (obj[key].Type!=JTokenType.Integer&&obj[key].Type!=JTokenType.Float))throw new FormatException("Missing motion setting: "+key);
            float n=obj[key].Value<float>();if(float.IsNaN(n)||float.IsInfinity(n)||n<0||n>10000)throw new FormatException("Invalid motion setting: "+key);return n;
        }
        var result=new PNMotionSettings{Distance=Number("distance"),Duration=Number("duration")};
        if(result.Duration<=0 || obj["ignoreEntities"]?.Type!=JTokenType.Boolean)throw new FormatException("Invalid motion duration or collision setting.");
        if (obj["permissionId"] != null && obj["permissionId"].Type != JTokenType.String) throw new FormatException("Motion permissionId must be an ID.");
        result.PermissionId = obj["permissionId"] == null ? PNControlIds.VoluntaryMotion : EveId.Parse(obj["permissionId"].Value<string>());
        result.IgnoreEntities=obj["ignoreEntities"].Value<bool>();return result;
    }
}
public sealed class PNMotionActionContext : PNSkillActionContext
{
    public float Distance { get; }
    public PNMotionActionContext(PNSkillActionContext source,float distance)
        : base(source.Skill,source.SkillId,source.Data,source.Registry,source.Targets,source.Direction,source.Invocation,source.AllowContributions,source.Position)
    { Distance=distance; Mode=source.Mode; }
}

// One local motion invocation, with collision clipping and no damage/projectile behavior.
public class PNMotionAction : PNSkillAction, IPNSkillMotionAction
{
    public override bool HasAuxiliaryTimeline => true;
    public override bool AdvanceAuxiliary(float delta) { Advance(delta); return IsComplete; }
    private float distance, duration;
    private float elapsed, moved;
    private PNSkillActionContext context;
    private PNSkillPlanarMotion motion;
    private bool performed, disposed;
    private Action movementOwner;
    public void BindMovementOwner(Action cancel)
    { if(context!=null || disposed)throw new InvalidOperationException("Bind motion owner before Start."); movementOwner=cancel??throw new ArgumentNullException(nameof(cancel)); }
    public virtual bool IsComplete => elapsed >= duration;
    public override void Start(PNSkillActionContext value)
    {
        if (context != null || disposed) throw new InvalidOperationException("Motion already started or disposed.");
        var settings=PNMotionSettings.Parse(value?.Data.Action.Perform);
        if(value==null || !value.Direction.HasValue)throw new ArgumentException("Motion needs a direction.");
        var direction=value.Direction.Value;direction.y=0;
        double magnitude=Math.Sqrt((double)direction.x*direction.x+(double)direction.z*direction.z);
        if(magnitude==0)throw new ArgumentException("Motion needs a planar direction.");
        direction=new Vector3((float)(direction.x/magnitude),0,(float)(direction.z/magnitude));
        duration=settings.Duration;
        distance=(value as PNMotionActionContext)?.Distance ?? settings.Distance;
        if(float.IsNaN(distance)||float.IsInfinity(distance)||distance<0||distance>settings.Distance)
            throw new ArgumentOutOfRangeException(nameof(distance),"Evaluated motion distance must be inside registered maximum.");
        motion = new PNSkillPlanarMotion(value, settings, direction, movementOwner != null);
        context = value;
        try
        {
            context.BeginAction();
            motion.Begin(settings.PermissionId,
                () => { try { movementOwner?.Invoke(); } finally { Dispose(); } },
                () => CreateOperation(context, EveId.Parse("base:lock_movement")));
        }
        catch
        {
            motion?.Dispose();
            motion = null;
            throw;
        }
    }
    public override void Perform(PNSkillActionContext value)
    {
        if(disposed || performed || !ReferenceEquals(value,context))throw new InvalidOperationException("Invalid motion perform.");
        performed=true;
    }
    public virtual void Advance(float delta)
    {
        if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!performed || disposed || context == null || IsComplete || delta == 0) return;
        if (motion?.CanAdvance != true) return;
        elapsed = Math.Min(duration, elapsed + delta);
        float next = distance * elapsed / duration;
        float step = Math.Max(0, next - moved);
        moved = next;
        motion.Step(step);
    }
    public override void Exit(PNSkillActionContext value) { value.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext value) { value?.CancelEffects(); Dispose(); }
    public override void Dispose()
    {
        if(disposed)return;disposed=true;
        motion?.Dispose();
        motion = null;
        movementOwner = null;
        context = null;
    }
}
