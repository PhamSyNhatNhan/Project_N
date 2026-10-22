using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// Registered templates are reusable schemas/factories, not automatically applied gameplay effects.
public static class PNBuffGameplayContent
{
    public static void Register(EveRegistryHub hub)
    {
        var registry = hub.Buffs();
        PNBuffElementPresenterContent.Register(hub);
        void Add(string name, Func<JToken, PNBuffBehavior> factory, string json, string[] tags = null, bool multiple = false)
        {
            var payload = JObject.Parse(json);
            // Stock timed templates opt in to the replica-derived countdown (explicit per definition; never inherited).
            registry.Register(new PNBuffDefinition(EveId.Parse("project_n:" + name), EveId.Parse("project_n:" + name), factory, payload,
                (tags ?? new[] { "neutral" }).Select(x => EveId.Parse("project_n:" + x)), multiple, multiple ? 4 : 1,
                validate: (data, content) => PNBuffBuiltinSettings.Validate(name, data, content),
                validateReferences: (data, context) => PNBuffPayloadReferences.Template(name, data, context)).WithCountdown(PNBuffCountdown.BottomRight));
        }
        Add("buff_stat_template", data => new PNBuffStatBehavior(data), "{'duration':5,'clock':'Game','statId':'base:attack_power','value':10,'group':0,'maximumStacks':3}", new[] { "buff" });
        void Dot(string name, string json, string[] tags, string iconPath = "", string glow = "")
        {
            registry.Register(new PNBuffDefinition(EveId.Parse("project_n:" + name), EveId.Parse("project_n:buff_dot"), data => new PNBuffDotBehavior(data), JObject.Parse(json),
                tags.Select(x => EveId.Parse("project_n:" + x)), scope: PNBuffInstanceScope.Source, iconPath: iconPath, glow: glow,
                validate: (data, content) => PNBuffBuiltinSettings.Validate("buff_dot_template", data, content), validateInput: PNBuffDotBehavior.ValidateInput,
                validateReferences: (data, context) => PNBuffPayloadReferences.Template("buff_dot_template", data, context), presenterId: PNBuffElementPresenterContent.Id)
                .WithCountdown(PNBuffCountdown.BottomRight));
        }
        Dot("buff_dot_template", "{'duration':5,'clock':'Game','amount':10,'attackRatio':0,'maximumStacks':1,'period':1,'damageTags':['base:physical'],'iframeDuration':0,'canCrit':false}", new[] { "debuff", "dot" });
        Dot("burn", "{'duration':15,'clock':'Entity','amount':0,'attackRatio':0.05,'maximumStacks':35,'period':0.8,'damageTags':['base:magic','base:fire'],'iframeDuration':0,'canCrit':false,'visual':{'kind':'Fire','slot':0,'color':[1,0.18,0.04],'size':0.26}}", new[] { "debuff", "dot", "fire" }, "PNBuffIcons/PNBurn", "red");
        Dot("electrocution", "{'duration':5,'clock':'Entity','amount':0,'attackRatio':0.10,'additionalStackAttackRatio':0.10,'additionalStackGrowth':0.03,'maximumStacks':10,'period':0.4,'damageTags':['base:magic','base:lightning'],'iframeDuration':0,'canCrit':false,'visual':{'kind':'Lightning','slot':0,'color':[1,0.85,0.08],'size':0.26}}", new[] { "debuff", "dot", "lightning" }, "PNBuffIcons/PNElectrocution", "gold");
        Dot("frostbite", "{'duration':12,'clock':'Entity','amount':0,'attackRatio':1.50,'additionalStackAttackRatio':0.50,'additionalStackGrowth':0,'maximumStacks':5,'period':1.5,'damageTags':['base:magic','base:ice'],'iframeDuration':0,'canCrit':false,'visual':{'kind':'Ice','slot':0,'color':[0.22,0.68,1],'size':0.117}}", new[] { "debuff", "dot", "ice" }, "PNBuffIcons/PNFrostbite", "blue");
        foreach (var element in new[] { "fire", "ice", "lightning" })
        {
            int slot = element == "fire" ? -1 : element == "ice" ? 0 : 1;
            string color = element == "fire" ? "[1,0.18,0.04]" : element == "ice" ? "[0.22,0.68,1]" : "[1,0.85,0.08]";
            // The vulnerability shares its element's glow colour (fire red, ice blue, lightning gold).
            string glow = element == "fire" ? "red" : element == "ice" ? "blue" : "gold";
            string iconPath = "PNBuffIcons/PN" + (element == "fire" ? "Fire" : element == "ice" ? "Ice" : "Lightning") + "Vulnerability";
            registry.Register(new PNBuffDefinition(EveId.Parse("project_n:" + element + "_vulnerability"), EveId.Parse("project_n:buff_vulnerability"), data => new PNBuffVulnerabilityBehavior(data),
                JObject.Parse("{'duration':15,'clock':'Entity','tagId':'base:" + element + "','value':0.75,'group':2,'calculationId':'base:mitigation','visual':{'kind':'Arrow','slot':" + slot + ",'color':" + color + ",'size':0.098}}"),
                new[] { "debuff", "vulnerability", element }.Select(x => EveId.Parse("project_n:" + x)),
                iconPath: iconPath, glow: glow,
                validate: PNBuffVulnerabilityBehavior.Validate, validateReferences: (data, context) => PNBuffPayloadReferences.Modifier(data, context, "tagId"), presenterId: PNBuffElementPresenterContent.Id)
                .WithCountdown(PNBuffCountdown.BottomRight));
        }
        Add("buff_bomb_template", data => new PNBuffDamageBehavior(data, true), "{'duration':3,'clock':'Game','amount':20,'damageTags':['base:physical'],'sourceMode':'Snapshot','iframeDuration':0,'canCrit':false}", new[] { "debuff" }, true);
        Add("buff_control_template", data => new PNBuffControlBehavior(data), "{'duration':1,'clock':'Game','permissionIds':['base:move','base:jump','base:skill_all','base:motion_voluntary'],'interrupt':true}");
        Add("buff_stack_limit_template", data => new PNBuffStackGrantBehavior(data), "{'duration':5,'clock':'Game','targetBuffId':'project_n:buff_stat_template','amount':2}", new[] { "buff" });
        Add("buff_override_template", data => new PNBuffOverrideBehavior(data), "{'duration':5,'clock':'Game','targetBuffId':'project_n:buff_stat_template','profileId':'project_n:buff_control_template','priority':0}");
        Add("buff_immunity_template", data => new PNBuffImmunityBehavior(data), "{'duration':5,'clock':'Game','buffIds':['project_n:buff_dot_template'],'tagIds':[],'purge':true}", new[] { "buff" });
        Add("buff_skill_effect_template", data => new PNBuffSkillEffectBehavior(data), "{'duration':5,'clock':'Game','contribution':{'readMode':'Snapshot','categoryIds':['base:attack'],'effects':[{'trigger':'OnHit','kind':'Operation','id':'base:apply_buff','data':{'buffId':'base:burn'},'target':'HitTarget'}]}}", new[] { "buff" });
        PNTimeScaleBuffContent.Register(hub);
        EveBurstBuffContent.Register(hub);
        PNLoricaBuffContent.Register(hub);
        foreach (var definition in registry.Snapshot().Values.Where(x => x.Id.Namespace == "project_n"))
            registry.RegisterBase(new EveId("base", definition.Id.Path), definition.Id, "project_n", compatible: (original, replacement) => true);
    }
}

