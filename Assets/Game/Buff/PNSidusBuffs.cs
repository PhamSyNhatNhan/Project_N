using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Sidus Buffs (Docs/SidusPlan.md). Sidus is a state of a normal enemy, not an enemy type: the Buff takes a Sidus lease on PNEnemyStat (`LockSidus`) and, optionally, a package of stat modifiers.
//       project_n:buff_sidus_template : the template. Payload `{duration, clock?, modifiers?}`: `clock` defaults to Game, `modifiers` (0..32) are `{statId, value, group, calculationId?, contributionId?}` like the stat template.
//       Packages are DATA, never code: a variant (the Starherald's `project_n:sidus_star`, 15 s, six modifiers) is a Buff entry in its character JSON (`extensions.project_n.buffs`, factoryId this template).
//     One instance per target. Another Sidus Buff replaces this one (the old one ends with Replaced); the same Buff again refreshes its time. A target that is not a ready normal enemy (a boss, a player) keeps the Buff
//     inert: it exists, but grants no lease and no modifier. Modifiers and the lease belong to the Buff's resource scope, so they are returned when it expires, is removed, is replaced, or its owner dies or unloads.
// VI: Buff Sidus. Sidus là trạng thái của quái normal: Buff lấy khóa Sidus trên PNEnemyStat và (tùy chọn) một gói modifier chỉ số. Chỉ một Buff Sidus mỗi mục tiêu: Buff Sidus khác thay cái cũ (kết thúc bằng Replaced),
//     cùng Buff thì làm mới thời gian. Mục tiêu không phải quái normal (boss, người chơi) thì Buff nằm im: có mặt nhưng không khóa và không modifier. Khóa và modifier thuộc scope của Buff nên tự trả khi Buff mất.
public static class PNSidusBuffContent
{
    public static readonly EveId Tag = EveId.Parse("project_n:sidus"), Template = EveId.Parse("project_n:buff_sidus_template"), Star = EveId.Parse("project_n:sidus_star");
    // A pale blue-violet glow no other Buff uses (speed green #6fdc8c, block gold #f2d98a).
    public const string Glow = "#9aa8ff";
    public const string Icon = "PNBuffIcons/PNSidus";
    // The id of the Starherald's package (defined in characters/StarheraldCharacter.json; its numbers live there only).
    // A technical bound against a broken payload, not a balance limit.
    public const int MaxModifiers = 32;

    public static void Register(EveRegistryHub hub)
    {
        var registry = hub.Buffs(); var tags = new[] { EveId.Parse("project_n:buff"), Tag };
        registry.Register(new PNBuffDefinition(Template, Template, data => new PNBuffSidusBehavior(data), JObject.Parse("{'duration':20,'clock':'Game','modifiers':[]}"), tags,
            validate: Validate, validateReferences: References).WithCountdown(PNBuffCountdown.BottomRight));
    }

    private static readonly string[] ModifierFields = { "statId", "value", "group", "calculationId", "contributionId" };

    // The modifiers of a payload (an absent field is an empty package).
    public static EveStatModifier[] Parse(JObject data)
    {
        var items = ModifierItems(data);
        return items.Select(item => PNStatModifierData.Parse(item)).ToArray();
    }

    private static List<JObject> ModifierItems(JObject data)
    {
        if (data["modifiers"] == null) return new List<JObject>();
        if (!(data["modifiers"] is JArray array)) throw new FormatException("modifiers must be an array.");
        if (array.Count > MaxModifiers) throw new FormatException("A Sidus package has at most " + MaxModifiers + " modifiers.");
        return array.Select(PNBuffContentBatch.Object).ToList();
    }

    public static void Validate(JToken token, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(obj, "duration", "clock", "modifiers");
        PNBuffCollection.Seconds(PNBuffContentBatch.Number(obj, "duration"), false);
        if (obj["clock"] != null) PNBuffBuiltinSettings.Clock(obj);
        foreach (var item in ModifierItems(obj))
        {
            PNBuffContentBatch.Fields(item, ModifierFields);
            if (PNBuffContentBatch.Integer(item, "group") < 0) throw new FormatException("A Sidus modifier group cannot be negative.");
            PNStatModifierData.Validate(item, hub);
        }
    }

    public static void References(JToken token, PNContentReferenceContext context)
    {
        foreach (var item in ModifierItems(PNBuffContentBatch.Object(token))) PNBuffPayloadReferences.Modifier(item, context);
    }
}

public sealed class PNBuffSidusBehavior : PNBuffTimedBehavior
{
    public override bool EventDrivenReplication => true;
    private readonly EveStatModifier[] modifiers;
    private IDisposable lease, modifier;
    private PNBuffResourceScope scope;

    public PNBuffSidusBehavior(JToken data) : base(data)
    {
        if (Data["clock"] == null) Data["clock"] = "Game";
        modifiers = PNSidusBuffContent.Parse(Data);
    }

    public override void OnApply(PNBuffContext context, JToken input)
    {
        base.OnApply(context, input);
        ReplaceOthers(context);
        Acquire(context);
        PublishNumbers();
    }

    // Another Sidus Buff on this target (any definition built on the Sidus template, whatever its tags) ends with Replaced (bypass: a Buff that cannot be dispelled is still replaced). The commands run right after this one at the same boundary, so
    // the new lease is already held when the old one is returned and the state never drops in between.
    private static void ReplaceOthers(PNBuffContext context)
    {
        var mine = context.Handle;
        foreach (var other in context.Collection.Snapshot())
            if (!other.Handle.Equals(mine) && context.Registry.Buffs().Get(other.Id).ImplementationId == PNSidusBuffContent.Template) context.Collection.Remove(other.Handle, PNBuffEndReason.Replaced, true);
    }

    // Lease and modifiers only for a ready normal enemy; any other target leaves the Buff inert. Once per resource scope (suspend releases the scope, resume gives a new one).
    private void Acquire(PNBuffContext context)
    {
        if (context.IsSuspended) return;
        if (ReferenceEquals(scope, context.Resources) && !scope.IsDisposed) return;
        scope = context.Resources;
        lease?.Dispose(); modifier?.Dispose(); lease = null; modifier = null;
        if (!(context.Services.Target is PNEnemyStat enemy)) return;
        var taken = enemy.LockSidus("buff:" + context.Handle.InstanceId);
        if (taken == null) return;
        lease = context.Resources.Own(taken);
        if (modifiers.Length > 0) modifier = context.ModifyStats(modifiers);
    }

    public override bool OnReapply(PNBuffContext context, PNBuffApplyRequest request)
    { context.SetDuration(PNBuffContentBatch.Number(Data, "duration")); return true; }

    public override void OnContributionsChanged(PNBuffContext context) { if (!context.IsSuspended) Acquire(context); }
    public override void OnResume(PNBuffContext context) => Acquire(context);

    public override void OnCleanup(PNBuffContext context)
    { modifier?.Dispose(); lease?.Dispose(); modifier = null; lease = null; scope = null; }
}
