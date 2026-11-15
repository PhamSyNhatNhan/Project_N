using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: State-bound effects (G2c). A skill declares "while this Ability state is active: this stat source and these Buffs"; the AUTHORITY applies them when the state starts and takes them back
//     when it ends, whatever ended it (expiry, End, Reset). Then it tells the skill (`OnAbilityEnded`) so rewards and clean-up run in one place. The Ability stays passive state; the
//     skill never writes "start" and "end" code for the effects, and a guess or a replay never touches them (their state follows the authority's).
// VI: Hiệu ứng gắn với trạng thái. Skill khai báo "khi state này đang bật: nguồn chỉ số này và các Buff này"; AUTHORITY áp khi state bắt đầu, gỡ khi kết thúc (hết hạn, End, Reset),
//     rồi báo skill (`OnAbilityEnded`) để phần thưởng/dọn dẹp chạy ở một chỗ. Ability vẫn là trạng thái thụ động; guess/replay không đụng vào.
public sealed class PNStateEffectData
{
    public EveId State { get; private set; }
    // Optional stat source: `SourceId` + `Modifiers` go together.
    public EveId SourceId { get; private set; }
    public IReadOnlyList<EveStatModifier> Modifiers { get; private set; } = Array.Empty<EveStatModifier>();
    public IReadOnlyList<EveId> BuffIds { get; private set; } = Array.Empty<EveId>();

    public static IReadOnlyList<PNStateEffectData> ParseAll(JToken token)
    {
        if (token == null) return Array.Empty<PNStateEffectData>();
        if (!(token is JArray array)) throw new FormatException("stateEffects must be an array.");
        var result = new List<PNStateEffectData>(); var states = new HashSet<EveId>();
        foreach (var item in array)
        {
            var parsed = Parse(item);
            if (!states.Add(parsed.State)) throw new FormatException("stateEffects declares state twice: " + parsed.State);
            result.Add(parsed);
        }
        return result;
    }
    public static PNStateEffectData Parse(JToken token)
    {
        if (!(token is JObject obj)) throw new FormatException("A stateEffects entry must be an object.");
        foreach (var property in obj.Properties())
            if (property.Name != "state" && property.Name != "statSource" && property.Name != "buffs")
                throw new FormatException("Unknown stateEffects field: " + property.Name);
        if (obj["state"]?.Type != JTokenType.String) throw new FormatException("stateEffects entry needs a state ID.");
        var data = new PNStateEffectData { State = EveId.Parse(obj["state"].Value<string>()) };
        if (obj["statSource"] != null)
        {
            if (!(obj["statSource"] is JObject source)) throw new FormatException("statSource must be an object.");
            foreach (var property in source.Properties())
                if (property.Name != "id" && property.Name != "modifiers") throw new FormatException("Unknown statSource field: " + property.Name);
            if (source["id"]?.Type != JTokenType.String) throw new FormatException("statSource needs an id.");
            if (!(source["modifiers"] is JArray list) || list.Count == 0) throw new FormatException("statSource needs at least one modifier.");
            var modifiers = new List<EveStatModifier>();
            foreach (var item in list)
            {
                if (!(item is JObject modifier)) throw new FormatException("A statSource modifier must be an object.");
                modifiers.Add(PNStatModifierData.Parse(modifier));
            }
            data.SourceId = EveId.Parse(source["id"].Value<string>()); data.Modifiers = modifiers;
        }
        if (obj["buffs"] != null)
        {
            if (!(obj["buffs"] is JArray buffs)) throw new FormatException("buffs must be an array.");
            var ids = new List<EveId>();
            foreach (var item in buffs)
            {
                if (item.Type != JTokenType.String) throw new FormatException("A stateEffects Buff must be an ID string.");
                var id = EveId.Parse(item.Value<string>());
                if (ids.Contains(id)) throw new FormatException("stateEffects lists a Buff twice: " + id);
                ids.Add(id);
            }
            data.BuffIds = ids;
        }
        if (!data.SourceId.IsValid && data.BuffIds.Count == 0) throw new FormatException("A stateEffects entry needs a statSource or buffs.");
        return data;
    }
    // Content check: every modifier resolves, every Buff exists.
    public void Validate(EveRegistryHub hub)
    {
        if (SourceId.IsValid && hub.IsSealed)
        {
            var source = new PNStatSet(hub); var added = new HashSet<EveId>();
            foreach (var modifier in Modifiers)
            {
                if (hub.Stats.TryGet(modifier.Id, out var definition))
                { if (added.Add(modifier.Id)) source.AddBase(definition.Kind == EveStatKind.Resource ? (EveBaseStat)new EveResourceStat(modifier.Id, 0d, 0d, definition.MaxChangePolicy) : new EveBaseStat(modifier.Id, 0d)); }
                else if (!hub.Tags().TryGet(modifier.Id, out _)) throw new FormatException("Unknown modifier selector " + modifier.Id);
            }
            source.SetSource(SourceId, Modifiers);
        }
        foreach (var id in BuffIds) hub.Buffs().Get(id);
    }
}

