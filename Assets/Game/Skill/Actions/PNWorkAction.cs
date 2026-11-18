#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Sends one packet of work (PNWork.cs) to the placed things of the grid world around a point: the skill's position, moved `forward` metres along its direction. It is the registered way for a skill to bring
//     things down; nothing else sends work. It touches no damage: a skill that also hurts what it hits runs a damage Action next to it. Only the authority changes a world, so on a replica it does nothing.
//     Tunable values (types and power, amount, radius, forward) live in the registered `perform` payload; the skill only supplies the position and direction.
// VI: Gửi một gói work tới các vật đặt quanh một điểm của world lưới. Là cách đăng ký để skill hạ vật; không đụng damage. Chỉ authority sửa world nên replica không làm gì.
public class PNWorkAction : PNSkillAction
{
    private PNSkillActionContext invocation;
    private PNWorkActionSettings settings;
    private bool performed, ended;
    private int worked, broken;
    // Things the packet was accepted by, and the ones it brought down (for checks and for a skill that wants to react).
    public int Worked => worked;
    public int Broken => broken;

    public static PNWorkActionSettings Validate(JToken payload, EveRegistryHub hub)
    { var parsed = PNWorkActionSettings.Parse(payload); parsed.ValidateReferences(hub); return parsed; }

    public override void Start(PNSkillActionContext context)
    {
        if (invocation != null || ended) throw new InvalidOperationException("Work action cannot start twice.");
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (!context.Position.HasValue) throw new InvalidOperationException("Work action needs a fresh position context.");
        settings = Validate(context.Data.Action.Perform, context.Registry);
        invocation = context;
        context.BeginAction();
    }

    public override void Perform(PNSkillActionContext context)
    {
        if (ended || invocation == null || !ReferenceEquals(context, invocation)) throw new InvalidOperationException("Work action is not active for this context.");
        if (performed) throw new InvalidOperationException("Work action performs once.");
        performed = true;
        PNPlacementWorkService.Apply(context, settings, ref worked, ref broken);
    }

    public override void Exit(PNSkillActionContext context)
    {
        if (ended || invocation == null || !ReferenceEquals(context, invocation)) throw new InvalidOperationException("Work action is not active for this context.");
        if (!performed) throw new InvalidOperationException("Work action must perform before exit.");
        context.CompleteAction(); Dispose();
    }

    public override void Cancel(PNSkillActionContext context)
    {
        if (invocation != null && !ReferenceEquals(context, invocation)) throw new InvalidOperationException("Wrong work invocation context.");
        context.CancelEffects(); Dispose();
    }

    public override void Dispose() { ended = true; invocation = null; settings = null; }
}
#endif
