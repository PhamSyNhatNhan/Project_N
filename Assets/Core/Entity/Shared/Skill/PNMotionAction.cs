using System;
using UnityEngine;
using Newtonsoft.Json.Linq;
using EveEngine;
#if UNITY_5_3_OR_NEWER
using UnityEngine.SceneManagement;
#endif

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
        foreach(var p in obj.Properties())if(p.Name!="distance"&&p.Name!="duration"&&p.Name!="ignoreEntities"&&p.Name!="tags"&&p.Name!="permissionId")throw new FormatException("Unknown motion setting: "+p.Name);
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
    { Distance=distance; }
}

// One local motion invocation, with collision clipping and no damage/projectile behavior.
public class PNMotionAction : PNSkillAction, IPNSkillMotionAction
{
    public override bool HasAuxiliaryTimeline => true;
    public override bool AdvanceAuxiliary(float delta) { Advance(delta); return IsComplete; }
    private Vector3 direction;
    private float distance, duration;
    private bool ignoreEntities;
    private float elapsed, moved;
    private PNSkillActionContext context;
    private PNSkillOperation movementLock;
    private bool performed, disposed;
    private PNMoveExecution movementExecution;
    private Action movementOwner;
    public void BindMovementOwner(Action cancel)
    { if(context!=null || disposed)throw new InvalidOperationException("Bind motion owner before Start."); movementOwner=cancel??throw new ArgumentNullException(nameof(cancel)); }
#if UNITY_5_3_OR_NEWER
    private RaycastHit[] sweepHits = new RaycastHit[16];
    private Rigidbody movingBody;
    private RigidbodyInterpolation previousInterpolation;
#endif
    public virtual bool IsComplete => elapsed >= duration;
    public override void Start(PNSkillActionContext value)
    {
        if (context != null || disposed) throw new InvalidOperationException("Motion already started or disposed.");
        var settings=PNMotionSettings.Parse(value?.Data.Action.Perform);
        if (value != null && !value.Skill.Controls.IsAllowed(settings.PermissionId)) throw new InvalidOperationException("Motion permission denied: " + settings.PermissionId);
        if(value==null || !value.Direction.HasValue)throw new ArgumentException("Motion needs a direction.");
        direction=value.Direction.Value;direction.y=0;
        double magnitude=Math.Sqrt((double)direction.x*direction.x+(double)direction.z*direction.z);
        if(magnitude==0)throw new ArgumentException("Motion needs a planar direction.");
        direction=new Vector3((float)(direction.x/magnitude),0,(float)(direction.z/magnitude));
        duration=settings.Duration;ignoreEntities=settings.IgnoreEntities;
        distance=(value as PNMotionActionContext)?.Distance ?? settings.Distance;
        if(float.IsNaN(distance)||float.IsInfinity(distance)||distance<0||distance>settings.Distance)
            throw new ArgumentOutOfRangeException(nameof(distance),"Evaluated motion distance must be inside registered maximum.");
        var move=value.Skill.GetComponent<PNMove>();
        if(move!=null && movementOwner==null)throw new InvalidOperationException("Motion on PNMove requires its execution owner before Start.");
        context = value ?? throw new ArgumentNullException(nameof(value));
        context.BeginAction();
        if(move!=null && movementOwner!=null)
            movementExecution=move.RegisterMotion(settings.PermissionId,()=>{try { movementOwner?.Invoke(); } finally { Dispose(); }});
        movementLock = CreateOperation(context,EveId.Parse("base:lock_movement")); movementLock.Execute(context, null);
#if UNITY_5_3_OR_NEWER
        // EN: Update-driven motion must use the physics pose, never a delayed interpolated Transform.
        // VI: Chuyển động trong Update dùng vị trí vật lý, không cộng vào Transform nội suy trễ.
        movingBody=context.Skill.GetComponent<Rigidbody>();
        if(movingBody!=null)
        {
            previousInterpolation=movingBody.interpolation;
            movingBody.interpolation=RigidbodyInterpolation.None;
            context.Skill.transform.position=movingBody.position;
            Physics.SyncTransforms();
        }
#endif
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
        if(!PNWorldGameplay.CanExecute(context.Skill))return;
        if(movementExecution!=null && !movementExecution.IsValid)return;
        // Host runs authoritative Skill effects for remote owners but never becomes a second pose writer.
        var move=context.Skill.GetComponent<PNMove>();
        bool remoteHost=move!=null && move.NetworkRole==PNMoveNetworkRole.RemoteOwnerHost;
        if(move!=null && !remoteHost && !move.CanWriteMotion)return;
        elapsed = Math.Min(duration, elapsed + delta);
        float next = distance * elapsed / duration;
        float step = Math.Max(0, next - moved); moved = next;
        if(remoteHost)return;
#if UNITY_5_3_OR_NEWER
        var root = context.Skill;
        var capsule = root.GetComponent<CapsuleCollider>();
        if (capsule != null && step > 0)
        {
            var bounds = capsule.bounds;
            float r = Math.Max(.02f, Math.Min(bounds.extents.x, bounds.extents.z) * .95f);
            // EN: Leave vertical contact skin: a floor-touching cast can report -direction at distance zero.
            // VI: Chừa khe tiếp xúc dọc; cast chạm sàn có thể trả pháp tuyến -direction và khoảng cách 0.
            float half = Math.Max(0, bounds.extents.y - r - .02f);
            int count;
            do
            {
                count = root.gameObject.scene.GetPhysicsScene().CapsuleCast(bounds.center + Vector3.up * half,
                    bounds.center - Vector3.up * half, r, direction, sweepHits, step + .03f, ~0, QueryTriggerInteraction.Ignore);
                if (count < sweepHits.Length) break;
                Array.Resize(ref sweepHits, sweepHits.Length * 2);
            } while (true);
            for (int i = 0; i < count; i++)
            {
                var hit = sweepHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(root.transform)) continue;
                if (ignoreEntities && hit.collider.GetComponentInParent<PNStat>() != null) continue;
                // Physical enemy bodies may overlap; this does not disable hurtbox queries.
                if (root.GetComponent<PNEnemyStat>() != null && (hit.collider.GetComponentInParent<PNEnemyStat>() != null ||
                    Physics.GetIgnoreLayerCollision(root.gameObject.layer, hit.collider.gameObject.layer))) continue;
                if (Vector3.Dot(direction, hit.normal) >= -.001f) continue;
                step = Math.Min(step, Math.Max(0, hit.distance - .03f));
            }
        }
        var position = (movingBody != null ? movingBody.position : root.transform.position) + direction * step;
        if (movingBody != null) movingBody.position = position;
        root.transform.position = position;
        Physics.SyncTransforms();
#else
        context.Skill.transform.position += direction * step;
#endif
    }
    public override void Exit(PNSkillActionContext value) { value.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext value) { value?.CancelEffects(); Dispose(); }
    public override void Dispose()
    {
        if(disposed)return;disposed=true;
        movementExecution?.Dispose(); movementExecution=null; movementOwner=null;
#if UNITY_5_3_OR_NEWER
        if(movingBody!=null) movingBody.interpolation=previousInterpolation;
        movingBody=null;
#endif
        movementLock?.Dispose(); movementLock = null; context = null;
    }
}