public partial class PNSkill
{
    private sealed class ActiveStateEffect : IDisposable
    {
        public PNStateEffectData Data; public PNSkill Owner; public PNStat Stat; public bool Held;
        public readonly List<IDisposable> Buffs = new List<IDisposable>();
        public bool Statted;
        public void Dispose()
        {
            for (int i = Buffs.Count - 1; i >= 0; i--) { var buff = Buffs[i]; Buffs.RemoveAt(i); buff.Dispose(); }
            if (Statted) { Statted = false; if (Stat != null && Stat.IsReady) Stat.RemoveSource(Data.SourceId); }
        }
    }
    private IReadOnlyList<PNStateEffectData> stateEffectData = Array.Empty<PNStateEffectData>();
    private readonly Dictionary<EveId, ActiveStateEffect> activeStateEffects = new Dictionary<EveId, ActiveStateEffect>();
    private PNAbilityCollection stateEffectAbilities;

    // The Buff a state-bound effect grants (the skill decides how: the Buff has an owner and a Buff context). The default grants nothing.
    protected virtual IDisposable StartStateBuff(EveId buffId) => throw new InvalidOperationException("This skill declares state Buffs but does not grant them (override StartStateBuff).");
    // A state ended (authority only; its stat source and Buffs are already gone). `reason` says how: Expired, Ended or Reset.
    protected virtual void OnAbilityEnded(EveId state, PNAbilityChangeReason reason) { }
    // The skill may hold the effects of a state that ended until it calls `ReleaseStateEffects` (a finishing move that still needs them).
    protected virtual bool KeepsStateEffects(EveId state) => false;

    protected void BindStateEffects(PNAbilityCollection abilities, IReadOnlyList<PNStateEffectData> effects)
    {
        if (abilities == null) throw new ArgumentNullException(nameof(abilities));
        UnbindStateEffects();
        stateEffectAbilities = abilities; stateEffectData = effects ?? Array.Empty<PNStateEffectData>();
        foreach (var effect in stateEffectData)
            if (abilities.Find(effect.State).Count == 0) throw new InvalidOperationException("stateEffects names a missing Ability: " + effect.State);
        abilities.Changed += OnStateEffectChange;
    }
    // Takes every effect still on back without telling the skill (unload, world change).
    protected void UnbindStateEffects()
    {
        if (stateEffectAbilities != null) stateEffectAbilities.Changed -= OnStateEffectChange;
        stateEffectAbilities = null;
        var left = new List<ActiveStateEffect>(activeStateEffects.Values); activeStateEffects.Clear();
        foreach (var effect in left) effect.Dispose();
        stateEffectData = Array.Empty<PNStateEffectData>();
    }
    protected bool HasStateEffects(EveId state) => activeStateEffects.ContainsKey(state);
    // Ends the held effects of `state` now (the skill kept them with `KeepsStateEffects`) and runs the end hook.
    protected void ReleaseStateEffects(EveId state, PNAbilityChangeReason reason)
    {
        if (!activeStateEffects.TryGetValue(state, out var effect) || !effect.Held) return;
        EndStateEffects(effect, reason);
    }
    private void OnStateEffectChange(PNAbilityChange change)
    {
        var after = change.After; if (after == null || !HasGameplayAuthority) return;
        PNStateEffectData data = null;
        foreach (var candidate in stateEffectData) if (candidate.State == after.Id) { data = candidate; break; }
        if (data == null) return;
        bool wasActive = change.Before != null && change.Before.IsActive;
        if (!wasActive && after.IsActive) StartStateEffects(data);
        else if (wasActive && !after.IsActive && activeStateEffects.TryGetValue(data.State, out var effect))
        {
            if (KeepsStateEffects(data.State)) effect.Held = true; else EndStateEffects(effect, change.Reason);
        }
    }
    private void StartStateEffects(PNStateEffectData data)
    {
        if (activeStateEffects.TryGetValue(data.State, out var previous)) { activeStateEffects.Remove(data.State); previous.Dispose(); }
        var effect = new ActiveStateEffect { Data = data, Owner = this, Stat = GetComponent<PNStat>() };
        activeStateEffects[data.State] = effect;
        try
        {
            if (data.SourceId.IsValid) { effect.Stat.SetSource(data.SourceId, data.Modifiers); effect.Statted = true; }
            foreach (var id in data.BuffIds) effect.Buffs.Add(StartStateBuff(id));
        }
        catch { activeStateEffects.Remove(data.State); effect.Dispose(); throw; }
    }
    private void EndStateEffects(ActiveStateEffect effect, PNAbilityChangeReason reason)
    {
        activeStateEffects.Remove(effect.Data.State);
        try { effect.Dispose(); }
        finally { OnAbilityEnded(effect.Data.State, reason); }
    }
}
