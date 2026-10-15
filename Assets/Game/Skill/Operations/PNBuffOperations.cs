using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

public class PNApplyBuffOperation : PNSkillOperation
{
    private bool executed;
    private readonly List<PNBuffReceipt> results = new List<PNBuffReceipt>();
    public IReadOnlyList<PNBuffReceipt> Results => results.AsReadOnly();
    public static EveId Validate(JToken payload, EveRegistryHub hub)
    {
        var data = PNBuffContentBatch.Object(payload); PNBuffContentBatch.Fields(data, "buffId", "input");
        var id = EveId.Parse(PNBuffContentBatch.String(data, "buffId")); hub.Buffs().GetEffective(id).ValidateInput(data["input"]); return id;
    }
    public override void Execute(PNSkillActionContext context, JToken payload)
    {
        if (executed) throw new InvalidOperationException("Operation has already executed or been disposed.");
        if (context == null || !context.Skill.IsReady) throw new InvalidOperationException("Ready skill context required.");
        var id = Validate(payload, context.Registry); var source = PNBuffSource.Capture(context.Skill.GetComponent<PNStat>());
        var targets = context.Targets.Distinct().Select(target =>
        {
            var manager = target.GetComponent<PNBuffManager>();
            if (manager == null || !manager.IsReady || manager.Buffs.SessionId != source.SessionId) throw new InvalidOperationException("Buff operation requires Ready targets in source session."); return manager;
        }).ToArray();
        executed = true;
        foreach (var manager in targets) results.Add(manager.Buffs.Apply(new PNBuffApplyRequest(id, source, ((JObject)payload)["input"])));
    }
    public override void Dispose() { executed = true; }
}

public class PNRemoveBuffOperation : PNSkillOperation
{
    private bool executed;
    protected virtual bool Bypass => false;
    public static EveId Validate(JToken payload, EveRegistryHub hub)
    {
        var data = PNBuffContentBatch.Object(payload); PNBuffContentBatch.Fields(data, "buffId");
        var id = EveId.Parse(PNBuffContentBatch.String(data, "buffId")); hub.Buffs().Get(id); return hub.Buffs().ResolveIdentity(id);
    }
    public override void Execute(PNSkillActionContext context, JToken payload)
    {
        if (executed) throw new InvalidOperationException("Operation has already executed or been disposed.");
        if (context == null || !context.Skill.IsReady) throw new InvalidOperationException("Ready skill context required.");
        var id = Validate(payload, context.Registry);
        var targets = context.Targets.Distinct().Select(x => x.GetComponent<PNBuffManager>()).ToArray();
        if (targets.Any(x => x == null || !x.IsReady || !ReferenceEquals(x.Buffs.Registry, context.Registry))) throw new InvalidOperationException("Removal requires Ready targets.");
        executed = true;
        foreach (var manager in targets) manager.Buffs.RemoveMatching(x => x.Id == id, Bypass ? PNBuffEndReason.Bypass : PNBuffEndReason.Dispelled, Bypass);
    }
    public override void Dispose() { executed = true; }
}
public class PNBypassRemoveBuffOperation : PNRemoveBuffOperation { protected override bool Bypass => true; }

// Selection is explicit: never trigger an arbitrary instance by Buff ID.
public class PNTriggerBuffOperation : PNSkillOperation
{
    private bool executed;
    protected virtual bool ChangesStacks => false;
    private readonly List<PNBuffReceipt> results = new List<PNBuffReceipt>();
    public IReadOnlyList<PNBuffReceipt> Results => results.AsReadOnly();
    public static EveId Validate(JToken payload, EveRegistryHub hub, bool changesStacks = false)
    {
        var data = PNBuffContentBatch.Object(payload);
        PNBuffContentBatch.Fields(data, changesStacks ? new[] { "buffId", "sourceSelection", "delta" } : new[] { "buffId", "sourceSelection" });
        var id = EveId.Parse(PNBuffContentBatch.String(data, "buffId")); hub.Buffs().Get(id);
        string selection = PNBuffContentBatch.String(data, "sourceSelection");
        if (selection != "Caster" && selection != "All") throw new FormatException("sourceSelection must be Caster or All.");
        if (changesStacks) PNBuffContentBatch.Integer(data, "delta");
        return hub.Buffs().ResolveIdentity(id);
    }
    public override void Execute(PNSkillActionContext context, JToken payload)
    {
        if (executed) throw new InvalidOperationException("Operation already executed or disposed.");
        if (context == null || !context.Skill.IsReady) throw new InvalidOperationException("Ready skill context required.");
        var id = Validate(payload, context.Registry, ChangesStacks); var data = (JObject)payload;
        var caster = context.Skill.GetComponent<PNStat>();
        if (caster == null || !caster.IsReady) throw new InvalidOperationException("Ready caster required.");
        var collections = context.Targets.Distinct().Select(x => x.GetComponent<PNBuffManager>()).ToArray();
        if (collections.Any(x => x == null || !x.IsReady || !ReferenceEquals(x.Buffs.Registry, context.Registry) || x.Buffs.SessionId != caster.SessionId))
            throw new InvalidOperationException("Ready same-session targets required.");
        executed = true;
        foreach (var manager in collections)
            foreach (var instance in manager.Buffs.Snapshot().Where(x => x.Id == id && (data["sourceSelection"].Value<string>() == "All" || x.SourceId == caster.EntityId)))
                results.Add(ChangesStacks ? manager.Buffs.ChangeStacks(instance.Handle, PNBuffContentBatch.Integer(data, "delta")) : manager.Buffs.Trigger(instance.Handle));
    }
    public override void Dispose() { executed = true; }
}
public class PNChangeBuffStacksOperation : PNTriggerBuffOperation { protected override bool ChangesStacks => true; }