public static class PNBuffBuiltinSettings
{
    public static void RequireAmount(double value) => PNBuffCollection.Seconds(value, true);
    public static void Validate(string template, JToken data, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(data);
        string[] extra;
        switch (template)
        {
            case "buff_stat_template": extra = new[] { "statId", "value", "group", "maximumStacks", "calculationId", "contributionId" }; break;
            case "buff_dot_template": extra = new[] { "amount", "attackRatio", "maximumStacks", "period", "damageTags", "iframeDuration", "canCrit" }; break;
            case "buff_bomb_template": extra = new[] { "amount", "damageTags", "sourceMode", "iframeDuration", "canCrit" }; break;
            case "buff_control_template": extra = new[] { "permissionIds", "interrupt" }; break;
            case "buff_stack_limit_template": extra = new[] { "targetBuffId", "amount" }; break;
            case "buff_override_template": extra = new[] { "targetBuffId", "profileId", "priority" }; break;
            case "buff_immunity_template": extra = new[] { "buffIds", "tagIds", "purge" }; break;
            case "buff_skill_effect_template": extra = new[] { "contribution" }; break;
            default: throw new FormatException("Unknown built-in template.");
        }
        PNBuffContentBatch.Fields(obj, new[] { "duration", "clock" }.Concat(extra).Concat(template == "buff_dot_template" ? new[] { "additionalStackAttackRatio", "additionalStackGrowth", "visual" } : Array.Empty<string>()).ToArray());
        if (template == "buff_dot_template") PNBuffElementVisualSettings.Validate(obj["visual"]);
        PNBuffCollection.Seconds(PNBuffContentBatch.Number(obj, "duration"), false);
        Clock(obj);
        if (template == "buff_skill_effect_template" && hub.IsSealed) new PNSkillContribution(obj["contribution"], hub);
        if (template == "buff_stat_template")
        {
            PNStatModifierData.Validate(obj, hub);
            if (PNBuffContentBatch.Integer(obj, "group") < 0 || PNBuffContentBatch.Integer(obj, "maximumStacks") < 1) throw new FormatException("Invalid stat Buff limits.");
        }
        if (template == "buff_dot_template" || template == "buff_bomb_template")
        {
            RequireAmount(PNBuffContentBatch.Number(obj, "amount"));
            if (template == "buff_dot_template")
            {
                PNBuffCollection.Seconds(PNBuffContentBatch.Number(obj, "period"), false);
                RequireAmount(PNBuffContentBatch.Number(obj, "attackRatio"));
                bool incremental = obj["additionalStackAttackRatio"] != null;
                if (incremental != (obj["additionalStackGrowth"] != null)) throw new FormatException("Incremental DOT needs both additional-stack fields.");
                if (incremental) { RequireAmount(PNBuffContentBatch.Number(obj, "additionalStackAttackRatio")); RequireAmount(PNBuffContentBatch.Number(obj, "additionalStackGrowth")); }
                if (PNBuffContentBatch.Integer(obj, "maximumStacks") < 1) throw new FormatException("DOT stack cap must be positive.");
            }
            var damageTags = Ids(obj, "damageTags"); foreach (var id in damageTags) hub.Tags().Get(id);
            if (hub.IsSealed) new PNStatSet(hub).ResolveDamageType(damageTags);
            PNBuffContentBatch.Number(obj, "iframeDuration"); PNBuffContentBatch.Bool(obj, "canCrit");
            if (template == "buff_bomb_template")
            { string mode = PNBuffContentBatch.String(obj, "sourceMode"); if (mode != "Live" && mode != "Snapshot") throw new FormatException("Source mode must be Live or Snapshot."); }
        }
        if (template == "buff_control_template")
        {
            if (Ids(obj, "permissionIds").Length == 0) throw new FormatException("Control Buff needs permissions.");
            foreach (var id in Ids(obj, "permissionIds")) hub.ControlPermissions().Get(id); PNBuffContentBatch.Bool(obj, "interrupt");
        }
        if (template == "buff_stack_limit_template") PNBuffContentBatch.Integer(obj, "amount");
        if (template == "buff_override_template") PNBuffContentBatch.Integer(obj, "priority");
        if (template == "buff_stack_limit_template" || template == "buff_override_template")
        {
            EveId.Parse(PNBuffContentBatch.String(obj, "targetBuffId"));
            if (hub.IsSealed) hub.Buffs().Get(EveId.Parse(PNBuffContentBatch.String(obj, "targetBuffId")));
        }
        if (template == "buff_override_template")
        {
            EveId.Parse(PNBuffContentBatch.String(obj, "profileId"));
            if (hub.IsSealed) hub.Buffs().Get(EveId.Parse(PNBuffContentBatch.String(obj, "profileId")));
        }
        if (template == "buff_immunity_template")
        {
            var ids = Ids(obj, "buffIds"); var tags = Ids(obj, "tagIds"); if (ids.Length + tags.Length == 0) throw new FormatException("Empty immunity.");
            PNBuffContentBatch.Bool(obj, "purge");
            if (hub.IsSealed) { foreach (var id in ids) hub.Buffs().Get(id); foreach (var id in tags) hub.BuffTags().Get(id); }
        }
    }
    public static PNBuffClock Clock(JObject obj)
    {
        string value = PNBuffContentBatch.String(obj, "clock");
        if (value == "Game") return PNBuffClock.Game;
        if (value == "Entity") return PNBuffClock.Entity;
        if (value == "EntityWithoutMinimum") return PNBuffClock.EntityWithoutMinimum;
        throw new FormatException("Unknown Buff clock.");
    }
    public static EveId[] Ids(JObject obj, string field)
    {
        if (!(obj[field] is JArray)) throw new FormatException("ID array required: " + field);
        return PNBuffContentBatch.Array(obj[field]).Select(x => x.Type == JTokenType.String ? EveId.Parse(x.Value<string>()) : throw new FormatException("ID string required.")).ToArray();
    }
}

