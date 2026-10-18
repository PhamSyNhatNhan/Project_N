using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Instant damage action with explicit targets; spatial hit detection is supplied by the caller.
// VI: Action damage tức thời có mục tiêu tường minh; caller cung cấp việc dò trúng theo không gian.
public class PNDamageAction : PNSkillAction
{
    private PNSkillActionContext invocation;
    private PNStat caster;
    private PNSkillValues values;
    private bool canCrit;
    private double? iframeDuration;
    private Guid sessionId;
    private bool performed, ended;
    private readonly List<PNDamageResult> results = new List<PNDamageResult>();
    public virtual PNDamage Damage { get; protected set; }
    public virtual IReadOnlyList<PNDamageResult> Results => results.AsReadOnly();

    public override void Start(PNSkillActionContext context)
    {
        if (invocation != null || ended) throw new InvalidOperationException("Damage action cannot start twice.");
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (!(context.Data.Action.Perform is JObject perform)) throw new FormatException("Damage perform payload must be an object.");
        var parsed = PNSkillValues.Parse(perform["value"]);
        if (perform["canCrit"] != null && perform["canCrit"].Type != JTokenType.Boolean)
            throw new FormatException("Damage canCrit must be a boolean.");
        canCrit = perform["canCrit"] == null || perform["canCrit"].Value<bool>();
        iframeDuration = PNDamage.ParseIFrameDuration(perform);
        var owner = context.Skill.GetComponent<PNStat>();
        if (!context.Skill.IsReady || owner == null || !owner.IsReady || !(owner.Set is PNStatSet stats) ||
            !ReferenceEquals(stats.Registry, context.Registry)) throw new InvalidOperationException("Damage action requires the caster's Ready game stats and skill.");
        stats.ResolveDamageType(context.Data.Action.PerformTags);
        sessionId = owner.SessionId;
        caster = owner;
        values = parsed;
        invocation = context;
        context.BeginAction();
    }

    public override void Perform(PNSkillActionContext context)
    {
        RequireInvocation(context);
        if (performed || !context.Skill.IsReady || caster.SessionId != sessionId)
            throw new InvalidOperationException("Invalid damage Perform transition or session.");
        var targets = new List<PNStat>();
        PNWorldGameplay.RequireTargets(context.Skill,context.Targets);
        var identities = new HashSet<EveEntityId>();
        foreach (var target in context.Targets)
        {
            var loader = target == null ? null : target.GetComponent<EveEntityLoader>();
            if (target == null || !target.IsReady || loader == null || !loader.IsReady || target.SessionId != sessionId ||
                !(target.Set is PNStatSet stats) || !ReferenceEquals(stats.Registry, context.Registry) ||
                !EveRegistrySession.Context.Entities.TryGet(target.EntityId, out var registered) || !ReferenceEquals(loader, registered))
                throw new InvalidOperationException("Damage target must be Ready in the same registry and session.");
            if (identities.Add(target.EntityId)) targets.Add(target);
        }
        // This action explicitly defines one damage amount as the sum of its components.
        double amount = 0d;
        foreach (var component in values.Components) amount += component.Evaluate(caster);
        Damage = caster.CreateDamage(amount, canCrit, iframeDuration, context.Data.Action.PerformTags.ToArray());
        // EN: Mark spent before delivery; a later receiver failure must not retry already committed hits.
        // VI: Đánh dấu đã thi triển trước khi gửi; receiver sau lỗi không được thử lại hit đã ghi.
        performed = true;
        var capture = caster.CaptureCombatSource();
        foreach (var target in targets)
        {
            results.Add(context.ReceiveHit(target, target.transform.position, Damage, amount, capture));
        }
    }

    public override void Exit(PNSkillActionContext context)
    {
        RequireInvocation(context);
        if (!performed) throw new InvalidOperationException("Damage action must perform before exit.");
        context.CompleteAction(); Dispose();
    }

    public override void Cancel(PNSkillActionContext context)
    {
        if (invocation != null && !ReferenceEquals(context, invocation)) throw new InvalidOperationException("Wrong damage invocation context.");
        context.CancelEffects(); Dispose();
    }

    private void RequireInvocation(PNSkillActionContext context)
    {
        if (ended || invocation == null || !ReferenceEquals(context, invocation))
            throw new InvalidOperationException("Damage action is not active for this context.");
    }

    public override void Dispose()
    {
        ended = true;
        invocation = null;
        caster = null;
        values = null;
    }
}
