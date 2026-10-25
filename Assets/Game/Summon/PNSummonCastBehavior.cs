using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Skill binding behavior of every "call a summon" skill (Docs/SummonPlan.md S4): on a press it checks the cooldown Ability, begins the cast, runs the skill's
//     summon action (which creates the summon through create_summon) and ends the cast in the same call. The cooldown is spent only when a summon was actually
//     started (not when a full group refused it or the queue was full). Profile data: {cooldownAbilityId, actionDataId}; the profile must declare the Ability and
//     list the action data skill in actionDataIds.
// VI: Behavior binding của mọi skill "gọi summon": bấm thì kiểm cooldown, mở lượt cast, chạy action summon của skill rồi kết thúc ngay. Chỉ tiêu cooldown khi
//     thật sự đã bắt đầu sinh một summon.
public sealed class PNSummonCastBehavior : PNSkillBehavior
{
    private readonly PNSkillBehaviorContext context;
    private readonly EveId cooldownId, actionId;
    private bool casting;
    public static int Casts, Refusals;
    public PNSummonCastBehavior(PNSkillBehaviorContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        var data = PNBuffContentBatch.Object(context.Profile.Data);
        cooldownId = EveId.Parse(PNBuffContentBatch.String(data, "cooldownAbilityId")); actionId = EveId.Parse(PNBuffContentBatch.String(data, "actionDataId"));
    }
    public static void Validate(JToken data, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "cooldownAbilityId", "actionDataId");
        EveId.Parse(PNBuffContentBatch.String(obj, "cooldownAbilityId")); EveId.Parse(PNBuffContentBatch.String(obj, "actionDataId"));
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
        if (!cooldown.CanConsume() || !context.TryBegin()) { Refusals++; return false; }
        casting = true; PNSkillAction action = null; PNSkillActionContext actionContext = null; bool cancelled = false;
        try
        {
            action = context.CreateAction(actionId, out actionContext);
            action.Start(actionContext); action.Perform(actionContext);
            var status = ((PNSummonAction)action).Status;
            bool started = status == PNSummonStatus.Spawning;
            if (started && !cooldown.TryConsume()) throw new InvalidOperationException("Summon cooldown changed during the cast.");
            context.Commit();
            action.Exit(actionContext);
            if (started) Casts++; else Refusals++;
            return started;
        }
        catch
        {
            cancelled = true;
            try { action?.Cancel(actionContext); } catch { }
            throw;
        }
        finally { try { action?.Dispose(); } finally { casting = false; context.Complete(cancelled); } }
    }
    public override void Cancel() { casting = false; }
}