public abstract class PNBuffTimedBehavior : PNBuffBehavior
{
    protected readonly JObject Data;
    private PNBuffPresentationWriter watchedPresentation;
    private long? displaySeconds;
    protected PNBuffTimedBehavior(JToken data) { Data = (JObject)data.DeepClone(); }
    protected void ObserveDuration()
    {
        if (ReferenceEquals(watchedPresentation, Presentation)) return;
        watchedPresentation = Presentation; displaySeconds = null;
        Presentation.WatchRemainingSeconds(seconds => { displaySeconds = seconds; PublishNumbers(); });
    }
    protected virtual void PublishNumbers()
    {
        long? stacks = StackCount.HasValue ? (long?)StackCount.Value : null;
        Presentation?.Publish(new PNBuffNumbers(topRight: stacks, bottomRight: displaySeconds));
    }
    protected void BeginTimer(PNBuffContext context)
    { context.SetClock(PNBuffBuiltinSettings.Clock(Data)); context.SetDuration(PNBuffContentBatch.Number(Data, "duration")); ObserveDuration(); }
    public override void OnApply(PNBuffContext context, JToken input) => BeginTimer(context);
    public override void OnBehaviorStarted(PNBuffContext context, bool migrated) { if (!migrated) BeginTimer(context); ObserveDuration(); OnResume(context); }
}

