using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Skill binding behavior of every "strike the nearest enemy in reach" skill (Docs/SkillNetworkFrameworkPlan.md G7, the trial character's slash). On a press it checks the cooldown Ability,
//     begins the cast, makes ONE decision (`strike_target`: the nearest enemy in reach, `invocation.Choose`), runs the skill's damage action on that target and ends the cast in the same call.
//     It has no network code: the guess on the owner's client, the authority's replay and an observer's view are the framework's (`Choose`, `context.Hit`, the Ability guess scope).
//     No enemy in reach = nothing happens and no cooldown is spent. Profile data: {cooldownAbilityId, actionDataId, reach (metres, default 3)}; the profile declares the Ability and lists the action skill in actionDataIds.
// VI: Behavior binding của skill "chém kẻ địch gần nhất trong tầm": một điểm quyết định (`strike_target`), chạy action sát thương lên mục tiêu đó, không có mã mạng.
public sealed class PNTargetStrikeBehavior : PNSkillBehavior
{
    public const string DecisionPoint = "strike_target";
    public const double DefaultReach = 3d;
    private readonly PNSkillBehaviorContext context;
    private readonly EveId cooldownId, actionId;
    private readonly float reach;
    private bool casting;
    public static int Strikes, NoTarget;
    public PNTargetStrikeBehavior(PNSkillBehaviorContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        var data = PNBuffContentBatch.Object(context.Profile.Data);
        cooldownId = EveId.Parse(PNBuffContentBatch.String(data, "cooldownAbilityId")); actionId = EveId.Parse(PNBuffContentBatch.String(data, "actionDataId"));
        reach = data["reach"] == null ? (float)DefaultReach : (float)PNBuffContentBatch.Number(data, "reach");
    }
    public static void Validate(JToken data, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "cooldownAbilityId", "actionDataId", "reach");
        EveId.Parse(PNBuffContentBatch.String(obj, "cooldownAbilityId")); EveId.Parse(PNBuffContentBatch.String(obj, "actionDataId"));
        if (obj["reach"] != null) { double value = PNBuffContentBatch.Number(obj, "reach"); if (!(value > 0d && value <= 50d)) throw new FormatException("target_strike reach must be in (0, 50]."); }
    }
    public static void ValidateReferences(PNContentReferenceContext context, JToken data)
    {
        var obj = PNBuffContentBatch.Object(data);
        context.Require<EveSkillDefinition>(obj["actionDataId"], "actionDataId");
    }
    public override void Input(PNSkillInputEvent input)
    {
        if (input.Phase != PNSkillInputPhase.Started) return;
        Cast();
    }
    // Public so a fixture or another behavior can cast without input.
    public bool Cast()
    {
        if (casting) return false;
        var cooldown = context.Abilities.GetCooldown(cooldownId);
        if (!cooldown.CanConsume() || !context.TryBegin()) return false;
        casting = true; PNSkillAction action = null; PNSkillActionContext actionContext = null; bool cancelled = false;
        try
        {
            var stat = context.Skill.GetComponent<PNStat>();
            // The one decision of the cast. Written once: the framework runs `compute` on the owner's guess and the authority's own choice, and offers the guess to the authority as a proposal.
            var target = context.Skill.CurrentInvocation.Choose<PNStat>(DecisionPoint,
                () => Nearest(stat), null, reach);
            if (target == null) { cancelled = true; NoTarget++; return false; }
            action = context.CreateAction(actionId, out actionContext, new[] { target });
            action.Start(actionContext); action.Perform(actionContext);
            if (!cooldown.TryConsume()) throw new InvalidOperationException("Strike cooldown changed during the cast.");
            context.Commit(new[] { target });
            action.Exit(actionContext);
            if (actionContext.Mode == PNSkillMode.Authority) Strikes++;
            return true;
        }
        catch
        {
            cancelled = true;
            try { action?.Cancel(actionContext); } catch { }
            throw;
        }
        finally { try { action?.Dispose(); } finally { casting = false; context.Complete(cancelled); } }
    }
    // The skill's own choice: the nearest enemy of the caster's target layers within reach (the held auto target is left alone).
    private PNStat Nearest(PNStat stat)
    {
#if UNITY_5_3_OR_NEWER
        return PNAutoTarget.For(stat).Resolve(reach, stat.TargetLayers.Mask, PNTargetRule.Autotarget(PNTargetMode.Nearest));
#else
        return null;
#endif
    }
    public override void Cancel() { casting = false; }
}
