#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Puts a locomotion on the skill's body for a while (Docs/SandboxPlan.md, step G12): at Perform it pushes the locomotion of the payload on the body's PNTraversal, on the auxiliary timeline it counts `duration`
//     seconds of the game clock, and it pops the push at Exit, at Cancel and when disposed, whichever comes first, so a cancelled or interrupted skill never leaves the body flying. Where the body ends up when the push ends is
//     the fall rule's business (PNTraversal). It moves nothing itself: a skill that wants a jump or a dash over a hole runs its motion separately. Tunable values live in the registered `perform` payload.
// VI: Đẩy một locomotion lên thân của skill trong một khoảng thời gian: Perform đẩy, timeline phụ đếm `duration`, Exit/Cancel/Dispose gỡ. Không tự di chuyển; chỗ thân đứng khi hết push do luật rơi lo.
public class PNLocomotionAction : PNSkillAction
{
    private PNSkillActionContext invocation;
    private PNLocomotionSettings settings;
    private PNTraversal traversal;
    private int handle;
    private float elapsed;
    private bool performed, ended;

    public override bool HasAuxiliaryTimeline => true;
    public override bool AdvanceAuxiliary(float delta) { Advance(delta); return IsComplete; }
    public virtual bool IsComplete => settings != null && performed && elapsed >= settings.Duration;
    // True while the push is on the body (for checks).
    public bool Pushed => handle != 0;

    public static PNLocomotionSettings Validate(JToken payload, EveRegistryHub hub)
    { var parsed = PNLocomotionSettings.Parse(payload); parsed.ValidateReferences(hub); return parsed; }

    public override void Start(PNSkillActionContext context)
    {
        if (invocation != null || ended) throw new InvalidOperationException("Locomotion action cannot start twice.");
        if (context == null) throw new ArgumentNullException(nameof(context));
        settings = Validate(context.Data.Action.Perform, context.Registry);
        traversal = context.Skill.GetComponent<PNTraversal>() ?? throw new InvalidOperationException("Locomotion action needs a body with a traversal (an entity spawned by the factory).");
        invocation = context;
        context.BeginAction();
    }

    public override void Perform(PNSkillActionContext context)
    {
        if (ended || invocation == null || !ReferenceEquals(context, invocation)) throw new InvalidOperationException("Locomotion action is not active for this context.");
        if (performed) throw new InvalidOperationException("Locomotion action performs once.");
        performed = true;
        handle = traversal.Push(settings.Locomotion);
    }

    public virtual void Advance(float delta)
    {
        if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0f) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!performed || ended || settings == null || IsComplete || delta == 0f) return;
        if (!PNWorldGameplay.CanExecute(invocation.Skill)) return;
        elapsed = Math.Min(settings.Duration, elapsed + delta);
    }

    public override void Exit(PNSkillActionContext context)
    {
        if (ended || invocation == null || !ReferenceEquals(context, invocation)) throw new InvalidOperationException("Locomotion action is not active for this context.");
        if (!performed) throw new InvalidOperationException("Locomotion action must perform before exit.");
        context.CompleteAction(); Dispose();
    }

    public override void Cancel(PNSkillActionContext context)
    {
        if (invocation != null && !ReferenceEquals(context, invocation)) throw new InvalidOperationException("Wrong locomotion invocation context.");
        context?.CancelEffects(); Dispose();
    }

    public override void Dispose()
    {
        if (handle != 0 && traversal != null) traversal.Pop(handle);
        handle = 0; traversal = null; ended = true; invocation = null; settings = null;
    }
}
#endif