public sealed class PNBuffStatBehavior : PNBuffTimedBehavior
{
    public override bool EventDrivenReplication => true;
    private int stacks = 1;
    private IDisposable modifier;
    private readonly EveStatModifier template;
    private readonly double baseValue;
    private PNBuffResourceScope modifierScope;
    private double appliedValue;
    public PNBuffStatBehavior(JToken data) : base(data) { template = PNStatModifierData.Parse(Data); baseValue = PNBuffContentBatch.Number(Data, "value"); }
    public override double? StackCount => stacks;
    public override bool Supports(Type contract) => contract == typeof(PNBuffStackLimitContract) || contract == typeof(PNBuffValueContract);
    public override void ValidateContributions(PNBuffContext context, Type contract, System.Collections.Generic.IReadOnlyList<PNBuffContribution> proposed)
    {
        if (!Supports(contract)) return;
        string field = contract == typeof(PNBuffStackLimitContract) ? "maximum" : "value";
        var numbers = proposed.OfType<PNBuffNumberContribution>().ToArray();
        if (numbers.Length != proposed.Count || numbers.Any(x => x.Field != field)) throw new ArgumentException("Invalid stat Buff contribution field/type.");
        if (contract == typeof(PNBuffStackLimitContract))
        {
            double value = PNBuffContentBatch.Integer(Data, "maximumStacks") + numbers.Sum(x => x.Value);
            if (value < 1 || value > int.MaxValue || value != Math.Truncate(value)) throw new ArgumentException("Invalid proposed stack limit.");
        }
    }
    private int Maximum(PNBuffContext context)
    {
        double maximum = context.Additive(typeof(PNBuffStackLimitContract), "maximum", PNBuffContentBatch.Integer(Data, "maximumStacks"));
        if (maximum < 1 || maximum > int.MaxValue || maximum != Math.Truncate(maximum)) throw new InvalidOperationException("Invalid effective stack limit."); return (int)maximum;
    }
    private void UpdateModifier(PNBuffContext context)
    {
        stacks = Math.Min(stacks, Maximum(context));
        double value = context.Additive(typeof(PNBuffValueContract), "value", baseValue) * stacks;
        if (modifier == null || !ReferenceEquals(modifierScope, context.Resources) || modifierScope.IsDisposed || appliedValue != value)
        { modifier?.Dispose(); modifier = context.ModifyStats(new[] { new EveStatModifier(template.Id, value, template.Group, template.CalculationId, template.ContributionId) }); modifierScope = context.Resources; appliedValue = value; }
        PublishNumbers();
    }
    public override void OnApply(PNBuffContext context, JToken input) { base.OnApply(context, input); UpdateModifier(context); }
    public override bool OnReapply(PNBuffContext context, PNBuffApplyRequest request)
    { if (stacks < Maximum(context)) stacks++; context.SetDuration(PNBuffContentBatch.Number(Data, "duration")); UpdateModifier(context); return true; }
    public override void OnContributionsChanged(PNBuffContext context)
    { stacks = Math.Min(stacks, Maximum(context)); if (!context.IsSuspended) UpdateModifier(context); else PublishNumbers(); }
    public override void OnResume(PNBuffContext context) => UpdateModifier(context);
    public override void OnCleanup(PNBuffContext context) { modifier?.Dispose(); modifier = null; }
}

