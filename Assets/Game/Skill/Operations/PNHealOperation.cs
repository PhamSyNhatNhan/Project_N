using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;

public sealed class PNHealSettings
{
    public PNSkillValues Values { get; }
    // The owning content module can call this during its startup validation.
    public void ValidateContent(EveRegistryHub hub) => Values.ValidateStats(hub);
    private PNHealSettings(PNSkillValues values) { Values = values; }
    public static PNHealSettings Parse(JToken payload)
    {
        if (!(payload is JObject data)) throw new FormatException("Heal operation requires an object payload.");
        foreach (var field in data.Properties())
            if (field.Name != "value") throw new FormatException("Unknown heal operation field: " + field.Name);
        var values = PNSkillValues.Parse(data["value"]);
        values.ValidateNonnegativeAmounts();
        return new PNHealSettings(values);
    }
}

// This operation explicitly sums caster-based components for one immutable heal packet.
// Other operations may select different stat sources or use components separately.
public class PNHealOperation : PNSkillOperation
{
    private bool executed, disposed;
    private readonly List<PNHealResult> results = new List<PNHealResult>();
    public virtual PNHeal Heal { get; protected set; }
    public virtual IReadOnlyList<PNHealResult> Results => results.AsReadOnly();

    public override void Execute(PNSkillActionContext context, JToken data)
    {
        if (executed || disposed) throw new InvalidOperationException("Heal operation cannot execute twice or after disposal.");
        if (context == null) throw new ArgumentNullException(nameof(context));
        var settings = PNHealSettings.Parse(data);
        settings.ValidateContent(context.Registry);
        var caster = context.Skill.GetComponent<PNStat>();
        if (!context.Skill.IsReady || caster == null || !caster.IsReady || !(caster.Set is PNStatSet senderStats) ||
            !ReferenceEquals(senderStats.Registry, context.Registry))
            throw new InvalidOperationException("Heal operation requires Ready caster stats and skill.");
        var targets = new List<PNStat>();
        PNWorldGameplay.RequireTargets(context.Skill,context.Targets);
        var identities = new HashSet<EveEntityId>();
        foreach (var target in context.Targets)
        {
            var loader = target.GetComponent<EveEntityLoader>();
            if (!target.IsReady || loader == null || !loader.IsReady || target.SessionId != caster.SessionId ||
                !(target.Set is PNStatSet stats) || !ReferenceEquals(stats.Registry, context.Registry) ||
                !EveRegistrySession.Context.Entities.TryGet(target.EntityId, out var registered) || !ReferenceEquals(loader, registered))
                throw new InvalidOperationException("Heal target must be Ready in the same registry and session.");
            if (identities.Add(target.EntityId)) targets.Add(target);
        }
        double amount = 0d;
        foreach (var component in settings.Values.Components) amount += component.Evaluate(caster);
        Heal = caster.CreateHeal(amount);
        executed = true; // Never retry committed targets after a later receiver failure.
        foreach (var target in targets) results.Add(target.ReceiveHeal(Heal));
    }

    public override void Cancel() => Dispose();
    public override void Dispose() { disposed = true; }
}