public sealed class PNBuffControlBehavior : PNBuffTimedBehavior
{
    public override bool EventDrivenReplication => true;
    public PNBuffControlBehavior(JToken data) : base(data) { }
    public override void OnApply(PNBuffContext context, JToken input)
    {
        base.OnApply(context, input); OnResume(context);
        if (PNBuffContentBatch.Bool(Data, "interrupt")) context.TryInterrupt(context.Snapshot.Id);
    }
    public override void OnResume(PNBuffContext context) => context.Lock(PNBuffBuiltinSettings.Ids(Data, "permissionIds"));
    public override bool OnReapply(PNBuffContext context, PNBuffApplyRequest request)
    { context.SetDuration(PNBuffContentBatch.Number(Data, "duration")); return true; }
}

// DOT contract: one instance/source by default, live loaded stats including death,
// cached last valid source calculation/ATK after unload, and explicit stack mutations.
public sealed class PNBuffSkillEffectBehavior : PNBuffTimedBehavior
{
    public override bool EventDrivenReplication => true;
    public PNBuffSkillEffectBehavior(JToken data) : base(data) { }
    public override void OnApply(PNBuffContext context, JToken input) { base.OnApply(context, input); OnResume(context); }
    public override bool OnReapply(PNBuffContext context, PNBuffApplyRequest request) { context.SetDuration(PNBuffContentBatch.Number(Data, "duration")); return true; }
    public override void OnResume(PNBuffContext context) => context.ModifySkill(new PNSkillContribution(Data["contribution"], context.Registry));
}

public sealed class PNBuffDotBehavior : PNBuffTimedBehavior
{
    public override bool EventDrivenReplication => true;
    private int stacks;
    private double lastAttack;
    private PNCombatSourceSnapshot lastSource;
    private readonly double duration, period, baseRatio, baseAmount, iframeDuration, additionalRatio, additionalGrowth;
    private readonly int maximumStacks;
    private readonly bool canCrit, incremental;
    private readonly EveId[] damageTags;
    private double effectiveRatio, effectiveAmount;
    public PNBuffDotBehavior(JToken data) : base(data)
    {
        duration = PNBuffContentBatch.Number(Data, "duration"); period = PNBuffContentBatch.Number(Data, "period");
        baseRatio = PNBuffContentBatch.Number(Data, "attackRatio"); baseAmount = PNBuffContentBatch.Number(Data, "amount");
        maximumStacks = PNBuffContentBatch.Integer(Data, "maximumStacks"); iframeDuration = PNBuffContentBatch.Number(Data, "iframeDuration");
        canCrit = PNBuffContentBatch.Bool(Data, "canCrit"); damageTags = PNBuffBuiltinSettings.Ids(Data, "damageTags");
        incremental = Data["additionalStackAttackRatio"] != null;
        if (incremental) { additionalRatio = PNBuffContentBatch.Number(Data, "additionalStackAttackRatio"); additionalGrowth = PNBuffContentBatch.Number(Data, "additionalStackGrowth"); }
    }
    public override double? StackCount => stacks;
    public override bool CanTrigger => true;
    public override bool CanChangeStacks => true;
    public static void ValidateInput(JToken input)
    {
        if (input == null || input.Type == JTokenType.Null) return;
        var obj = PNBuffContentBatch.Object(input); PNBuffContentBatch.Fields(obj, "stacks");
        if (obj["stacks"] != null && PNBuffContentBatch.Integer(obj, "stacks") < 1) throw new FormatException("Apply stacks must be positive.");
    }
    private static int Incoming(JToken input)
    { ValidateInput(input); return input == null || input.Type == JTokenType.Null || input["stacks"] == null ? 1 : PNBuffContentBatch.Integer((JObject)input, "stacks"); }
    public override bool Supports(Type contract) => contract == typeof(PNBuffStackLimitContract) || contract == typeof(PNBuffValueContract);
    public override void ValidateContributions(PNBuffContext context, Type contract, System.Collections.Generic.IReadOnlyList<PNBuffContribution> proposed)
    {
        if (!Supports(contract)) return;
        var values = proposed.OfType<PNBuffNumberContribution>().ToArray();
        if (values.Length != proposed.Count || values.Any(x => contract == typeof(PNBuffStackLimitContract) ? x.Field != "maximum" : x.Field != "amount" && x.Field != "attackRatio"))
            throw new ArgumentException("Invalid DOT contribution field/type.");
        if (contract == typeof(PNBuffStackLimitContract)) ValidateMaximum(PNBuffContentBatch.Integer(Data, "maximumStacks") + values.Sum(x => x.Value));
        else foreach (var field in new[] { "amount", "attackRatio" }) PNBuffBuiltinSettings.RequireAmount(PNBuffContentBatch.Number(Data, field) + values.Where(x => x.Field == field).Sum(x => x.Value));
    }
    private static int ValidateMaximum(double value)
    { if (value < 1 || value > int.MaxValue || value != Math.Truncate(value)) throw new ArgumentException("Invalid DOT stack cap."); return (int)value; }
    private int Maximum(PNBuffContext context) => ValidateMaximum(context.Additive(typeof(PNBuffStackLimitContract), "maximum", maximumStacks));
    private void RefreshSource(PNBuffContext context)
    {
        using (EveEngine.EveHotPathMetrics.Measure("dot.source"))
        {
        var live = context.Source.LoadedSource;
        if (live != null)
        {
            var capture = live.CaptureBuffCombatSource();
            double attack = capture.Evaluate(PNContentIds.AttackPower); PNBuffBuiltinSettings.RequireAmount(attack);
            lastAttack = attack; lastSource = capture;
        }
        else if (lastSource == null)
        {
            lastSource = context.Source.Snapshot ?? throw new InvalidOperationException("DOT requires a captured combat source.");
            lastAttack = lastSource.Evaluate(PNContentIds.AttackPower); PNBuffBuiltinSettings.RequireAmount(lastAttack);
        }
    
        }
    }
    private void Begin(PNBuffContext context)
    {
        RefreshValues(context);
        BeginTimer(context); RefreshSource(context);
        context.Schedule("tick", period, period); OnResume(context);
    }
    public override void OnApply(PNBuffContext context, JToken input)
    { stacks = Math.Min(Incoming(input), Maximum(context)); Begin(context); }
    public override bool OnReapply(PNBuffContext context, PNBuffApplyRequest request)
    {
        stacks = (int)Math.Min((long)Maximum(context), (long)stacks + Incoming(request.Input));
        context.SetDuration(duration); RefreshSource(context); UpdateVisual(context); return true;
    }
    public override void OnContributionsChanged(PNBuffContext context) { RefreshValues(context); stacks = Math.Min(stacks, Maximum(context)); UpdateVisual(context); }
    private void RefreshValues(PNBuffContext context)
    {
        effectiveRatio = context.Additive(typeof(PNBuffValueContract), "attackRatio", baseRatio);
        effectiveAmount = context.Additive(typeof(PNBuffValueContract), "amount", baseAmount);
    }
    private void UpdateVisual(PNBuffContext context) { Presentation.PublishVisual(new JObject { ["stacks"] = stacks, ["maximum"] = Maximum(context) }); PublishNumbers(); }
    public override void OnResume(PNBuffContext context) { RefreshValues(context); UpdateVisual(context); }
    public override void ChangeStacks(PNBuffContext context, int delta)
    {
        stacks = (int)Math.Max(0, Math.Min((long)Maximum(context), (long)stacks + delta));
        UpdateVisual(context);
        if (stacks == 0) context.End(PNBuffEndReason.StacksDepleted);
    }
    public override void OnScheduled(PNBuffContext context, string key) { if (key == "tick") Trigger(context); }
    public override void Trigger(PNBuffContext context)
    {
        using (EveEngine.EveHotPathMetrics.Measure("dot.trigger"))
        {
        if (context.Services.Target == null || !context.Services.Target.IsReady || context.Services.Target.IsDead) return;
        RefreshSource(context);
        double ratio = effectiveRatio;
        ratio = !incremental ? ratio * stacks : ratio + (stacks - 1d) * additionalRatio + (stacks * (stacks + 1d) / 2d - 1d) * additionalGrowth;
        double amount = effectiveAmount * stacks + lastAttack * ratio;
        PNBuffBuiltinSettings.RequireAmount(amount);
        PNDamage damage;
        using (EveEngine.EveHotPathMetrics.Measure("dot.create-damage")) damage = lastSource.CreateDamage(amount, canCrit, iframeDuration, lastSource.GetType() == typeof(PNCombatSourceSnapshot) ? damageTags : (EveId[])damageTags.Clone());
        using (EveEngine.EveHotPathMetrics.Measure("dot.apply-damage")) context.Damage(damage);
        using (EveEngine.EveHotPathMetrics.Measure("dot.cue")) Presentation.Cue("tick");
    
        }
    }
    public override bool TryMigrateFrom(PNBuffBehavior previous, PNBuffContext context)
    {
        if (!(previous is PNBuffDotBehavior dot)) return false;
        // Keep the timeline only when its clock/period are compatible.
        if (PNBuffBuiltinSettings.Clock(Data) != PNBuffBuiltinSettings.Clock(dot.Data) || PNBuffContentBatch.Number(Data, "period") != PNBuffContentBatch.Number(dot.Data, "period")) return false;
        stacks = Math.Min(dot.stacks, Maximum(context)); lastAttack = dot.lastAttack; lastSource = dot.lastSource; return true;
    }
    public override void OnBehaviorStarted(PNBuffContext context, bool migrated)
    { if (!migrated) { stacks = Math.Min(1, Maximum(context)); Begin(context); } else { ObserveDuration(); OnResume(context); } }
}

public sealed class PNBuffDamageBehavior : PNBuffTimedBehavior
{
    public override bool EventDrivenReplication => true;
    private readonly bool bomb;
    public PNBuffDamageBehavior(JToken data, bool bomb) : base(data) { this.bomb = bomb; }
    public override bool Supports(Type contract) => contract == typeof(PNBuffValueContract);
    public override void OnApply(PNBuffContext context, JToken input)
    {
        base.OnApply(context, input);
        if (PNBuffContentBatch.String(Data, "sourceMode") == "Snapshot" && context.Source.Snapshot == null) throw new InvalidOperationException("Damage Buff requires captured combat source.");
        if (!bomb) { double period = PNBuffContentBatch.Number(Data, "period"); context.Schedule("tick", period, period); }
    }
    public override void OnBehaviorStarted(PNBuffContext context, bool migrated) { if (!migrated) OnApply(context, null); }
    private void Deal(PNBuffContext context)
    {
        double amount = context.Additive(typeof(PNBuffValueContract), "amount", PNBuffContentBatch.Number(Data, "amount")); PNBuffBuiltinSettings.RequireAmount(amount);
        var tags = PNBuffBuiltinSettings.Ids(Data, "damageTags"); bool crit = PNBuffContentBatch.Bool(Data, "canCrit"); double iframe = PNBuffContentBatch.Number(Data, "iframeDuration");
        PNDamage packet;
        if (PNBuffContentBatch.String(Data, "sourceMode") == "Snapshot") packet = context.Source.Snapshot.CreateDamage(amount, crit, iframe, tags);
        else
        {
            var source = context.Source.Live; // Built-in live mode stops when the source becomes unavailable.
            if (source == null) { context.End(PNBuffEndReason.SourceUnavailable); return; }
            packet = source.CreateDamage(amount, crit, iframe, tags);
        }
        context.Services.Target.ReceiveDamage(packet);
    }
    public override void OnScheduled(PNBuffContext context, string key) { if (key == "tick") Deal(context); }
    public override void OnEnding(PNBuffContext context, PNBuffEndReason reason, PNBuffSnapshot finalState)
    { if (bomb && reason == PNBuffEndReason.Expired && context.Services.Target != null && !context.Services.Target.IsDead) Deal(context); }
}

public sealed class PNBuffStackGrantBehavior : PNBuffTimedBehavior
{
    public override bool EventDrivenReplication => true;
    public PNBuffStackGrantBehavior(JToken data) : base(data) { }
    public override void OnApply(PNBuffContext context, JToken input) { base.OnApply(context, input); OnResume(context); }
    public override void OnResume(PNBuffContext context) => context.Contribute(new PNBuffNumberContribution(typeof(PNBuffStackLimitContract), "maximum", PNBuffContentBatch.Integer(Data, "amount")), EveId.Parse(PNBuffContentBatch.String(Data, "targetBuffId")));
}
public sealed class PNBuffOverrideBehavior : PNBuffTimedBehavior
{
    public override bool EventDrivenReplication => true;
    public PNBuffOverrideBehavior(JToken data) : base(data) { }
    public override void OnApply(PNBuffContext context, JToken input) { base.OnApply(context, input); OnResume(context); }
    public override void OnResume(PNBuffContext context) => context.Contribute(new PNBuffOverrideContribution(EveId.Parse(PNBuffContentBatch.String(Data, "profileId")), PNBuffContentBatch.Integer(Data, "priority")), EveId.Parse(PNBuffContentBatch.String(Data, "targetBuffId")));
}
public sealed class PNBuffImmunityBehavior : PNBuffTimedBehavior
{
    public override bool EventDrivenReplication => true;
    public PNBuffImmunityBehavior(JToken data) : base(data) { }
    public override void OnApply(PNBuffContext context, JToken input) { base.OnApply(context, input); OnResume(context); }
    public override void OnResume(PNBuffContext context) => context.Immune(PNBuffBuiltinSettings.Ids(Data, "buffIds"), PNBuffBuiltinSettings.Ids(Data, "tagIds"), PNBuffContentBatch.Bool(Data, "purge"));
}
